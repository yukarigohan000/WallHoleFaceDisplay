namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // ItemInfo.itemKey only; no GameObject-name or legacy prototype alias.
    internal static class MonitorFacilityIdentity
    {
        internal static bool IsFixed(string itemKey){return itemKey==PrototypeItemRegistration.Id;}
        internal static bool Supports(string itemKey){return itemKey==PrototypeItemRegistration.Id;}
    }
}
