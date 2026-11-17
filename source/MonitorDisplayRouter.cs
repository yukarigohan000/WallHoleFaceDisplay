using System;
using BepInEx.Logging;
using UnityEngine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    internal sealed class MonitorVideoConditionException : Exception
    {
        internal MonitorVideoConditionException(string code) : base(code) { }
    }
    internal enum MonitorDisplayMode { LegacyFace, RestraintVideo }

    internal sealed class MonitorDisplayRouter : IDisposable
    {
        private readonly MonitorFaceDisplay legacy;
        private readonly RestraintMonitorVideo video;
        private readonly ManagersScript templateManagers;
        private readonly Transform templateParent;private readonly Camera templateCamera;private readonly Rect templateScreen;private readonly WoodPanelSurface templateWall;
        private readonly int templateLayer,templateOrder;private readonly ManualLogSource templateLog;
        private readonly int performanceMonitorId;
        private readonly MonitorScreenGeometry screenGeometry;
        private readonly bool fixedFacility;
        private CommonStates facilityNpc;
        private MonitorStateSnapshot lastSnapshot;
        private readonly MonitorNpcProfiles automaticProfiles=new MonitorNpcProfiles(BepInEx.Paths.ConfigPath);
        private bool followOccupant=true,automaticLoading;
        internal bool FollowsOccupant {get {return followOccupant;}}
        private CommonStates automaticNpc;
        private Spine.Skeleton automaticSkeleton;
        private float nextAutomaticAttempt;
        private string automaticFailure;
        internal void FollowOccupant(){followOccupant=true;automaticNpc=null;automaticSkeleton=null;nextAutomaticAttempt=0;SyncOccupant();}
        private void SyncOccupant()
        {
            using(MonitorPerformance.Measure(MonitorPerfStage.AutoSync)) { /*WHFDPerfScope*/
            if(!followOccupant||video==null)return;
            if(facilityNpc==null){if(Template!=null)UseFacility();automaticNpc=null;automaticSkeleton=null;return;}
            if(facilityNpc.npcID==15){if(Template!=null)UseFacility();return;}
            int index;if(!MonitorFacilityRecipe.TryIndex(facilityNpc.npcID,out index)){
                if(Template!=null)UseFacility();return;
            }
            var skeleton=facilityNpc.anim==null?null:facilityNpc.anim.Skeleton;
            if(ReferenceEquals(automaticNpc,facilityNpc)&&ReferenceEquals(automaticSkeleton,skeleton)&&Template!=null&&Template.IsFacility&&Template.NpcId==facilityNpc.npcID)return;
            if(Time.unscaledTime<nextAutomaticAttempt)return;nextAutomaticAttempt=Time.unscaledTime+1f;
            MonitorPerformance.Count(MonitorPerfEvent.AutoLoadAttempt); /*WHFDPerfEvent*/
            automaticLoading=true;
            try{if(TryLoadNpc(facilityNpc.npcID,ProfileProvider==null?automaticProfiles.Get(facilityNpc.npcID):ProfileProvider(facilityNpc.npcID))){automaticNpc=facilityNpc;automaticSkeleton=skeleton;automaticFailure=null;}else if(automaticFailure!=LoadStatus){automaticFailure=LoadStatus;templateLog.LogWarning("[MonitorAuto] npcId="+facilityNpc.npcID+"; "+LoadStatus);}}
            finally{automaticLoading=false;}
        
            } /*WHFDPerfEnd*/}
        internal MonitorTemplateDisplay Template {get;private set;}
        internal bool IsTemplate {get {return Template!=null;}}
        /*WHFDPerfOnlyBegin*/
        internal bool PerformanceVisible {get {return Template!=null?Template.PerformanceVisible:video!=null&&video.PerformanceVisible;}}
        /*WHFDPerfOnlyEnd*/
        internal string LoadStatus {get;private set;}
        internal Func<MonitorNpcValues> TemplateValuesProvider;
        internal void SetPreviewRendering(bool value){if(video!=null)video.ForceRender=value;if(Template!=null)Template.ForceRender=value;}
        internal Func<int,MonitorNpcProfileSettings> ProfileProvider;
        internal bool TryLoadNpc(int id,MonitorNpcProfileSettings settings,bool preferFacility=true)
        {
            using(MonitorPerformance.Owner(id,performanceMonitorId)) using(MonitorPerformance.Measure(MonitorPerfStage.LoadNpc)) { /*WHFDPerfScope*/
            MonitorTemplateDisplay candidate=null;
            try{
                if(fixedFacility&&(facilityNpc==null||facilityNpc.npcID!=id))throw new InvalidOperationException("Fixed monitor requires its assigned NPC");
                if(video==null)throw new InvalidOperationException("RestraintVideo display mode is required");
                if(id==15){if(facilityNpc==null||facilityNpc.npcID!=15||!video.VideoReady)throw new InvalidOperationException("ID 15 compatibility display requires the existing facility individual");UseFacility();if(!automaticLoading)followOccupant=false;LoadStatus="NPC ID 15 / existing compatible facility display";MonitorPerformance.Count(MonitorPerfEvent.LoadSuccess); /*WHFDPerfEvent*/ return true;}
                if(settings==null||settings.NpcId!=id)throw new InvalidOperationException("Profile ID mismatch");
                int facilityIndex;
                var asset=preferFacility&&MonitorFacilityRecipe.TryIndex(id,out facilityIndex)
                    ? MonitorFacilitySource.Load(templateManagers,id,facilityNpc,settings.FacilityAnimations)
                    : MonitorNpcAssetSource.Load(templateManagers,id,facilityNpc);
                if(preferFacility&&asset.FacilityAnimations==null)asset.Appearance="gen_restrain_01 mapping unreviewed for this ID; "+asset.Appearance;
                // All allocation, rig checks and first render finish before publication.
                candidate=new MonitorTemplateDisplay(asset,settings,templateParent,templateCamera,templateScreen,templateWall,templateLayer,templateOrder,templateLog,screenGeometry);
                if(candidate.IsFacility){int initialMode=MonitorPlaybackMode.FromSnapshot(lastSnapshot,id);candidate.PrepareMode(initialMode<0?0:initialMode);}
                var prepared=candidate;prepared.ValuesProvider=()=>TemplateValuesProvider==null?prepared.GetValues(prepared.Mode):TemplateValuesProvider();
                candidate.Publish();video.SetDisplayVisible(false);
                var previous=Template;Template=candidate;candidate=null;
                if(previous!=null){try{previous.Dispose();}catch(Exception e){templateLog.LogWarning("[MonitorTemplate] previous resource cleanup: "+e.GetType().Name);}}
                if(!automaticLoading)followOccupant=false;LoadStatus=Template.Status;MonitorPerformance.Count(MonitorPerfEvent.LoadSuccess); /*WHFDPerfEvent*/ return true;
            }catch(Exception e){if(candidate!=null){try{candidate.Dispose();}catch{}}
                if(Template!=null)Template.Publish();else if(video!=null)video.SetDisplayVisible(true);
                LoadStatus="Load failed; current display kept: "+e.Message;MonitorPerformance.Count(MonitorPerfEvent.LoadFailure); /*WHFDPerfEvent*/ return false;}
        
            } /*WHFDPerfEnd*/}
        internal void UseFacility(){var previous=Template;Template=null;if(previous!=null)previous.Dispose();if(video!=null){if(previous!=null)video.TickAnimation(0,lastSnapshot);video.SetDisplayVisible(true);}LoadStatus="Facility compatibility display";}

        internal MonitorDisplayRouter(MonitorDisplayMode mode, ItemInfo owner, ManagersScript managers, Transform parent,
            Camera camera, Rect screen, WoodPanelSurface surface, int layer, int order, ManualLogSource log, FaceDepthMode depth,
            Func<int, string> poseByMode, Func<int, string> faceByMode, Func<float> blendSeconds,MonitorScreenGeometry screenGeometry)
        {
            fixedFacility=MonitorFacilityIdentity.IsFixed(owner.itemKey);
            performanceMonitorId=owner.GetInstanceID();this.screenGeometry=screenGeometry;templateManagers=managers;templateParent=parent;templateCamera=camera;templateScreen=screen;templateWall=surface;templateLayer=layer;templateOrder=order;templateLog=log;
            if (mode == MonitorDisplayMode.LegacyFace)
                legacy = new MonitorFaceDisplay(parent, camera, screen, surface, layer, order, log, depth);
            else {
                log.LogInfo("[MonitorVideoTrace] stage=1; branch=RestraintVideo; entered=true");
                video = new RestraintMonitorVideo(owner, managers, parent, camera, screen, surface, layer, order, log,
                    poseByMode, faceByMode, blendSeconds,screenGeometry);
            }
        }
        internal int? NumericNpcId { get { return Template!=null?(int?)Template.NpcId:video == null ? (int?)null : video.NumericNpcId; } }
        internal bool TorsoAvailable {get {return video!=null && video.TorsoAvailable;}}
        internal string TorsoStatus {get {return video==null?"RestraintVideo required":video.TorsoStatus;}}
        internal void SetTorsoProvider(Func<MonitorTorsoValues> provider) {if(video!=null)video.TorsoProvider=provider;}
        internal bool HeadAvailable {get {return video!=null && video.HeadAvailable;}}
        internal string HeadStatus {get {return video==null?"RestraintVideo required":video.HeadStatus;}}
        internal void SetHeadProvider(Func<MonitorHeadValues> provider) {if(video!=null)video.HeadProvider=provider;}
        internal bool ArmAvailable {get {return video!=null && video.ArmAvailable;}}
        internal string ArmStatus {get {return video==null?"RestraintVideo required":video.ArmStatus;}}
        internal void SetArmProvider(Func<MonitorArmValues> provider) {if(video!=null)video.ArmProvider=provider;}
        internal int PoseMode {get {return Template!=null?Template.Mode:video==null?-1:video.PoseMode;}}
        internal bool VideoReady { get { return Template!=null || video != null && video.VideoReady; } }
        internal Texture VideoTexture { get { return Template!=null?Template.Texture:video == null ? null : video.VideoTexture; } }
        internal string[] PoseNames { get { return video == null ? new string[0] : video.PoseNames; } }
        internal string[] FaceNames { get { return video == null ? new string[0] : video.FaceNames; } }
        internal void RefreshPlaybackSettings() { if (video != null) video.RefreshPlaybackSettings(); }
        internal void SetFramingProvider(Func<int, MonitorFramingValue> provider) { if(video!=null) video.FramingProvider=provider; }
        internal void SetNpc(CommonStates npc) {
            if(fixedFacility){
                bool changed=!ReferenceEquals(facilityNpc,npc);
                int index;bool supported=npc!=null&&(npc.npcID==15||MonitorFacilityRecipe.TryIndex(npc.npcID,out index));
                if(changed||!supported){
                    var previous=Template;Template=null;if(previous!=null)previous.Dispose();
                    followOccupant=true;automaticNpc=null;automaticSkeleton=null;nextAutomaticAttempt=0;
                }
                facilityNpc=npc;
                if(legacy!=null)legacy.SetNpc(supported?npc:null);else{video.SetNpc(supported?npc:null);video.SetDisplayVisible(true);}
                if(!supported)LoadStatus="Fixed facility / no supported NPC";
                return;
            }
if(followOccupant&&!ReferenceEquals(facilityNpc,npc)&&Template!=null)UseFacility();facilityNpc=npc; if (legacy != null) legacy.SetNpc(npc); else {video.SetNpc(npc);if(Template!=null)video.SetDisplayVisible(false);} }
        internal void SetUseState(bool value) { if (legacy != null) legacy.SetUseState(value); else {video.SetUseState(value);if(Template!=null)video.SetDisplayVisible(false);} }
        internal void TickAnimation(float deltaTime, MonitorStateSnapshot snapshot) {
            using(MonitorPerformance.Owner(NumericNpcId??-1,performanceMonitorId)){
            lastSnapshot=snapshot;SyncOccupant();
            if(Template!=null){video.SetDisplayVisible(false);Template.Tick(deltaTime,snapshot);return;}
            if (legacy != null) legacy.TickAnimation(deltaTime); else video.TickAnimation(deltaTime, snapshot);}
        }
        public void Dispose() {if(Template!=null){Template.Dispose();Template=null;} if (legacy != null) legacy.Dispose(); if (video != null) video.Dispose(); }
    }
}
