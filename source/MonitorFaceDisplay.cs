using System;
using System.Linq;
using BepInEx.Logging;
using Spine.Unity;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Independent face renderer and private playback. Never writes to the source Skeleton or clones gameplay components.
    internal sealed class MonitorFaceDisplay : IDisposable
    {
        private readonly Transform parent;
        private readonly Plane contentPlane;
        private readonly Vector3 planeNormal, nearOrigin, nearX, nearY, farOrigin, farX, farY;
        private readonly Rect screen;
        private readonly ManualLogSource log;
        private readonly int layer, order;
        private readonly FaceDepthMode depthMode;
        private readonly float cavityDepth;
        private GameObject content;
        private Mesh mesh;
        private MeshRenderer faceRenderer;
        private CommonStates currentNpc;
        private SkeletonAnimation sourceAnimator;
        private MonitorFacePlayback playback;
        private bool suppressed;
        private string lastSelectionReason, renderPhase;
        private readonly MeshGenerator generator = new MeshGenerator();
        private readonly SkeletonRendererInstruction instruction = new SkeletonRendererInstruction();

        internal MonitorFaceDisplay(Transform parent, Camera camera, Rect screen, WoodPanelSurface surface,
            int layer, int order, ManualLogSource log, FaceDepthMode depthMode)
        {
            this.parent = parent; this.screen = screen;
            this.layer = layer; this.order = order; this.log = log; this.depthMode = depthMode;
            var normal = Vector3.Cross(surface.RightDirection, surface.UpDirection).normalized;
            if (Vector3.Dot(normal, camera.transform.position - surface.BaseCenter) < 0) normal = -normal;
            planeNormal = parent.InverseTransformDirection(normal).normalized;
            contentPlane = new Plane(planeNormal, parent.InverseTransformPoint(
                surface.BaseCenter + normal * (surface.Width * 0.0105f)));
            // Existing front bezel = 1.6% wall width, face baseline = 1.05%.
            // Derive available depth from those fixed surfaces; no shell movement.
            cavityDepth = parent.InverseTransformVector(normal * (surface.Width * (0.016f - 0.0105f))).magnitude;
            // Snapshot the screen-to-local rays at decoration creation, not at NPC insertion.
            // Subsequent camera/player movement cannot move the portrait off its monitor.
            float near = camera.nearClipPlane, far = near + 1f;
            nearOrigin = LocalPoint(camera, parent, screen.xMin, screen.yMin, near);
            nearX = LocalPoint(camera, parent, screen.xMax, screen.yMin, near) - nearOrigin;
            nearY = LocalPoint(camera, parent, screen.xMin, screen.yMax, near) - nearOrigin;
            farOrigin = LocalPoint(camera, parent, screen.xMin, screen.yMin, far);
            farX = LocalPoint(camera, parent, screen.xMax, screen.yMin, far) - farOrigin;
            farY = LocalPoint(camera, parent, screen.xMin, screen.yMax, far) - farOrigin;
        }

        internal void SetNpc(CommonStates npc)
        {
            // Reinitializing the same NPC's source renderer after use must not reopen
            // a display explicitly suppressed by the observed use-end transition.
            bool staySuppressed = suppressed && ReferenceEquals(currentNpc, npc);
            Clear(); currentNpc = npc; suppressed = staySuppressed; lastSelectionReason = null;
            if (suppressed || npc == null || npc.npcID != 15) return;
            try
            {
                renderPhase = "source";
                sourceAnimator = MonitorFaceSource.ResolveInstance(npc);
                var original = sourceAnimator.Skeleton;
                playback = new MonitorFacePlayback(MonitorFacePose.CaptureFull(original));
                Rebuild(sourceAnimator, MonitorFacePose.Capture(original), original, true);
            }
            catch (Exception error) { StopOnError(error); }
        }

        internal void SetUseState(bool inUse)
        {
            // Invoked only on verified SexPlace.user changes, never inferred from body tracks.
            if (currentNpc == null || currentNpc.npcID != 15) return;
            if (!inUse) { suppressed = true; Clear(); log.LogInfo("[MonitorFacePlayback] npcId=15; display=standby; reason=facility-use-ended"); }
            else { suppressed = false; SetNpc(currentNpc); }
        }

        internal void TickAnimation(float deltaTime)
        {
            if (suppressed || currentNpc == null || currentNpc.npcID != 15 || sourceAnimator == null || playback == null) return;
            try
            {
                renderPhase = "expression-selection";
                string reason;
                var selection = MonitorExpressionSelection.Read(sourceAnimator.AnimationState, playback.Pose.Data, out reason);
                if (selection == null)
                {
                    if (playback.IsPlaying)
                    {
                        playback = new MonitorFacePlayback(MonitorFacePose.CaptureFull(sourceAnimator.Skeleton));
                        Rebuild(sourceAnimator, MonitorFacePose.Capture(sourceAnimator.Skeleton), sourceAnimator.Skeleton, false);
                    }
                    if (lastSelectionReason != reason)
                        log.LogInfo("[MonitorFacePlayback] npcId=15; mode=static; reason=" + reason);
                    lastSelectionReason = reason; return;
                }
                if (!playback.IsPlaying) playback = new MonitorFacePlayback(MonitorFacePose.CaptureFull(sourceAnimator.Skeleton));
                renderPhase = "expression-playback";
                bool changed = playback.Advance(selection, deltaTime * sourceAnimator.timeScale, sourceAnimator.AnimationState.TimeScale);
                Rebuild(sourceAnimator, playback.Pose, sourceAnimator.Skeleton, false);
                if (changed) log.LogInfo("[MonitorFacePlayback] npcId=15; mode=auto; sourceTrack=2; animation=" +
                    selection.Animation.Name + "; duration=" + selection.Animation.Duration + "; loop=" + selection.Loop);
                lastSelectionReason = reason;
            }
            catch (Exception error) { StopOnError(error); }
        }

        private void StopOnError(Exception error)
        {
            Clear();
            log.LogWarning("[MonitorFace] npcId=15; display=standby; phase=" + renderPhase + "; error=" + error.GetType().Name);
        }

        private void Rebuild(SkeletonAnimation source, Spine.Skeleton skeleton, Spine.Skeleton original, bool audit)
        {
            renderPhase = "mesh-generation";
            var slotMaterials = new System.Collections.Generic.Dictionary<Spine.Slot, Material>();
            foreach (var entry in source.CustomSlotMaterials)
                slotMaterials[skeleton.Slots.Items[entry.Key.Data.Index]] = entry.Value;
            MeshGenerator.GenerateSkeletonRendererInstruction(instruction, skeleton, slotMaterials, null, false, false);
            if (source.CustomMaterialOverride.Count > 0)
                MeshGenerator.TryReplaceMaterials(instruction.submeshInstructions, source.CustomMaterialOverride);
            if (instruction.submeshInstructions.Count == 0) throw new InvalidOperationException("No visible instance-face geometry");
            var materials = instruction.submeshInstructions.Select(part => part.material).ToArray();
            if (materials.Any(material => material == null)) throw new InvalidOperationException("Missing face material");
            generator.settings = new MeshGenerator.Settings {
                useClipping = true, pmaVertexColors = source.pmaVertexColors,
                tintBlack = source.tintBlack, zSpacing = source.zSpacing,
                addNormals = source.addNormals, calculateTangents = source.calculateTangents
            };
            generator.Begin(); generator.BuildMesh(instruction, true);
            if (mesh == null) mesh = new Mesh { name = "WHFD_Face15" };
            else mesh.Clear();
            generator.FillVertexData(mesh); generator.FillTriangles(mesh); generator.FillLateVertexData(mesh);
            var vertices = mesh.vertices;
            var auditInput = audit ? (Vector3[])vertices.Clone() : null;
            if (vertices.Length == 0 || mesh.triangles.Length == 0) throw new InvalidOperationException("Empty face after clipping");
            // MeshGenerator uploads capacity-sized buffers with unused zero vertices.
            // Only triangle-referenced vertices may define the portrait bounds.
            var used = mesh.triangles.Distinct().ToArray();
            float left = used.Min(i => vertices[i].x), right = used.Max(i => vertices[i].x);
            float bottom = used.Min(i => vertices[i].y), top = used.Max(i => vertices[i].y);
            var referenced = new System.Collections.Generic.HashSet<int>(used);
            if (!Finite(left) || !Finite(right) || !Finite(bottom) || !Finite(top) || right <= left || top <= bottom)
                throw new InvalidOperationException("Invalid face bounds");
            float scale = 0.90f * Mathf.Min(screen.width / (right - left), screen.height / (top - bottom));
            renderPhase = "screen-fit";
            float minimumZ = used.Min(i => vertices[i].z), maximumZ = used.Max(i => vertices[i].z);
            float depthScale = 0;
            if (depthMode == FaceDepthMode.PreserveRelative)
            {
                var center = PointOnBaseline(0.5f, 0.5f);
                float dx = Vector3.Distance(center, PointOnBaseline(0.5f + 1f / screen.width, 0.5f));
                float dy = Vector3.Distance(center, PointOnBaseline(0.5f, 0.5f + 1f / screen.height));
                // Convert source mesh units using the fitted plane's local metric.
                // Uniform depth compression, only if necessary, retains all relative gaps.
                depthScale = FaceDepthTransform.Scale(scale * Mathf.Sqrt(dx * dy), minimumZ, maximumZ, cavityDepth);
            }
            var normals = new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                if (!referenced.Contains(i)) vertices[i] = new Vector3((left + right) * 0.5f, (bottom + top) * 0.5f, maximumZ);
                var pixel = screen.center + new Vector2(vertices[i].x - (left + right) * 0.5f,
                    vertices[i].y - (bottom + top) * 0.5f) * scale;
                float u = (pixel.x - screen.xMin) / screen.width, v = (pixel.y - screen.yMin) / screen.height;
                var origin = nearOrigin + nearX * u + nearY * v;
                var farPoint = farOrigin + farX * u + farY * v;
                var ray = new Ray(origin, farPoint - origin);
                float offset = depthMode == FaceDepthMode.PreserveRelative
                    ? FaceDepthTransform.Offset(vertices[i].z, maximumZ, depthScale) : 0;
                vertices[i] = FaceDepthTransform.Project(contentPlane, ray, offset);
                normals[i] = planeNormal;
            }
            if (audit) log.LogInfo("[MonitorFaceMode] npcId=15; mode=" + depthMode + "; inputZ=" + minimumZ + ".." + maximumZ +
                "; depthScale=" + depthScale + "; towardViewerOffset=0.." + ((maximumZ - minimumZ) * depthScale) +
                "; cavityDepth=" + cavityDepth + "; mask=unchanged");
            if (audit) MonitorFaceAudit.Report(original, skeleton, source.zSpacing, auditInput, mesh.triangles,
                generator.VertexCount, vertices, contentPlane, materials, log);
            mesh.vertices = vertices; mesh.normals = normals; mesh.RecalculateBounds();
            if (source.calculateTangents) mesh.RecalculateTangents();
            renderPhase = "publish";
            if (content == null)
            {
                content = new GameObject("FaceContent15"); content.layer = parent.gameObject.layer;
                content.transform.SetParent(parent, false);
                content.AddComponent<MeshFilter>().sharedMesh = mesh;
                faceRenderer = content.AddComponent<MeshRenderer>();
                faceRenderer.sortingLayerID = layer; faceRenderer.sortingOrder = order;
                faceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                faceRenderer.receiveShadows = false;
            }
            faceRenderer.sharedMaterials = materials;
            var originalRenderer = source.GetComponent<MeshRenderer>();
            if (audit) log.LogInfo("[MonitorFace] npcId=15; display=static-instance; source=inventory-common.anim; sourceSkeleton=independent; vertices=" + vertices.Length +
                "; runtimeSkin=" + (original.Skin != null) + "; faceAttachments=" + skeleton.Slots.Count(slot => slot.Attachment is Spine.RegionAttachment || slot.Attachment is Spine.MeshAttachment) +
                "; deformSlots=" + skeleton.Slots.Count(slot => slot.Attachment != null && slot.Deform.Count > 0) +
                "; customSlotMaterials=" + source.CustomSlotMaterials.Count + "; materialOverrides=" + source.CustomMaterialOverride.Count +
                "; sourcePropertyBlock=" + (originalRenderer != null && originalRenderer.HasPropertyBlock()));
        }

        private Vector3 PointOnBaseline(float u, float v)
        {
            var origin = nearOrigin + nearX * u + nearY * v;
            var point = farOrigin + farX * u + farY * v;
            return FaceDepthTransform.Project(contentPlane, new Ray(origin, point - origin), 0);
        }
        private static Vector3 LocalPoint(Camera camera, Transform parent, float x, float y, float z)
        {
            return parent.InverseTransformPoint(camera.ScreenToWorldPoint(new Vector3(x, y, z)));
        }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private void Clear()
        {
            if (content != null) { content.SetActive(false); Object.Destroy(content); }
            if (mesh != null) Object.Destroy(mesh);
            content = null; mesh = null; faceRenderer = null; sourceAnimator = null; playback = null;
        }
        public void Dispose() { Clear(); currentNpc = null; }
    }
}
