using System;
using UnityEngine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Immutable aperture in the SAME local frame as the casing mesh. Captured from
    // its actual black quad, never reconstructed from old screen pixels later.
    internal sealed class MonitorScreenGeometry
    {
        private readonly Transform owner;
        private readonly Vector3[] corners;
        internal MonitorScreenGeometry(Transform owner,Vector3[] vertices,int first)
        {
            if(owner==null||vertices==null||first<0||first>vertices.Length-4)throw new ArgumentException("Monitor aperture unavailable");
            this.owner=owner;corners=new Vector3[4];
            for(int i=0;i<4;i++){
                var p=vertices[first+i];
                if(!Finite(p.x)||!Finite(p.y)||!Finite(p.z))throw new ArgumentException("Nonfinite monitor aperture");corners[i]=p;
            }
        }
        internal Vector3[] CopyFor(Transform requestedOwner)
        {
            if(!ReferenceEquals(owner,requestedOwner)||owner==null)throw new InvalidOperationException("Monitor aperture owner mismatch");
            return (Vector3[])corners.Clone();
        }
        private static bool Finite(float v){return !float.IsNaN(v)&&!float.IsInfinity(v);}
    }
}
