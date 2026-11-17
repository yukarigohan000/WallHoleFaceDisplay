using System;
using Spine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Native PositionEditing exposes spine_02_pos via SearchComp + SkeletonUtilityBone
    // Override (rotation=true, parentReference=null, alpha=1). Keep only that local
    // rotation operation on the monitor's private pose; native Transforms are never used.
    internal sealed class MonitorTorsoOffsets
    {
        private readonly Bone control;
        private float rotation;
        private bool applied;
        private MonitorTorsoOffsets(Skeleton pose,bool structural,int npcId)
        {
            control=pose.FindBone("spine_02_pos");
            if(structural){MonitorHeadOffsets verifiedHead;string headReason;if(!MonitorHeadOffsets.TryCreate(pose,out verifiedHead,out headReason,true,npcId))throw new InvalidOperationException("TorsoFaceSafety:"+headReason);}
            var lower=pose.FindBone("spine_01");var upper=pose.FindBone("spine_02");
            if(control==null || lower==null || upper==null || !ReferenceEquals(control.Parent,lower) ||
                !ReferenceEquals(upper.Parent,control) || control.Data.TransformMode!=TransformMode.Normal ||
                (upper.Data.TransformMode!=TransformMode.NoScale&&(!structural||upper.Data.TransformMode!=TransformMode.Normal)))
                throw new InvalidOperationException("VerifiedTorsoHierarchyMismatch");
            var neck=pose.FindBone("neck");var head=pose.FindBone("head_pos");
            if(neck==null || head==null || !(ReferenceEquals(neck.Parent,upper)||(npcId==44&&pose.FindBone("neck_pos")!=null&&ReferenceEquals(neck.Parent,pose.FindBone("neck_pos"))&&ReferenceEquals(neck.Parent.Parent,upper)&&neck.Parent.Data.TransformMode==TransformMode.Normal)) || !ReferenceEquals(head.Parent,neck))
                throw new InvalidOperationException("TorsoHeadHierarchyMismatch");
            if(pose.TransformConstraints.Count!=0 || pose.PathConstraints.Count!=0)
                throw new InvalidOperationException("UnverifiedTorsoConstraints");
            foreach(var ik in pose.IkConstraints)
                foreach(var bone in ik.Bones)
                    if(ReferenceEquals(bone,control) || ReferenceEquals(bone,upper) || ReferenceEquals(bone,lower))
                        throw new InvalidOperationException("TorsoIKUnverified");
            VerifyArm(pose,"L_hand_pos","L_HandIK","L_arm","L_forearm",upper,structural);
            VerifyArm(pose,"R_hand_pos","R_HandIK","R_arm","R_shoulder3",upper,structural);
            var hips=pose.FindBone("hips");
            if(hips==null || !ReferenceEquals(hips.Parent,lower.Parent))
                throw new InvalidOperationException("TorsoHipHierarchyMismatch");
        }
        private static void VerifyArm(Skeleton pose,string name,string targetName,string first,string second,Bone upper,bool structural)
        {
            var control=pose.FindBone(name);var ik=pose.FindIkConstraint(targetName);
            if(control==null || !ReferenceEquals(control.Parent,pose.RootBone) || ik==null ||
                ik.Target==null || !ReferenceEquals(ik.Target.Parent,control) || ik.Bones.Count!=2 ||
                ik.Bones.Items[0].Data.Name!=first || (!structural&&ik.Bones.Items[1].Data.Name!=second) ||
                !ReferenceEquals(ik.Bones.Items[1].Parent,ik.Bones.Items[0]) ||
                ik.Bones.Items[0].Parent==null || !ReferenceEquals(ik.Bones.Items[0].Parent.Parent,upper))
                throw new InvalidOperationException("TorsoArmRigMismatch:"+name);
        }
        internal static bool TryCreate(Skeleton privatePose,out MonitorTorsoOffsets result,out string reason,bool structural=false,int npcId=-1)
        {
            result=null;reason=null;
            try{if(privatePose==null)throw new InvalidOperationException("PrivatePoseUnavailable");result=new MonitorTorsoOffsets(privatePose,structural,npcId);return true;}
            catch(Exception error){reason=error is InvalidOperationException?error.Message:"TorsoRigValidationFailure:"+error.GetType().Name;return false;}
        }
        internal void RestoreBeforeAnimation()
        {if(!applied)return;control.Rotation=rotation;applied=false;}
        internal bool ApplyAfterAnimation(MonitorTorsoValues values,out string reason)
        {
            reason=null;if(applied){reason="TorsoRestoreRequired";return false;}
            if(values==null)return true;
            if(!values.Valid){reason="InvalidTorsoOffsets";return false;}
            float next=control.Rotation+values.Rotation;
            if(float.IsNaN(next)||float.IsInfinity(next)){reason="NonfiniteTorsoResult";return false;}
            if(values.Rotation==0)return true;
            rotation=control.Rotation;control.Rotation=next;applied=true;return true;
        }
    }
}
