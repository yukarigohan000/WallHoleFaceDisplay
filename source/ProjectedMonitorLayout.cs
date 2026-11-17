using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    internal static class ProjectedMonitorLayout
    {
        // Latest user observation: 45 px clearance next to a 102 px high old frame.
        // Ratios, not fixed render-resolution pixel coordinates.
        internal const float ObservedGapRatio = 45f / 102f;
        internal const float DesiredGapRatio = 17.5f / 102f;

        internal static Rect Fit(Vector2[] panel, Rect reference, float inset)
        {
            if (panel == null || panel.Length != 4 || reference.width <= 0 || reference.height <= 0 || inset < 0 || inset >= 0.5f)
                throw new ArgumentException("Invalid projected monitor inputs");
            var center = Vector2.zero;
            foreach (var point in panel)
            {
                if (!Finite(point.x) || !Finite(point.y)) throw new ArgumentException("Nonfinite panel projection");
                center += point * 0.25f;
            }
            var inner = new Vector2[4];
            for (int i = 0; i < 4; i++) inner[i] = center + (panel[i] - center) * (1 - 2 * inset);
            float sign = 0;
            for (int i = 0; i < 4; i++)
            {
                float turn = Cross(inner[(i + 1) % 4] - inner[i], inner[(i + 2) % 4] - inner[(i + 1) % 4]);
                if (Mathf.Abs(turn) < 0.000001f || (sign != 0 && turn * sign <= 0))
                    throw new ArgumentException("Panel projection is not a usable convex quadrilateral");
                sign = turn;
            }
            float bottom = reference.yMin - (ObservedGapRatio - DesiredGapRatio) * reference.height;
            // Target 67.5% of projected wall width; only shrink for panel containment.
            float requested = (Vector2.Distance(panel[0], panel[1]) + Vector2.Distance(panel[3], panel[2])) * 0.5f * 0.675f;
            if (!Finite(bottom) || !Finite(requested)) throw new ArgumentException("Nonfinite reference rectangle");
            Rect result;
            if (Candidate(inner, bottom, requested, out result) && Contains(inner, result, sign)) return result;
            float low = 0, high = requested; Rect best = new Rect();
            for (int iteration = 0; iteration < 28; iteration++)
            {
                float side = (low + high) * 0.5f;
                if (Candidate(inner, bottom, side, out result) && Contains(inner, result, sign)) { low = side; best = result; }
                else high = side;
            }
            if (low <= requested * 0.0001f) throw new InvalidOperationException("No projected square fits above the calibrated head position");
            return best;
        }

        internal static Rect InnerScreen(Rect frame)
        {
            float side = frame.width * 0.91f;
            return new Rect(frame.center.x - side * 0.5f, frame.center.y - side * 0.5f, side, side);
        }
        private static bool Candidate(Vector2[] polygon, float bottom, float side, out Rect rectangle)
        {
            float y = bottom + side * 0.5f;
            var intersections = new List<float>();
            for (int i = 0; i < 4; i++)
            {
                var a = polygon[i]; var b = polygon[(i + 1) % 4];
                if (y < Mathf.Min(a.y, b.y) || y > Mathf.Max(a.y, b.y)) continue;
                if (Mathf.Abs(b.y - a.y) < 0.000001f) { intersections.Add(a.x); intersections.Add(b.x); }
                else intersections.Add(a.x + (b.x - a.x) * ((y - a.y) / (b.y - a.y)));
            }
            if (intersections.Count < 2) { rectangle = new Rect(); return false; }
            float min = intersections[0], max = min;
            foreach (float x in intersections) { min = Mathf.Min(min, x); max = Mathf.Max(max, x); }
            rectangle = new Rect((min + max - side) * 0.5f, bottom, side, side);
            return true;
        }
        private static bool Contains(Vector2[] polygon, Rect rectangle, float sign)
        {
            foreach (var point in new[] { new Vector2(rectangle.xMin, rectangle.yMin), new Vector2(rectangle.xMax, rectangle.yMin),
                new Vector2(rectangle.xMax, rectangle.yMax), new Vector2(rectangle.xMin, rectangle.yMax) })
                for (int i = 0; i < 4; i++)
                {
                    var edge = polygon[(i + 1) % 4] - polygon[i];
                    float cross = Cross(edge, point - polygon[i]);
                    if (cross * (sign > 0 ? 1 : -1) < -edge.sqrMagnitude * 0.0000001f) return false;
                }
            return true;
        }
        private static float Cross(Vector2 a, Vector2 b) { return a.x * b.y - a.y * b.x; }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
