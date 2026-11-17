using System;using System.Collections.Generic;using System.Linq;using System.Runtime.Serialization;using UnityEngine;
namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    internal sealed class MonitorMoveReceipt
    {
        internal sealed class Entry{internal Transform Transform;internal Mesh Mesh;internal Vector3[] Before;internal Bounds Bounds;internal Vector3 LocalDelta;}
        [DataContract]internal sealed class Record{[DataMember]public string Path;[DataMember]public float[] LocalDelta;[DataMember]public float[] WorldDelta;[DataMember]public float[] WallUp;[DataMember]public float[] BeforeVertices;[DataMember]public float[] AfterVertices;[DataMember]public bool ExactTranslation;}
        private readonly Entry[] entries;private readonly Vector3 worldDelta,wallUp;
        private MonitorMoveReceipt(Entry[] entries,Vector3 delta,Vector3 up){this.entries=entries;worldDelta=delta;wallUp=up;}
        internal static MonitorMoveReceipt Apply(Renderer frame,Transform parent,WoodPanelSurface surface,float originalWallHeight)
        {
            var up=surface.UpDirection.normalized;if(up.y<=0)throw new InvalidOperationException("Wall up is not upward");var mesh=frame.GetComponent<MeshFilter>().sharedMesh;
            if(mesh==null||!mesh.isReadable)throw new InvalidOperationException("Readable monitor mesh required");
            float highest=mesh.vertices.Max(v=>Vector3.Dot(frame.transform.TransformPoint(v),up));
            float wallTop=Vector3.Dot(surface.BaseCenter+up*surface.Height,up);
            float frameWidth=mesh.vertices.Max(v=>Vector3.Dot(frame.transform.TransformPoint(v),surface.RightDirection))-mesh.vertices.Min(v=>Vector3.Dot(frame.transform.TransformPoint(v),surface.RightDirection));
            float margin=Mathf.Max(0,(surface.Width-frameWidth)*.5f);
            float amount=Mathf.Max(0,wallTop-margin-highest);if(float.IsNaN(amount)||float.IsInfinity(amount))throw new InvalidOperationException("Nonfinite monitor move");if(amount==0)return null;
            var led=frame.transform.Find("PowerLed");if(led==null)throw new InvalidOperationException("Monitor LED missing");
            Vector3 delta=up*amount;
            var list=new List<Entry>();foreach(var transform in new[]{frame.transform,led}){
                var filter=transform.GetComponent<MeshFilter>();if(filter==null||filter.sharedMesh==null||!filter.sharedMesh.isReadable)throw new InvalidOperationException("Monitor part mesh missing");
                var part=filter.sharedMesh;list.Add(new Entry{Transform=transform,Mesh=part,Before=part.vertices,Bounds=part.bounds,LocalDelta=transform.InverseTransformVector(delta)});
            }
            var receipt=new MonitorMoveReceipt(list.ToArray(),delta,up);
            try{foreach(var entry in receipt.entries){entry.Mesh.vertices=entry.Before.Select(v=>v+entry.LocalDelta).ToArray();entry.Mesh.bounds=new Bounds(entry.Bounds.center+entry.LocalDelta,entry.Bounds.size);}receipt.Validate();return receipt;}catch{receipt.Restore();throw;}
        }
        internal bool TryGet(Transform transform,out Entry result){result=entries.FirstOrDefault(e=>e.Transform==transform);return result!=null;}
        internal void Validate()
        {
            if(worldDelta.y<=0||Vector3.Dot(worldDelta,wallUp)<=0||Vector3.Cross(worldDelta,wallUp).sqrMagnitude>1e-10f)throw new InvalidOperationException("Non-upward monitor move rejected");
            foreach(var entry in entries){var now=entry.Mesh.vertices;if(now.Length!=entry.Before.Length||!entry.Mesh.bounds.center.Equals(entry.Bounds.center+entry.LocalDelta)||!entry.Mesh.bounds.size.Equals(entry.Bounds.size))throw new InvalidOperationException("Monitor size/bounds changed");for(int i=0;i<now.Length;i++)if(!now[i].Equals(entry.Before[i]+entry.LocalDelta))throw new InvalidOperationException("Nonuniform monitor translation rejected");}
        }
        internal MonitorScreenGeometry Aperture(MonitorScreenGeometry original,Transform parent)
        {Validate();var delta=parent.InverseTransformVector(worldDelta);return new MonitorScreenGeometry(parent,original.CopyFor(parent).Select(v=>v+delta).ToArray(),0);}
        internal void Restore(){foreach(var entry in entries)if(entry.Mesh!=null){entry.Mesh.vertices=entry.Before;entry.Mesh.bounds=entry.Bounds;}}
        private static float[] Flat(IEnumerable<Vector3> values){return values.SelectMany(v=>new[]{v.x,v.y,v.z}).ToArray();}
        internal Record[] Records(Transform root)
        {Validate();return entries.Select(e=>new Record{Path=PathOf(e.Transform,root),LocalDelta=Flat(new[]{e.LocalDelta}),WorldDelta=Flat(new[]{worldDelta}),WallUp=Flat(new[]{wallUp}),BeforeVertices=Flat(e.Before),AfterVertices=Flat(e.Mesh.vertices),ExactTranslation=true}).ToArray();}
        private static string PathOf(Transform t,Transform root){var list=new List<string>();for(var current=t;current!=root;current=current.parent){if(current==null)throw new InvalidOperationException("Monitor outside owner");list.Add(current.name);}list.Reverse();return string.Join("/",list.ToArray());}
    }
}
