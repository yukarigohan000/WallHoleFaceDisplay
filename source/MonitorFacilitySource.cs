using System;
using System.Collections.Generic;
using Spine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    internal static class MonitorFacilitySource
    {
        internal static MonitorNpcAssetSource Load(ManagersScript managers,int id,CommonStates individual,MonitorFacilityAnimationSettings tracks)
        {
            using(MonitorPerformance.Measure(MonitorPerfStage.FacilityAssetLoad)) { /*WHFDPerfScope*/
            int index;if(!MonitorFacilityRecipe.TryIndex(id,out index))throw new InvalidOperationException("Facility model mapping is not reviewed for this ID");
            if(id==15)throw new InvalidOperationException("ID15 retains its unchanged compatibility path");
            if(managers==null||managers.sexMN==null||managers.sexMN.sexList==null||managers.sexMN.sexList.Length<=5)throw new InvalidOperationException("Facility asset registry unavailable");
            var group=managers.sexMN.sexList[5];if(group==null||group.sexObj==null||index>=group.sexObj.Length||group.sexObj[index]==null)throw new InvalidOperationException("Native CreateRestrain model unavailable");
            var candidates=group.sexObj[index].GetComponentsInChildren<Spine.Unity.SkeletonAnimation>(true);if(candidates.Length!=1)throw new InvalidOperationException("Facility skeleton missing or ambiguous");
            var template=candidates[0];var data=template.skeletonDataAsset==null?null:template.skeletonDataAsset.GetSkeletonData(false);if(data==null)throw new InvalidOperationException("Facility SkeletonData unavailable");
            var seed=new Skeleton(data){ScaleX=template.initialFlipX?-1:1,ScaleY=template.initialFlipY?-1:1};MonitorPerformance.Count(MonitorPerfEvent.SourceSeedSkeleton); /*WHFDPerfEvent*/if(!string.IsNullOrEmpty(template.initialSkinName))seed.SetSkin(template.initialSkinName);seed.SetToSetupPose();
            if((id==44||id==90)&&(individual==null||individual.npcID!=id))throw new InvalidOperationException("Assigned individual is required for this numeric NPC ID");
            string appearance="facility authored defaults";
            if(individual!=null&&individual.npcID==id){
                if(individual.anim==null||individual.anim.Skeleton==null)throw new InvalidOperationException("Individual appearance not ready; current model retained");
                MonitorFacilityAppearance.TransferAppearance(seed,individual.anim.Skeleton,id);appearance="individual appearance mapped into facility-owned attachments";
            }
            MonitorFacilityRecipe.Prepare(seed,id);
            string[] bloodSlots=null;Func<int?> bloodState=null;
            if(id==44){if(managers.randChar==null||individual==null||individual.parameters==null||individual.parameters.Length<=7)throw new InvalidOperationException("Native ID44 blood state unavailable");bloodSlots=managers.randChar.GetLayeredSlots("blood");bloodState=()=>individual==null||individual.npcID!=44||individual.parameters==null||individual.parameters.Length<=7?(int?)null:individual.parameters[7];}
            return new MonitorNpcAssetSource {BloodSlots=bloodSlots,BloodState=bloodState,NpcId=id,Template=template,Seed=seed,AuditIdentity=MonitorNpcAssetSource.Identity(individual),Appearance="source=gen_restrain_01; native model index="+index+"; "+appearance,FacilityAnimations=tracks};
        
            } /*WHFDPerfEnd*/}
    }
}
