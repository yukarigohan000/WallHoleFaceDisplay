using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using Spine;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Observes only the passed private pose. Never changes any Spine/runtime values.
    internal static class MonitorRigMapDiagnostic
    {
        private static readonly ConditionalWeakTable<Skeleton,object> Attempted=new ConditionalWeakTable<Skeleton,object>();
        internal static void WriteOnce(Skeleton pose,int npcId,ManualLogSource log)
        {
            if(!MonitorDebugSettings.Enabled || pose==null || npcId!=15) return;
            lock(Attempted) {
                object marker;if(Attempted.TryGetValue(pose,out marker))return;
                Attempted.Add(pose,new object());
            }
            string path=Path.Combine(Paths.ConfigPath,"WallHoleFaceDisplay.rig-map-15.txt");
            try {
                string text=Describe(pose);
                File.WriteAllText(path,text,new UTF8Encoding(false));
                log?.LogInfo("[MonitorRigMap] npcId=15; result=success; path="+path);
            } catch(Exception error) {
                log?.LogWarning("[MonitorRigMap] npcId=15; result=failed; path="+path+"; error="+error.GetType().Name);
            }
        }
        private static string Name(Bone bone) {return bone==null?"<none>":Safe(bone.Data.Name);}
        private static string Safe(string value) {return value==null?"<null>":value.Replace("\\","\\\\").Replace("\r","\\r").Replace("\n","\\n").Replace("\t","\\t");}
        internal static string Describe(Skeleton pose)
        {
            if(pose==null)throw new ArgumentNullException(nameof(pose));
            var output=new StringBuilder();
            output.AppendLine("WallHoleFaceDisplay private monitor rig / npcId=15");
            output.AppendLine("UTC="+DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture));
            output.AppendLine("[Slots] count="+pose.Slots.Count);
            for(int i=0;i<pose.Slots.Count;i++) {
                var slot=pose.Slots.Items[i];
                output.AppendLine(i+" slot="+Safe(slot.Data.Name)+" bone="+Name(slot.Bone));
            }
            output.AppendLine("[Bones] count="+pose.Bones.Count);
            for(int i=0;i<pose.Bones.Count;i++) {
                var bone=pose.Bones.Items[i];
                output.AppendLine(i+" bone="+Name(bone)+" parent="+Name(bone.Parent));
            }
            output.AppendLine("[IK constraints] count="+pose.IkConstraints.Count);
            foreach(var ik in pose.IkConstraints)
                output.AppendLine("name="+Safe(ik.Data.Name)+" bones=["+string.Join(",",ik.Bones.Select(Name))+ "] target="+Name(ik.Target)+
                    " mix="+ik.Mix.ToString("R",CultureInfo.InvariantCulture)+" declaredOrder="+ik.Data.Order);
            output.AppendLine("[Transform constraints] count="+pose.TransformConstraints.Count);
            foreach(var constraint in pose.TransformConstraints)
                output.AppendLine("name="+Safe(constraint.Data.Name)+" bones=["+string.Join(",",constraint.Bones.Select(Name))+
                    "] target="+Name(constraint.Target)+" declaredOrder="+constraint.Data.Order);
            // Complete cache, not guessed arm names: allows dependency/order review later.
            output.AppendLine("[Runtime update cache] count="+pose.UpdateCacheList.Count);
            for(int i=0;i<pose.UpdateCacheList.Count;i++) {
                var item=pose.UpdateCacheList.Items[i];
                var bone=item as Bone;var ik=item as IkConstraint;var constraint=item as TransformConstraint;
                string value=bone!=null?"Bone "+Name(bone):ik!=null?"IK "+Safe(ik.Data.Name):constraint!=null?"Transform "+Safe(constraint.Data.Name):item.GetType().FullName;
                output.AppendLine(i+" "+value);
            }
            return output.ToString();
        }
    }
}
