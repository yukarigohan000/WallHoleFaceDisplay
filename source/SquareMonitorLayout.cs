using System;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    internal struct SquareMonitorLayout
    {
        internal float Side, Bottom, CenterHeight;

        internal static SquareMonitorLayout Calculate(float width, float height, float requestedSide,
            float bottomRatio, float insetRatio)
        {
            foreach (float value in new[] { width, height, requestedSide, bottomRatio, insetRatio })
                if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("Monitor dimensions must be finite");
            if (width <= 0 || height <= 0 || requestedSide <= 0 || bottomRatio < 0 || bottomRatio >= 1 ||
                insetRatio < 0 || insetRatio >= 0.5f)
                throw new ArgumentException("Invalid square monitor layout");
            float bottom = height * bottomRatio;
            float availableWidth = width * (1 - 2 * insetRatio);
            float availableHeight = height * (1 - insetRatio) - bottom;
            float side = Math.Min(requestedSide, Math.Min(availableWidth, availableHeight));
            if (side <= 0) throw new ArgumentException("No square fits above the fixed monitor bottom");
            return new SquareMonitorLayout { Side = side, Bottom = bottom, CenterHeight = bottom + side * 0.5f };
        }
    }
}
