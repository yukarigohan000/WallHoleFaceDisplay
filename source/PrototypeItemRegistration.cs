using System;using System.Linq;using BepInEx.Logging;using UnityEngine;using Object=UnityEngine.Object;
namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Phase 1 only: reuse the existing private-definition registration mechanism.
    // No NPC, placement, inventory or save patches are installed here.
    internal sealed class PrototypeItemRegistration
    {
        internal const string Id="whfd_prototype_00", DisplayName="壁尻（モニター付き）";
        internal const string Description="拘束した者の顔と名前をモニターに映し出す壁型設備。";
        internal const int CraftNpcId=113,CraftCategory=16;
        private PrototypeItemIcon icon;
        private readonly ManualLogSource log;
        private bool failed;
        internal bool Registering {get;private set;}
        private GameObject root;
        private ItemManager items;private CraftManager crafts;
        private ItemData definition;private CraftInfo recipe;
        private CraftManager.CraftData[] original,published;
        internal PrototypeItemRegistration(ManualLogSource log){this.log=log;}
        internal ItemData Find(ItemManager owner){return !Registering&&items==owner?definition:null;}
        internal bool EnsureRegistered(ManagersScript managers)
        {
            if(Registering||failed)return false;
            if(managers==null||!managers.allSet||managers.itemMN==null||managers.craftMN==null||managers.inventory==null||managers.workshopItemMN==null||managers.workshopItemMN.workshopItemData==null)return false;
            if(definition!=null&&items==managers.itemMN&&crafts==managers.craftMN)return true;
            var categories=managers.craftMN.craftData;
            if(categories==null||categories.Length<=CraftCategory||categories.Any(c=>c==null||c.craftInfo==null))return false;
            Registering=true;
            try {
                Release();
                var source=managers.itemMN.FindItem(Plugin.SourceId);
                if(source==null||source.itemObj==null)return false;
                if(source.itemType!=ItemData.ItemType.Prop||source.itemObj.GetComponent<ItemInfo>()==null)throw new InvalidOperationException("Prototype source is not placeable");
                if(managers.itemMN.FindItem(Id)!=null||categories.Any(c=>c.craftInfo.Any(r=>r!=null&&r.name==Id)))throw new InvalidOperationException("Prototype ID collision: "+Id);
                var sourceRecipe=source.GetComponent<CraftInfo>();
                if(sourceRecipe==null||!categories.Any(c=>c.craftInfo.Contains(sourceRecipe)))return false;
                if(categories[CraftCategory].craftInfo.Length>=managers.inventory.craftCount)throw new InvalidOperationException("NPC ID 113 craft category is full");
                var materialIds=new[]{"ironingot_01","titanium_01","glass_01"};var counts=new[]{5f,3f,10f};
                var required=new CraftInfo.Required[materialIds.Length];for(int i=0;i<materialIds.Length;i++){
                    var material=managers.itemMN.FindItem(materialIds[i]);if(material==null||material.name!=materialIds[i])throw new InvalidOperationException("Craft material unavailable: "+materialIds[i]);
                    required[i]=new CraftInfo.Required{itemData=material,count=counts[i]};
                }
                root=new GameObject("WHFD_Prototype_Definitions");root.SetActive(false);
                var item=Object.Instantiate(source.gameObject,root.transform,false);item.name=Id;
                definition=item.GetComponent<ItemData>();recipe=item.GetComponent<CraftInfo>();
                if(definition==null||recipe==null)throw new InvalidOperationException("Cloned prototype definition incomplete");
                definition.itemName=DisplayName;
                definition.localizeName=source.localizeName==null?new string[10]:(string[])source.localizeName.Clone();
                for(int i=0;i<definition.localizeName.Length;i++)definition.localizeName[i]=DisplayName;
                definition.TipsText=Description;definition.localizedTipsText=source.localizedTipsText==null?new string[10]:(string[])source.localizedTipsText.Clone();
                for(int i=0;i<definition.localizedTipsText.Length;i++)definition.localizedTipsText[i]=Description;
                icon=new PrototypeItemIcon();definition.itemSprite=icon.Sprite;
                recipe.required=required;recipe.shopTrade=CopyMaterials(sourceRecipe.shopTrade);
                var placed=Object.Instantiate(source.itemObj,root.transform,false);placed.name=Id;
                var info=placed.GetComponent<ItemInfo>();if(info==null)throw new InvalidOperationException("Prototype placement ItemInfo missing");
                info.itemKey=Id;placed.AddComponent<IntegratedMonitorFacility>();definition.itemObj=placed;
                // Placement geometry/collider/native components remain cloned; only private item metadata changes.
                var next=(CraftManager.CraftData[])categories.Clone();
                next[CraftCategory]=new CraftManager.CraftData{craftName=categories[CraftCategory].craftName,craftInfo=categories[CraftCategory].craftInfo.Concat(new[]{recipe}).ToArray()};
                items=managers.itemMN;crafts=managers.craftMN;original=categories;published=next;crafts.craftData=next;
                log.LogInfo("[WHFD_Prototype] registered; source="+Plugin.SourceId+"; itemId="+Id+"; displayName="+DisplayName+"; craftNpcId=113; craftCategory=16; result=1");
                return true;
            }catch(Exception error){failed=true;Release();log.LogError("[WHFD_Prototype] registration failed; existing facility retained: "+error);return false;}
            finally{Registering=false;}
        }
        private static CraftInfo.Required[] CopyMaterials(CraftInfo.Required[] source){return source==null?null:source.Select(p=>p==null?null:new CraftInfo.Required{itemData=p.itemData,count=p.count}).ToArray();}
        internal void SceneUnloaded(int handle){if(root!=null&&root.scene.handle==handle)Release();}
        internal void Release()
        {
            if(crafts!=null&&recipe!=null&&crafts.craftData!=null){
                if(ReferenceEquals(crafts.craftData,published))crafts.craftData=original;
                else {
                    var next=(CraftManager.CraftData[])crafts.craftData.Clone();
                    for(int i=0;i<next.Length;i++)if(next[i]!=null&&next[i].craftInfo!=null&&next[i].craftInfo.Contains(recipe))next[i]=new CraftManager.CraftData{craftName=next[i].craftName,craftInfo=next[i].craftInfo.Where(r=>r!=recipe).ToArray()};
                    crafts.craftData=next;
                }
            }
            definition=null;recipe=null;items=null;crafts=null;original=null;published=null;
            if(icon!=null)icon.Dispose();icon=null;
            if(root!=null)Object.Destroy(root);root=null;
        }
    }
}
