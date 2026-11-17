using System.Collections.Generic;
using UnityEngine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Joined casing sides, edge chamfer, matte front and inner lip. Screen plane stays fixed.
    internal sealed class MonitorBezelSurface
    {
        internal readonly List<Vector2> Points = new List<Vector2>();
        internal readonly List<float> Relief = new List<float>();
        internal readonly List<Color> SrgbColors = new List<Color>();
        internal readonly List<int> Triangles = new List<int>();
        internal int ScreenVertexStart {get;private set;}
        internal int LedVertexStart = -1; // One independent quad, excluded from body triangles.
        internal readonly List<int> LedTriangles = new List<int>();

        internal static MonitorBezelSurface Create(Rect frame, Rect screen, Color tint, Color standby)
        {
            var result = new MonitorBezelSurface();
            // Partition the same thin border; the outer envelope and display aperture do not move.
            float[] fraction = { 0f, 0.28f, 0.38f, 0.80f, 1f };
            // Screen reference plane = 0. Rear is behind that plane but remains 0.6%
            // wall width above the wood (base stand-off is 1%). Total shell depth = 1%.
            float[] relief = { -0.004f, 0.005f, 0.006f, 0.006f, 0f };
            var contours = new Vector2[fraction.Length][];
            for (int ring = 0; ring < fraction.Length; ring++)
            {
                float f = fraction[ring];
                float left = Mathf.Lerp(frame.xMin, screen.xMin, f);
                float right = Mathf.Lerp(frame.xMax, screen.xMax, f);
                float bottom = Mathf.Lerp(frame.yMin, screen.yMin, f);
                float top = Mathf.Lerp(frame.yMax, screen.yMax, f);
                contours[ring] = new[] { new Vector2(left, bottom), new Vector2(right, bottom),
                    new Vector2(right, top), new Vector2(left, top) };
            }
            // Bottom/right are slightly shaded; top/left gently catch the light.
            float[] edgeShade = { 1.08f, 1.04f, 0.80f, 0.86f };
            // Default face #181818; side #272727, edge #303030, inner lip #0C0C0C.
            float[] tone = { 0.42f, 0.72f, 0.88f, 0.35f };
            for (int ring = 0; ring < fraction.Length - 1; ring++) for (int edge = 0; edge < 4; edge++)
            {
                int next = (edge + 1) % 4;
                float factor = tone[ring] * (ring == 2 ? 1f : edgeShade[edge]);
                var color = new Color(Mathf.Clamp01(tint.r * factor), Mathf.Clamp01(tint.g * factor),
                    Mathf.Clamp01(tint.b * factor), 1f);
                if (ring == 2 && edge == 0)
                {
                    result.BottomDetails(contours[ring], contours[ring + 1], relief[ring], color, frame.width);
                    continue;
                }
                result.Quad(contours[ring][edge], contours[ring][next], contours[ring + 1][edge],
                    contours[ring + 1][next], relief[ring], relief[ring + 1], color);
            }
            var inner = contours[contours.Length - 1];
            result.ScreenVertexStart=result.Points.Count;
            result.Quad(inner[0], inner[1], inner[3], inner[2], 0f, 0f, standby);
            return result;
        }

        private void BottomDetails(Vector2[] outer, Vector2[] inner, float depth, Color baseColor, float side)
        {
            float centerX = (outer[0].x + outer[1].x) * 0.5f;
            float centerY = (outer[0].y + inner[0].y) * 0.5f;
            var slots = new List<Vector2>();
            for (int i = -1; i <= 1; i++)
                slots.Add(new Vector2(centerX + side * (i * 0.022f - 0.0035f), centerX + side * (i * 0.022f + 0.0035f)));
            slots.Add(new Vector2(centerX + side * 0.363f, centerX + side * 0.377f));
            float[] ys = { outer[0].y, centerY - side * 0.00175f, centerY + side * 0.00175f, inner[0].y };
            for (int row = 0; row < 3; row++)
            {
                float t0 = (ys[row] - outer[0].y) / (inner[0].y - outer[0].y);
                float t1 = (ys[row + 1] - outer[0].y) / (inner[0].y - outer[0].y);
                var xs0 = new List<float> { Mathf.Lerp(outer[0].x, inner[0].x, t0) };
                var xs1 = new List<float> { Mathf.Lerp(outer[0].x, inner[0].x, t1) };
                foreach (var slot in slots) { xs0.Add(slot.x); xs0.Add(slot.y); xs1.Add(slot.x); xs1.Add(slot.y); }
                xs0.Add(Mathf.Lerp(outer[1].x, inner[1].x, t0));
                xs1.Add(Mathf.Lerp(outer[1].x, inner[1].x, t1));
                for (int col = 0; col < xs0.Count - 1; col++)
                {
                    bool mark = row == 1 && col % 2 == 1;
                    bool led = mark && col == 7;
                    var color = led ? new Color(0.24f, 0.40f, 0.28f, 1f) : mark ? new Color(0.105f, 0.105f, 0.105f, 1f) : baseColor;
                    int first = Points.Count, triangleStart = Triangles.Count;
                    Quad(new Vector2(xs0[col], ys[row]), new Vector2(xs0[col + 1], ys[row]),
                        new Vector2(xs1[col], ys[row + 1]), new Vector2(xs1[col + 1], ys[row + 1]), depth, depth, color);
                    if (led)
                    {
                        LedVertexStart = first;
                        LedTriangles.AddRange(Triangles.GetRange(triangleStart, 6));
                        Triangles.RemoveRange(triangleStart, 6);
                    }
                }
            }
        }

        private void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float outerRelief, float innerRelief, Color color)
        {
            int n = Points.Count;
            Points.AddRange(new[] { a, b, c, d });
            Relief.AddRange(new[] { outerRelief, outerRelief, innerRelief, innerRelief });
            for (int i = 0; i < 4; i++) SrgbColors.Add(color);
            Triangles.AddRange(new[] { n, n + 2, n + 1, n + 1, n + 2, n + 3 });
        }
    }
}
