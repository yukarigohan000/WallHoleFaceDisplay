using System;
using System.Collections.Generic;
using Spine;
namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Read-only projection of RandomCharacter.LoadGenUnder's parameters[7] rule.
    // Names come from the verified v0.6 GetLayeredSlots("blood") API; no NPC state is changed.
    internal sealed class MonitorFacilityBlood
    {
        private readonly Slot[] slots;
        private readonly Func<int?> readState;
        internal MonitorFacilityBlood(Skeleton pose,string[] nativeSlots,Func<int?> readState)
        {
            if(pose==null||nativeSlots==null||readState==null)throw new InvalidOperationException("Native blood rule unavailable");
            var found=new List<Slot>();foreach(var name in nativeSlots){var slot=pose.FindSlot(name);if(slot!=null)found.Add(slot);}slots=found.ToArray();this.readState=readState;
        }
        internal void Apply()
        {
            var state=readState();if(!state.HasValue)throw new InvalidOperationException("ID44 blood state unavailable; frame retained");
            // Nonzero leaves the restored authored appearance and animation's result intact,
            // as vanilla does. Zero must also win over attachment animation timelines.
            if(state.Value==0)foreach(var slot in slots)if(slot.Attachment!=null)slot.Attachment=null;
        }
    }
}
