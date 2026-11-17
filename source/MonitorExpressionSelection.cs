using System;
using Spine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Policy only. No guessed default, random cycle or body-animation interpretation.
    internal static class MonitorExpressionSelection
    {
        internal const int FacialTrack = 2; // StoryManager.Facial, verified in the current game assembly.

        internal static TrackEntry Read(AnimationState source, SkeletonData data, out string reason)
        {
            if (source == null || !ReferenceEquals(source.Data.SkeletonData, data))
            { reason = "source-state-unavailable"; return null; }
            var entry = source.GetCurrent(FacialTrack);
            if (entry == null || entry.Animation == null)
            { reason = "no-source-facial-selection"; return null; }
            string name = entry.Animation.Name;
            if (name != "Face_00" && name != "Face_01" && name != "Face_02" && name != "Face_03" && name != "Face_04")
            { reason = "no-supported-source-facial-selection"; return null; }
            // A copied pose already includes the source blend. Do not double-apply an
            // unknown additive/partial blend or infer a neutral pose during a transition.
            if (entry.Alpha != 1f || entry.MixBlend == MixBlend.Add ||
                (entry.MixingFrom != null && entry.MixTime < entry.MixDuration))
            { reason = "source-transition-or-partial-blend"; return null; }
            reason = "source-track-2";
            return entry;
        }
    }
}
