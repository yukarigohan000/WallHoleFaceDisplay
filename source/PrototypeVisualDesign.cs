using System;using System.Collections.Generic;using System.Linq;using BepInEx.Logging;using Spine;using Spine.Unity;using TMPro;using UnityEngine;using Object=UnityEngine.Object;
namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    internal sealed class PrototypeVisualDesign:IDisposable
    {
        private readonly List<Object> owned=new List<Object>();private readonly ManualLogSource log;
        private ConcreteMaterialLease lease;private SkeletonAnimation skeleton;private Material sourceMaterial,wallMaterial;private Renderer wall,panel;private Material[] oldWall,oldPanel;
        private MaterialPropertyBlock wallBlock,panelBlock;private bool wallBlockChanged,panelBlockChanged;
        private readonly List<ReferenceDecorationLease> decorationLeases=new List<ReferenceDecorationLease>();private RuntimeNameFont handwriting;
        private float decorationRelief;private GameObject root;private TextMeshPro nameText;private CommonStates npc;private string lastName;private float nextName;private bool disposed;
        internal PrototypeVisualDesign(ItemInfo owner,Renderer wall,Renderer panel,Transform parent,Camera camera,WoodPanelSurface surface,Rect monitor,ManualLogSource log)
        {
            this.log=log;if(owner==null||owner.itemKey!=PrototypeItemRegistration.Id)throw new InvalidOperationException("Visual design is prototype-only");
            this.wall=wall;this.panel=panel;skeleton=wall.GetComponent<SkeletonAnimation>();
            var slot=skeleton==null||skeleton.skeleton==null||skeleton.skeleton.Slots.Count==0?null:skeleton.skeleton.Slots.Items[0];
            var mesh=slot==null?null:slot.Attachment as MeshAttachment;
            if(slot==null||slot.Data.Name!="Wall_00"||mesh==null||mesh.Name!="wall_00"||mesh.WorldVerticesLength!=16)throw new InvalidOperationException("Reviewed wall structure unavailable");
            var region=mesh.RendererObject as AtlasRegion;sourceMaterial=region==null||region.page==null?null:region.page.rendererObject as Material;
            if(sourceMaterial==null||skeleton.CustomMaterialOverride.ContainsKey(sourceMaterial))throw new InvalidOperationException("Wall material already overridden; original retained");
            oldWall=wall.sharedMaterials;oldPanel=panel.sharedMaterials;
            var matches=oldWall.Where(m=>m!=null&&(m==sourceMaterial||(m.mainTexture==sourceMaterial.mainTexture&&m.shader==sourceMaterial.shader))).Distinct().ToArray();
            if(matches.Length!=1||oldPanel.Length!=1||oldPanel[0]!=matches[0])throw new InvalidOperationException("Wall/panel material ownership ambiguous");wallMaterial=matches[0];
            // This captures all EXISTING components, before any material/decoration changes.
            
            try{
                root=new GameObject("WHFD_VisualDesign");root.layer=parent.gameObject.layer;root.transform.SetParent(parent,false);
                var positions=new float[mesh.WorldVerticesLength];mesh.ComputeWorldVertices(slot,positions);
                var faceNormal=Vector3.Cross(surface.RightDirection,surface.UpDirection).normalized;if(Vector3.Dot(faceNormal,camera.transform.position-surface.BaseCenter)<0)faceNormal=-faceNormal;
                for(int vi=0;vi<positions.Length;vi+=2)decorationRelief=Mathf.Max(decorationRelief,Vector3.Dot(wall.transform.TransformPoint(new Vector3(positions[vi],positions[vi+1],0))-surface.BaseCenter,faceNormal));
                foreach(var vertex in panel.GetComponent<MeshFilter>().sharedMesh.vertices)decorationRelief=Mathf.Max(decorationRelief,Vector3.Dot(panel.transform.TransformPoint(vertex)-surface.BaseCenter,faceNormal));
                var leftTop=camera.WorldToScreenPoint(WoodPanelSurface.WorldAt(wall,mesh,positions,new Vector2(.04f,.04f)));
                var rightTop=camera.WorldToScreenPoint(WoodPanelSurface.WorldAt(wall,mesh,positions,new Vector2(.96f,.04f)));
                var leftBottom=camera.WorldToScreenPoint(WoodPanelSurface.WorldAt(wall,mesh,positions,new Vector2(.04f,.25f)));
                if(leftTop.z<=camera.nearClipPlane||rightTop.z<=camera.nearClipPlane||leftBottom.z<=camera.nearClipPlane)throw new InvalidOperationException("Decoration safe band behind camera");
                VisualRect plate,warning;var protectedPlate=new VisualRect();ConcreteInkStamp plateInk=null;
                if(VisualDecorationLayout.TryCreate(Mathf.Min(leftTop.x,rightTop.x),Mathf.Max(leftTop.x,rightTop.x),leftBottom.y,Mathf.Min(leftTop.y,rightTop.y),monitor.yMin,out plate,out warning)){
                    VisualRect centered;
                    if(VisualDecorationLayout.TryCenterUnderMonitor(new VisualRect{X=monitor.xMin,Y=monitor.yMin,W=monitor.width,H=monitor.height},leftBottom.y,plate,warning,out centered)){
                        plateInk=BuildPlate(centered,camera,surface,wall.sortingLayerID,panel.sortingOrder+1);protectedPlate=centered;
                        nameText=Text("NpcName",centered,camera,surface,wall.sortingLayerID,panel.sortingOrder+2,.12f);
                        if(MonitorDebugSettings.Enabled)log.LogInfo("[WHFDVisualLayout] monitorCenterX="+(monitor.xMin+monitor.width*.5f).ToString("R")+"; plateCenterX="+(centered.X+centered.W*.5f).ToString("R")+"; monitorBottom="+monitor.yMin.ToString("R")+"; plateTop="+centered.Top.ToString("R"));
                    }else log.LogWarning("[WHFDVisual] centered plate has insufficient clear space; existing parts and warning unchanged");
                    // Prior warning/WHFD-01 deliberately hidden to match the supplied finished layout.
                }else log.LogWarning("[WHFDVisual] no non-overlapping upper-band space; new decorations omitted; existing parts unchanged");
                var ink=AddReferenceDecorations(owner,wall,camera,surface,wall.sortingLayerID,panel.sortingOrder+1,monitor,protectedPlate);
                if(plateInk!=null)ink=ink.Concat(new[]{plateInk}).ToArray();
                lease=ConcreteMaterialLease.Acquire(wallMaterial,mesh,wall,panel,surface,ink);
                var audit=new VisualGeometryAudit(owner,wall,panel);audit.Save("before");
                skeleton.CustomMaterialOverride.Add(sourceMaterial,lease.Material);
                wall.sharedMaterials=oldWall.Select(m=>m==wallMaterial?lease.Material:m).ToArray();panel.sharedMaterials=new[]{lease.PanelMaterial};
                wallBlock=ReplaceBlockTexture(wall);wallBlockChanged=wallBlock!=null;panelBlock=ReplaceBlockTexture(panel,lease.PanelMaterial.mainTexture);panelBlockChanged=panelBlock!=null;

                audit.Verify(wall,panel);audit.Save("after");if(MonitorDebugSettings.Enabled)log.LogInfo("[WHFDVisual] buildID="+owner.buildID+"; geometry=exact; appearance=dedicated-material; snapshot=WHFD-VisualAudit/v0266");
            }catch{Dispose();throw;}
        }
        private ConcreteInkStamp[] AddReferenceDecorations(ItemInfo owner,Renderer source,Camera camera,WoodPanelSurface surface,int layer,int order,Rect monitor,VisualRect plate)
        {
            var slot=skeleton.skeleton.FindSlot("WallHole_00");var hole=slot==null?null:slot.Attachment as MeshAttachment;
            if(hole==null||hole.Name!="wallhole_00"||hole.WorldVerticesLength!=16){log.LogWarning("[WHFDVisual] reviewed hole attachment unavailable; fixed decals omitted");return new ConcreteInkStamp[0];}
            var vertices=new float[hole.WorldVerticesLength];hole.ComputeWorldVertices(slot,vertices);var projected=new List<Vector3>();
            for(int i=0;i<vertices.Length;i+=2){var point=camera.WorldToScreenPoint(source.transform.TransformPoint(new Vector3(vertices[i],vertices[i+1],0)));if(point.z<=camera.nearClipPlane)throw new InvalidOperationException("Hole behind camera");projected.Add(point);}
            var bounds=new VisualRect{X=projected.Min(p=>p.x),Y=projected.Min(p=>p.y),W=projected.Max(p=>p.x)-projected.Min(p=>p.x),H=projected.Max(p=>p.y)-projected.Min(p=>p.y)};
            var wallSlot=skeleton.skeleton.Slots.Items[0];var wallMesh=(MeshAttachment)wallSlot.Attachment;var wallVertices=new float[wallMesh.WorldVerticesLength];wallMesh.ComputeWorldVertices(wallSlot,wallVertices);
            var wallPoints=new List<Vector3>();for(int i=0;i<wallVertices.Length;i+=2)wallPoints.Add(camera.WorldToScreenPoint(source.transform.TransformPoint(new Vector3(wallVertices[i],wallVertices[i+1],0))));
            var wallBounds=new VisualRect{X=wallPoints.Min(p=>p.x),Y=wallPoints.Min(p=>p.y),W=wallPoints.Max(p=>p.x)-wallPoints.Min(p=>p.x),H=wallPoints.Max(p=>p.y)-wallPoints.Min(p=>p.y)};
            // Reserve the native body's measured top without moving or updating its pose.
            float bodyTop=bounds.Top+bounds.H*1.1f;
            foreach(var native in owner.GetComponentsInChildren<Renderer>(true)){
                if(native==source||native.transform.IsChildOf(root.transform.parent)||native.bounds.size.sqrMagnitude<1e-8f)continue;
                var b=native.bounds;for(int i=0;i<8;i++){var world=new Vector3((i&1)==0?b.min.x:b.max.x,(i&2)==0?b.min.y:b.max.y,(i&4)==0?b.min.z:b.max.z);var point=camera.WorldToScreenPoint(world);if(point.z>camera.nearClipPlane)bodyTop=Mathf.Max(bodyTop,point.y);}
            }
            var ink=new List<ConcreteInkStamp>();var placed=new List<VisualRect>();
            foreach(var info in ReferenceDecorationCatalog.Items){
                var lease=ReferenceDecorationLease.Acquire(info);decorationLeases.Add(lease);var r=ReferenceDecorationCatalog.Placement(info,bounds,wallBounds,plate,bodyTop);
                if(r.W<=0||r.H<=0||r.X<wallBounds.X||r.Right>wallBounds.Right||r.Y<wallBounds.Y){log.LogWarning("[WHFDVisual267] insufficient safe space; omitted "+info.Name);continue;}
                if(placed.Any(other=>!lease.ClearOfRect(r,other))){log.LogWarning("[WHFDVisual267] fixed decal overlaps another decoration; omitted "+info.Name);continue;}
                if(!lease.ClearOfHole(r,bounds)||!lease.ClearOfRect(r,new VisualRect{X=monitor.xMin,Y=monitor.yMin,W=monitor.width,H=monitor.height})||!lease.ClearOfRect(r,plate)){log.LogWarning("[WHFDVisual] fixed decal intersects protected opening/monitor/plate; omitted: "+info.Name);continue;}
                placed.Add(r);
                var center=Point(new Vector2(r.X+r.W*.5f,r.Y+r.H*.5f),camera,surface);var a=Point(new Vector2(r.X,r.Y),camera,surface);var b=Point(new Vector2(r.Right,r.Y),camera,surface);var d=Point(new Vector2(r.X,r.Top),camera,surface);
                if(info.Name=="RedWriting"||info.Name=="PinkWriting"||info.Name.StartsWith("Balloon",StringComparison.Ordinal)){
                    ink.Add(new ConcreteInkStamp{Identity=info.Resource+"|"+info.Hash,Pixels=lease.Pixels,Width=lease.Width,Height=lease.Height,A=a,B=b,D=d});continue;
                }
                var go=new GameObject(info.Name);go.layer=root.layer;go.transform.SetParent(root.transform,false);go.transform.position=center;go.transform.rotation=Quaternion.LookRotation(Vector3.Cross(b-a,d-a),d-a);
                go.transform.localScale=new Vector3(Vector3.Distance(a,b)/lease.Sprite.bounds.size.x,Vector3.Distance(a,d)/lease.Sprite.bounds.size.y,1);
                var renderer=go.AddComponent<SpriteRenderer>();renderer.sprite=lease.Sprite;renderer.sharedMaterial=lease.Material;renderer.sortingLayerID=layer;renderer.sortingOrder=order;renderer.color=Color.white;
            }
            return ink.ToArray();
        }
        private MaterialPropertyBlock ReplaceBlockTexture(Renderer renderer,Texture replacement=null)
        {
            var original=new MaterialPropertyBlock();renderer.GetPropertyBlock(original);
            if(original.GetTexture("_MainTex")==null)return null;
            var changed=new MaterialPropertyBlock();renderer.GetPropertyBlock(changed);changed.SetTexture("_MainTex",replacement??lease.Material.mainTexture);renderer.SetPropertyBlock(changed);return original;
        }
        private Vector3 Point(Vector2 screen,Camera camera,WoodPanelSurface surface,float extra=0)
        {
            var normal=Vector3.Cross(surface.RightDirection,surface.UpDirection).normalized;
            if(Vector3.Dot(normal,camera.transform.position-surface.BaseCenter)<0)normal=-normal;
            var plane=new Plane(normal,surface.BaseCenter+normal*(decorationRelief+surface.Width*(.003f+extra)));var ray=camera.ScreenPointToRay(screen);float distance;
            if(!plane.Raycast(ray,out distance))throw new InvalidOperationException("Decoration projection failed");return ray.GetPoint(distance);
        }
        private void MeshObject(string name,List<Vector2> points,List<Color> colors,List<int> triangles,Camera camera,WoodPanelSurface surface,int layer,int order)
        {
            var mesh=new Mesh{name=name};owned.Add(mesh);mesh.vertices=points.Select(p=>root.transform.InverseTransformPoint(Point(p,camera,surface))).ToArray();mesh.colors=colors.Select(c=>QualitySettings.activeColorSpace==ColorSpace.Linear?c.linear:c).ToArray();mesh.uv=Enumerable.Repeat(new Vector2(.5f,.5f),points.Count).ToArray();mesh.triangles=triangles.ToArray();mesh.RecalculateBounds();
            var shader=Shader.Find("Sprites/Default");if(shader==null)throw new InvalidOperationException("Decoration shader unavailable");var material=new Material(shader){mainTexture=Texture2D.whiteTexture,color=Color.white};owned.Add(material);
            var go=new GameObject(name);go.layer=root.layer;go.transform.SetParent(root.transform,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.sortingLayerID=layer;r.sortingOrder=order;r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;r.receiveShadows=false;
        }
        private static void Quad(List<Vector2> p,List<Color> c,List<int> t,VisualRect r,Color color)
        {int n=p.Count;p.Add(new Vector2(r.X,r.Y));p.Add(new Vector2(r.Right,r.Y));p.Add(new Vector2(r.X,r.Top));p.Add(new Vector2(r.Right,r.Top));for(int i=0;i<4;i++)c.Add(color);t.AddRange(new[]{n,n+2,n+1,n+1,n+2,n+3});}
        private ConcreteInkStamp BuildPlate(VisualRect r,Camera camera,WoodPanelSurface surface,int layer,int order)
        {
            // Static background only. NpcName is still a separate live TextMeshPro object.
            var info=ReferenceDecorationCatalog.Plate;var image=ReferenceDecorationLease.Acquire(info);decorationLeases.Add(image);
            return new ConcreteInkStamp{Identity=info.Resource+"|"+info.Hash,Pixels=image.Pixels,Width=image.Width,Height=image.Height,
                A=Point(new Vector2(r.X,r.Y),camera,surface),B=Point(new Vector2(r.Right,r.Y),camera,surface),D=Point(new Vector2(r.X,r.Top),camera,surface)};
        }
        private void BuildWarning(VisualRect r,Camera camera,WoodPanelSurface surface,int layer,int order)
        {
            var p=new List<Vector2>();var c=new List<Color>();var t=new List<int>();float center=r.X+r.W*.5f;
            p.Add(new Vector2(center,r.Top));p.Add(new Vector2(center-r.H*.24f,r.Y+r.H*.40f));p.Add(new Vector2(center+r.H*.24f,r.Y+r.H*.40f));for(int i=0;i<3;i++)c.Add(new Color(.64f,.51f,.20f));t.AddRange(new[]{0,1,2});
            Quad(p,c,t,new VisualRect{X=center-r.H*.013f,Y=r.Y+r.H*.56f,W=r.H*.026f,H=r.H*.22f},new Color(.16f,.15f,.12f));Quad(p,c,t,new VisualRect{X=center-r.H*.013f,Y=r.Y+r.H*.48f,W=r.H*.026f,H=r.H*.03f},new Color(.16f,.15f,.12f));MeshObject("WarningMark",p,c,t,camera,surface,layer,order);
            var text=Text("EquipmentNumber",new VisualRect{X=r.X,Y=r.Y,W=r.W,H=r.H*.35f},camera,surface,layer,order+1,0);if(text!=null)text.text="WHFD-01";
        }
        private static TMP_FontAsset selectedFont;private static bool fontSearched;
        private static TMP_FontAsset Font()
        {
            if(selectedFont!=null)return selectedFont;if(fontSearched)return null;fontSearched=true;
            // Read existing game fonts once; do not import a new UI or font system.
            var fonts=Resources.FindObjectsOfTypeAll<TMP_FontAsset>();selectedFont=fonts.FirstOrDefault(f=>f!=null&&f.HasCharacter('\u65e5',true,true)&&f.HasCharacter('\u672c',true,true)&&f.HasCharacter('\u8a9e',true,true));
            if(selectedFont==null&&TMP_Settings.defaultFontAsset!=null&&TMP_Settings.fallbackFontAssets!=null)selectedFont=TMP_Settings.fallbackFontAssets.FirstOrDefault(f=>f!=null&&f.HasCharacter('\u65e5',true,true));
            return selectedFont;
        }
        private TextMeshPro Text(string name,VisualRect r,Camera camera,WoodPanelSurface surface,int layer,int order,float inset)
        {
            var font=Font();if(font==null){log.LogWarning("[WHFDVisual] loaded Japanese font unavailable; text left blank");return null;}
            if(name=="NpcName"){handwriting=RuntimeNameFont.Acquire(font);font=handwriting.Asset;}
            var go=new GameObject(name);go.layer=root.layer;go.transform.SetParent(root.transform,false);var text=go.AddComponent<TextMeshPro>();text.font=font;text.fontSharedMaterial=font.material;
            var center=Point(new Vector2(r.X+r.W*.5f,r.Y+r.H*.5f),camera,surface,.001f);var a=Point(new Vector2(r.X,r.Y),camera,surface,.001f);var b=Point(new Vector2(r.Right,r.Y),camera,surface,.001f);var d=Point(new Vector2(r.X,r.Top),camera,surface,.001f);
            go.transform.position=center;go.transform.rotation=Quaternion.LookRotation(Vector3.Cross(b-a,d-a),d-a);text.rectTransform.sizeDelta=new Vector2(Vector3.Distance(a,b)*(1-inset*2),Vector3.Distance(a,d)*(name=="NpcName"?.8736f:.78f));
            text.alignment=TextAlignmentOptions.Center;text.color=name=="NpcName"?new Color(8f/255f,8f/255f,7f/255f):new Color(.12f,.12f,.10f);text.enableAutoSizing=true;text.fontSizeMin=.01f;text.fontSizeMax=name=="NpcName"?3.36f:3f;text.fontSize=text.fontSizeMax;if(name=="NpcName")text.fontStyle=FontStyles.Bold;text.enableWordWrapping=false;text.overflowMode=TextOverflowModes.Ellipsis;text.richText=false;text.text=string.Empty;if(name=="NpcName")text.OnPreRenderText+=RuntimeNameFont.Wobble;
            text.renderer.sortingLayerID=layer;text.renderer.sortingOrder=order;return text;
        }
        internal void SetNpc(CommonStates value){npc=value;nextName=0;RefreshName();}
        internal void PollName(){if(Time.unscaledTime<nextName)return;nextName=Time.unscaledTime+.5f;RefreshName();}
        private void RefreshName()
        {
            string value=ConcretePattern.NameText(npc==null?(int?)null:npc.npcID,npc==null?null:npc.charaName);
            if(value==lastName)return;lastName=value;if(nameText!=null)nameText.text=value;
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;npc=null;if(nameText!=null)nameText.OnPreRenderText-=RuntimeNameFont.Wobble;
            if(root!=null){root.SetActive(false);Object.Destroy(root);}root=null;nameText=null;
            if(skeleton!=null&&sourceMaterial!=null&&lease!=null){Material current;if(skeleton.CustomMaterialOverride.TryGetValue(sourceMaterial,out current)&&current==lease.Material)skeleton.CustomMaterialOverride.Remove(sourceMaterial);}
            if(wall!=null&&oldWall!=null){wall.sharedMaterials=oldWall;if(wallBlockChanged)wall.SetPropertyBlock(wallBlock);}if(panel!=null&&oldPanel!=null){panel.sharedMaterials=oldPanel;if(panelBlockChanged)panel.SetPropertyBlock(panelBlock);}
            foreach(var resource in owned)if(resource!=null)Object.Destroy(resource);owned.Clear();foreach(var decoration in decorationLeases)decoration.Dispose();decorationLeases.Clear();if(handwriting!=null)handwriting.Dispose();handwriting=null;if(lease!=null)lease.Dispose();lease=null;
        }
    }
}

