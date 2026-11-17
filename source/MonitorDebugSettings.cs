using BepInEx.Configuration;
using System;
namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Sampled at plugin startup: changing the setting requires a game restart.
    internal static class MonitorDebugSettings
    {
        internal static bool Enabled {get;private set;}
        internal static void Initialize(ConfigFile config,Action<string> warning=null)
        {
            Enabled=false;
            try{Enabled=config.Bind("MonitorDebug","EnableDebugUI",false,
                "Enable existing developer UI (LeftCtrl+LeftAlt+F6), F9 rendering comparison and detailed diagnostics. Requires restart. Normal monitor playback and F10/F11 operations remain available.").Value;}
            catch(Exception error){warning?.Invoke("Optional debug configuration unavailable; debug UI disabled: "+error.GetType().Name);}
        }
    }
}
