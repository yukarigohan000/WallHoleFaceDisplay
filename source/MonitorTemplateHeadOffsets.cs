using System;
using Spine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Normal NPC rigs have no head_pos. Accept head only after checking the actual
    // hierarchy, every skin's face/hair weights and all constraints. No guessed target.
    internal sealed class MonitorTemplateHeadOffsets
    {
        private readonly Skeleton pose;private readonly Bone head;private float rotation;private bool applied;
        private MonitorTemplateHeadOffsets(Skeleton pose)
        {
            this.pose=pose;head=pose.FindBone("head");
            if(head==null||head.Parent==null||head.Parent.Data.Name!="neck"||pose.FindBone("head_pos")!=null)throw new InvalidOperationException("NormalHeadHierarchyUnavailable");
            if(pose.TransformConstraints.Count!=0||pose.PathConstraints.Count!=0)throw new InvalidOperationException("NormalHeadConstraintsUnverified");
            foreach(var b in pose.Bones)if(Under(b)&&b.Data.TransformMode!=TransformMode.Normal)throw new InvalidOperationException("NormalHeadInheritanceUnverified");
            foreach(var ik in pose.IkConstraints)foreach(var b in ik.Bones)if(Under(b))throw new InvalidOperationException("NormalHeadIKUnverified");
            foreach(var name in new[]{"Head","EyeWhite","Eye","Mouth","HairFront","HairBack","Face","EyeBrow","Nose"}){
                var slot=pose.FindSlot(name);if(slot==null)throw new InvalidOperationException("NormalFaceSlotMissing:"+name);bool found=false;
                foreach(var skin in pose.Data.Skins)foreach(var entry in skin.GetAttachments())if(entry.SlotIndex==slot.Data.Index){Validate(slot,entry.Attachment,true);found=true;}
                if(!found)throw new InvalidOperationException("NormalFaceAttachmentsMissing:"+name);
            }
            // Include effects/accessories and runtime skin, not just slot labels.
            foreach(var skin in pose.Data.Skins)foreach(var entry in skin.GetAttachments())Validate(pose.Slots.Items[entry.SlotIndex],entry.Attachment,false);
            if(pose.Skin!=null)foreach(var entry in pose.Skin.GetAttachments())Validate(pose.Slots.Items[entry.SlotIndex],entry.Attachment,false);
            foreach(var slot in pose.Slots)if(slot.Attachment!=null)Validate(slot,slot.Attachment,false);
        }
        private bool Under(Bone b){for(;b!=null;b=b.Parent)if(ReferenceEquals(b,head))return true;return false;}
        private void Validate(Slot slot,Attachment attachment,bool required)
        {
            var vertex=attachment as VertexAttachment;bool inside=false,outside=false;
            if(vertex!=null&&vertex.Bones!=null){int i=0;var bones=vertex.Bones;while(i<bones.Length){int count=bones[i++];if(count<=0||count>bones.Length-i)throw new InvalidOperationException("InvalidHeadWeights");while(count-->0){int index=bones[i++];if(index<0||index>=pose.Bones.Count)throw new InvalidOperationException("InvalidHeadBoneIndex");if(Under(pose.Bones.Items[index]))inside=true;else outside=true;}}}
            else if(vertex!=null||attachment is RegionAttachment){inside=Under(slot.Bone);outside=!inside;}
            else if(required)throw new InvalidOperationException("HeadGeometryUnavailable");
            if((required&&!inside)||(inside&&outside))throw new InvalidOperationException("MixedOrUnverifiedHeadWeights:"+slot.Data.Name);
        }
        internal static bool TryCreate(Skeleton pose,out MonitorTemplateHeadOffsets result,out string reason)
        {result=null;reason=null;try{result=new MonitorTemplateHeadOffsets(pose);return true;}catch(Exception e){reason=e.Message;return false;}}
        internal void RestoreBeforeAnimation(){if(applied){head.Rotation=rotation;applied=false;}}
        internal void Apply(float value){if(applied)throw new InvalidOperationException("Head restore required");if(float.IsNaN(value)||float.IsInfinity(value)||Math.Abs(value)>180)throw new InvalidOperationException("Invalid head angle");if(value==0)return;rotation=head.Rotation;head.Rotation=rotation+value;applied=true;}
    }
}
