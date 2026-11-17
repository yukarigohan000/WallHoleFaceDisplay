using System;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // One UI/config owner, with independent instance-owned attachments and video resources.
    internal sealed class WallDecoration : IDisposable
    {
        private readonly ManualLogSource log;
        private readonly WallDecorationOptions options;
        private readonly MonitorFramingDebugUI framingUI;
        private readonly MonitorAttachmentRegistry<ItemInfo,WallMonitorAttachment> monitors=new MonitorAttachmentRegistry<ItemInfo,WallMonitorAttachment>();
        private WallMonitorAttachment selected;
        private ManagersScript managers;
        private bool disposed;
        /*WHFDPerfOnlyBegin*/
        private Vector3 perfPreviousPosition;private bool perfHadPlayer;
        private void SamplePerformance()
        {
            if(!MonitorPerformance.Enabled)return;
            var value=new MonitorPerfContext{Monitors=monitors.Count,Motion=-1};
            var mn=Managers();bool hasPlayer=mn!=null&&mn.gameMN!=null&&mn.gameMN.player!=null;var position=hasPlayer?mn.gameMN.player.transform.position:Vector3.zero;
            if(hasPlayer&&perfHadPlayer)value.Motion=(position-perfPreviousPosition).sqrMagnitude>0.000001f?1:0;
            perfHadPlayer=hasPlayer;perfPreviousPosition=position;float radius=options.range.Value;
            foreach(var pair in monitors){var monitor=pair.Value;
                if(hasPlayer&&monitor.Owner!=null&&(monitor.Owner.transform.position-position).sqrMagnitude<=radius*radius)value.Near++;
                var display=monitor.Display;if(display==null)continue;
                if(display.PerformanceVisible)value.Visible++;if(display.VideoReady)value.Ready++;
                if(monitor.PerformanceNpcId.HasValue){value.Assigned++;int id=monitor.PerformanceNpcId.Value;if(id==15)value.N15++;else if(id==16)value.N16++;else if(id==17)value.N17++;}
            }
            MonitorPerformance.BeginFrame(Time.frameCount,value,Time.unscaledDeltaTime*1000d);
        }
        /*WHFDPerfOnlyEnd*/
        private readonly MonitorPlacementSettings placements=new MonitorPlacementSettings(System.IO.Path.Combine(BepInEx.Paths.ConfigPath,"WallHoleFaceDisplay.monitors.json"));
        private readonly System.Collections.Generic.HashSet<int> restoreErrors=new System.Collections.Generic.HashSet<int>();
        private float nextRestore;
        private readonly System.Collections.Generic.Dictionary<ItemInfo,CommonStates> manualOff=new System.Collections.Generic.Dictionary<ItemInfo,CommonStates>();
        private void SuppressAutomaticInstall(ItemInfo target)
        {
            CommonStates npc=null;bool known=false;
            try{known=MonitorNpcBinding.TryReadAssignedNpc(target.GetComponent<InventorySlot>(),Managers(),out npc);}catch{}
            if(known&&npc==null)manualOff.Remove(target);else manualOff[target]=npc;
        }
        private MonitorPlacement Placement(ItemInfo target)
        {
            var mn=Managers();if(mn==null||mn.save==null||mn.save.currentLoadID<0||target==null||target.buildID<0)return null;
            var scene=target.gameObject.scene;string sceneKey=string.IsNullOrEmpty(scene.path)?scene.name:scene.path;
            if(string.IsNullOrEmpty(sceneKey))return null;
            var p=target.transform.position;return new MonitorPlacement{Context=mn.save.currentLoadID+"|"+sceneKey,BuildId=target.buildID,X=p.x,Y=p.y,Z=p.z};
        }
        private void Remember(ItemInfo target,bool enabled)
        {
            var value=Placement(target);if(value==null){log.LogWarning("[MonitorAuto] 設置記録は保存データ読込後のみ利用できます。現在のモニターは維持します。");return;}
            try{placements.Set(value,enabled);}catch(Exception e){log.LogWarning("[MonitorAuto] 設置記録の保存失敗（モニターは維持）: "+e.Message);}
        }
        private WallMonitorAttachment Add(ItemInfo target)
        {
            if(target==null||!MonitorFacilityIdentity.Supports(target.itemKey))throw new InvalidOperationException("Facility is not a monitor target");
            if(target.transform.Find("WHFD_WallMonitorPrototype")!=null)throw new InvalidOperationException("Unowned attachment marker exists");
            var mn=Managers();var source=mn==null||mn.itemMN==null?null:mn.itemMN.FindItem(Plugin.SourceId);
            if(source==null||source.itemObj==null)throw new InvalidOperationException("Source prefab unavailable");
            var added=new WallMonitorAttachment(options,log,framingUI);
            try{added.Attach(target,source.itemObj,mn);monitors.Add(target,added);}
            catch{added.Dispose();throw;}return added;
        }
        internal bool EnsureIntegrated(ItemInfo target)
        {
            if(disposed||target==null||!MonitorFacilityIdentity.IsFixed(target.itemKey)||target.buildID<0||!target.gameObject.activeInHierarchy)return false;
            WallMonitorAttachment existing;if(monitors.TryGet(target,out existing))return true;
            var mn=Managers();
            if(mn==null||!mn.allSet||mn.buildMN==null||mn.buildMN.build==null||!mn.buildMN.build.Contains(target)||Camera.main==null)return false;
            try{Add(target);restoreErrors.Remove(target.GetInstanceID());return true;}
            catch(Exception e){if(restoreErrors.Add(target.GetInstanceID()))log.LogWarning("[MonitorFixed] buildID="+target.buildID+"; initialize unavailable: "+e.Message);return false;}
        }
        internal void ReleaseIntegrated(ItemInfo target)
        {if(!ReferenceEquals(target,null))Remove(target);}
        private bool HandleIntegratedInput(ItemInfo target,bool remove,bool compare)
        {
            if(!MonitorFacilityIdentity.IsFixed(target.itemKey))return false;
            if(remove){log.LogInfo("このモニターは施設に固定されています");return true;}
            WallMonitorAttachment existing;
            if(monitors.TryGet(target,out existing)){Select(existing);if(compare)existing.CycleComparison();}
            return true;
        }
        private void RemoveVanillaMonitors()
        {
            // Only dispose attachments owned by this registry; never search/delete by object name.
            foreach(var pair in monitors.Snapshot()){
                var owner=pair.Key;
                if(owner==null||owner.itemKey!=Plugin.SourceId)continue;
                Remove(owner);manualOff.Remove(owner);restoreErrors.Remove(owner.GetInstanceID());
            }
        }
        private void RestoreDecorations()
        {
            if(Time.unscaledTime<nextRestore)return;nextRestore=Time.unscaledTime+1f;
            var mn=Managers();if(mn==null||!mn.allSet||mn.buildMN==null||mn.buildMN.build==null)return;
            using(MonitorPerformance.Measure(MonitorPerfStage.RestoreScan)) { /*WHFDPerfScope*/
            foreach(var target in mn.buildMN.build){
                MonitorPerformance.Count(MonitorPerfEvent.RestoreTargets); /*WHFDPerfEvent*/
                if(target==null||!MonitorFacilityIdentity.Supports(target.itemKey)||!target.gameObject.activeInHierarchy)continue;
                if(MonitorFacilityIdentity.IsFixed(target.itemKey)){EnsureIntegrated(target);continue;}
                WallMonitorAttachment existing;bool installed=monitors.TryGet(target,out existing);
                if(installed&&!manualOff.ContainsKey(target))continue;
                CommonStates npc=null;bool known=false;
                try{known=MonitorNpcBinding.TryReadAssignedNpc(target.GetComponent<InventorySlot>(),mn,out npc);}catch{}
                CommonStates blocked;
                if(manualOff.TryGetValue(target,out blocked)){
                    if(!known)continue;
                    if(npc!=null&&(blocked==null||ReferenceEquals(npc,blocked))){manualOff[target]=npc;continue;}
                    manualOff.Remove(target);
                }
                if(installed)continue;
                bool automatic=known&&npc!=null&&(npc.npcID==15||npc.npcID==16||npc.npcID==17||npc.npcID==44||npc.npcID==90);
                var p=Placement(target);bool recorded=p!=null&&placements.Contains(p.Context,p.BuildId,p.X,p.Y,p.Z);
                if(!automatic&&!recorded)continue;
                if(target.transform.Find("WHFD_WallMonitorPrototype")!=null)continue;
                try{MonitorPerformance.Count(MonitorPerfEvent.RestoreAttempt); /*WHFDPerfEvent*/ Add(target);if(automatic&&!recorded)Remember(target,true);restoreErrors.Remove(target.GetInstanceID());}
                catch(Exception e){if(restoreErrors.Add(target.GetInstanceID()))log.LogWarning("[MonitorAuto] buildID="+target.buildID+"; restore unavailable: "+e.Message);}
            }
            } /*WHFDPerfEnd*/
        }
        internal WallDecoration(ConfigFile config,ManualLogSource logger)
        {
            log=logger;options=new WallDecorationOptions(config);if(placements.Error!=null)log.LogWarning("[MonitorAuto] "+placements.Error);
            framingUI=new MonitorFramingDebugUI(config,()=>selected==null?null:selected.Display,
                ()=>selected==null?null:selected.Diagnostics,options.playbackPoses,options.playbackFaces,
                ()=>{foreach(var pair in monitors.Snapshot())if(pair.Value.Display!=null)pair.Value.Display.RefreshPlaybackSettings();},
                ()=>selected==null?"WHFD / no selected monitor":"WHFD / NPC ID "+(selected.Display!=null && selected.Display.NumericNpcId.HasValue?selected.Display.NumericNpcId.Value.ToString():"Unknown")+" / buildID "+selected.BuildId+" / instance "+selected.InstanceId,text=>log.LogWarning(text));
        }
        private ManagersScript Managers()
        {
            if(managers==null){var go=GameObject.Find("Managers");managers=go==null?null:go.GetComponent<ManagersScript>();}
            return managers;
        }
        private ItemInfo NearestFacility()
        {
            var mn=Managers();
            if(mn==null||!mn.allSet||mn.buildMN==null||mn.buildMN.build==null||mn.gameMN==null||mn.gameMN.player==null)return null;
            var origin=mn.gameMN.player.transform.position;float radius=options.range.Value;
            return mn.buildMN.build.Where(b=>b!=null&&MonitorFacilityIdentity.Supports(b.itemKey)&&b.buildID>=0&&b.gameObject.activeInHierarchy)
                .Where(b=>(b.transform.position-origin).sqrMagnitude<=radius*radius)
                .OrderBy(b=>(b.transform.position-origin).sqrMagnitude).ThenBy(b=>b.buildID).ThenBy(b=>b.GetInstanceID()).FirstOrDefault();
        }
        private WallMonitorAttachment NearestMonitor()
        {
            using(MonitorPerformance.Measure(MonitorPerfStage.NearestSelection)) { /*WHFDPerfScope*/
            if(monitors.Count==0)return null;
            var mn=Managers();if(mn==null||mn.gameMN==null||mn.gameMN.player==null)return null;
            var origin=mn.gameMN.player.transform.position;float radius=options.range.Value;
            return monitors.Snapshot().Select(p=>p.Value).Where(v=>v.Owner!=null&&MonitorFacilityIdentity.Supports(v.Owner.itemKey)&&v.Owner.gameObject.activeInHierarchy)
                .Where(v=>(v.Owner.transform.position-origin).sqrMagnitude<=radius*radius)
                .OrderBy(v=>(v.Owner.transform.position-origin).sqrMagnitude).ThenBy(v=>v.InstanceId).FirstOrDefault();
        
            } /*WHFDPerfEnd*/}
        private void Select(WallMonitorAttachment value)
        {
            if(ReferenceEquals(selected,value))return;
            MonitorPerformance.Count(MonitorPerfEvent.SelectionChanged); /*WHFDPerfEvent*/
            framingUI.Cancel();selected=value;
        }
        private void Remove(ItemInfo key)
        {
            WallMonitorAttachment value;if(!monitors.TryGet(key,out value))return;
            if(ReferenceEquals(selected,value))Select(null);
            try{monitors.Remove(key);log.LogInfo("Monitor removed: buildID="+value.BuildId+"; instance="+value.InstanceId+"; remaining="+monitors.Count);}
            catch(Exception error){log.LogWarning("Monitor cleanup failed for instance="+value.InstanceId+": "+error.GetType().Name);}
        }
        internal void Tick()
        {
            if(disposed)return;
            RemoveVanillaMonitors();
            SamplePerformance(); /*WHFDPerfEvent*/
            RestoreDecorations();
            foreach(var pair in monitors.Snapshot()){
                try{if(!pair.Value.Poll())Remove(pair.Key);}
                catch(Exception error){log.LogWarning("Monitor stopped: instance="+pair.Value.InstanceId+"; "+error.GetType().Name);Remove(pair.Key);}
            }
            // Nearest attached monitor selects the shared editor; lock selection while open.
            // This scans owned monitors only, never all placed facilities every frame.
            if(MonitorDebugSettings.Enabled){if(!framingUI.IsOpen)Select(NearestMonitor());framingUI.Tick();}
            bool remove=options.clear.Value.IsDown(),toggle=options.toggle.Value.IsDown(),compare=MonitorDebugSettings.Enabled&&options.compare.Value.IsDown();
            if(!remove&&!toggle&&!compare)return;
            try{
                var target=NearestFacility();if(target==null){log.LogInfo("No nearby supported facility; no monitors changed.");return;}
                if(HandleIntegratedInput(target,remove,compare))return;
                WallMonitorAttachment existing;
                if(monitors.TryGet(target,out existing)){
                    if(remove||toggle){SuppressAutomaticInstall(target);Remember(target,false);Remove(target);}
                    else{Select(existing);existing.CycleComparison();}
                    return;
                }
                // F11 and F9 never fall through to a more distant monitor or remove all.
                if(remove||compare){log.LogInfo("Nearest facility has no owned monitor; no monitors changed.");return;}
                var added=Add(target);manualOff.Remove(target);Remember(target,true);
                Select(added);
                log.LogInfo("Monitor added: buildID="+added.BuildId+"; instance="+added.InstanceId+"; total="+monitors.Count);
            }catch(Exception error){log.LogWarning("Monitor operation failed: "+error.GetType().Name);}
        }
        internal void TickFaceAnimation()
        {
            if(disposed)return;
            RemoveVanillaMonitors();
            foreach(var pair in monitors.Snapshot()){
                try{pair.Value.TickFaceAnimation();}
                catch(Exception error){log.LogWarning("Monitor frame failed: instance="+pair.Value.InstanceId+"; "+error.GetType().Name);Remove(pair.Key);}
            }
            if(MonitorDebugSettings.Enabled)framingUI.TickDiagnostics();
        }
        internal void OwnerSceneUnloaded(int handle)
        {foreach(var pair in monitors.Snapshot())if(pair.Value.SceneHandle==handle)Remove(pair.Key);foreach(var key in manualOff.Keys.Where(k=>k==null||k.gameObject.scene.handle==handle).ToArray())manualOff.Remove(key);}
        internal void DrawFramingUI(){if(!disposed)framingUI.Draw();}
        internal void CancelFramingUI(){framingUI.Cancel();}
        public void Dispose()
        {
            if(disposed)return;disposed=true;framingUI.Dispose();selected=null;manualOff.Clear();
            foreach(var pair in monitors.Snapshot())Remove(pair.Key);
        }
    }
}
