using System;
using System.Globalization;
using System.Linq;
using BepInEx.Logging;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Read only the known static wall attachment; never enumerate NPCs or other slots.
    internal static class WallAttachmentDiagnostic
    {
        internal static void Log(Renderer wall, Renderer panel, Camera camera, int buildId, ManualLogSource log)
        {
            try
            {
                var component = wall.GetComponent<SkeletonAnimation>();
                var skeleton = component == null ? null : component.skeleton;
                if (skeleton == null || skeleton.Slots.Count == 0)
                    throw new InvalidOperationException("Static wall skeleton/slot 0 unavailable");
                var slot = skeleton.Slots.Items[0];
                var mesh = slot.Attachment as MeshAttachment;
                if (slot.Data.Name != "Wall_00" || mesh == null || mesh.Name != "wall_00")
                    throw new InvalidOperationException("Known static slot/attachment identity differs; no other slots inspected");
                var region = mesh.RendererObject as AtlasRegion;
                if (region == null || region.page == null || mesh.WorldVerticesLength < 6 || mesh.WorldVerticesLength > 64)
                    throw new InvalidOperationException("Atlas metadata missing or static attachment exceeds bounded diagnostic size");
                int count = mesh.WorldVerticesLength / 2;
                if (mesh.UVs == null || mesh.RegionUVs == null || mesh.UVs.Length != count * 2 || mesh.RegionUVs.Length != count * 2 ||
                    mesh.Triangles == null || mesh.Triangles.Length > 192)
                    throw new InvalidOperationException("Static attachment UV/triangle layout differs");
                var positions = new float[mesh.WorldVerticesLength];
                mesh.ComputeWorldVertices(slot, positions);
                float top = float.NegativeInfinity, bottom = float.PositiveInfinity;
                var vertices = new string[count];
                for (int i = 0; i < count; i++)
                {
                    // Spine world vertices are in the SkeletonAnimation object's coordinates.
                    var local = new Vector3(positions[i * 2], positions[i * 2 + 1], 0);
                    var screen = camera.WorldToScreenPoint(wall.transform.TransformPoint(local));
                    top = Mathf.Max(top, screen.y); bottom = Mathf.Min(bottom, screen.y);
                    vertices[i] = i + ":xy=" + F(local.x) + "," + F(local.y) +
                        "/screen=" + F(screen.x) + "," + F(screen.y) + "," + F(screen.z) +
                        "/regionUV=" + F(mesh.RegionUVs[i * 2]) + "," + F(mesh.RegionUVs[i * 2 + 1]) +
                        "/atlasUV=" + F(mesh.UVs[i * 2]) + "," + F(mesh.UVs[i * 2 + 1]);
                }
                var material = region.page.rendererObject as Material;
                var materials = wall.sharedMaterials;
                string matches = string.Join(",", materials.Select((m, i) => new { m, i })
                    .Where(x => material != null && x.m != null && (x.m == material ||
                        (x.m.mainTexture == material.mainTexture && x.m.shader == material.shader)))
                    .Select(x => x.i.ToString(CultureInfo.InvariantCulture)).ToArray());
                string prefix = "[WallAttachment] buildID=" + buildId;
                log.LogInfo(prefix + "; slot=0; attachment=wall_00; vertices=" + count +
                    "; frontTop=" + F(top) + "; frontBottom=" + F(bottom) +
                    "; hullLength=" + mesh.HullLength + "; materialIndices=" + matches +
                    "; shader=" + (material == null || material.shader == null ? "none" : material.shader.name));
                log.LogInfo(prefix + "; atlasXY=" + region.x + "," + region.y +
                    "; packedSize=" + region.width + "," + region.height +
                    "; originalSize=" + region.originalWidth + "," + region.originalHeight +
                    "; trimOffset=" + F(region.offsetX) + "," + F(region.offsetY) +
                    "; rotation=" + region.degrees + "; rotate=" + region.rotate +
                    "; atlasUV=" + F(region.u) + "," + F(region.v) + "," + F(region.u2) + "," + F(region.v2) +
                    "; pageSize=" + region.page.width + "," + region.page.height);
                log.LogInfo(prefix + "; " + string.Join("; ", vertices) +
                    "; triangles=" + string.Join(",", mesh.Triangles.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToArray()));
                LogConnection(wall, panel, mesh, buildId, log);
            }
            catch (Exception error) { log.LogWarning("[WallAttachment] unavailable: " + error.Message); }
        }
        private static void LogConnection(Renderer wall, Renderer panel, MeshAttachment attachment,
            int buildId, ManualLogSource log)
        {
            var sourceFilter = wall.GetComponent<MeshFilter>();
            var panelFilter = panel == null ? null : panel.GetComponent<MeshFilter>();
            var sourceMesh = sourceFilter == null ? null : sourceFilter.sharedMesh;
            var panelMesh = panelFilter == null ? null : panelFilter.sharedMesh;
            if (sourceMesh == null || panelMesh == null || !sourceMesh.isReadable || !panelMesh.isReadable ||
                sourceMesh.vertexCount > 256 || panelMesh.vertexCount > 256)
                throw new InvalidOperationException("Bounded readable wall/panel meshes required for 3D diagnostic");
            var left = RenderedCorner(wall.transform, sourceMesh, CornerUV(attachment, new Vector2(0, 0)));
            var right = RenderedCorner(wall.transform, sourceMesh, CornerUV(attachment, new Vector2(1, 0)));
            var baseLeft = RenderedCorner(wall.transform, sourceMesh, CornerUV(attachment, new Vector2(0, 1)));
            var points = panelMesh.vertices;
            if (points.Length < 4) throw new InvalidOperationException("Panel connection vertices unavailable");
            var panelLeft = panel.transform.TransformPoint(points[0]);
            var panelRight = panel.transform.TransformPoint(points[1]);
            var up = (left - baseLeft).normalized;
            var planeNormal = Vector3.Cross(right - left, up).normalized;
            var deltaLeft = panelLeft - left; var deltaRight = panelRight - right;
            string prefix = "[WallConnection3D] buildID=" + buildId;
            log.LogInfo(prefix + "; sourceTopL=" + V(left) + "; sourceTopR=" + V(right) +
                "; panelBottomL=" + V(panelLeft) + "; panelBottomR=" + V(panelRight) +
                "; signedDepthL=" + F(Vector3.Dot(deltaLeft, planeNormal)) +
                "; signedDepthR=" + F(Vector3.Dot(deltaRight, planeNormal)) +
                "; alongWallL=" + F(Vector3.Dot(deltaLeft, up)) +
                "; alongWallR=" + F(Vector3.Dot(deltaRight, up)));
            log.LogInfo(prefix + "; sourceRotation=" + wall.transform.rotation.ToString("G9") +
                "; sourceScale=" + V(wall.transform.lossyScale) + "; panelRotation=" + panel.transform.rotation.ToString("G9") +
                "; panelScale=" + V(panel.transform.lossyScale) +
                "; sourceNormal=" + WorldNormal(wall.transform, sourceMesh) +
                "; panelNormal=" + WorldNormal(panel.transform, panelMesh));
            var sun = RenderSettings.sun;
            log.LogInfo(prefix + "; sourceCasting=" + wall.shadowCastingMode + "; panelCasting=" + panel.shadowCastingMode +
                "; sourceReceive=" + wall.receiveShadows + "; panelReceive=" + panel.receiveShadows +
                "; sameMaterial=" + (wall.sharedMaterial == panel.sharedMaterial) +
                "; sourceSorting=" + wall.sortingLayerID + "," + wall.sortingOrder +
                "; panelSorting=" + panel.sortingLayerID + "," + panel.sortingOrder +
                "; sourceCasterBounds=" + wall.bounds + "; panelCasterBounds=" + panel.bounds +
                "; sunType=" + (sun == null ? "unassigned" : sun.type.ToString()) +
                "; sunDirection=" + (sun == null ? "unassigned" : V(sun.transform.forward)));
        }
        private static Vector2 CornerUV(MeshAttachment mesh, Vector2 corner)
        {
            for (int i = 0; i < mesh.RegionUVs.Length; i += 2)
                if (Mathf.Abs(mesh.RegionUVs[i] - corner.x) < 0.00001f && Mathf.Abs(mesh.RegionUVs[i + 1] - corner.y) < 0.00001f)
                    return new Vector2(mesh.UVs[i], mesh.UVs[i + 1]);
            throw new InvalidOperationException("Known wall corner not present");
        }
        private static Vector3 RenderedCorner(Transform transform, Mesh mesh, Vector2 uv)
        {
            var coordinates = mesh.uv; var vertices = mesh.vertices;
            bool found = false; Vector3 point = Vector3.zero;
            for (int i = 0; i < coordinates.Length; i++)
                if ((coordinates[i] - uv).sqrMagnitude < 0.0000000001f)
                {
                    var candidate = transform.TransformPoint(vertices[i]);
                    if (found && (candidate - point).sqrMagnitude > 0.00000001f)
                        throw new InvalidOperationException("Rendered wall UV maps to distinct 3D positions");
                    found = true; point = candidate;
                }
            if (!found) throw new InvalidOperationException("Rendered wall corner UV not found");
            return point;
        }
        private static string WorldNormal(Transform transform, Mesh mesh)
        {
            var normals = mesh.normals;
            return normals.Length == 0 ? "none" : V(transform.localToWorldMatrix.inverse.transpose.MultiplyVector(normals[0]).normalized);
        }
        private static string V(Vector3 value) { return "(" + F(value.x) + "," + F(value.y) + "," + F(value.z) + ")"; }
        private static string F(float value) { return value.ToString("G9", CultureInfo.InvariantCulture); }
    }
}
