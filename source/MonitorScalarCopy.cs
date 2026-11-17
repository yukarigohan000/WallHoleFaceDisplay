using System;
using System.Reflection;
using System.Reflection.Emit;

namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // Copies the EXACT same cached scalar fields as the legacy reflection loop.
    // Typed ldfld/stfld avoids GetValue/SetValue boxing. Unsupported runtimes fall
    // back to the original operation; no Skeleton or mutable data is shared.
    internal static class MonitorScalarCopy<T> where T:class
    {
        private static bool initialized,zeroInitialized;
        private static Action<T,T> copy;
        private static Action<T> zero;
        internal static bool UsingTyped {get {return copy!=null;}}
        internal static void Copy(T source,T target,FieldInfo[] fields)
        {
            if(MonitorPerformance.TypedPoseCopy){
                if(!initialized){initialized=true;try{var method=new DynamicMethod("WHFDScalarCopy",typeof(void),new[]{typeof(T),typeof(T)},typeof(MonitorScalarCopy<T>).Module,true);var il=method.GetILGenerator();
                    foreach(var field in fields){if(field.IsInitOnly)throw new NotSupportedException("Readonly scalar");il.Emit(OpCodes.Ldarg_1);il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldfld,field);il.Emit(OpCodes.Stfld,field);}il.Emit(OpCodes.Ret);copy=(Action<T,T>)method.CreateDelegate(typeof(Action<T,T>));}catch{copy=null;MonitorPerformance.Count(MonitorPerfEvent.TypedCopyFallback);}}
                if(copy!=null){try{copy(source,target);return;}catch{copy=null;MonitorPerformance.Count(MonitorPerfEvent.TypedCopyFallback);}}
            }
            foreach(var field in fields)field.SetValue(target,field.GetValue(source));
        }
        internal static void Zero(T target,FieldInfo field)
        {
            if(MonitorPerformance.TypedPoseCopy){
                if(!zeroInitialized){zeroInitialized=true;try{if(field==null||field.FieldType!=typeof(int)||field.IsInitOnly)throw new NotSupportedException();var method=new DynamicMethod("WHFDScalarZero",typeof(void),new[]{typeof(T)},typeof(MonitorScalarCopy<T>).Module,true);var il=method.GetILGenerator();il.Emit(OpCodes.Ldarg_0);il.Emit(OpCodes.Ldc_I4_0);il.Emit(OpCodes.Stfld,field);il.Emit(OpCodes.Ret);zero=(Action<T>)method.CreateDelegate(typeof(Action<T>));}catch{zero=null;MonitorPerformance.Count(MonitorPerfEvent.TypedCopyFallback);}}
                if(zero!=null){try{zero(target);return;}catch{zero=null;MonitorPerformance.Count(MonitorPerfEvent.TypedCopyFallback);}}
            }
            field.SetValue(target,0);
        }
    }
}
