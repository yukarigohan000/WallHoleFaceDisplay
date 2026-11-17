using System;
using BepInEx.Logging;
using Spine.Unity;
using UnityEngine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Finite observation window and transition deduplication; no animation control.
    internal sealed class MonitorStateTraceWindow
    {
        internal bool Running {get;private set;}
        internal double Until {get;private set;}
        private long lastFrame=-1;
        private string lastSignature;
        internal void Begin(double now) {Running=true;Until=now+30;lastFrame=-1;lastSignature=null;}
        internal void Stop() {Running=false;lastSignature=null;}
        internal bool TryFrame(long frame,double now,out bool ended)
        {
            ended=false;
            if(!Running) return false;
            if(now>=Until) {Stop();ended=true;return false;}
            if(frame<=lastFrame) return false;
            lastFrame=frame;return true;
        }
        internal bool Changed(string signature)
        {
            if(lastSignature==signature) return false;
            lastSignature=signature;return true;
        }
        internal static string StateSuffix(string animation)
        {
            if(animation==null) return "Unknown";
            foreach(var suffix in new[]{"A_Loop_01","A_Loop_02","A_Finish_idle","A_Finish"})
                if(animation.EndsWith(suffix,StringComparison.Ordinal)) return suffix;
            return "Other";
        }
    }
    internal sealed class MonitorStateSnapshot
    {
        internal int? NpcId;
        internal bool? NpcSet, InUse, SharedRefPresent, SharedMatchesOwner;
        internal string Animation="Unknown", State="Unknown", Reason="Not sampled";
        internal int Frame;
        internal string Signature {get {return "npcId="+(NpcId.HasValue?NpcId.Value.ToString():"none")+
            "; set="+NpcSet+"; inUse="+InUse+"; sharedPresent="+SharedRefPresent+"; ownerMatch="+SharedMatchesOwner+
            "; animation="+Animation+"; state="+State+"; source="+Reason;}}
    }
    // One selected facility only. Holds no Skeleton, TrackEntry or shared animation ref.
    internal sealed class MonitorStateDiagnostics : IDisposable
    {
        private readonly ItemInfo owner;
        private readonly ManagersScript managers;
        private readonly ManualLogSource log;
        private readonly int buildId;
        private readonly MonitorStateTraceWindow window=new MonitorStateTraceWindow();
        private bool disposed;
        internal MonitorStateSnapshot Snapshot {get;private set;}=new MonitorStateSnapshot();
        internal bool Running {get {return window.Running;}}
        internal float Remaining {get {return Running?Mathf.Max(0,(float)(window.Until-Time.unscaledTime)):0;}}
        internal MonitorStateDiagnostics(ItemInfo owner,ManagersScript managers,ManualLogSource log)
        {this.owner=owner;this.managers=managers;this.log=log;buildId=owner.buildID;}
        internal void Begin()
        {
            if(disposed) return;
            window.Begin(Time.unscaledTime);
            log.LogInfo("[MonitorState] buildID="+buildId+"; capture=begin; seconds=30; readOnly=true");Tick();
        }
        internal void Stop(string reason)
        {
            if(!Running) return;
            window.Stop();log.LogInfo("[MonitorState] buildID="+buildId+"; capture=end; reason="+reason);
        }
        internal void Tick()
        {
            if(disposed) return;
            bool ended;
            if(!window.TryFrame(Time.frameCount,Time.unscaledTime,out ended)) {
                if(ended) log.LogInfo("[MonitorState] buildID="+buildId+"; capture=end; reason=timeout");
                return;
            }
            Refresh();
            if(window.Changed(Snapshot.Signature)) log.LogInfo("[MonitorState] buildID="+buildId+"; frame="+Snapshot.Frame+"; "+Snapshot.Signature);
        }
        internal void Refresh()
        {
            if(disposed) return;
            var value=new MonitorStateSnapshot {Frame=Time.frameCount};Snapshot=value;
            try {
                if(owner==null || !owner.gameObject.activeInHierarchy || !MonitorFacilityIdentity.Supports(owner.itemKey) || owner.buildID!=buildId) {value.Reason="FacilityUnavailable";return;}
                if(managers==null || managers.inventory==null) {value.Reason="InventoryUnavailable";return;}
                var rack=owner.GetComponent<InventorySlot>();
                if(rack==null || rack.slots==null || rack.slots.Length==0) {value.Reason="InventoryUnavailable";return;}
                var slot=managers.inventory.GetCurrentItemSlot(rack,0);
                var npc=slot!=null && !string.IsNullOrEmpty(slot.itemKey)?slot.common:null;
                value.NpcSet=npc!=null;if(npc!=null) value.NpcId=npc.npcID;
                var usage=owner.GetComponent<SexPlace>();
                value.InUse=usage!=null?(bool?)(usage.user!=null):null;
                if(managers.sexMN==null) {value.Reason="SharedSourceUnavailable";return;}
                var shared=managers.sexMN.tmpCommonAnim;
                var node=owner.transform.Find("Scale/Anim");
                var expected=node==null?null:node.GetComponent<SkeletonAnimation>();
                value.SharedRefPresent=shared!=null;
                value.SharedMatchesOwner=expected==null?(bool?)null:shared==expected;
                // Inspect only pointer identity when inactive/mismatched; never read its track.
                if(usage==null) {value.Reason="UsageUnavailable";return;}
                if(usage.user==null) {value.Reason="NotInUse-source-rejected";return;}
                if(npc==null) {value.Reason="NoNpc-source-rejected";return;}
                if(managers.gameMN==null || usage.user!=managers.gameMN.player) {value.Reason="UserMismatch";return;}
                if(value.SharedMatchesOwner!=true) {value.Reason="FacilitySourceMismatch";return;}
                if(managers.sexMN.tmpCommonGirl!=npc) {value.Reason="NpcInstanceMismatch";return;}
                if(shared.state==null) {value.Reason="AnimationStateUnavailable";return;}
                var entry=shared.state.GetCurrent(0);
                if(entry==null || entry.Animation==null) {value.Reason="Track0Unavailable";return;}
                if(string.IsNullOrEmpty(entry.Animation.Name)) {value.Reason="AnimationNameUnavailable";return;}
                value.Animation=entry.Animation.Name;
                value.State=MonitorStateTraceWindow.StateSuffix(value.Animation);
                value.Reason="MatchedFacility-Scale/Anim";
            } catch(Exception error) {value.Animation="Unknown";value.State="Unknown";value.Reason="ReadFailure:"+error.GetType().Name;}
        }
        public void Dispose() {Stop("facility-detach");disposed=true;Snapshot=new MonitorStateSnapshot {Reason="Disposed"};}
    }
}
