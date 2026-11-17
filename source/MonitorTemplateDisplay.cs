using System;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    internal sealed class MonitorTemplateDisplay : IDisposable
    {
        internal readonly int NpcId;
        internal readonly MonitorNpcProfileSettings Settings;
        internal readonly MonitorTemplatePlayback Playback;
        private readonly MonitorTemplateSurface surface;
        private readonly string information;
        internal bool ForceRender {set {surface.ForceRender=value;}}
        private readonly MonitorFacilityAnimationSettings facilityAnimations;
        private readonly ManualLogSource log;
        private string lastPlayback;
        internal bool IsFacility {get {return facilityAnimations!=null;}}
        /*WHFDPerfOnlyBegin*/
        internal bool PerformanceVisible {get {return surface.PerformanceVisible;}}
        /*WHFDPerfOnlyEnd*/
        private MonitorNpcProfile standardProfile;private MonitorFacilityTrackDocument standardTracks;private int animationWarnings;
        internal MonitorNpcValues GetValues(int mode)
        {
            var value=Settings.Get(mode);if(facilityAnimations!=null)value=facilityAnimations.Apply(mode,value);
            bool body=MonitorNpcProfileSettings.AnimationValid(value.Body,Playback.Animations),face=MonitorNpcProfileSettings.AnimationValid(value.Face,Playback.Animations);
            if(body&&face)return value;
            if(standardProfile==null)standardProfile=MonitorStandardDefaults.Fallback<MonitorNpcProfile>("WallHoleFaceDisplay.profile-"+NpcId+".json",()=>new MonitorNpcProfile{NpcId=NpcId});
            var fallback=MonitorNpcValues.Resolve(standardProfile.Common,mode<0?null:standardProfile.Mode(mode));
            if(facilityAnimations!=null){
                fallback=MonitorFacilityAnimationSettings.Defaults(mode,fallback,NpcId);
                if(standardTracks==null)standardTracks=MonitorStandardDefaults.Fallback<MonitorFacilityTrackDocument>("WallHoleFaceDisplay.facility-animations-"+NpcId+".json",()=>new MonitorFacilityTrackDocument{NpcId=NpcId});
                var selected=standardTracks.Mode(mode);fallback.Body=selected?.Body??standardTracks.Common?.Body??fallback.Body;fallback.Face=selected?.Face??standardTracks.Common?.Face??fallback.Face;
            }
            if(!body)value.Body=fallback.Body;if(!face)value.Face=fallback.Face;
            int bit=1<<(mode+1);if((animationWarnings&bit)==0){animationWarnings|=bit;log.LogWarning("[MonitorSettings] npcId="+NpcId+"; mode="+mode+"; unavailable user animation; embedded mapping selected; file preserved.");}
            return value; // Existing Validate still refuses a genuinely missing required asset.
        }
        internal string GroupError(string group){return IsFacility&&group=="animation"?facilityAnimations.Error:Settings.Error;}
        internal void SaveGroup(int scope,string group,MonitorNpcValues value){if(IsFacility&&group=="animation")facilityAnimations.Save(scope,value,Playback.Animations);else Settings.Save(scope,group,value,Playback.Animations);}
        internal void ClearGroup(int scope,string group){if(IsFacility&&group=="animation")facilityAnimations.Clear(scope);else Settings.Clear(scope,group);}

        internal bool AuditPreview;internal int AuditEditScope=-2;
        internal Func<MonitorNpcValues> ValuesProvider;
        internal int Mode {get;private set;}
        internal bool FollowFacility=true;
        internal string Status {get;private set;}
        internal Texture Texture {get {return surface.Texture;}}
        internal MonitorTemplateDisplay(MonitorNpcAssetSource asset,MonitorNpcProfileSettings settings,Transform parent,Camera camera,Rect screen,WoodPanelSurface wall,int layer,int order,ManualLogSource log,MonitorScreenGeometry screenGeometry)
        {
            NpcId=asset.NpcId;Settings=settings;facilityAnimations=asset.FacilityAnimations;this.log=log;Playback=new MonitorTemplatePlayback(asset.Seed,asset.NpcId,asset.BloodSlots,asset.BloodState);
            var initial=GetValues(0);Playback.AuditNext=MonitorDebugSettings.Enabled;Playback.Advance(0,initial,false);
            surface=new MonitorTemplateSurface(parent,camera,screen,wall,layer,order,log,asset.Template,Playback.Pose,asset.NpcId,screenGeometry);
            if(MonitorDebugSettings.Enabled)surface.AuditSource=asset.AuditIdentity+";templateAnimationId="+asset.Template.GetInstanceID()+";asset="+asset.Template.skeletonDataAsset.name+";transformScale="+asset.Template.transform.lossyScale+";seed="+MonitorTemplatePlayback.AuditPose(asset.Seed);
            surface.AuditStages=Playback.AuditStages;surface.AuditSettings=Settings.AuditFrameSource(0);
            try{surface.AuditMode=0;Playback.Advance(0,initial);surface.Draw(Playback.Pose,Framing(initial));}
            catch{surface.Dispose();throw;}
            Status="NPC ID "+NpcId+" / "+asset.Appearance+" / bones="+Playback.Pose.Bones.Count+" slots="+Playback.Pose.Slots.Count+" skins="+Playback.Pose.Data.Skins.Count+" attachments="+Playback.Pose.Slots.Count(s=>s.Attachment!=null)+" IK="+Playback.Pose.IkConstraints.Count+" transform="+Playback.Pose.TransformConstraints.Count+" path="+Playback.Pose.PathConstraints.Count;
            information=Status;
            if(MonitorDebugSettings.Enabled)log.LogInfo("[MonitorRig] npcId="+NpcId+"; arms="+Playback.ArmsAvailable+" ("+Playback.ArmReason+"); head="+Playback.HeadAvailable+" ("+Playback.HeadReason+"); torso="+Playback.TorsoAvailable+" ("+Playback.TorsoReason+")");
        }
        private MonitorFramingValue Framing(MonitorNpcValues v){return new MonitorFramingValue {NpcId=NpcId,X=v.X??0,Y=v.Y??0,Zoom=v.Zoom??1};}
        internal void SetMode(int mode)
        {if(mode<0||mode>3)throw new ArgumentOutOfRangeException("mode");Playback.Validate(GetValues(mode));Mode=mode;}
        internal void PrepareMode(int mode)
        {SetMode(mode);var value=GetValues(mode);Playback.AuditNext=surface.NeedsAudit(mode,false);Playback.Advance(0,value);surface.AuditMode=mode;surface.AuditSettings=Settings.AuditFrameSource(mode);surface.AuditPreview=false;surface.AuditStages=Playback.AuditStages;surface.Draw(Playback.Pose,Framing(value));}
        internal void Publish(){surface.SetVisible(true);}
        internal void Tick(float seconds,MonitorStateSnapshot snapshot)
        {
            using(MonitorPerformance.Measure(MonitorPerfStage.TemplateTick)) { /*WHFDPerfScope*/
            try{
                if(FollowFacility){int requested=MonitorPlaybackMode.FromSnapshot(snapshot,NpcId);if(requested>=0)SetMode(requested);}
                var value=ValuesProvider==null?GetValues(Mode):ValuesProvider();surface.AuditMode=Mode;surface.AuditPreview=AuditPreview;Playback.AuditNext=surface.NeedsAudit(Mode,AuditPreview);if(Playback.AuditNext)surface.AuditSettings=Settings.AuditFrameSource(AuditPreview?AuditEditScope:Mode)+";editScope="+AuditEditScope+";previewGroupMayOverride="+AuditPreview;using(MonitorPerformance.Measure(MonitorPerfStage.TemplateAnimation)) { Playback.Advance(seconds,value); }surface.AuditStages=Playback.AuditStages;surface.Draw(Playback.Pose,Framing(value));
                if(MonitorDebugSettings.Enabled){string playback="npcId="+NpcId+"; source="+(IsFacility?"gen_restrain_01":"normal")+"; mode="+Mode+"; track0="+Playback.TrackName(0)+"; track2="+Playback.TrackName(2)+"; inUse="+(snapshot==null||!snapshot.InUse.HasValue?"Unknown":snapshot.InUse.Value.ToString());
                if(lastPlayback!=playback){lastPlayback=playback;log.LogInfo("[MonitorFacilityPlayback] "+playback);}}
                Status=information+" / track0="+Playback.TrackName(0)+" / track2="+Playback.TrackName(2);
            }catch(Exception e){Status="NPC ID "+NpcId+" / last displayed frame retained; "+e.Message;}
        
            } /*WHFDPerfEnd*/}
        public void Dispose(){surface.Dispose();}
    }
}
