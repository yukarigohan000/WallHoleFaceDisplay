using System;
using UnityEngine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    internal enum FaceDepthMode { Flatten, PreserveRelative }

    // Depth only: input XY/UV/triangles/pose and the original projection rays are unchanged.
    internal static class FaceDepthTransform
    {
        internal static float Scale(float sourceToLocalScale, float minimumZ, float maximumZ, float cavityDepth)
        {
            if (!Finite(sourceToLocalScale) || sourceToLocalScale <= 0 || !Finite(minimumZ) ||
                !Finite(maximumZ) || maximumZ < minimumZ || !Finite(cavityDepth) || cavityDepth <= 0)
                throw new ArgumentException("Invalid face depth inputs");
            float span = maximumZ - minimumZ;
            return span == 0 ? 0 : Mathf.Min(sourceToLocalScale, cavityDepth / span);
        }

        internal static float Offset(float z, float backmostZ, float scale)
        {
            // Spine's negative Z is forward. Positive offset is toward the viewer.
            return (backmostZ - z) * scale;
        }

        internal static Vector3 Project(Plane baseline, Ray ray, float offset)
        {
            // Move parallel planes along their viewer-facing normal. Keeping the same
            // ray maintains the attachment-view screen coordinates at every depth.
            var plane = offset == 0 ? baseline : new Plane(baseline.normal, baseline.distance - offset);
            float distance;
            if (!plane.Raycast(ray, out distance)) throw new InvalidOperationException("Face depth plane not visible");
            return ray.GetPoint(distance);
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
