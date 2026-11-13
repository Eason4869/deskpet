using System;
using System.Collections.Generic;
using System.Linq;
namespace Dafeiyu
{
    // Thread-safe decoded-memory LRU. Pins represent resources currently rendered;
    // the budget applies to everything else, including inactive sources.
    internal sealed class CompanionDecodedCache<T> where T:class
    {
        sealed class Entry {internal T Value;internal long Bytes;}
        readonly object gate=new object();readonly long budget;readonly Action<T> release;
        readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>(StringComparer.Ordinal);
        readonly Dictionary<string,int> pins=new Dictionary<string,int>(StringComparer.Ordinal);
        readonly LinkedList<string> recent=new LinkedList<string>();
        internal CompanionDecodedCache(long bytes,Action<T> dispose=null){budget=bytes;release=dispose;}
        internal long UnpinnedBytes {get{lock(gate)return entries.Where(p=>!pins.ContainsKey(p.Key)).Sum(p=>p.Value.Bytes);}}
        internal long RetainedBytes {get{lock(gate)return entries.Values.Sum(entry=>entry.Bytes);}}
        internal long PinnedBytes {get{lock(gate)return entries.Where(p=>pins.ContainsKey(p.Key)).Sum(p=>p.Value.Bytes);}}
        // Count all pin holders, including preparation keys with no decoded entry.
        internal int PinnedResourceCount {get{lock(gate)return pins.Count;}}
        internal int PinLeaseCount {get{lock(gate)return pins.Values.Sum();}}
        internal T Get(string key){lock(gate){Entry entry;if(!entries.TryGetValue(key,out entry))return null;recent.Remove(key);recent.AddLast(key);return entry.Value;}}
        internal void Put(string key,T value,long bytes){lock(gate){Remove(key);entries[key]=new Entry{Value=value,Bytes=bytes};recent.AddLast(key);Trim();}}
        internal void Pin(string key){lock(gate){int n;pins.TryGetValue(key,out n);pins[key]=n+1;}}
        internal void Unpin(string key){lock(gate){int n;if(pins.TryGetValue(key,out n)){if(n<=1)pins.Remove(key);else pins[key]=n-1;}Trim();}}
        internal void Remove(string key){lock(gate){Entry entry;if(entries.TryGetValue(key,out entry)){entries.Remove(key);recent.Remove(key);if(release!=null)release(entry.Value);}}}
        internal void RemovePrefix(string prefix){lock(gate)foreach(var key in entries.Keys.Where(k=>k.StartsWith(prefix,StringComparison.Ordinal)).ToArray())Remove(key);}
        internal string KeyFor(T value){lock(gate)return entries.FirstOrDefault(p=>Object.ReferenceEquals(p.Value.Value,value)).Key;}
        void Trim(){while(UnpinnedBytes>budget){var key=recent.FirstOrDefault(k=>!pins.ContainsKey(k));if(key==null)break;Remove(key);}}
    }
}
