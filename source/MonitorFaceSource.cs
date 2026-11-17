using System;
using System.Linq;
using BepInEx.Logging;
using Spine.Unity;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Inspect the original ID 15 restraint data only; never instantiate its gameplay prefab.
    // Selection/rendering are deliberately not inferred from the editor's numbered labels.
    internal static class MonitorFaceSource
    {
        internal static SkeletonAnimation ResolveInstance(CommonStates npc)
        {
            if (npc == null || npc.npcID != 15 || npc.anim == null || npc.anim.Skeleton == null)
                throw new InvalidOperationException("Target instance Skeleton not ready");
            return npc.anim;
        }
        internal static SkeletonAnimation Resolve(ManagersScript managers)
        {
            var prefab = managers.sexMN.sexList[5].sexObj[0];
            var sources = prefab.GetComponentsInChildren<SkeletonAnimation>(true);
            if (sources.Length != 1 || sources[0].skeletonDataAsset == null)
                throw new InvalidOperationException("ID 15 restraint skeleton is not unique or ready");
            return sources[0];
        }
        internal static void Report(ManagersScript managers, ManualLogSource log)
        {
            try
            {
                // RandomCharacter.CreateRestrain: npcID 15 -> sexList[5].sexObj[0].
                var prefab = managers.sexMN.sexList[5].sexObj[0];
                var sources = prefab.GetComponentsInChildren<SkeletonAnimation>(true);
                if (sources.Length != 1)
                {
                    log.LogWarning("[MonitorFaceSource] npcId=15; skeletonCandidates=" + sources.Length +
                        "; need the authored facial skeleton path; no display created.");
                    return;
                }
                var asset = sources[0].skeletonDataAsset;
                var data = asset == null ? null : asset.GetSkeletonData(false);
                if (data == null) throw new InvalidOperationException("Skeleton data unavailable");
                string[] headSlots = { "Head", "Head2", "Eye", "EyeWhite", "EyeBrow", "Face", "Mouth",
                    "Nose", "HairFront", "HairBack", "Ear", "EarBack", "Tears", "Drool", "Droll" };
                var available = headSlots.Where(name => data.FindSlot(name) != null).ToArray();
                log.LogInfo("[MonitorFaceSource] npcId=15; knownHeadSlots=" + string.Join(",", available) +
                    "; totalSlots=" + data.Slots.Count + "; materialPages=" + asset.atlasAssets.Length);
                var expressions = data.Animations.Where(animation =>
                    animation.Name.StartsWith("Face_", StringComparison.Ordinal)).Take(16).ToArray();
                log.LogInfo("[MonitorFaceSource] npcId=15; facialAnimations=" +
                    string.Join(",", expressions.Select(animation => animation.Name).ToArray()));
                foreach (var animation in expressions)
                {
                    var targets = animation.Timelines.Select(timeline =>
                    {
                        var type = timeline.GetType();
                        var property = type.GetProperty("SlotIndex") ?? type.GetProperty("BoneIndex");
                        return type.Name + (property == null ? "" : ":" + property.Name + "=" + property.GetValue(timeline, null));
                    }).Distinct().Take(32).ToArray();
                    log.LogInfo("[MonitorFaceSource] npcId=15; animation=" + animation.Name +
                        "; targets=" + string.Join(",", targets));
                }
                log.LogInfo("[MonitorFaceSource] npcId=15; metadata-only; need verified head mask and face playback track/default before rendering.");
            }
            catch (Exception error)
            {
                log.LogWarning("[MonitorFaceSource] npcId=15; metadata unavailable: " + error.GetType().Name +
                    "; no gameplay object instantiated or modified.");
            }
        }
    }
}
