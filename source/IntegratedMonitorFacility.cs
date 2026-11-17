using HarmonyLib;
using UnityEngine;
namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Cloned only onto the prototype placement prefab. No persistent save fields.
    internal sealed class IntegratedMonitorFacility : MonoBehaviour
    {
        private ItemInfo owner;
        private bool initialized;
        private float nextAttempt;
        private void OnEnable(){owner=GetComponent<ItemInfo>();initialized=false;nextAttempt=0;}
        private void LateUpdate()
        {
            if(initialized||Time.unscaledTime<nextAttempt)return;
            nextAttempt=Time.unscaledTime+0.25f;
            initialized=Plugin.EnsureIntegratedMonitor(owner);
        }
        private void OnDestroy(){if(!ReferenceEquals(owner,null))Plugin.ReleaseIntegratedMonitor(owner);}
    }
    [HarmonyPatch(typeof(BuildManager),nameof(BuildManager.AddBuild))]
    internal static class IntegratedMonitorBuildCompletion
    {
        private static void Postfix(ItemInfo tmpInfo)
        {if(tmpInfo!=null&&MonitorFacilityIdentity.IsFixed(tmpInfo.itemKey))Plugin.EnsureIntegratedMonitor(tmpInfo);}
    }
}
