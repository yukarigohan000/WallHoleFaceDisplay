using System;using System.Collections.Generic;using System.Linq;using System.Reflection;using System.Reflection.Emit;using System.Runtime.CompilerServices;using BepInEx.Logging;using HarmonyLib;
namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Identity predicates only. Never changes an ItemInfo, ItemData, slot or NPC.
    internal sealed class PrototypeNpcIntegration:IDisposable
    {
        internal const string Owner="local.madisland.wallholefacedisplay.phase2";
        private readonly ManualLogSource log;private Harmony patches;
        internal bool Enabled {get;private set;}
        internal PrototypeNpcIntegration(ManualLogSource log){this.log=log;}
        internal static bool IsPrototype(string key){return key==PrototypeItemRegistration.Id;}
        internal static bool FacilityEquals(string actual,string expected){return actual==expected||(expected==Plugin.SourceId&&IsPrototype(actual));}
        private static MethodBase Iterator(Type owner,string name)
        {
            var method=AccessTools.Method(owner,name);if(method==null)throw new InvalidOperationException("Missing native iterator "+owner.Name+"."+name);
            var attribute=method.GetCustomAttributes(typeof(IteratorStateMachineAttribute),false).Cast<IteratorStateMachineAttribute>().SingleOrDefault();
            var state=attribute==null?owner.GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic).Single(t=>t.Name.StartsWith("<"+name+">",StringComparison.Ordinal)):attribute.StateMachineType;
            var move=AccessTools.Method(state,"MoveNext");if(move==null)throw new InvalidOperationException("Missing iterator body");return move;
        }
        internal static MethodBase[] Targets(){return new[]{AccessTools.Method(typeof(UIManager),"ItemDescOpen",new[]{typeof(int)}),AccessTools.Method(typeof(UIManager),"PropPanelTypeChange",new[]{typeof(InventorySlot),typeof(UnityEngine.GameObject)}),Iterator(typeof(GameManager),"ToiletCheck"),Iterator(typeof(UIManager),"PropAction")};}
        internal bool Install()
        {
            if(Enabled)return true;
            try{
                var targets=Targets();if(targets.Any(t=>t==null))throw new InvalidOperationException("Missing phase 2 target");
                // Validate all four original method bodies before installing any patch.
                foreach(var target in targets)Transform(PatchProcessor.GetOriginalInstructions(target),target);
                patches=new Harmony(Owner);
                foreach(var target in targets)patches.Patch(target,transpiler:new HarmonyMethod(typeof(PrototypeNpcIntegration),nameof(Transpiler)));
                Enabled=true;log.LogInfo("[WHFD_Prototype] phase2=enabled; reviewedMethods=4; identitySites=5; persistentIds=unchanged; monitorMigration=none");return true;
            }catch(Exception error){if(patches!=null){try{patches.UnpatchSelf();patches=null;}catch(Exception rollback){log.LogError("[WHFD_Prototype] phase2 patch cleanup failed; do not use prototype NPC operations: "+rollback);}}Enabled=false;log.LogError("[WHFD_Prototype] phase2 unavailable; phase1/source facility retained: "+error);return false;}
        }
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod){return Transform(instructions,__originalMethod);}
        private static int LocalIndex(CodeInstruction code,bool store=false)
        {
            if(code.opcode==(store?OpCodes.Stloc_0:OpCodes.Ldloc_0))return 0;if(code.opcode==(store?OpCodes.Stloc_1:OpCodes.Ldloc_1))return 1;if(code.opcode==(store?OpCodes.Stloc_2:OpCodes.Ldloc_2))return 2;if(code.opcode==(store?OpCodes.Stloc_3:OpCodes.Ldloc_3))return 3;
            if(code.opcode!=(store?OpCodes.Stloc:OpCodes.Ldloc)&&code.opcode!=(store?OpCodes.Stloc_S:OpCodes.Ldloc_S))return -1;
            var local=code.operand as LocalVariableInfo;return local==null?Convert.ToInt32(code.operand):local.LocalIndex;
        }
        internal static List<CodeInstruction> Transform(IEnumerable<CodeInstruction> instructions,MethodBase original)
        {
            bool description=original.DeclaringType==typeof(UIManager)&&original.Name=="ItemDescOpen";
            int expected;
            if(description)expected=1;
            else if(original.DeclaringType==typeof(UIManager)&&original.Name=="PropPanelTypeChange")expected=1;
            else if(original.Name=="MoveNext"&&original.DeclaringType.DeclaringType==typeof(GameManager)&&original.DeclaringType.Name.StartsWith("<ToiletCheck>",StringComparison.Ordinal))expected=1;
            else if(original.Name=="MoveNext"&&original.DeclaringType.DeclaringType==typeof(UIManager)&&original.DeclaringType.Name.StartsWith("<PropAction>",StringComparison.Ordinal))expected=2;
            else throw new InvalidOperationException("Unreviewed phase 2 method");
            var code=instructions.Select(c=>new CodeInstruction(c)).ToList();
            var equality=AccessTools.Method(typeof(string),"op_Equality",new[]{typeof(string),typeof(string)});
            var sites=Enumerable.Range(0,code.Count).Where(i=>code[i].opcode==OpCodes.Ldstr&&(string)code[i].operand==Plugin.SourceId).ToArray();
            if(sites.Length!=expected)throw new InvalidOperationException("Native facility comparison count changed: "+original.Name);
            foreach(int site in sites)if(site<1||site+2>=code.Count||LocalIndex(code[site-1])<0||code[site+1].opcode!=OpCodes.Call||!Equals(code[site+1].operand,equality)||(code[site+2].opcode!=OpCodes.Brtrue&&code[site+2].opcode!=OpCodes.Brtrue_S)||!(code[site+2].operand is Label))throw new InvalidOperationException("Native facility comparison shape changed: "+original.Name);
            if(!description){
                var predicate=AccessTools.Method(typeof(PrototypeNpcIntegration),nameof(FacilityEquals));foreach(int site in sites)code[site+1].operand=predicate;
            }else{
                // The description uses a hashed switch. Add a new-key-only jump to the
                // existing case body BEFORE hashing; leave its string/local/ID untouched.
                int site=sites[0],key=LocalIndex(code[site-1]);var positions=new List<int>();
                for(int i=1;i<site;i++){var method=code[i].operand as MethodInfo;if(code[i].opcode==OpCodes.Call&&method!=null&&method.Name=="ComputeStringHash"&&method.ReturnType==typeof(uint)&&method.GetParameters().Length==1&&method.GetParameters()[0].ParameterType==typeof(string)&&LocalIndex(code[i-1])==key)positions.Add(i-1);}
                if(positions.Count!=1)throw new InvalidOperationException("Native description hash dispatch changed");int at=positions[0];
                if(at<1||LocalIndex(code[at-1],true)!=key||code.Skip(at).Take(site+3-at).Any(c=>c.blocks.Count!=0))throw new InvalidOperationException("Native description stack/exception boundary changed");
                var label=(Label)code[site+2].operand;if(!code.Any(c=>c.labels.Contains(label)))throw new InvalidOperationException("Native description case label missing");
                var load=new CodeInstruction(code[site-1].opcode,code[site-1].operand);load.labels.AddRange(code[at].labels);code[at].labels.Clear();
                code.InsertRange(at,new[]{load,new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(PrototypeNpcIntegration),nameof(IsPrototype))),new CodeInstruction(OpCodes.Brtrue,label)});
            }
            return code;
        }
        public void Dispose(){Enabled=false;if(patches!=null){patches.UnpatchSelf();patches=null;}}
    }
}
