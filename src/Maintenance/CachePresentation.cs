using System.Linq;
using System.Globalization;

namespace Dafeiyu.Maintenance
{
    public static class CachePresentation
    {
        public static long CategoryBytes(CacheScan scan,string category){return scan.Candidates.Where(x=>x.Category==category).Sum(x=>x.Bytes);}
        public static string FormatBytes(long bytes)
        {
            if(bytes<1024)return bytes.ToString("N0",CultureInfo.CurrentCulture)+" B";
            double value=bytes;string[] units={"KiB","MiB","GiB","TiB"};int unit=-1;
            do{value/=1024;unit++;}while(value>=1024&&unit<units.Length-1);
            return value.ToString("N1",CultureInfo.CurrentCulture)+" "+units[unit];
        }
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
