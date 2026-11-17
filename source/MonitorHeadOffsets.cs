using System;
using Spine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Owns local values only on the caller's private monitor Skeleton.
    internal sealed class MonitorHeadOffsets
    {
        private readonly Skeleton pose;
        private readonly Bone control;
        private readonly Bone auxiliary,upper;
        private readonly Slot rootEffect;
        private float auxiliaryX,auxiliaryY,auxiliaryRotation;
        private bool auxiliaryApplied,captured;
        private float oldA,oldB,oldC,oldD,oldX,oldY;
        private readonly float[] effectWorld;
        internal bool RequiresWorldCapture(float headAngle,float torsoAngle){return (auxiliary!=null&&headAngle!=0)||(rootEffect!=null&&rootEffect.Attachment!=null&&(headAngle!=0||torsoAngle!=0));}
        private float rotation;
        private bool applied;
        private MonitorHeadOffsets(Skeleton pose,bool structural,int npcId)
        {
            this.pose=pose;control=pose.FindBone("head_pos");
            var head=pose.FindBone("head");
            if(structural&&(npcId==16||npcId==44||npcId==90)){
                upper=pose.FindBone("spine_02");
                var neck=pose.FindBone("neck");var neckPos=pose.FindBone("neck_pos");
                if(upper==null||neck==null||!(ReferenceEquals(neck.Parent,upper)||(npcId==44&&neckPos!=null&&ReferenceEquals(neck.Parent,neckPos)&&ReferenceEquals(neckPos.Parent,upper))))throw new InvalidOperationException("ReviewedFaceParentMismatch");
                if(npcId==44||npcId==90){
                    auxiliary=pose.FindBone(npcId==44?"hairback":"bone4");
                    if(auxiliary==null||!ReferenceEquals(auxiliary.Parent,upper)||auxiliary.Children.Count!=0||auxiliary.Data.TransformMode!=TransformMode.Normal)throw new InvalidOperationException("ReviewedHairRigMismatch");
                    foreach(var ik in pose.IkConstraints){if(ReferenceEquals(ik.Target,auxiliary))throw new InvalidOperationException("AuxiliaryHairIKTarget");}
                }
                if(npcId==16){
                    rootEffect=pose.FindSlot("Drool");
                    if(rootEffect==null||!ReferenceEquals(rootEffect.Bone,pose.RootBone))throw new InvalidOperationException("ReviewedDroolRigMismatch");
                    int count=0;foreach(var skin in pose.Data.Skins)foreach(var entry in skin.GetAttachments())if(entry.SlotIndex==rootEffect.Data.Index){var mesh=entry.Attachment as MeshAttachment;if(mesh==null||mesh.Bones!=null||mesh.WorldVerticesLength<=0||mesh.WorldVerticesLength>4096)throw new InvalidOperationException("ReviewedDroolMeshMismatch");count=Math.Max(count,mesh.WorldVerticesLength);}
                    if(count==0)throw new InvalidOperationException("ReviewedDroolMissing");effectWorld=new float[count];
                }
            }
            if(control==null || control.Parent==null || control.Parent.Data.Name!="neck" || head==null || !ReferenceEquals(head.Parent,control))
                throw new InvalidOperationException("HeadHierarchyMismatch");
            if(pose.TransformConstraints.Count!=0 || pose.PathConstraints.Count!=0)
                throw new InvalidOperationException("UnverifiedHeadConstraints");
            foreach(var bone in pose.Bones)
                if(Descendant(bone) && bone.Data.TransformMode!=TransformMode.Normal)
                    throw new InvalidOperationException("HeadInheritanceMismatch:"+bone.Data.Name);
            foreach(var ik in pose.IkConstraints)
                foreach(var bone in ik.Bones)
                    if(Descendant(bone))throw new InvalidOperationException("HeadIKUnverified");
            // Slots on root can still be fully weighted to head descendants.
            // Inspect every skin variant so animation attachment changes stay safe.
            foreach(var name in (structural?new[]{"Head","EyeWhite","Eye","EyeBrow","Mouth","HairFront","HairBack","HighLight","Face","Nose","face_red","face_blue","Tatoo","Tears","Acce","Ear","Drool"}:new[]{"Head","EyeWhite","Eye","EyeBrow","Mouth","HairFront","HairBack","HighLight","Face","Nose","face_red","face_blue","Tatoo","Tears","Acce"}))
            {
                var slot=pose.FindSlot(name);
                if(slot==null){if(structural&&Array.IndexOf(new[]{"Head","EyeWhite","Eye","Mouth","HairFront","HairBack","Face","EyeBrow","Nose"},name)<0)continue;throw new InvalidOperationException("HeadSlotMissing:"+name);}
                bool found=false;
                foreach(var skin in pose.Data.Skins)
                    foreach(var entry in skin.GetAttachments())
                        if(entry.SlotIndex==slot.Data.Index){Validate(slot,entry.Attachment);found=true;}
                if(pose.Skin!=null)
                    foreach(var entry in pose.Skin.GetAttachments())
                        if(entry.SlotIndex==slot.Data.Index)Validate(slot,entry.Attachment);
                if(slot.Attachment!=null)Validate(slot,slot.Attachment);
                if(!found)throw new InvalidOperationException("HeadAttachmentsMissing:"+name);
            }
            if(npcId==16||npcId==44||npcId==90)foreach(var name in new[]{"Highlight","face_blood"}){var slot=pose.FindSlot(name);if(slot!=null)foreach(var skin in pose.Data.Skins)foreach(var entry in skin.GetAttachments())if(entry.SlotIndex==slot.Data.Index)Validate(slot,entry.Attachment);}
            if(structural){
                foreach(var skin in pose.Data.Skins)foreach(var entry in skin.GetAttachments())ValidateMixed(pose.Slots.Items[entry.SlotIndex],entry.Attachment);
                if(pose.Skin!=null)foreach(var entry in pose.Skin.GetAttachments())ValidateMixed(pose.Slots.Items[entry.SlotIndex],entry.Attachment);
                foreach(var slot in pose.Slots)if(slot.Attachment!=null)ValidateMixed(slot,slot.Attachment);
            }
        }
        private void ValidateMixed(Slot slot,Attachment attachment)
        {
            var vertex=attachment as VertexAttachment;bool inside=false,outside=false;
            if(vertex!=null&&vertex.Bones!=null){int i=0;var bones=vertex.Bones;
                while(i<bones.Length){int count=bones[i++];if(count<=0||count>bones.Length-i)throw new InvalidOperationException("InvalidHeadWeights");
                    while(count-->0){int index=bones[i++];if(index<0||index>=pose.Bones.Count)throw new InvalidOperationException("InvalidHeadBoneIndex");if(auxiliary!=null&&ReferenceEquals(pose.Bones.Items[index],auxiliary)&&slot.Data.Name!="HairFront"&&slot.Data.Name!="HairBack")throw new InvalidOperationException("HairBoneUsedOutsideReviewedSlots:"+slot.Data.Name);if(Descendant(pose.Bones.Items[index]))inside=true;else outside=true;}}}
            if(inside&&outside)throw new InvalidOperationException("MixedHeadWeights:"+slot.Data.Name);
        }
        private bool Descendant(Bone bone)
        {for(;bone!=null;bone=bone.Parent)if(ReferenceEquals(bone,control)||ReferenceEquals(bone,auxiliary))return true;return false;}
        private void Validate(Slot slot,Attachment attachment)
        {
            var vertex=attachment as VertexAttachment;
            if(ReferenceEquals(slot,rootEffect)&&rootEffect!=null&&attachment is MeshAttachment&&vertex.Bones==null&&vertex.WorldVerticesLength<=effectWorld.Length)return;
            if(vertex!=null && vertex.Bones!=null) {
                var bones=vertex.Bones;int i=0;
                while(i<bones.Length){int count=bones[i++];if(count<=0 || count>bones.Length-i)throw new InvalidOperationException("InvalidHeadWeights");
                    while(count-->0){int index=bones[i++];if(index<0 || index>=pose.Bones.Count || !Descendant(pose.Bones.Items[index]))
                        throw new InvalidOperationException("HeadWeightOutsideHierarchy:"+slot.Data.Name+"/"+attachment.Name);}}
            } else if((attachment is RegionAttachment || vertex!=null) && Descendant(slot.Bone))return;
            else throw new InvalidOperationException("HeadAttachmentUnverified:"+slot.Data.Name+"/"+attachment.Name);
        }
        internal static bool TryCreate(Skeleton privatePose,out MonitorHeadOffsets result,out string reason,bool structural=false,int npcId=-1)
        {
            result=null;reason=null;
            try{if(privatePose==null)throw new InvalidOperationException("PrivatePoseUnavailable");result=new MonitorHeadOffsets(privatePose,structural,npcId);return true;}
            catch(Exception error){reason=error is InvalidOperationException?error.Message:"HeadRigValidationFailure:"+error.GetType().Name;return false;}
        }
        private static void ToLocal(Bone bone,ref float x,ref float y){float dx=x-bone.WorldX,dy=y-bone.WorldY;float det=bone.A*bone.D-bone.B*bone.C;if(!Finite(det)||Math.Abs(det)<1e-8f)throw new InvalidOperationException("SingularFaceBasis");x=(bone.D*dx-bone.B*dy)/det;y=(bone.A*dy-bone.C*dx)/det;}
        internal void CaptureBeforeOffsets(){
            captured=false;if(rootEffect==null||rootEffect.Attachment==null)return;
            var mesh=rootEffect.Attachment as MeshAttachment;if(mesh==null||mesh.Bones!=null||mesh.WorldVerticesLength<=0||mesh.WorldVerticesLength>effectWorld.Length)throw new InvalidOperationException("LiveDroolMeshUnverified");
            oldA=control.A;oldB=control.B;oldC=control.C;oldD=control.D;oldX=control.WorldX;oldY=control.WorldY;
            mesh.ComputeWorldVertices(rootEffect,0,mesh.WorldVerticesLength,effectWorld,0,2);captured=true;
        }
        internal void ApplyAfterWorld(){
            if(!captured)return;captured=false;var mesh=(MeshAttachment)rootEffect.Attachment;
            float det=oldA*oldD-oldB*oldC;if(!Finite(det)||Math.Abs(det)<1e-8f)throw new InvalidOperationException("SingularPreviousHeadBasis");
            rootEffect.Deform.Clear();
            for(int i=0;i<mesh.WorldVerticesLength;i+=2){float dx=effectWorld[i]-oldX,dy=effectWorld[i+1]-oldY;float lx=(oldD*dx-oldB*dy)/det,ly=(oldA*dy-oldC*dx)/det;
                float x=control.A*lx+control.B*ly+control.WorldX,y=control.C*lx+control.D*ly+control.WorldY;ToLocal(rootEffect.Bone,ref x,ref y);if(!Finite(x)||!Finite(y))throw new InvalidOperationException("NonfiniteEffectCorrection");rootEffect.Deform.Add(x);rootEffect.Deform.Add(y);
            }
        }
        internal void RestoreBeforeAnimation()
        {captured=false;if(auxiliaryApplied){auxiliary.X=auxiliaryX;auxiliary.Y=auxiliaryY;auxiliary.Rotation=auxiliaryRotation;auxiliaryApplied=false;}if(!applied)return;control.Rotation=rotation;applied=false;}
        private static bool Finite(float v){return !float.IsNaN(v)&&!float.IsInfinity(v);}
        internal bool ApplyAfterAnimation(MonitorHeadValues values,out string reason)
        {
            reason=null;if(applied){reason="HeadRestoreRequired";return false;}
            if(values==null)return true;
            if(!values.Valid){reason="InvalidHeadOffsets";return false;}
            float nr=control.Rotation+values.Rotation;
            if(!Finite(nr)){reason="NonfiniteHeadResult";return false;}
            if(values.Rotation==0)return true;
            if(auxiliary!=null){
                for(var b=control.Parent;!ReferenceEquals(b,upper);b=b.Parent){if(b==null||b.Data.TransformMode!=TransformMode.Normal||b.ScaleX<=0||Math.Abs(b.ScaleX-b.ScaleY)>.00001f||Math.Abs(b.ShearX)>.00001f||Math.Abs(b.ShearY)>.00001f)throw new InvalidOperationException("NonconformalHeadParent");}
                float px=control.WorldX,py=control.WorldY;ToLocal(upper,ref px,ref py);
                auxiliaryX=auxiliary.X;auxiliaryY=auxiliary.Y;auxiliaryRotation=auxiliary.Rotation;
                double angle=values.Rotation*Math.PI/180;float co=(float)Math.Cos(angle),si=(float)Math.Sin(angle),dx=auxiliaryX-px,dy=auxiliaryY-py;
                auxiliary.X=px+co*dx-si*dy;auxiliary.Y=py+si*dx+co*dy;auxiliary.Rotation=auxiliaryRotation+values.Rotation;auxiliaryApplied=true;
            }
            rotation=control.Rotation;
            control.Rotation=nr;applied=true;return true;
        }
    }
}
