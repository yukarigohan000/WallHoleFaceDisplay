namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    internal static class MonitorPlaybackMode
    {
        internal static int FromSnapshot(MonitorStateSnapshot observed,int npcId)
{
            if (observed == null || observed.NpcId != npcId || observed.NpcSet != true) return -1;
            if (observed.InUse == false) return 0;
            if (observed.InUse != true || observed.SharedMatchesOwner != true ||
                observed.Reason != "MatchedFacility-Scale/Anim") return -1;
            switch (observed.State) {
                case "A_Loop_01": return 1;
                case "A_Loop_02": return 2;
                case "A_Finish": return 3;
                case "A_Finish_idle":
                case "Other": // Only the verified A_idle is a standby state.
                    return observed.State == "A_Finish_idle" || observed.Animation == "A_idle" ||
                        observed.Animation == npcId.ToString(System.Globalization.CultureInfo.InvariantCulture)+"_A_idle" ? 0 : -1;
                default: return -1;
            }
        }
    }
}
