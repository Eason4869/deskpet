using System;
using System.Diagnostics;
using System.Threading;
namespace Dafeiyu
{
    internal static class HolidayLocalClock
    {
        static long nextZoneRefresh;
        internal static DateTime Now {
            get {
                long now=Stopwatch.GetTimestamp(),next=Interlocked.Read(ref nextZoneRefresh);
                // Local timezone changes must also be observed by a running process.
                if(now>=next&&Interlocked.CompareExchange(ref nextZoneRefresh,now+Stopwatch.Frequency,next)==next)TimeZoneInfo.ClearCachedData();
                return DateTime.Now;
            }
        }
    }
}
