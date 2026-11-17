using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Spine;
using Spine.Unity;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;
namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Uses the proven command-buffer mesh, shader and screen projection path. Does not animate.
    internal sealed class MonitorTemplateSurface : IDisposable
    {
        private readonly Transform parent;private readonly int performanceNpcId,performanceMonitorId;
        /*WHFDPerfOnlyBegin*/
        private MeshRenderer performanceRenderer;
        private MonitorRenderGate renderGate;internal bool ForceRender;
        internal bool PerformanceVisible {get {return performanceRenderer!=null&&performanceRenderer.isVisible;}}
        /*WHFDPerfOnlyEnd*/
        private readonly int layer,order;
        private readonly ManualLogSource log;
        private readonly Vector3[] screenVertices;
        private readonly bool pma,tintBlack,addNormals,tangents;
        private readonly float zSpacing;
        private readonly Dictionary<Slot,Material> slotMaterials=new Dictionary<Slot,Material>();
        private readonly Dictionary<Material,Material> materialOverrides=new Dictionary<Material,Material>();
        private readonly Dictionary<Material,Material> clones=new Dictionary<Material,Material>();
        private readonly Dictionary<Material,int[]> passes=new Dictionary<Material,int[]>();
        private readonly HashSet<string> traced=new HashSet<string>();
        private readonly MeshGenerator generator=new MeshGenerator();
        private readonly SkeletonRendererInstruction instruction=new SkeletonRendererInstruction();
        private Camera camera;private GameObject cameraObject,screenObject;private CommandBuffer commands;
        private Mesh modelMesh,screenMesh;private Material screenMaterial;private RenderTexture texture;
        internal int AuditMode=-1;internal bool AuditPreview;internal string AuditStages,AuditSource,AuditSettings;
        private int auditMask;
        internal bool NeedsAudit(int mode,bool preview){return MonitorDebugSettings.Enabled&&mode>=0&&mode<4&&(auditMask&(1<<(mode+(preview?4:0))))==0;}
        private string referencePath="unavailable",referenceFailure="none";
        private string phase;private bool referenceReady;private Vector2 referenceCenter;private float referenceHalf;
        internal Texture Texture {get {return texture;}}
        internal MonitorTemplateSurface(Transform parent,Camera mainCamera,Rect screen,WoodPanelSurface surface,int layer,int order,ManualLogSource log,SkeletonAnimation template,Skeleton pose,int npcId,MonitorScreenGeometry screenGeometry)
        {
            using(MonitorPerformance.Measure(MonitorPerfStage.TemplateSurfaceInit)) { /*WHFDPerfScope*/
            performanceNpcId=npcId;performanceMonitorId=parent.parent.GetInstanceID();this.parent=parent;this.layer=layer;this.order=order;this.log=log;
            pma=template.pmaVertexColors;tintBlack=template.tintBlack;addNormals=template.addNormals;tangents=template.calculateTangents;zSpacing=template.zSpacing;
            if(zSpacing>=0)throw new InvalidOperationException("Unverified non-negative depth spacing");
            var originalRenderer=template.GetComponent<MeshRenderer>();if(originalRenderer!=null&&originalRenderer.HasPropertyBlock())throw new InvalidOperationException("Instance material property blocks require a reviewed appearance adapter");
            foreach(var entry in template.CustomSlotMaterials){if(entry.Key.Data.Index>=pose.Slots.Count)throw new InvalidOperationException("Custom slot layout mismatch");slotMaterials[pose.Slots.Items[entry.Key.Data.Index]]=entry.Value;}
            foreach(var entry in template.CustomMaterialOverride)materialOverrides[entry.Key]=entry.Value;
            if(screenGeometry==null)throw new InvalidOperationException("Monitor aperture unavailable");
            screenVertices=screenGeometry.CopyFor(parent);
            try{
                if(GraphicsSettings.currentRenderPipeline!=null)throw new InvalidOperationException("Built-in renderer required");
                texture=new RenderTexture(512,512,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Default){name="WHFD_TemplateTarget",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,antiAliasing=1,useMipMap=false,autoGenerateMips=false};
                if(!texture.Create())throw new InvalidOperationException("RenderTexture creation failed");
                MonitorPerformance.Count(MonitorPerfEvent.TemplateRTCreate); /*WHFDPerfEvent*/
                cameraObject=new GameObject("WHFD_TemplateCamera");cameraObject.hideFlags=HideFlags.HideAndDontSave;UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject,parent.gameObject.scene);
                camera=cameraObject.AddComponent<Camera>();camera.enabled=false;camera.cullingMask=0;camera.orthographic=true;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.targetTexture=texture;camera.aspect=1;camera.renderingPath=RenderingPath.Forward;camera.allowHDR=false;camera.allowMSAA=false;camera.useOcclusionCulling=false;
                commands=new CommandBuffer {name="WHFD_TemplatePrivateRig"};camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque,commands);modelMesh=new Mesh {name="WHFD_TemplateMesh"};
                RenderPose(pose,new MonitorFramingValue());CreateScreen();SetVisible(false);
            }catch{Dispose();throw;}
        
            } /*WHFDPerfEnd*/}
        internal void Draw(Skeleton pose,MonitorFramingValue framing){try{RenderPose(pose,framing);}catch(Exception e){throw new InvalidOperationException("Template render phase="+phase+": "+e.Message,e);}}
        internal void SetVisible(bool value){if(screenObject!=null)screenObject.SetActive(value);}
        private void Trace(string key,string message){if(MonitorDebugSettings.Enabled&&traced.Add(key))log.LogInfo("[MonitorTemplate] "+message);}
        private void CaptureReference(Skeleton pose,Vector3[] vertices)
        {
            try{var view=RestraintBustFraming.Measure(pose);HeadFramingReference face;if(RestraintBustFraming.TryCaptureFace(pose,view,out face)){referencePath="head-attachment-span";referenceCenter=face.Center;referenceHalf=face.Half;referenceReady=true;return;}referenceFailure="TryCaptureFace=false";}catch(Exception e){referenceFailure=e.GetType().Name+":"+e.Message;}
            if(performanceNpcId==44||performanceNpcId==90)throw new InvalidOperationException("Head-based framing unavailable for NPC ID "+performanceNpcId+": "+referenceFailure);
            referencePath="all-visible-triangles-fallback";var used=modelMesh.triangles.Distinct().ToArray();if(used.Length==0)throw new InvalidOperationException("Initial model has no visible triangles");
            float left=used.Min(i=>vertices[i].x),right=used.Max(i=>vertices[i].x),bottom=used.Min(i=>vertices[i].y),top=used.Max(i=>vertices[i].y);
            referenceCenter=new Vector2((left+right)*.5f,(bottom+top)*.5f);referenceHalf=Mathf.Max(right-left,top-bottom)*.54f;
            if(float.IsNaN(referenceHalf)||float.IsInfinity(referenceHalf)||referenceHalf<=0||float.IsNaN(referenceCenter.x)||float.IsNaN(referenceCenter.y)||float.IsInfinity(referenceCenter.x)||float.IsInfinity(referenceCenter.y))throw new InvalidOperationException("Initial visible bounds unavailable");
            referenceReady=true;
        }
        private void RenderPose(Skeleton renderPose,MonitorFramingValue framing)
        {
            using(MonitorPerformance.Measure(MonitorPerfStage.TemplateRender)) { /*WHFDPerfScope*/
            phase = "mesh-and-crop";
            using(MonitorPerformance.Measure(MonitorPerfStage.TemplateMeshAndSubmit)) { /*WHFDPerfScope*/
            MeshGenerator.GenerateSkeletonRendererInstruction(instruction, renderPose, slotMaterials, null, false, false);
            if(materialOverrides.Count>0)MeshGenerator.TryReplaceMaterials(instruction.submeshInstructions,materialOverrides);
            generator.settings = new MeshGenerator.Settings { useClipping = true, pmaVertexColors = pma,
                tintBlack = tintBlack, zSpacing = zSpacing, addNormals = addNormals, calculateTangents = tangents };
            generator.Begin(); generator.BuildMesh(instruction, true); modelMesh.Clear();
            generator.FillVertexData(modelMesh); generator.FillTriangles(modelMesh); generator.FillLateVertexData(modelMesh);
            if (generator.VertexCount == 0) throw new MonitorVideoConditionException("Private pose has no mesh");
            var vertices = MonitorPerformance.ReuseVertexBuffer?generator.Buffers.vertexBuffer:generator.Buffers.vertexBuffer.Take(generator.VertexCount).ToArray();
            if(!referenceReady)CaptureReference(renderPose,vertices);
            var center=referenceCenter;float half=referenceHalf;
            float minZ,maxZ;MonitorVertexRange.ZBounds(vertices,generator.VertexCount,out minZ,out maxZ);
            if (framing == null || !framing.Valid) framing = new MonitorFramingValue();
            camera.transform.position = new Vector3(center.x + framing.X * half * 2f,
                center.y + framing.Y * half * 2f, minZ - 1f);
            camera.transform.rotation = Quaternion.identity; camera.orthographicSize = half / framing.Zoom;
            camera.nearClipPlane = 0.01f; camera.farClipPlane = maxZ - minZ + 2f;
            if(NeedsAudit(AuditMode,AuditPreview)){
                auditMask|=1<<(AuditMode+(AuditPreview?4:0));
                try{log.LogInfo(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "[MonitorFramingAudit] npcId={0}; monitorId={1}; mode={2}; preview={3}; route=template; referencePath={4}; failure={5}; X={6:R}; Y={7:R}; Zoom={8:R}; refX={9:R}; refY={10:R}; referenceHalf={11:R}; camera={12}; orthographicSize={13:R}; clip={14:R},{15:R}; meshBounds={16}; RT=512x512/ARGB32/depth24/aspect1; settings={21}; source={17}; stages={18}; final={19}; slots={20}",
                    performanceNpcId,performanceMonitorId,AuditMode,AuditPreview,referencePath,referenceFailure,framing.X,framing.Y,framing.Zoom,referenceCenter.x,referenceCenter.y,referenceHalf,string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0:R},{1:R},{2:R}",camera.transform.position.x,camera.transform.position.y,camera.transform.position.z),camera.orthographicSize,camera.nearClipPlane,camera.farClipPlane,modelMesh.bounds,AuditSource,AuditStages,MonitorTemplatePlayback.AuditPose(renderPose),RestraintBustFraming.AuditAttachments(renderPose),AuditSettings));}catch(Exception e){log.LogWarning("[MonitorFramingAudit] template diagnostic unavailable: "+e.GetType().Name);}
            }
            commands.Clear();
            // The offscreen camera frustum and texture rectangle are the only crop.
            // Do not cut the model at an inferred neck boundary or hide individual slots.
            int drawCalls = 0;
            for (int i = 0; i < instruction.submeshInstructions.Count; i++)
            {
                var material = GetMaterial(instruction.submeshInstructions.Items[i].material);
                foreach (int pass in passes[material]) { commands.DrawMesh(modelMesh, Matrix4x4.identity, material, i, pass); drawCalls++; }
            }
            if (!traced.Contains("visibility")) {
                int visible=0;for(int vi=0;vi<generator.VertexCount;vi++){var v=camera.WorldToViewportPoint(vertices[vi]);if(v.z>=camera.nearClipPlane&&v.z<=camera.farClipPlane&&v.x>=0&&v.x<=1&&v.y>=0&&v.y<=1)visible++;}
                Trace("visibility", "stage=7; worldModelRenderer=none; commandDraws=" + drawCalls +
                    "; submeshes=" + instruction.submeshInstructions.Count + "; vertices=" + generator.VertexCount + "; verticesInFrustum=" + visible);
            }
            } /*WHFDPerfEnd*/
            phase = "offscreen-render"; if(renderGate==null)renderGate=new MonitorRenderGate(camera,MonitorPerfStage.TemplateCamera,MonitorPerfEvent.TemplateCameraRender);
            renderGate.Request(performanceRenderer,ForceRender,renderPose.Data==null?-1:performanceNpcId,performanceMonitorId);
            Trace("rendered", "stage=7; cameraRenderReturned=true");
        
            } /*WHFDPerfEnd*/}
        private Material GetMaterial(Material source)
        {
            Material material;
            if (source == null || source.mainTexture == null)
                throw new MonitorVideoConditionException("Pose atlas texture unavailable");
            if (clones.TryGetValue(source, out material)) return material;
            // This exact unlit shader/pass rendered the original mesh/UV/vertex RGBA in
            // v0.2.27. Never replace the source material or inherit Lit/stencil keywords.
            var shader = Shader.Find("Sprites/Default");
            if (shader == null || !shader.isSupported)
                throw new MonitorVideoConditionException("Verified video unlit shader unavailable");
            material = new Material(shader);
            clones.Add(source, material);
            material.mainTexture = source.mainTexture;
            material.color = Color.white;
            if (material.HasProperty("_RendererColor")) material.SetColor("_RendererColor", Color.white);
            if (material.passCount < 1) throw new MonitorVideoConditionException("Verified video unlit pass unavailable");
            passes.Add(material, new[] { 0 });
            Trace("material-" + source.GetInstanceID(), "stage=material; shader=Sprites/Default; selectedPasses=0; videoOnly=true; atlasReused=true; vertexRGBA=unchanged");
            return material;
        }
        private void CreateScreen()
        {
            phase = "screen-publication";
            var shader = Shader.Find("Sprites/Default"); if (shader == null) throw new MonitorVideoConditionException("Video display shader unavailable");
            screenMaterial = new Material(shader); screenMaterial.mainTexture = texture; screenMaterial.color = Color.white;
            if (screenMaterial.HasProperty("_RendererColor")) screenMaterial.SetColor("_RendererColor", Color.white);
            screenMesh = new Mesh { name = "WHFD_TemplateSurface" }; screenMesh.vertices = screenVertices;
            screenMesh.uv = new[] { new Vector2(0,0), new Vector2(1,0), new Vector2(0,1), new Vector2(1,1) };
            screenMesh.colors = Enumerable.Repeat(Color.white, 4).ToArray(); screenMesh.triangles = new[] { 0,2,1,1,2,3 }; screenMesh.RecalculateBounds();
            screenObject = new GameObject("WHFD_TemplateVideo"); screenObject.layer = parent.gameObject.layer; screenObject.transform.SetParent(parent, false);
            screenObject.AddComponent<MeshFilter>().sharedMesh = screenMesh;
            var renderer = screenObject.AddComponent<MeshRenderer>();
            performanceRenderer=renderer; /*WHFDPerfEvent*/ renderer.sharedMaterial = screenMaterial;
            renderer.sortingLayerID = layer; renderer.sortingOrder = order; renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            Trace("screen", "stage=9; textureAssigned=" + (screenMaterial.mainTexture == texture) + "; rendererEnabled=" + renderer.enabled + "; screenActive=" + screenObject.activeInHierarchy);
        }
        public void Dispose()
        {
            using(MonitorPerformance.Measure(MonitorPerfStage.TemplateDispose)) { /*WHFDPerfScope*/
            if(renderGate!=null){renderGate.Dispose();renderGate=null;}performanceRenderer=null; /*WHFDPerfEvent*/
            if(screenObject!=null){screenObject.SetActive(false);Object.Destroy(screenObject);}
            if(camera!=null){camera.RemoveAllCommandBuffers();camera.targetTexture=null;}
            if(commands!=null){commands.Release();commands=null;}if(cameraObject!=null)Object.Destroy(cameraObject);
            if(texture!=null){MonitorPerformance.Count(MonitorPerfEvent.TemplateRTRelease); /*WHFDPerfEvent*/ texture.Release();Object.Destroy(texture);}foreach(var material in clones.Values)Object.Destroy(material);clones.Clear();passes.Clear();slotMaterials.Clear();materialOverrides.Clear();
            if(screenMaterial!=null)Object.Destroy(screenMaterial);if(modelMesh!=null)Object.Destroy(modelMesh);if(screenMesh!=null)Object.Destroy(screenMesh);
            screenObject=null;cameraObject=null;camera=null;texture=null;modelMesh=null;screenMesh=null;screenMaterial=null;
            Array.Clear(instruction.submeshInstructions.Items,0,instruction.submeshInstructions.Items.Length);instruction.Clear();
        
            } /*WHFDPerfEnd*/}
    }
}
