using System;
using UnityEngine;
namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    internal static class MonitorVertexRange
    {
        internal static void ZBounds(Vector3[] vertices,int count,out float min,out float max)
        {
            if(vertices==null||count<=0||count>vertices.Length)throw new ArgumentException("Invalid active vertex range");
            min=max=vertices[0].z;for(int i=1;i<count;i++){float z=vertices[i].z;if(z<min)min=z;if(z>max)max=z;}
        }
    }
}
