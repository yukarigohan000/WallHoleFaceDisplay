using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
namespace GameModding.MadIsland.WallHoleFaceDisplay
{
    // CLR object identity, including Unity objects whose native instance was destroyed.
    // Keys are never numeric NPC IDs, build IDs or creation order.
    internal sealed class MonitorAttachmentRegistry<TKey,TValue> where TKey:class where TValue:class,IDisposable
    {
        private sealed class Identity : IEqualityComparer<TKey>
        {
            public bool Equals(TKey a,TKey b){return ReferenceEquals(a,b);}
            public int GetHashCode(TKey key){return RuntimeHelpers.GetHashCode(key);}
        }
        private readonly Dictionary<TKey,TValue> entries=new Dictionary<TKey,TValue>(new Identity());
        internal int Count {get {return entries.Count;}}
        /*WHFDPerfOnlyBegin*/
        public Dictionary<TKey,TValue>.Enumerator GetEnumerator(){return entries.GetEnumerator();}
        /*WHFDPerfOnlyEnd*/
        private KeyValuePair<TKey,TValue>[] cachedSnapshot;
        internal KeyValuePair<TKey,TValue>[] Snapshot(){if(!MonitorPerformance.ReuseVertexBuffer)return entries.ToArray();return cachedSnapshot??(cachedSnapshot=entries.ToArray());}
        internal bool TryGet(TKey key,out TValue value){value=null;return key!=null && entries.TryGetValue(key,out value);}
        internal void Add(TKey key,TValue value)
        {
            if(key==null || value==null)throw new ArgumentNullException();
            if(entries.ContainsKey(key)||entries.Values.Any(v=>ReferenceEquals(v,value)))throw new InvalidOperationException("Monitor already owned");
            entries.Add(key,value);cachedSnapshot=null;
        }
        internal bool Remove(TKey key)
        {
            TValue value;if(!TryGet(key,out value))return false;
            entries.Remove(key);cachedSnapshot=null;value.Dispose();return true;
        }
    }
}
