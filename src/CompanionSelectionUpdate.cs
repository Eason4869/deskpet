using System;
namespace Dafeiyu
{
    public sealed class CompanionSelectionUpdate
    {
        internal static void WriteTrace(string value){foreach(System.Diagnostics.TraceListener listener in System.Diagnostics.Trace.Listeners)try{listener.WriteLine(value);}catch{}}
        public long Request {get;private set;}
        public string PickerKey {get;private set;}
        public string Origin {get;private set;}
        public string Stage {get;private set;}
        public double Milliseconds {get;private set;}
        public bool CacheHit {get;private set;}
        public OfficialCompanionProgress Progress {get;private set;}
        public CompanionSelectionUpdate(long request,string pickerKey,string origin,string stage,double milliseconds,bool cacheHit=false,OfficialCompanionProgress progress=null)
        {Request=request;PickerKey=pickerKey;Origin=origin;Stage=stage;Milliseconds=milliseconds;CacheHit=cacheHit;Progress=progress;}
    }
}
