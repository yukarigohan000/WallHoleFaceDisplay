using System;
using System.Collections;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    [BepInPlugin("local.madisland.wallholefacedisplay", "WallHoleFaceDisplay", "0.2.78")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal const string SourceId = "gen_asswall_00", FacilityId = "whfd_asswall_00";
        private const string FacilityName = "壁尻（試作）";
        private static Plugin instance;
        private Harmony harmony;
        private Coroutine pending;
        private bool registering, failed;
        private GameObject root;
        private ItemManager items;
        private CraftManager crafts;
        private ItemData definition;
        private WallDecoration decoration;
        private PrototypeItemRegistration prototype;
        private PrototypeNpcIntegration prototypeNpc;

        private void Awake()
        {
            try
            {
                Config.SaveOnConfigSet=false; // Bind/read must not require a writable config directory.
                if (!Config.Bind("General", "Enabled", true, "Enable after restart.").Value) return;
                // The installed game's path/hash/patch version is not a compatibility contract.
                // Native API lookups, reviewed patch shapes and asset checks remain in their owners.
                try{MonitorDebugSettings.Initialize(Config,text=>Logger.LogWarning(text));}
                catch(Exception error){Logger.LogWarning("Optional debug configuration unavailable; normal registration retained: "+error.GetType().Name);}
                MonitorPerformance.Initialize(Paths.ConfigPath,text=>Logger.LogInfo(text)); /*WHFDPerfEvent*/
                instance = this;
                prototype = new PrototypeItemRegistration(Logger);
                harmony = new Harmony("local.madisland.wallholefacedisplay");
                harmony.PatchAll(typeof(Plugin).Assembly);
                prototypeNpc = new PrototypeNpcIntegration(Logger);
                try{prototypeNpc.Install();}
                catch(Exception error){Logger.LogError("NPC compatibility API unavailable; item registration and other monitors retained: "+error);}
                try { decoration = new WallDecoration(Config, Logger); }
                catch (Exception error) { Logger.LogWarning("Decoration unavailable; facility registration retained: " + error); }
                SceneManager.sceneLoaded += SceneLoaded;
                SceneManager.sceneUnloaded += SceneUnloaded;
                Logger.LogInfo("WallHoleFaceDisplay loaded. Facility registration pending; NPC ID branches unchanged.");
                Schedule();
                // Generate a first-install cfg if possible; never rewrite an existing user's cfg.
                try{if(!System.IO.File.Exists(Config.ConfigFilePath))Config.Save();}
                catch(Exception error){Logger.LogWarning("Config defaults active in memory; initial cfg could not be written: "+error.GetType().Name);}
            }
            catch (Exception error) { Logger.LogError(error); Cleanup(); }
        }
        private void SceneLoaded(Scene scene, LoadSceneMode mode) { Schedule(); }
        private void Schedule()
        {
            if (pending != null) StopCoroutine(pending);
            pending = StartCoroutine(RegisterWhenReady());
        }
        private IEnumerator RegisterWhenReady()
        {
            yield return null;
            for (int i = 0; i < 120 && !failed; i++)
            {
                if (EnsureRegistered()) { pending = null; yield break; }
                yield return null;
            }
            pending = null;
            if (!failed) Logger.LogWarning("Facility not registered: gameplay Managers/source recipes not ready within 120 frames.");
        }
        private bool EnsureRegistered()
        {
            if (registering || failed) return false;
            var go = GameObject.Find("Managers");
            var managers = go == null ? null : go.GetComponent<ManagersScript>();
            if (managers == null || !managers.allSet || managers.itemMN == null || managers.craftMN == null ||
                managers.inventory == null || managers.workshopItemMN == null || managers.workshopItemMN.workshopItemData == null)
                return false;
            if (definition != null && items == managers.itemMN && crafts == managers.craftMN) { prototype.EnsureRegistered(managers); return true; }
            var categories = managers.craftMN.craftData;
            if (categories == null || categories.Length == 0 || categories.Any(c => c == null || c.craftInfo == null)) return false;
            registering = true;
            try
            {
                Release();
                var source = managers.itemMN.FindItem(SourceId);
                if (source == null || source.itemObj == null) return false;
                if (source.itemType != ItemData.ItemType.Prop || source.itemObj.GetComponent<ItemInfo>() == null)
                    throw new InvalidOperationException("Source is not a placeable facility");
                if (managers.itemMN.FindItem(FacilityId) != null || categories.Any(c => c.craftInfo.Any(r => r != null && r.name == FacilityId)))
                    throw new InvalidOperationException("Facility ID collision: " + FacilityId);
                root = new GameObject("WallHoleFaceDisplayDefinitions");
                root.SetActive(false);
                var cloned = Object.Instantiate(source.gameObject, root.transform, false);
                cloned.name = FacilityId;
                definition = cloned.GetComponent<ItemData>();
                definition.itemName = FacilityName;
                definition.localizeName = source.localizeName == null ? new string[10] : (string[])source.localizeName.Clone();
                for (int i = 0; i < definition.localizeName.Length; i++) definition.localizeName[i] = FacilityName;
                var placed = Object.Instantiate(source.itemObj, root.transform, false);
                placed.name = FacilityId;
                placed.GetComponent<ItemInfo>().itemKey = FacilityId;
                definition.itemObj = placed;
                // Preserve all source components, geometry, colliders, animations and asset references.
                // Compatibility-only definition: not published to any craft/acquisition list.
                items = managers.itemMN; crafts = managers.craftMN;
                Logger.LogInfo("Facility registered: " + SourceId + " -> " + FacilityId + "; name=" + FacilityName + "; legacy save compatibility only; new crafting/placement disabled.");
                prototype.EnsureRegistered(managers);
                return true;
            }
            catch (Exception error)
            {
                failed = true;
                Release();
                Logger.LogError("Facility registration failed; source definition unchanged: " + error);
                return false;
            }
            finally { registering = false; }
        }
        private void SceneUnloaded(Scene scene)
        {
            if(prototype!=null)prototype.SceneUnloaded(scene.handle);
            if (decoration != null) decoration.OwnerSceneUnloaded(scene.handle);
            if (root != null && root.scene.handle == scene.handle) Release();
        }
        private void Release()
        {
            if(prototype!=null)prototype.Release();
            // No legacy recipe was published. Never mutate vanilla/other-mod craft arrays here.
            definition = null; items = null; crafts = null;
            if (root != null) Object.Destroy(root);
            root = null;
        }
        private void Cleanup()
        {
            try { if(prototypeNpc!=null)prototypeNpc.Dispose(); } catch(Exception error) { Logger.LogError(error); }
            prototypeNpc=null;
            if (decoration != null) { decoration.Dispose(); decoration = null; }
            MonitorPerformance.Shutdown(); /*WHFDPerfEvent*/
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneUnloaded -= SceneUnloaded;
            if (pending != null) { StopCoroutine(pending); pending = null; }
            Release();
            try { if (harmony != null) harmony.UnpatchSelf(); }
            catch (Exception error) { Logger.LogError(error); }
            finally { harmony = null; if (instance == this) instance = null; }
        }
        internal static bool EnsureIntegratedMonitor(ItemInfo owner)
        {return instance!=null&&instance.decoration!=null&&instance.decoration.EnsureIntegrated(owner);}
        internal static void ReleaseIntegratedMonitor(ItemInfo owner)
        {if(instance!=null&&instance.decoration!=null)instance.decoration.ReleaseIntegrated(owner);}
        private void OnDestroy() { Cleanup(); }

        private void Update()
        {
            if (decoration == null) return;
            try { using(MonitorPerformance.Measure(MonitorPerfStage.Update)) { decoration.Tick(); } }
            catch (Exception error)
            {
                Logger.LogError("Decoration stopped; facility registration retained: " + error);
                decoration.Dispose(); decoration = null;
            }
        }

        private bool framingUiError;
        private void OnGUI()
        {
            if (!MonitorDebugSettings.Enabled || decoration == null || framingUiError) return;
            bool enabled = GUI.enabled;
            try { decoration.DrawFramingUI(); }
            catch (Exception error) { framingUiError = true; decoration.CancelFramingUI(); Logger.LogWarning("Framing UI disabled: " + error.GetType().Name); }
            finally { GUI.enabled = enabled; }
        }

        private void LateUpdate()
        {
            if (decoration == null) return;
            try { using(MonitorPerformance.Measure(MonitorPerfStage.LateUpdate)) { decoration.TickFaceAnimation(); }
                if(!MonitorRenderGate.CompletesOnMainCamera)MonitorPerformance.EndFrame(Time.frameCount); /*WHFDPerfEvent*/ }
            catch (Exception error)
            {
                Logger.LogError("Face playback stopped; facility registration retained: " + error.GetType().Name);
                decoration.Dispose(); decoration = null;
            }
        }

        // Registration lookup only; no NPC, UI, unlock or existing ID branch patches.
        [HarmonyPatch(typeof(ItemManager), nameof(ItemManager.FindItem))]
        private static class FindFacility
        {
            private static void Postfix(ItemManager __instance, string itemKey, ref ItemData __result)
            {
                if ((itemKey != FacilityId && itemKey != PrototypeItemRegistration.Id) || __result != null || instance == null || instance.registering || (instance.prototype!=null&&instance.prototype.Registering)) return;
                if (instance.EnsureRegistered() && instance.items == __instance) __result = itemKey==FacilityId?instance.definition:instance.prototype.Find(__instance);
            }
        }
    }
}
