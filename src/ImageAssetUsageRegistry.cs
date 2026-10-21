using System;
using System.Collections.Generic;
using System.Linq;
namespace Dafeiyu
{
    // A deletion reservation and ordinary usage are mutually exclusive. Callers
    // hold a reservation across the complete delete transaction and reference check.
    public sealed class ImageAssetUsageRegistry
    {
        readonly object gate=new object();
        readonly Dictionary<string,int> uses=new Dictionary<string,int>(StringComparer.Ordinal);
        readonly HashSet<string> deleted=new HashSet<string>(StringComparer.Ordinal);
        internal void MarkDeleted(IEnumerable<string> ids){lock(gate)foreach(string id in ids)deleted.Add(id);}
        readonly HashSet<string> deleting=new HashSet<string>(StringComparer.Ordinal);
        internal static IEnumerable<string> ReferenceKeys(IEnumerable<ChatMessage> messages)
        {return messages.Where(m=>m!=null).SelectMany(m=>(m.GeneratedImages??new List<GeneratedImage>()).Where(i=>i!=null).SelectMany(i=>String.IsNullOrEmpty(i.ReferenceImageId)?new[]{i.Id}:new[]{i.Id,i.ReferenceImageId}).Concat((m.ImageGenerationTaskIds??new List<string>()).Select(id=>"job:"+id))).Where(id=>!String.IsNullOrWhiteSpace(id)).Distinct();}
        internal IDisposable AcquireMessages(IEnumerable<ChatMessage> messages)
        {var leases=new List<IDisposable>();try{foreach(string id in ReferenceKeys(messages))leases.Add(Acquire(id,"history-save"));}catch{foreach(var lease in leases)lease.Dispose();throw;}return new Lease(delegate{foreach(var lease in leases)lease.Dispose();});}
        public IDisposable Acquire(string assetId,string ownerKey)
        {
            if(String.IsNullOrWhiteSpace(assetId) || String.IsNullOrWhiteSpace(ownerKey))throw new ArgumentException("请指定资产和使用者。");
            lock(gate){if(deleting.Contains(assetId)||deleted.Contains(assetId))throw new InvalidOperationException("图片正在删除。");int count;uses.TryGetValue(assetId,out count);uses[assetId]=count+1;}
            return new Lease(delegate{lock(gate){if(--uses[assetId]==0)uses.Remove(assetId);}});
        }
        public IDisposable TryAcquireDeletion(string assetId)
        {
            if(String.IsNullOrWhiteSpace(assetId))throw new ArgumentException("请指定资产。");
            lock(gate){if(uses.ContainsKey(assetId) || !deleting.Add(assetId))return null;}
            return new Lease(delegate{lock(gate)deleting.Remove(assetId);});
        }
        public bool IsInUse(string assetId){lock(gate)return uses.ContainsKey(assetId);}
        sealed class Lease : IDisposable
        {Action release;public Lease(Action release){this.release=release;}public void Dispose(){var action=System.Threading.Interlocked.Exchange(ref release,null);if(action!=null)action();}}
    }
}
