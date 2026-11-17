using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Decorative geometry plus read-only numeric NPC binding; no inventory/save mutations.
    internal sealed class WallMonitorAttachment : IDisposable
    {
        private const string Marker = "WHFD_WallMonitorPrototype";
        private readonly ManualLogSource log;
        private readonly MonitorFramingDebugUI framingUI;
        private readonly ConfigEntry<float> heightRatio, minimumHeightRatio, monitorWidth, monitorHeight, depth;
        private readonly ConfigEntry<float> monitorBottomRatio, monitorInsetRatio;
        private readonly ConfigEntry<string> wallRendererPath;
        private readonly ConfigEntry<string> monitorFrameColor, monitorStandbyColor;
        private readonly ConfigEntry<int> orderOffset;
        private readonly ConfigEntry<bool> layoutDiagnostics;
        private readonly ConfigEntry<FaceDepthMode> faceDepthMode;
        private readonly ConfigEntry<MonitorDisplayMode> displayMode;
        private readonly ConfigEntry<string>[] playbackPoses = new ConfigEntry<string>[4];
        private readonly ConfigEntry<string>[] playbackFaces = new ConfigEntry<string>[4];
        private readonly ConfigEntry<float> playbackBlend;
        private Renderer diagnosticWall;
        private Camera diagnosticCamera;
        private float diagnosticTime;
        private GameObject attachment;
        private ItemInfo owner;
        private PanelRenderingComparison comparison;
        private MonitorNpcBinding npcBinding;
        private MonitorStateDiagnostics stateDiagnostics;
        private MonitorDisplayRouter faceDisplay;
        private PrototypeVisualDesign visualDesign;
        private readonly List<Object> owned = new List<Object>();

        internal ItemInfo Owner {get {return owner;}}
        internal int BuildId {get;private set;}
        internal int InstanceId {get;private set;}
        internal int SceneHandle {get;private set;}
        internal MonitorDisplayRouter Display {get {return faceDisplay;}}
        /*WHFDPerfOnlyBegin*/
        internal int? PerformanceNpcId {get {return npcBinding==null?null:npcBinding.CurrentNpcId;}}
        /*WHFDPerfOnlyEnd*/
        internal MonitorStateDiagnostics Diagnostics {get {return stateDiagnostics;}}
        internal WallMonitorAttachment(WallDecorationOptions options,ManualLogSource logger,MonitorFramingDebugUI ui)
        {
            log=logger;framingUI=ui;
            heightRatio=options.heightRatio;
            minimumHeightRatio=options.minimumHeightRatio;
            monitorWidth=options.monitorWidth;
            monitorHeight=options.monitorHeight;
            depth=options.depth;
            monitorBottomRatio=options.monitorBottomRatio;
            monitorInsetRatio=options.monitorInsetRatio;
            wallRendererPath=options.wallRendererPath;
            monitorFrameColor=options.monitorFrameColor;
            monitorStandbyColor=options.monitorStandbyColor;
            orderOffset=options.orderOffset;
            layoutDiagnostics=options.layoutDiagnostics;
            faceDepthMode=options.faceDepthMode;
            displayMode=options.displayMode;
            playbackPoses=options.playbackPoses;
            playbackFaces=options.playbackFaces;
            playbackBlend=options.playbackBlend;
        }

        private string StatePose(int mode) { return framingUI.PlaybackPoseFor(faceDisplay,mode,playbackPoses[mode].Value); }
        private string StateFace(int mode) { return framingUI.PlaybackFaceFor(faceDisplay,mode,playbackFaces[mode].Value); }

        internal bool Poll()
        {
            using(MonitorPerformance.Measure(MonitorPerfStage.Poll)) { /*WHFDPerfScope*/
            if(owner==null || attachment==null || npcBinding==null || !npcBinding.FacilityValid) return false;
            npcBinding.Tick();
            if(visualDesign!=null)visualDesign.PollName();
            if(diagnosticWall!=null && Time.unscaledTime>=diagnosticTime) {
                var wall=diagnosticWall;var camera=diagnosticCamera;
                diagnosticWall=null;diagnosticCamera=null;DiagnoseLayout(wall,camera,"settled");
            }
            return true;
        
            } /*WHFDPerfEnd*/}
        internal void CycleComparison()
        {
            if(comparison==null) if(MonitorDebugSettings.Enabled)log.LogInfo("[WallCompare] Add a monitor to the nearest facility first.");
            else try{comparison.Next();}catch(Exception error){log.LogWarning("Comparison restored to baseline: "+error.GetType().Name);}
        }

        internal void Attach(ItemInfo target, GameObject prefab, ManagersScript managers)
        {
            if(target==null||!MonitorFacilityIdentity.Supports(target.itemKey))throw new InvalidOperationException("Facility is not a monitor target");
            using(MonitorPerformance.Measure(MonitorPerfStage.Attach)) { /*WHFDPerfScope*/
            BuildId=target.buildID;InstanceId=target.GetInstanceID();SceneHandle=target.gameObject.scene.handle;
            if (new[] { heightRatio.Value, minimumHeightRatio.Value, monitorWidth.Value, monitorHeight.Value, depth.Value }.Any(v => !Finite(v)))
                throw new InvalidOperationException("Decoration settings must be finite numbers");
            var camera = Camera.main;
            if (camera == null) throw new InvalidOperationException("Main camera unavailable");
            var primary = ResolveWall(target, prefab);
            // Measure only the selected placed wall, not the prefab union, colliders or NPC visuals.
            var points = GeometryPoints(primary).Select(p => camera.WorldToScreenPoint(primary.transform.TransformPoint(p))).ToArray();
            if (points.Length == 0 || points.Any(p => !Finite(p.x) || !Finite(p.y) || !Finite(p.z) || p.z <= camera.nearClipPlane))
                throw new InvalidOperationException("Wall projection unavailable or behind camera");
            float left = points.Min(p => p.x), right = points.Max(p => p.x);
            float bottom = points.Min(p => p.y), top = points.Max(p => p.y);
            if (right - left <= 0.01f || top - bottom <= 0.01f)
                throw new InvalidOperationException("Wall renderer has no usable projected width/height");
            float effectiveRatio = Mathf.Max(heightRatio.Value, minimumHeightRatio.Value);
            float screenHeight = (top - bottom) * effectiveRatio;
            float screenCenterX = (left + right) * 0.5f;
            // The entire extension is strictly above the actual wall's screen-space top.
            float planeDepth = camera.WorldToScreenPoint(primary.bounds.center).z + depth.Value;
            if (planeDepth <= camera.nearClipPlane) throw new InvalidOperationException("Decoration depth crosses camera near plane");
            var lowerLeft = camera.ScreenToWorldPoint(new Vector3(left, top, planeDepth));
            var lowerRight = camera.ScreenToWorldPoint(new Vector3(right, top, planeDepth));
            var upperLeft = camera.ScreenToWorldPoint(new Vector3(left, top + screenHeight, planeDepth));
            float width = Vector3.Distance(lowerLeft, lowerRight);
            float panelHeight = Vector3.Distance(lowerLeft, upperLeft);
            if (!Finite(width) || !Finite(panelHeight) || width <= 0 || panelHeight <= 0)
                throw new InvalidOperationException("Invalid measured panel dimensions");
            var center = camera.ScreenToWorldPoint(new Vector3(screenCenterX, top + screenHeight * 0.5f, planeDepth));
            // Uniform positive parent scale allows an exact camera-aligned Sprite rectangle.
            // Fail closed for shear/nonuniform scale instead of silently covering the original wall.
            var scale = target.transform.lossyScale;
            if (scale.x <= 0 || Mathf.Abs(scale.x - scale.y) > 0.001f || Mathf.Abs(scale.x - scale.z) > 0.001f)
                throw new InvalidOperationException("Nonuniform/mirrored facility scale is not supported by exact renderer layout");
            int layer = primary.sortingLayerID;
            int order = Mathf.Clamp(primary.sortingOrder + orderOffset.Value, -32768, 32765);
            try
            {
                owner = target;
                attachment = new GameObject(Marker);
                attachment.SetActive(false);
                attachment.layer = primary.gameObject.layer;
                attachment.transform.SetParent(target.transform, false);
                attachment.transform.position = center;
                attachment.transform.rotation = camera.transform.rotation;
                attachment.transform.localScale = Vector3.one / scale.x;
                var surface = WoodPanelSurface.Create(primary, attachment.transform, effectiveRatio, layer, order, owned);
                var plain = SolidSprite();
                // Reproduce v0.2.10 as a hidden probe, then measure actual projected geometry.
                var referenceLayout = SquareMonitorLayout.Calculate(surface.Width, surface.Height,
                    Mathf.Max(monitorWidth.Value, monitorHeight.Value), monitorBottomRatio.Value, monitorInsetRatio.Value);
                var referencePosition = surface.BaseCenter + surface.UpDirection * referenceLayout.CenterHeight;
                var referenceRotation = Quaternion.LookRotation(Vector3.Cross(surface.RightDirection, surface.UpDirection), surface.UpDirection);
                var probe = AddSprite("MonitorProjectionProbe", plain, referencePosition,
                    referenceLayout.Side, referenceLayout.Side, layer, order + 1, Color.white, referenceRotation);
                probe.enabled = false;
                var referenceRect = ProjectedBounds(camera, probe);
                var panelCorners = new[] {
                    surface.BaseCenter - surface.RightDirection * (surface.Width * 0.5f),
                    surface.BaseCenter + surface.RightDirection * (surface.Width * 0.5f),
                    surface.BaseCenter + surface.RightDirection * (surface.Width * 0.5f) + surface.UpDirection * surface.Height,
                    surface.BaseCenter - surface.RightDirection * (surface.Width * 0.5f) + surface.UpDirection * surface.Height
                };
                var panelProjection = panelCorners.Select(point => {
                    var screen = camera.WorldToScreenPoint(point);
                    if (screen.z <= camera.nearClipPlane) throw new InvalidOperationException("Monitor panel crosses camera near plane");
                    return new Vector2(screen.x, screen.y);
                }).ToArray();
                var frameRect = ProjectedMonitorLayout.Fit(panelProjection, referenceRect, monitorInsetRatio.Value);
                var screenRect = ProjectedMonitorLayout.InnerScreen(frameRect);
                // One unlit mesh: frame is a ring, not a full quad behind the screen.
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) throw new InvalidOperationException("Monitor unlit shader unavailable");
                var monitorMaterial = new Material(shader);
                owned.Add(monitorMaterial); monitorMaterial.mainTexture = plain.texture;
                monitorMaterial.color = Color.white;
                if (monitorMaterial.HasProperty("_RendererColor")) monitorMaterial.SetColor("_RendererColor", Color.white);
                Object.Destroy(probe.gameObject);
                MonitorScreenGeometry aperture;
                var frame = AddMonitorMesh(camera, frameRect, screenRect, surface, monitorMaterial, layer, order + 1,out aperture);
                var panelRenderer=attachment.transform.Find("WoodExtension").GetComponent<Renderer>();
                MonitorMoveReceipt move=null;
                try {
                    
                    var wallAnimation=primary.GetComponent<Spine.Unity.SkeletonAnimation>();var wallSlot=wallAnimation.skeleton.Slots.Items[0];var wallMesh=(Spine.MeshAttachment)wallSlot.Attachment;var wallPositions=new float[wallMesh.WorldVerticesLength];wallMesh.ComputeWorldVertices(wallSlot,wallPositions);
                    float originalHeight=Vector3.Distance(WoodPanelSurface.WorldAt(primary,wallMesh,wallPositions,new Vector2(0,0)),WoodPanelSurface.WorldAt(primary,wallMesh,wallPositions,new Vector2(0,1)));
                    WoodPanelSurface.ExtendPrototypeTop(surface,panelRenderer,originalHeight*.30f);
                    var placementAudit=new VisualGeometryAudit(target,primary,panelRenderer);placementAudit.Save("before");
                    var oldBounds=ProjectedBounds(camera,frame);move=MonitorMoveReceipt.Apply(frame,attachment.transform,surface,originalHeight);
                    var movedBounds=ProjectedBounds(camera,frame);if(movedBounds.yMax>ProjectedBounds(camera,panelRenderer).yMax)throw new InvalidOperationException("Moved monitor exceeds wall top");
                    placementAudit.Verify(primary,panelRenderer,move);placementAudit.Save("after",move);
                    if(move!=null){aperture=move.Aperture(aperture,attachment.transform);screenRect.position+=movedBounds.center-oldBounds.center;}
                    if(MonitorDebugSettings.Enabled)log.LogInfo("[WHFDVisual266] wallTopAdded="+(originalHeight*.30f).ToString("R")+"; monitorScreenDelta="+(movedBounds.center-oldBounds.center)+"; width/height unchanged");
                }catch(Exception error){if(move!=null)move.Restore();log.LogWarning("[WHFDVisual] Upward monitor move rejected: "+error.Message);}
                if (MonitorDebugSettings.Enabled && layoutDiagnostics.Value) {
                    var actualRect = ProjectedBounds(camera, frame);
                    if(MonitorDebugSettings.Enabled)log.LogInfo("[MonitorAppearance] colorSpace=" + QualitySettings.activeColorSpace +
                        "; hex=sRGB; vertex=active-space; frame=" + monitorFrameColor.Value +
                        "; standby=" + monitorStandbyColor.Value + "; legacyDefaults=black-housing/black");
                    if(MonitorDebugSettings.Enabled)log.LogInfo("[MonitorProjection] reference=" + referenceRect.width + "x" + referenceRect.height +
                        "; result=" + actualRect.width + "x" + actualRect.height +
                        "; targetGap=" + referenceRect.height * ProjectedMonitorLayout.DesiredGapRatio +
                        "; fit=projected-panel; head=calibrated-fixed-reference");
                }
                attachment.SetActive(true);
                MonitorPerformance.Count(MonitorPerfEvent.AttachmentAdded); /*WHFDPerfEvent*/
                faceDisplay = new MonitorDisplayRouter(displayMode.Value, target, managers, attachment.transform, camera, screenRect, surface, layer, order + 2, log, faceDepthMode.Value,
                    StatePose, StateFace, () => playbackBlend.Value,aperture);
                var display=faceDisplay;
                faceDisplay.SetFramingProvider(id=>framingUI.FramingFor(display,id));
                faceDisplay.SetArmProvider(()=>framingUI.ArmsFor(display));
                faceDisplay.SetHeadProvider(()=>framingUI.HeadFor(display));
                faceDisplay.SetTorsoProvider(()=>framingUI.TorsoFor(display));
                framingUI.BindRouter(faceDisplay);
                stateDiagnostics = new MonitorStateDiagnostics(target, managers, log);
                npcBinding = new MonitorNpcBinding(target, managers, log);
                npcBinding.Changed += faceDisplay.SetNpc;
                npcBinding.UseChanged += faceDisplay.SetUseState;
                npcBinding.Tick();
                try { comparison = new PanelRenderingComparison(primary, attachment.transform.Find("WoodExtension").GetComponent<MeshRenderer>(), target.buildID, log); }
                catch (Exception error) { log.LogWarning("Rendering comparison unavailable; decoration retained: " + error.Message); }
                try {
                    visualDesign=new PrototypeVisualDesign(target,primary,attachment.transform.Find("WoodExtension").GetComponent<Renderer>(),attachment.transform,camera,surface,ProjectedBounds(camera,frame),log);
                    npcBinding.Changed+=visualDesign.SetNpc;visualDesign.SetNpc(npcBinding.CurrentNpc);
                }catch(Exception error){log.LogWarning("[WHFDVisual] Original appearance retained; "+error.Message);}
                if (MonitorDebugSettings.Enabled && layoutDiagnostics.Value)
                {
                    DiagnoseLayout(primary, camera, "created");
                    diagnosticWall = primary; diagnosticCamera = camera;
                    diagnosticTime = Time.unscaledTime + 0.25f;
                }
                if(MonitorDebugSettings.Enabled)log.LogInfo("Renderer layout: path=" + RelativePath(primary.transform, target.transform) +
                    "; wallScreenTop=" + top + "; panelScreenBottom=" + top + "; width=" + width +
                    "; wallHeightPixels=" + (top - bottom) + "; extensionRatio=" + effectiveRatio + "; wood=original-material; connection=attachment-top-edge");
            }
            catch { Detach(); throw; }
        
            } /*WHFDPerfEnd*/}

        private void DiagnoseLayout(Renderer wall, Camera camera, string phase)
        {
            if(!MonitorDebugSettings.Enabled)return;
            // Diagnostic errors must not destroy or reposition a valid attachment.
            try
            {
                if (wall == null || camera == null || owner == null || attachment == null) return;
                var panelNode = attachment.transform.Find("WoodExtension");
                var panel = panelNode == null ? null : panelNode.GetComponent<Renderer>();
                if (panel == null) return;
                var wallPoints = GeometryPoints(wall).Select(v => camera.WorldToScreenPoint(wall.transform.TransformPoint(v))).ToArray();
                var panelPoints = GeometryPoints(panel).Select(v => camera.WorldToScreenPoint(panel.transform.TransformPoint(v))).ToArray();
                if (wallPoints.Length == 0 || panelPoints.Length == 0) return;
                float actualTop = wallPoints.Max(v => v.y), actualBottom = panelPoints.Min(v => v.y);
                string prefix = "[LayoutDiagnostic " + phase + "] buildID=" + owner.buildID;
                if(MonitorDebugSettings.Enabled)log.LogInfo(prefix + "; wallPath=" + RelativePath(wall.transform, owner.transform) +
                    "; rendererType=" + wall.GetType().Name + "; wallBounds=" + wall.bounds +
                    "; projectedWallTop=" + actualTop + "; actualPanelBottom=" + actualBottom +
                    "; geometryGapPixels=" + (actualBottom - actualTop));
                if(MonitorDebugSettings.Enabled)log.LogInfo(prefix + "; wall " + TransformInfo(wall.transform) +
                    "; owner " + TransformInfo(owner.transform) + "; attachment " + TransformInfo(attachment.transform) +
                    "; panel " + TransformInfo(panel.transform));
                var spriteWall = wall as SpriteRenderer;
                var filter = wall.GetComponent<MeshFilter>();
                string sourceInfo = spriteWall == null ? "sourceSprite=none; meshBounds=" +
                    (filter == null || filter.sharedMesh == null ? "none" : filter.sharedMesh.bounds.ToString()) : SpriteInfo(spriteWall);
                var texture = wall.sharedMaterial == null ? null : wall.sharedMaterial.mainTexture;
                if(MonitorDebugSettings.Enabled)log.LogInfo(prefix + "; " + sourceInfo + "; panelGeometry " + (panel is SpriteRenderer ? SpriteInfo((SpriteRenderer)panel) : "MeshRenderer") +
                    "; sourceTextureSize=" + (texture == null ? "none" : texture.width + "x" + texture.height) +
                    "; sourceTextureReadable=" + (texture is Texture2D && ((Texture2D)texture).isReadable) +
                    "; sourceShader=" + (wall.sharedMaterial == null || wall.sharedMaterial.shader == null ? "none" : wall.sharedMaterial.shader.name) +
                    "; cameraPos=" + camera.transform.position + "; cameraRotation=" + camera.transform.rotation +
                    "; orthographic=" + camera.orthographic + "; orthoSize=" + camera.orthographicSize +
                    "; fov=" + camera.fieldOfView + "; pixelRect=" + camera.pixelRect);
                if (phase == "created") WallAttachmentDiagnostic.Log(wall, panel, camera, owner.buildID, log);
            }
            catch (Exception error) { log.LogWarning("Layout diagnostic unavailable: " + error.GetType().Name); }
        }
        private static string TransformInfo(Transform value)
        {
            return "worldPos=" + value.position + ",localPos=" + value.localPosition +
                ",rotation=" + value.rotation + ",localScale=" + value.localScale + ",lossyScale=" + value.lossyScale;
        }
        private static string SpriteInfo(SpriteRenderer renderer)
        {
            var sprite = renderer.sprite;
            return sprite == null ? "sprite=none" : "bounds=" + sprite.bounds + ",pivot=" + sprite.pivot +
                ",rect=" + sprite.rect + ",drawMode=" + renderer.drawMode + ",size=" + renderer.size +
                ",flip=" + renderer.flipX + "/" + renderer.flipY;
        }

        private Renderer ResolveWall(ItemInfo target, GameObject prefab)
        {
            // The known replaceable visual subtree is excluded before inspecting any renderer.
            var dynamicVisual = prefab.transform.Find("Scale/Anim");
            var candidates = new List<Renderer>();
            foreach (var source in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (dynamicVisual != null && (source.transform == dynamicVisual || source.transform.IsChildOf(dynamicVisual))) continue;
                if (!(source is SpriteRenderer) && !(source is MeshRenderer)) continue;
                string path = RelativePath(source.transform, prefab.transform);
                var node = path == "." ? target.transform : target.transform.Find(path);
                if (node == null) continue;
                var actual = node.GetComponent(source.GetType()) as Renderer;
                if (actual == null || !actual.enabled || !actual.gameObject.activeInHierarchy) continue;
                if (GeometryPoints(actual).Length == 0) continue;
                if (!string.IsNullOrEmpty(wallRendererPath.Value))
                {
                    if (path == wallRendererPath.Value) return actual;
                    continue;
                }
                candidates.Add(actual);
            }
            if (!string.IsNullOrEmpty(wallRendererPath.Value))
                throw new InvalidOperationException("WallRendererPath does not resolve to an active authored static wall renderer");
            var named = candidates.Where(IsWoodWall).ToArray();
            if (named.Length == 1) return named[0];
            if (named.Length == 0 && candidates.Count == 1) return candidates[0];
            // Paths are authored structural identifiers only; NPC subtrees were excluded above.
            throw new InvalidOperationException("Static wall renderer ambiguous; set WallRendererPath. Candidates: " +
                string.Join(", ", candidates.Select(r => RelativePath(r.transform, target.transform)).ToArray()));
        }
        private static string RelativePath(Transform node, Transform root)
        {
            var parts = new List<string>();
            for (var current = node; current != root; current = current.parent)
            {
                if (current == null) throw new InvalidOperationException("Renderer is outside the facility root");
                parts.Add(current.name);
            }
            parts.Reverse(); return parts.Count == 0 ? "." : string.Join("/", parts.ToArray());
        }
        private static bool IsWoodWall(Renderer renderer)
        {
            var sprite = renderer as SpriteRenderer;
            string label = renderer.name + (sprite == null || sprite.sprite == null ? "" : "/" + sprite.sprite.name);
            return label.IndexOf("wood", StringComparison.OrdinalIgnoreCase) >= 0 || label.IndexOf("wall", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private static Vector3[] GeometryPoints(Renderer renderer)
        {
            var sprite = renderer as SpriteRenderer;
            if (sprite != null)
                return sprite.sprite == null ? new Vector3[0] : sprite.sprite.vertices.Select(v =>
                    new Vector3(sprite.flipX ? -v.x : v.x, sprite.flipY ? -v.y : v.y, 0)).ToArray();
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return new Vector3[0];
            if (filter.sharedMesh.isReadable) return filter.sharedMesh.vertices;
            // Unreadable meshes expose a local bound; only flat display geometry is accepted.
            var b = filter.sharedMesh.bounds;
            if (b.size.x <= 0 || b.size.y <= 0 || b.size.z > Mathf.Max(b.size.x, b.size.y) * 0.001f) return new Vector3[0];
            return new[] { new Vector3(b.min.x,b.min.y,b.center.z), new Vector3(b.max.x,b.min.y,b.center.z),
                new Vector3(b.max.x,b.max.y,b.center.z), new Vector3(b.min.x,b.max.y,b.center.z) };
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private Sprite SolidSprite()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            owned.Add(texture); texture.SetPixel(0, 0, Color.white); texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            owned.Add(sprite); return sprite;
        }
        private SpriteRenderer AddSprite(string name, Sprite sprite, Vector3 position, float width, float heightValue, int layer, int order, Color color, Quaternion rotation)
        {
            var child = new GameObject(name);
            child.layer = attachment.layer;
            child.transform.SetParent(attachment.transform, false);
            child.transform.position = position;
            child.transform.rotation = rotation;
            child.transform.localScale = new Vector3(width / sprite.bounds.size.x, heightValue / sprite.bounds.size.y, 1f);
            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite; renderer.color = color; renderer.sortingLayerID = layer; renderer.sortingOrder = order;
            return renderer;
        }
        private static Rect ProjectedBounds(Camera camera, Renderer renderer)
        {
            var points = GeometryPoints(renderer).Select(p => camera.WorldToScreenPoint(renderer.transform.TransformPoint(p))).ToArray();
            if (points.Length == 0 || points.Any(p => p.z <= camera.nearClipPlane || !Finite(p.x) || !Finite(p.y)))
                throw new InvalidOperationException("Monitor projection unavailable");
            return Rect.MinMaxRect(points.Min(p => p.x), points.Min(p => p.y), points.Max(p => p.x), points.Max(p => p.y));
        }
        private MeshRenderer AddMonitorMesh(Camera camera, Rect frame, Rect screen, WoodPanelSurface surface,
            Material material, int layer, int order,out MonitorScreenGeometry aperture)
        {
            // A physical casing stand-off of 1% wall width keeps every vertex on the
            // camera side of the wood plane, instead of intersecting it at the center.
            var normal = Vector3.Cross(surface.RightDirection, surface.UpDirection).normalized;
            if (Vector3.Dot(normal, camera.transform.position - surface.BaseCenter) < 0) normal = -normal;
            float standOff = surface.Width * 0.01f;
            var vertices = new List<Vector3>(); var colors = new List<Color>();
            var frameColor = ReadMonitorColor(monitorFrameColor, new Color(24f / 255f, 24f / 255f, 24f / 255f), "#101214", "#707070", "#858585");
            var standbyColor = ReadMonitorColor(monitorStandbyColor, Color.black, "#06101C");
            var bezel = MonitorBezelSurface.Create(frame, screen, frameColor, standbyColor);
            for (int i = 0; i < bezel.Points.Count; i++)
            {
                // All contours are ray-projected onto their own parallel casing plane.
                // Shared contour vertices have identical depth; adjoining faces never overlap.
                var plane = new Plane(normal, surface.BaseCenter + normal * (standOff + surface.Width * bezel.Relief[i]));
                var point = bezel.Points[i];
                var ray = camera.ScreenPointToRay(new Vector3(point.x, point.y, 0));
                float distance;
                if (!plane.Raycast(ray, out distance)) throw new InvalidOperationException("Monitor plane not visible");
                var world = ray.GetPoint(distance);
                if (camera.WorldToScreenPoint(world).z <= camera.nearClipPlane)
                    throw new InvalidOperationException("Monitor plane crosses camera near clip");
                vertices.Add(attachment.transform.InverseTransformPoint(world));
                var srgb = bezel.SrgbColors[i];
                colors.Add(QualitySettings.activeColorSpace == ColorSpace.Linear ? srgb.linear : srgb);
            }
            var mesh = new Mesh { name = "WHFD_StableMonitor" }; owned.Add(mesh);
            var localVertices=vertices.ToArray();
            aperture=new MonitorScreenGeometry(attachment.transform,localVertices,bezel.ScreenVertexStart);
            mesh.vertices = localVertices; mesh.colors = colors.ToArray();
            mesh.uv = Enumerable.Repeat(new Vector2(0.5f, 0.5f), vertices.Count).ToArray();
            mesh.triangles = bezel.Triangles.ToArray(); mesh.RecalculateBounds();
            var child = new GameObject("DummyMonitor"); child.layer = attachment.layer;
            child.transform.SetParent(attachment.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = child.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            renderer.sortingLayerID = layer; renderer.sortingOrder = order;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // The LED owns the exact cutout; there is no bezel quad underneath it.
            // Kept independently addressable for future state changes, with no NPC logic now.
            var ledMesh = new Mesh { name = "WHFD_PowerLed" }; owned.Add(ledMesh);
            ledMesh.vertices = vertices.Skip(bezel.LedVertexStart).Take(4).ToArray();
            ledMesh.colors = colors.Skip(bezel.LedVertexStart).Take(4).ToArray();
            ledMesh.uv = Enumerable.Repeat(new Vector2(0.5f, 0.5f), 4).ToArray();
            ledMesh.triangles = new[] { 0, 2, 1, 1, 2, 3 }; ledMesh.RecalculateBounds();
            var led = new GameObject("PowerLed"); led.layer = child.layer;
            led.transform.SetParent(child.transform, false);
            led.AddComponent<MeshFilter>().sharedMesh = ledMesh;
            var ledRenderer = led.AddComponent<MeshRenderer>(); ledRenderer.sharedMaterial = material;
            ledRenderer.sortingLayerID = layer; ledRenderer.sortingOrder = order;
            ledRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ledRenderer.receiveShadows = false;
            return renderer;
        }
        private Color ReadMonitorColor(ConfigEntry<string> entry, Color fallback, params string[] previousDefaults)
        {
            var text = (entry.Value ?? "").Trim();
            // Do not rewrite the user's game config; only old shipped defaults are aliases.
            if (previousDefaults.Any(value => string.Equals(text, value, StringComparison.OrdinalIgnoreCase))) return fallback;
            uint rgb;
            if (text.Length == 7 && text[0] == '#' && uint.TryParse(text.Substring(1),
                System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out rgb))
                return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
            log.LogWarning("Invalid MonitorAppearance." + entry.Definition.Key + "; using default opaque color.");
            return fallback;
        }
        internal void TickFaceAnimation()
        {
            if(owner!=null&&MonitorFacilityIdentity.IsFixed(owner.itemKey)){
                int? id=npcBinding==null?null:npcBinding.CurrentNpcId;
                if(id!=15&&id!=16&&id!=17&&id!=44&&id!=90)return;
            }
            // Every-frame validated native snapshot; the 30-second diagnostic window
            // still controls logging independently and never writes native game state.
            if (stateDiagnostics != null) using(MonitorPerformance.Measure(MonitorPerfStage.StateObservation)) { stateDiagnostics.Refresh(); }
            if (faceDisplay != null) faceDisplay.TickAnimation(Time.deltaTime,
                stateDiagnostics == null ? null : stateDiagnostics.Snapshot);
        }
        private void Detach()
        {
            if(visualDesign!=null){if(npcBinding!=null)npcBinding.Changed-=visualDesign.SetNpc;visualDesign.Dispose();visualDesign=null;}
            if (stateDiagnostics != null) { stateDiagnostics.Dispose(); stateDiagnostics = null; }
            if (npcBinding != null) { npcBinding.Dispose(); npcBinding = null; }
            if (faceDisplay != null) { faceDisplay.Dispose(); faceDisplay = null; }
            if (comparison != null) { comparison.Dispose(); comparison = null; }
            diagnosticWall = null; diagnosticCamera = null;
            if (attachment != null) { MonitorPerformance.Count(MonitorPerfEvent.AttachmentDetached); /*WHFDPerfEvent*/ attachment.SetActive(false); Object.Destroy(attachment); }
            attachment = null; owner = null;
            foreach (var resource in owned) if (resource != null) Object.Destroy(resource);
            owned.Clear();
        }
        public void Dispose() { Detach(); }
    }
}
