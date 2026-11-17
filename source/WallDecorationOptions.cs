using BepInEx.Configuration;
using UnityEngine;
namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    internal sealed class WallDecorationOptions
    {
        internal readonly ConfigEntry<KeyboardShortcut> toggle, clear, compare;
        internal readonly ConfigEntry<float> range, heightRatio, minimumHeightRatio, monitorWidth, monitorHeight, depth;
        internal readonly ConfigEntry<float> monitorBottomRatio, monitorInsetRatio;
        internal readonly ConfigEntry<string> wallRendererPath;
        internal readonly ConfigEntry<string> monitorFrameColor, monitorStandbyColor;
        internal readonly ConfigEntry<int> orderOffset;
        internal readonly ConfigEntry<bool> layoutDiagnostics;
        internal readonly ConfigEntry<FaceDepthMode> faceDepthMode;
        internal readonly ConfigEntry<MonitorDisplayMode> displayMode;
        internal readonly ConfigEntry<string>[] playbackPoses = new ConfigEntry<string>[4];
        internal readonly ConfigEntry<string>[] playbackFaces = new ConfigEntry<string>[4];
        internal readonly ConfigEntry<float> playbackBlend;
        internal WallDecorationOptions(ConfigFile config)
        {
            toggle = config.Bind("Decoration", "ToggleNearest", new KeyboardShortcut(KeyCode.F10, KeyCode.LeftControl, KeyCode.LeftAlt), "Toggle monitor on nearest original facility; independent per placed instance.");
            clear = config.Bind("Decoration", "Clear", new KeyboardShortcut(KeyCode.F11, KeyCode.LeftControl, KeyCode.LeftAlt), "Remove only the nearest original facility monitor.");
            compare = config.Bind("Decoration", "CompareRendering", new KeyboardShortcut(KeyCode.F9, KeyCode.LeftControl, KeyCode.LeftAlt), "Cycle owned panel comparisons: hidden, sorting, normals, shadow off, two-sided, baseline. Source wall unchanged.");
            range = config.Bind("Decoration", "SelectionRadius", 5f, new ConfigDescription("Maximum player distance in world units.", new AcceptableValueRange<float>(0.1f, 100f)));
            heightRatio = config.Bind("Decoration", "ExtensionHeightRatio", 0.9f, new ConfigDescription("Panel height / measured original wall height.", new AcceptableValueRange<float>(0.1f, 2f)));
            minimumHeightRatio = config.Bind("Decoration", "MinimumExtensionHeightRatio", 0.9f, new ConfigDescription("Minimum ratio also applied to old 0.625 settings.", new AcceptableValueRange<float>(0.1f, 2f)));
            wallRendererPath = config.Bind("Decoration", "WallRendererPath", "", "Original static wall renderer path relative to facility root. Empty requires a unique static wall candidate.");
            monitorWidth = config.Bind("Decoration", "MonitorWidth", 0.55f, new ConfigDescription("Requested square side; larger of MonitorWidth/MonitorHeight is used, then shrunk to fit.", new AcceptableValueRange<float>(0.05f, 10f)));
            monitorHeight = config.Bind("Decoration", "MonitorHeight", 0.55f, new ConfigDescription("Requested square side; outer frame and screen remain square.", new AcceptableValueRange<float>(0.05f, 10f)));
            monitorBottomRatio = config.Bind("Decoration", "MonitorBottomRatio", 0.47f, new ConfigDescription("Fixed monitor bottom above original wall top / extension height. No NPC tracking.", new AcceptableValueRange<float>(0f, 0.95f)));
            monitorInsetRatio = config.Bind("Decoration", "MonitorInsetRatio", 0.04f, new ConfigDescription("Minimum side/top inset relative to extension width/height.", new AcceptableValueRange<float>(0f, 0.25f)));
            monitorFrameColor = config.Bind("MonitorAppearance", "FrameColor", "#181818", "Matte black bezel face, sRGB #RRGGBB; casing/edge/lip tones derive from this tint. Old shipped defaults use the new black finish; custom colors are preserved.");
            monitorStandbyColor = config.Bind("MonitorAppearance", "StandbyColor", "#000000", "Always-on black background, sRGB #RRGGBB. Legacy shipped #06101C uses the new default in memory; custom colors are preserved. No NPC detection.");
            orderOffset = config.Bind("Decoration", "SortingOrderOffset", 1, new ConfigDescription("Decoration order relative to authored renderer; frame +1, screen +2.", new AcceptableValueRange<int>(-1000, 1000)));
            depth = config.Bind("Decoration", "LocalDepthOffset", -0.01f, new ConfigDescription("Depth offset along authored display axis.", new AcceptableValueRange<float>(-1f, 1f)));
            displayMode = config.Bind("MonitorRendering", "DisplayMode", MonitorDisplayMode.RestraintVideo, "RestraintVideo: fixed ID15 gen_restrain_01 pose rendered only while facility is in use. LegacyFace restores v0.2.23. Restart/re-add to apply.");
            faceDepthMode = config.Bind("MonitorRendering", "FaceDepthMode", FaceDepthMode.PreserveRelative, "Compare Flatten (v0.2.21) and PreserveRelative (negative source Z toward viewer, uniformly scaled to casing cavity). Restart and reattach to apply; no face-mask changes.");
            layoutDiagnostics = config.Bind("Decoration", "LayoutDiagnostics", false, "Log only the selected wall/panel at attachment and once 0.25 seconds later; no NPC data.");
            // Exact names come from the target's actual SkeletonData; invalid names fall back
            // to the already verified fixed playback. Empty values are also safe fallback.
            string[] names = {"Standby", "Normal", "Fast", "Finish"};
            for (int i = 0; i < names.Length; i++) {
                playbackPoses[i] = config.Bind("MonitorStatePlayback", names[i] + "Pose",
                    MonitorStandardDefaults.Playback(names[i]+"Pose",RestraintVideoRecipe.Pose), "Existing target SkeletonData animation name for track 0.");
                playbackFaces[i] = config.Bind("MonitorStatePlayback", names[i] + "Face",
                    MonitorStandardDefaults.Playback(names[i]+"Face",RestraintVideoRecipe.Expression), "Existing target SkeletonData animation name for track 2.");
            }
            playbackBlend = config.Bind("MonitorStatePlayback", "BlendSeconds", MonitorStandardDefaults.PlaybackBlend(0.2f),
                new ConfigDescription("Blend time for private monitor-only track changes.",new AcceptableValueRange<float>(0f,1f)));
        }
    }
}
