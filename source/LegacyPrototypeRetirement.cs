using System.Collections;
using HarmonyLib;
namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Retain FindItem/prefab resolution for old inventory and placed save records.
    // Only player-started placement is refused; loading/AddBuild/BuildMove are untouched.
    internal static class LegacyPrototypeRetirement
    {
        internal static bool IsRetired(string itemKey){return itemKey==Plugin.FacilityId;}
        private static IEnumerator NoPlacement(){yield break;}
        [HarmonyPatch(typeof(BuildManager),nameof(BuildManager.BuildStart))]
        private static class PreventNewPlacement
        {
            private static bool Prefix(int slotID,ManagersScript ___mn,ref IEnumerator __result)
            {
                var inventory=___mn==null?null:___mn.inventory;
                var slots=inventory==null?null:inventory.itemSlot;
                if(slots==null||slotID<0||slotID>=slots.Length||slots[slotID]==null||!IsRetired(slots[slotID].itemKey))return true;
                // Return a valid empty coroutine before vanilla sets nowBuild or consumes an item.
                __result=NoPlacement();return false;
            }
        }
    }
}
