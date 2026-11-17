using System;
using Spine;
using Spine.Unity;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    internal sealed class MonitorNpcAssetSource
    {
        internal int NpcId;
        internal SkeletonAnimation Template;
        internal Skeleton Seed;
        internal string Appearance;
        internal string[] BloodSlots;internal Func<int?> BloodState;
        internal string AuditIdentity;
        internal static string Identity(CommonStates npc){
            if(!MonitorDebugSettings.Enabled)return "";
            try{return npc==null?"npcInstanceId=none":"npcInstanceId="+npc.GetInstanceID()+";individualAnimationId="+(npc.anim==null?0:npc.anim.GetInstanceID())+";individualTransformScale="+npc.transform.lossyScale+";individualSkeleton="+(npc.anim==null||npc.anim.Skeleton==null?"unavailable":MonitorTemplatePlayback.AuditPose(npc.anim.Skeleton));}catch(Exception e){return "identityUnavailable="+e.GetType().Name;}
        }
        internal MonitorFacilityAnimationSettings FacilityAnimations;
        internal static MonitorNpcAssetSource Load(ManagersScript managers,int id,CommonStates individual)
        {
            using(MonitorPerformance.Measure(MonitorPerfStage.NormalAssetLoad)) { /*WHFDPerfScope*/
            if(id<0||id==15)throw new InvalidOperationException("ID 15 uses the facility compatibility path");
            SkeletonAnimation template=null;Skeleton seed=null;string appearance;
            if(individual!=null && individual.npcID==id){
                if(individual.anim==null||individual.anim.Skeleton==null)throw new InvalidOperationException("Bound individual's Skeleton is not ready; no default appearance substitution performed");
                template=individual.anim;seed=individual.anim.Skeleton;appearance="facility individual snapshot";
            }else{
                if(managers==null||managers.npcMN==null||managers.npcMN.npcPrefab==null)throw new InvalidOperationException("NPC asset registry unavailable");
                var list=managers.npcMN.npcPrefab;
                if(id>=list.Length||list[id]==null)throw new InvalidOperationException("NPC ID has no prefab in npcPrefab registry");
                var definition=list[id].GetComponent<CommonStates>();
                var candidates=list[id].GetComponentsInChildren<SkeletonAnimation>(true);
                if(candidates.Length!=1)throw new InvalidOperationException("A single SkeletonAnimation is required; multi-model prefabs need an adapter");
                template=definition==null?null:definition.anim;
                if(template==null)template=candidates[0];
                if(template!=candidates[0])throw new InvalidOperationException("Registry main animator differs from the only prefab skeleton");
                if(template.transform!=list[id].transform&&!template.transform.IsChildOf(list[id].transform))throw new InvalidOperationException("Registry animator belongs to another prefab hierarchy");
                appearance="authored asset defaults (no individual generated)";
            }
            if(template.skeletonDataAsset==null)throw new InvalidOperationException("SkeletonDataAsset unavailable");
            var data=template.skeletonDataAsset.GetSkeletonData(false);if(data==null)throw new InvalidOperationException("SkeletonData unavailable");
            if(seed==null){seed=new Skeleton(data){ScaleX=template.initialFlipX?-1:1,ScaleY=template.initialFlipY?-1:1};MonitorPerformance.Count(MonitorPerfEvent.SourceSeedSkeleton); /*WHFDPerfEvent*/if(!string.IsNullOrEmpty(template.initialSkinName))seed.SetSkin(template.initialSkinName);seed.SetToSetupPose();seed.UpdateWorldTransform();}
            if(!ReferenceEquals(seed.Data,data))throw new InvalidOperationException("Instance and asset SkeletonData differ");
            return new MonitorNpcAssetSource {NpcId=id,Template=template,Seed=seed,AuditIdentity=Identity(individual),Appearance=appearance};
        
            } /*WHFDPerfEnd*/}
    }
}
