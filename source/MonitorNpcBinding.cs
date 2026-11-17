using System;
using BepInEx.Logging;
using UnityEngine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Read-only observation of one selected facility, including the live open-inventory slot.
    internal sealed class MonitorNpcBinding : IDisposable
    {
        private readonly ItemInfo owner;
        private readonly InventorySlot rack;
        private readonly ManagersScript managers;
        private readonly ManualLogSource log;
        private readonly int buildId;
        private readonly SexPlace usage;
        private bool usageKnown, wasInUse;
        private float nextCheck;
        private bool initialized, readError;
        private Spine.Unity.SkeletonAnimation lastAnimation;
        private Spine.Skeleton lastSkeleton;
        internal event Action<CommonStates> Changed;
        internal event Action<bool> UseChanged;
        internal CommonStates CurrentNpc { get; private set; }
        internal int? CurrentNpcId { get; private set; }
        internal bool HasSupportedNpc { get { return CurrentNpcId == 15; } }

        internal MonitorNpcBinding(ItemInfo owner, ManagersScript managers, ManualLogSource log)
        {
            this.owner = owner; this.managers = managers; this.log = log;
            buildId = owner.buildID;
            rack = owner.GetComponent<InventorySlot>();
            usage = owner.GetComponent<SexPlace>();
        }

        internal bool FacilityValid
        {
            get { return owner != null && owner.gameObject.activeInHierarchy &&
                MonitorFacilityIdentity.Supports(owner.itemKey) && owner.buildID == buildId; }
        }

        // Shared read path for existing binding and the one-second restoration scan.
        internal static bool TryReadAssignedNpc(InventorySlot rack,ManagersScript managers,out CommonStates npc)
        {
            npc=null;if(rack==null||rack.slots==null||rack.slots.Length==0||managers==null||managers.inventory==null)return false;
            var slot=managers.inventory.GetCurrentItemSlot(rack,0);
            if(slot!=null&&!string.IsNullOrEmpty(slot.itemKey))npc=slot.common;
            return true;
        }
        internal void Tick()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.2f;
            int? id = null;
            CommonStates observed = null;
            try
            {
                if(FacilityValid&&TryReadAssignedNpc(rack,managers,out observed)&&observed!=null)id=observed.npcID;
            }
            catch (Exception)
            {
                if (!readError) log.LogWarning("[MonitorBinding] slot read unavailable; display remains standby.");
                readError = true;
            }
            if (usage != null)
            {
                bool inUse = usage.user != null;
                if (usageKnown && wasInUse != inUse && UseChanged != null) UseChanged(inUse);
                usageKnown = true; wasInUse = inUse;
            }
            var animation = observed != null ? observed.anim : null;
            var skeleton = animation == null ? null : animation.Skeleton;
            if (initialized && CurrentNpcId == id && ReferenceEquals(CurrentNpc, observed) &&
                ReferenceEquals(lastAnimation, animation) && ReferenceEquals(lastSkeleton, skeleton)) return;
            MonitorPerformance.Count(MonitorPerfEvent.BindingChanged); /*WHFDPerfEvent*/
            initialized = true; CurrentNpcId = id; CurrentNpc = observed;
            lastAnimation = animation; lastSkeleton = skeleton;
            if(MonitorDebugSettings.Enabled)log.LogInfo("[MonitorBinding] buildID=" + buildId + "; npcId=" +
                (id.HasValue ? id.Value.ToString() : "none") + "; supported=" + HasSupportedNpc +
                "; pose=instance-snapshot; usageSource=" + (usage != null ? "SexPlace.user" : "unavailable"));
            try{if(observed!=null)if(MonitorDebugSettings.Enabled)log.LogInfo("[MonitorFramingAudit] route=binding; buildId="+buildId+"; ownerId="+owner.transform.GetInstanceID()+"; npcId="+observed.npcID+"; npcInstanceId="+observed.GetInstanceID()+"; animationId="+(animation==null?0:animation.GetInstanceID())+"; transformScale="+observed.transform.lossyScale+"; source="+(skeleton==null?"unavailable":MonitorTemplatePlayback.AuditPose(skeleton)));}catch(Exception e){log.LogWarning("[MonitorFramingAudit] binding diagnostic unavailable: "+e.GetType().Name);}
            if (Changed != null) Changed(observed);
        }

        public void Dispose()
        {
            CurrentNpcId = null; CurrentNpc = null; lastAnimation = null; lastSkeleton = null; initialized = false; Changed = null; UseChanged = null; usageKnown = false;
        }
    }
}
