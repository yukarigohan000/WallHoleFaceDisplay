using System;
using System.Collections.Generic;
using Spine;
namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    internal static class MonitorFacilityAppearance
    {
        private static readonly HashSet<string> FaceSlots=new HashSet<string>(StringComparer.Ordinal){"Head","EyeWhite","Eye","EyeBrow","Face","Nose","Mouth","HairFront","HairBack","HairBack2","HairBack_H","HairBack_L","Tatoo","Tears","HighLight","Acce"};
        internal static void TransferAppearance(Skeleton target,Skeleton source,int npcId=-1)
        {
            if(source.Skin!=null){if(ReferenceEquals(source.Data,target.Data))target.SetSkin(source.Skin);else if(source.Skin.Name!="default")throw new InvalidOperationException("Cross-model custom Skin requires a reviewed adapter");}
            foreach(var slot in target.Slots){var from=source.FindSlot(slot.Data.Name);if((npcId==44||npcId==90)&&slot.Data.Name=="BodyBase")from=source.FindSlot("Body");if(npcId==90&&slot.Data.Name=="Highlight")from=source.FindSlot("HighLight");if(from==null)continue;
                slot.R=from.R;slot.G=from.G;slot.B=from.B;slot.A=from.A;slot.R2=from.R2;slot.G2=from.G2;slot.B2=from.B2;
                // New facility assets expose these target-owned body/face variants as well.
                bool extra=(npcId==44||npcId==90)&&Array.IndexOf(new[]{"Highlight","Body","BodyBase","Oppai","Nipple","Ear","EarBack","L_arm","L_forearm","R_arm","R_forearm","L_upleg","L_leg","R_upleg","R_leg"},slot.Data.Name)>=0;
                if(FaceSlots.Contains(slot.Data.Name)||extra){
                    if(from.Attachment==null){slot.Attachment=null;continue;}
                    var mapped=ResolveAttachment(target,slot,source,from);
                    // Verified aliases between this numeric ID's normal and facility assets.
                    // Always resolve in target data, never transplant source mesh weights.
                    if(mapped==null&&npcId==44&&slot.Data.Name=="Head"&&from.Attachment.Name=="Head_00")mapped=target.GetAttachment(slot.Data.Index,"Head");
                    if(mapped==null&&(npcId==44||npcId==90)&&slot.Data.Name=="Body"&&from.Attachment.Name=="Body")mapped=target.GetAttachment(slot.Data.Index,"Body_00");
                    if(mapped==null&&(npcId==44||npcId==90)&&slot.Data.Name=="BodyBase"&&from.Attachment.Name=="Body")mapped=target.GetAttachment(slot.Data.Index,"BodyBase_00");
                    if(mapped==null&&npcId==90&&slot.Data.Name=="Highlight"&&from.Attachment.Name=="HighLight_00")mapped=target.GetAttachment(slot.Data.Index,"Highlight_00");
                    if(mapped==null)throw new InvalidOperationException("Facility appearance attachment unavailable: "+slot.Data.Name);
                    slot.Attachment=mapped; // Re-resolve in target data. Never transplant source weighted geometry.
                }
            }
            target.R=source.R;target.G=source.G;target.B=source.B;target.A=source.A;
        }
        private static Attachment ResolveAttachment(Skeleton target,Slot slot,Skeleton source,Slot from)
        {
            // Attachment.Name may be an atlas/path name, not the skin lookup key.
            // Resolve the live source attachment's actual key, then look it up anew in target data.
            foreach(var skin in new[]{source.Skin,source.Data.DefaultSkin}){
                if(skin==null)continue;
                foreach(var entry in skin.GetAttachments())if(entry.SlotIndex==from.Data.Index&&ReferenceEquals(entry.Attachment,from.Attachment)){
                    var match=target.GetAttachment(slot.Data.Index,entry.Name);if(match!=null)return match;
                }
            }
            return target.GetAttachment(slot.Data.Index,from.Attachment.Name);
        }
    }
}
