using System;using System.Collections.Generic;using System.Globalization;using System.IO;using System.Linq;using System.Runtime.Serialization;using System.Runtime.Serialization.Json;using System.Text;using UnityEngine;using Object=UnityEngine.Object;
namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    internal sealed class VisualGeometryAudit
    {
        [DataContract]internal sealed class Row{[DataMember]public string Path;[DataMember]public string Geometry;[DataMember]public string Materials;[DataMember]public string CanonicalGeometry;internal Transform Target;}
        [DataContract]internal sealed class Report{[DataMember]public string Phase;[DataMember]public int BuildId;[DataMember]public int OwnerId;[DataMember]public Row[] Existing;[DataMember]public string[] Unavailable;[DataMember]public string[] AllowedMaterialPaths;[DataMember]public MonitorMoveReceipt.Record[] MonitorTranslations;}
        private readonly string auditId=Guid.NewGuid().ToString("N");
        private readonly string[] allowedMaterialPaths;
        private readonly ItemInfo owner;private readonly Row[] rows;private readonly string[] unavailable;
        internal VisualGeometryAudit(ItemInfo owner,Renderer wall,Renderer panel)
        {
            this.owner=owner;allowedMaterialPaths=new[]{PathOf(wall.transform,owner.transform),PathOf(panel.transform,owner.transform)};var missing=new List<string>();rows=owner.GetComponentsInChildren<Transform>(true).Select(t=>Capture(t,owner.transform,missing)).ToArray();unavailable=missing.ToArray();
        }
        private static string F(float v){return v.ToString("R",CultureInfo.InvariantCulture);}
        private static void V(StringBuilder s,Vector3 v){s.Append(F(v.x)).Append(',').Append(F(v.y)).Append(',').Append(F(v.z)).Append(';');}
        private static void Q(StringBuilder s,Quaternion v){V(s,new Vector3(v.x,v.y,v.z));s.Append(F(v.w)).Append(';');}
        private static string PathOf(Transform t,Transform root){if(t==root)return ".";var p=new List<string>();for(var n=t;n!=root;n=n.parent){if(n==null)return "<outside>";p.Add(n.name);}p.Reverse();return string.Join("/",p.ToArray());}
        private static Row Capture(Transform t,Transform root,List<string> missing,MonitorMoveReceipt translation=null)
        {
            string path=PathOf(t,root);var s=new StringBuilder();s.Append("instance=").Append(t.GetInstanceID()).Append(";parent=").Append(t.parent==null?0:t.parent.GetInstanceID()).Append(";sibling=").Append(t.GetSiblingIndex()).Append(';');
            V(s,t.localPosition);Q(s,t.localRotation);V(s,t.localScale);V(s,t.position);Q(s,t.rotation);V(s,t.lossyScale);s.Append(t.gameObject.activeSelf).Append(';').Append(t.gameObject.layer).Append(';');
            var rect=t as RectTransform;if(rect!=null){s.Append(rect.rect).Append(';');V(s,rect.anchoredPosition3D);}
            foreach(var c in t.GetComponents<Component>())s.Append("component=").Append(c==null?"missing":c.GetType().FullName+":"+c.GetInstanceID()).Append(';');
            var r=t.GetComponent<Renderer>();string materials="none";
            if(r!=null){s.Append(r.GetType().FullName).Append(';').Append(r.enabled).Append(';').Append(r.sortingLayerID).Append(';').Append(r.sortingOrder).Append(';').Append(r.shadowCastingMode).Append(';').Append(r.receiveShadows).Append(';');
                materials=string.Join(",",r.sharedMaterials.Select(m=>m==null?"null":m.GetInstanceID()+":"+m.name+":"+(m.mainTexture==null?"null":m.mainTexture.GetInstanceID()+"/"+m.mainTexture.width+"x"+m.mainTexture.height)).ToArray());
            }
            var sr=r as SpriteRenderer;if(sr!=null){var sp=sr.sprite;s.Append("sprite=").Append(sp==null?0:sp.GetInstanceID()).Append(';').Append(sr.drawMode).Append(';').Append(sr.flipX).Append(';').Append(sr.flipY).Append(';');V(s,sr.size);s.Append(sr.color).Append(';');
                if(sp!=null){s.Append("PPU=").Append(F(sp.pixelsPerUnit)).Append(";rect=").Append(sp.rect).Append(";pivot=");V(s,sp.pivot);V(s,sp.bounds.center);V(s,sp.bounds.size);foreach(var v in sp.vertices)V(s,v);foreach(var uv in sp.uv)V(s,uv);foreach(var i in sp.triangles)s.Append(i).Append(',');}}
            var mf=t.GetComponent<MeshFilter>();if(mf!=null&&mf.sharedMesh!=null){var m=mf.sharedMesh;MonitorMoveReceipt.Entry moved=null;if(translation!=null)translation.TryGet(t,out moved);var recordedBounds=moved==null?m.bounds:moved.Bounds;s.Append("mesh=").Append(m.GetInstanceID()).Append(';');V(s,recordedBounds.center);V(s,recordedBounds.size);s.Append(m.vertexCount).Append(';');
                if(m.isReadable){foreach(var v in moved==null?m.vertices:moved.Before)V(s,v);foreach(var uv in m.uv)V(s,uv);foreach(var v in m.normals)V(s,v);foreach(var uv in m.tangents){V(s,uv);s.Append(F(uv.w));}foreach(var c in m.colors32)s.Append(c.r).Append(',').Append(c.g).Append(',').Append(c.b).Append(',').Append(c.a).Append(';');foreach(var i in m.triangles)s.Append(i).Append(',');}
                else missing.Add(path+": unreadable mesh vertices/UV");}
            foreach(var c in t.GetComponents<Collider>()){s.Append("collider=").Append(c.GetType().FullName).Append(':').Append(c.GetInstanceID()).Append(';').Append(c.enabled).Append(';').Append(c.isTrigger).Append(';');V(s,c.bounds.center);V(s,c.bounds.size);
                var box=c as BoxCollider;if(box!=null){V(s,box.center);V(s,box.size);}var sphere=c as SphereCollider;if(sphere!=null){V(s,sphere.center);s.Append(F(sphere.radius));}var cap=c as CapsuleCollider;if(cap!=null){V(s,cap.center);s.Append(F(cap.radius)).Append(';').Append(F(cap.height)).Append(';').Append(cap.direction);}var mesh=c as MeshCollider;if(mesh!=null)s.Append(mesh.sharedMesh==null?0:mesh.sharedMesh.GetInstanceID()).Append(';').Append(mesh.convex);}
            var sk=t.GetComponent<Spine.Unity.SkeletonAnimation>();if(sk!=null&&sk.skeleton!=null){s.Append("skeleton=").Append(sk.skeleton.Data.Name).Append(';').Append(F(sk.skeleton.ScaleX)).Append(';').Append(F(sk.skeleton.ScaleY));foreach(var b in sk.skeleton.Bones){s.Append(b.Data.Name).Append(':').Append(F(b.X)).Append(',').Append(F(b.Y)).Append(',').Append(F(b.Rotation)).Append(',').Append(F(b.ScaleX)).Append(',').Append(F(b.ScaleY)).Append(',').Append(F(b.WorldX)).Append(',').Append(F(b.WorldY)).Append(';');}}
            return new Row{Path=path,Geometry=s.ToString(),CanonicalGeometry=s.ToString(),Materials=materials,Target=t};
        }
        internal void Verify(Renderer allowedWall,Renderer allowedPanel,MonitorMoveReceipt translation=null)
        {if(translation!=null)translation.Validate();foreach(var row in rows){if(row.Target==null)throw new InvalidOperationException("Existing object destroyed: "+row.Path);var now=Capture(row.Target,owner.transform,new List<string>(),translation);if(row.Path!=now.Path||row.Geometry!=now.Geometry)throw new InvalidOperationException("Existing geometry changed: "+row.Path);var renderer=row.Target.GetComponent<Renderer>();if(renderer!=allowedWall&&renderer!=allowedPanel&&row.Materials!=now.Materials)throw new InvalidOperationException("Unrelated material changed: "+row.Path);}}
        internal void Save(string phase,MonitorMoveReceipt translation=null)
        {
            if(!MonitorDebugSettings.Enabled)return;
            try {
            var directory=System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath,"WHFD-VisualAudit","v0266");Directory.CreateDirectory(directory);
            string file=System.IO.Path.Combine(directory,"build-"+owner.buildID+"-owner-"+owner.GetInstanceID()+"-"+auditId+"-"+phase+".json");
            var recorded=phase=="before"?rows:rows.Select(row=>Capture(row.Target,owner.transform,new List<string>())).ToArray();
            if(translation!=null){translation.Validate();for(int i=0;i<recorded.Length;i++)recorded[i].CanonicalGeometry=Capture(rows[i].Target,owner.transform,new List<string>(),translation).Geometry;}
            var data=new Report{Phase=phase,BuildId=owner.buildID,OwnerId=owner.GetInstanceID(),Existing=recorded,Unavailable=unavailable,AllowedMaterialPaths=allowedMaterialPaths,MonitorTranslations=translation==null?null:translation.Records(owner.transform)};
            using(var stream=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.Read))new DataContractJsonSerializer(typeof(Report)).WriteObject(stream,data);
            }catch(Exception error){UnityEngine.Debug.LogWarning("[WallHoleFaceDisplay] optional visual audit output unavailable: "+error.GetType().Name);}

        }
    }
}
