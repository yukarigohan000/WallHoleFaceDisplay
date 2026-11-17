using System;
using System.Linq;
using Spine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Private pose/state. Reuses existing snapshot and verified per-feature rig binders.
    internal sealed class MonitorTemplatePlayback
    {
        internal readonly Skeleton Pose;
        internal bool AuditNext;
        internal string AuditStages;
        internal static string AuditPose(Skeleton pose)
        {
            try{var b=new System.Text.StringBuilder();
            b.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,"pose={0};data={1};skin={2};xy={3:R},{4:R};scale={5:R},{6:R}",System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pose),System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pose.Data),pose.Skin==null?"none":pose.Skin.Name,pose.X,pose.Y,pose.ScaleX,pose.ScaleY);
            foreach(var bone in pose.Bones) b.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,"|{0}[parent={1};local={2:R},{3:R},{4:R};scale={5:R},{6:R};world={7:R},{8:R};matrix={9:R},{10:R},{11:R},{12:R}]",bone.Data.Name,bone.Parent==null?"none":bone.Parent.Data.Name,bone.X,bone.Y,bone.Rotation,bone.ScaleX,bone.ScaleY,bone.WorldX,bone.WorldY,bone.A,bone.B,bone.C,bone.D);
            return b.ToString();}catch(Exception e){return "unavailable:"+e.GetType().Name;}
        }
        private readonly Skeleton baseline;
        private readonly AnimationState state;
        private readonly MonitorFacilityBlood blood;
        internal readonly string[] Animations;
        private readonly MonitorArmOffsets arms;
        private readonly MonitorHeadOffsets head;
        private readonly MonitorTemplateHeadOffsets normalHead;
        private readonly MonitorTorsoOffsets torso;
        private readonly MonitorArmValues armInput=new MonitorArmValues();private readonly MonitorHeadValues headInput=new MonitorHeadValues();private readonly MonitorTorsoValues torsoInput=new MonitorTorsoValues();
        internal readonly string ArmReason,HeadReason,TorsoReason;
        internal bool ArmsAvailable {get {return arms!=null;}}
        internal bool HeadAvailable {get {return head!=null||normalHead!=null;}}
        internal bool TorsoAvailable {get {return torso!=null;}}
        internal MonitorTemplatePlayback(Skeleton seed,int npcId=-1,string[] bloodSlots=null,Func<int?> bloodState=null)
        {
            baseline=MonitorFacePose.CaptureFull(seed);Pose=MonitorFacePose.CaptureFull(baseline);state=new AnimationState(new AnimationStateData(Pose.Data));
            if(npcId==44&&bloodSlots!=null&&bloodState!=null)blood=new MonitorFacilityBlood(Pose,bloodSlots,bloodState);
            MonitorPerformance.Count(MonitorPerfEvent.TemplateSkeleton,2);MonitorPerformance.Count(MonitorPerfEvent.TemplateState); /*WHFDPerfEvent*/
            Animations=Pose.Data.Animations.Select(a=>a.Name).Distinct(StringComparer.Ordinal).OrderBy(n=>n,StringComparer.Ordinal).ToArray();
            MonitorArmOffsets.TryCreate(Pose,out arms,out ArmReason,true);MonitorHeadOffsets.TryCreate(Pose,out head,out HeadReason,true,npcId);MonitorTorsoOffsets.TryCreate(Pose,out torso,out TorsoReason,true,npcId);
            if(head==null&&Pose.FindBone("head_pos")==null){if(MonitorTemplateHeadOffsets.TryCreate(Pose,out normalHead,out HeadReason))HeadReason="Verified normal-rig head weights / head";}
        }
        internal void Validate(MonitorNpcValues values)
        {
            if(values==null||!values.Valid)throw new InvalidOperationException("Invalid profile values");
            if(!MonitorNpcProfileSettings.AnimationValid(values.Body,Animations)||!MonitorNpcProfileSettings.AnimationValid(values.Face,Animations))throw new InvalidOperationException("Profile animation is not present in this SkeletonData");
        }
        internal void Advance(float seconds,MonitorNpcValues values,bool offsets=true)
        {
            bool audit=AuditNext;AuditNext=false;if(audit)AuditStages="before="+AuditPose(Pose);
            Validate(values); // Must happen before touching tracks or the last successful pose.
            if(float.IsNaN(seconds)||float.IsInfinity(seconds)||seconds<0||seconds>60)throw new ArgumentOutOfRangeException("seconds");
            if(arms!=null)arms.RestoreBeforeAnimation();if(torso!=null)torso.RestoreBeforeAnimation();if(head!=null)head.RestoreBeforeAnimation();
            if(normalHead!=null)normalHead.RestoreBeforeAnimation();
            Ensure(0,values.Body);Ensure(2,values.Face);
            using(MonitorPerformance.Measure(MonitorPerfStage.RestorePose)){MonitorFacePose.RestoreForAnimation(baseline,Pose);if(audit)AuditStages+=";restored="+AuditPose(Pose);}
            using(MonitorPerformance.Measure(MonitorPerfStage.StateUpdateApply)){state.Update(seconds);state.Apply(Pose);if(audit)AuditStages+=";stateApplied_worldNotUpdated="+AuditPose(Pose);}
            using(MonitorPerformance.Measure(MonitorPerfStage.OffsetsWorld)){
            bool supplemental=offsets&&head!=null&&head.RequiresWorldCapture(values.HeadRotation??0,values.TorsoRotation??0);
            if(supplemental){Pose.UpdateWorldTransform();head.CaptureBeforeOffsets();}
            if(offsets){string reason;
                armInput.LeftX=values.LeftX??0;armInput.LeftY=values.LeftY??0;armInput.RightX=values.RightX??0;armInput.RightY=values.RightY??0;
                headInput.Rotation=values.HeadRotation??0;torsoInput.Rotation=values.TorsoRotation??0;
                // Existing scalar DTOs retain their legacy validation contract. Profile ID ownership is outside these offset values.
                if(arms!=null&&!arms.ApplyAfterAnimation((MonitorPerformance.TypedPoseCopy?armInput:new MonitorArmValues {LeftX=values.LeftX??0,LeftY=values.LeftY??0,RightX=values.RightX??0,RightY=values.RightY??0}),out reason))throw new InvalidOperationException(reason);
                if(torso!=null&&!torso.ApplyAfterAnimation((MonitorPerformance.TypedPoseCopy?torsoInput:new MonitorTorsoValues {Rotation=values.TorsoRotation??0}),out reason))throw new InvalidOperationException(reason);
                if(head!=null&&!head.ApplyAfterAnimation((MonitorPerformance.TypedPoseCopy?headInput:new MonitorHeadValues {Rotation=values.HeadRotation??0}),out reason))throw new InvalidOperationException(reason);
                if(normalHead!=null)normalHead.Apply(values.HeadRotation??0);
            }
            Pose.UpdateWorldTransform();if(supplemental)head.ApplyAfterWorld();if(blood!=null)blood.Apply();if(audit)AuditStages+=";offsetsAndWorld="+AuditPose(Pose);}
        }
        internal string TrackName(int track){var entry=state.GetCurrent(track);return entry==null?"(none)":entry.Animation.Name;}
        private void Ensure(int track,string name)
        {
            if(string.IsNullOrEmpty(name)){if(state.GetCurrent(track)!=null)state.ClearTrack(track);return;}
            var current=state.GetCurrent(track);if(current!=null&&current.Animation.Name==name&&current.Loop)return;
            state.SetAnimation(track,name,true).MixDuration=.2f;
        }
    }
}
