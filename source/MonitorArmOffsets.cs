using System;
using Spine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Only the caller's private Skeleton is accepted. No AnimationState or game lookup.
    internal sealed class MonitorArmOffsets
    {
        private readonly Bone left,right;
        internal float LeftUnit {get;private set;}
        internal float RightUnit {get;private set;}
        private float leftX,leftY,rightX,rightY;
        private bool leftApplied,rightApplied;
        internal bool HasApplied {get {return leftApplied||rightApplied;}}
        private MonitorArmOffsets(Skeleton pose,bool structural)
        {
            if(pose.TransformConstraints.Count!=0)throw new InvalidOperationException("UnexpectedTransformConstraints");
            if(structural&&pose.PathConstraints.Count!=0)throw new InvalidOperationException("UnverifiedArmPathConstraints");
            left=Resolve(pose,"L_hand_pos","L_HandIK","L_arm","L_forearm",structural);
            right=Resolve(pose,"R_hand_pos","R_HandIK","R_arm","R_shoulder3",structural);
            if(ReferenceEquals(left,right))throw new InvalidOperationException("SharedArmControl");
            LeftUnit=Reach(pose,pose.FindIkConstraint("L_HandIK"));
            RightUnit=Reach(pose,pose.FindIkConstraint("R_HandIK"));
        }
        internal static bool TryCreate(Skeleton privatePose,out MonitorArmOffsets result,out string reason,bool structural=false)
        {
            result=null;reason=null;
            if(privatePose==null){reason="PrivatePoseUnavailable";return false;}
            try{result=new MonitorArmOffsets(privatePose,structural);return true;}
            catch(Exception error){reason=error is InvalidOperationException?error.Message:"RigValidationFailure:"+error.GetType().Name;return false;}
        }
        private static Bone Resolve(Skeleton pose,string controlName,string targetName,string first,string second,bool structural)
        {
            var control=pose.FindBone(controlName);var target=pose.FindBone(targetName);var ik=pose.FindIkConstraint(targetName);
            if(control==null||target==null||ik==null||!ReferenceEquals(control.Parent,pose.RootBone)||!ReferenceEquals(target.Parent,control)||
                !ReferenceEquals(ik.Target,target)||ik.Bones.Count!=2||ik.Bones.Items[0].Data.Name!=first||(!structural&&ik.Bones.Items[1].Data.Name!=second)||
                !ReferenceEquals(ik.Bones.Items[1].Parent,ik.Bones.Items[0]))
                throw new InvalidOperationException("VerifiedArmRigMismatch:"+targetName);
            return control;
        }
        private static float Reach(Skeleton pose,IkConstraint ik)
        {
            var root=pose.RootBone;double determinant=root.A*root.D-root.B*root.C;
            if(double.IsNaN(determinant)||double.IsInfinity(determinant)||Math.Abs(determinant)<1e-12)
                throw new InvalidOperationException("ArmRootScaleUnavailable");
            double reach=0;
            for(int i=0;i<ik.Bones.Count;i++){
                var bone=ik.Bones.Items[i];
                // Actual joint spacing for the first segment; declared end length for
                // the final segment, matching a two-bone IK chain's geometry.
                double wx,wy;
                if(i+1<ik.Bones.Count){var child=ik.Bones.Items[i+1];wx=child.WorldX-bone.WorldX;wy=child.WorldY-bone.WorldY;}
                else {wx=bone.A*bone.Data.Length;wy=bone.C*bone.Data.Length;}
                double x=(root.D*wx-root.B*wy)/determinant,y=(root.A*wy-root.C*wx)/determinant;
                reach+=Math.Sqrt(x*x+y*y);
            }
            if(double.IsNaN(reach)||double.IsInfinity(reach)||reach<=1e-6||reach>float.MaxValue)
                throw new InvalidOperationException("ArmLengthUnavailable");
            return (float)reach; // fixed per private-pose lifetime; root-local units
        }
        private static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
        internal void RestoreBeforeAnimation()
        {
            if(leftApplied){left.X=leftX;left.Y=leftY;leftApplied=false;}
            if(rightApplied){right.X=rightX;right.Y=rightY;rightApplied=false;}
        }
        internal bool ApplyAfterAnimation(MonitorArmValues values,out string reason)
        {
            reason=null;
            if(leftApplied||rightApplied){reason="ArmRestoreRequired";return false;}
            if(values==null)return true;
            if(!values.Valid){reason="InvalidArmOffsets";return false;}
            float lx=left.X+values.LeftX*LeftUnit,ly=left.Y+values.LeftY*LeftUnit;
            float rx=right.X+values.RightX*RightUnit,ry=right.Y+values.RightY*RightUnit;
            if(!Finite(lx)||!Finite(ly)||!Finite(rx)||!Finite(ry)){reason="NonfiniteArmResult";return false;}
            if(values.LeftX!=0||values.LeftY!=0){leftX=left.X;leftY=left.Y;leftApplied=true;left.X=lx;left.Y=ly;}
            if(values.RightX!=0||values.RightY!=0){rightX=right.X;rightY=right.Y;rightApplied=true;right.X=rx;right.Y=ry;}
            return true;
        }
    }
}
