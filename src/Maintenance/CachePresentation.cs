using System.Linq;

namespace Dafeiyu.Maintenance
{
    public static class CachePresentation
    {
        public static string Describe(CacheScan scan)
        {
            return TextResources.Format("cache.total",scan.TotalBytes)+"\n"+
                TextResources.Format("cache.updates",scan.Candidates.Where(x=>x.Category=="updates").Sum(x=>x.Bytes))+"\n"+
                TextResources.Format("cache.downloads",scan.Candidates.Where(x=>x.Category=="downloads").Sum(x=>x.Bytes))+"\n"+
                TextResources.Format("cache.protected",scan.ProtectedItems,scan.FailedItems);
        }
        public static string Describe(CacheCleanResult result){return TextResources.Format("cache.result",result.FreedBytes,result.DeletedItems,result.SkippedItems,result.FailedItems);}
    }
}
