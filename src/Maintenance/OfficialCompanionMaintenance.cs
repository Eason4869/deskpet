using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Diagnostics;

namespace Dafeiyu.Maintenance
{
    // v2-* is deliberately outside the pointer grammar of pre-maintenance clients.
    // Their lazy readers can never select a managed root; legacy v-* is never adopted.
    internal static class OfficialCompanionMaintenance
    {
        internal static CacheCleanResult MaintainHistory(string dataRoot,DateTime now,CancellationToken cancel)
        {
            CacheCleanResult result;int protectedItems=0;
            try{
                cancel.ThrowIfCancellationRequested();var service=new CacheMaintenanceService(dataRoot);
                var scan=service.ScanPackHistory(now,cancel);protectedItems=scan.ProtectedItems;
                cancel.ThrowIfCancellationRequested();result=service.Clean(scan,now,cancel);result.FailedItems+=scan.FailedItems;
            }catch(OperationCanceledException){result=new CacheCleanResult{SkippedItems=1};}
            catch(Exception){Trace.TraceWarning("Official companion history maintenance could not complete.");result=new CacheCleanResult{FailedItems=1};}
            if(!cancel.IsCancellationRequested)try{
                CachePaths.Check(dataRoot);if(!Directory.Exists(dataRoot))throw new IOException("Data root is unavailable.");
                string registry=OfficialCompanionStorage.SafePath(dataRoot,"cache-maintenance");Directory.CreateDirectory(registry);
                Write(registry,"last-companion-history.json",new{schema_version=1,checked_utc_ticks=now.Ticks,finished_utc_ticks=DateTime.UtcNow.Ticks,freed_bytes=result.FreedBytes,deleted_items=result.DeletedItems,skipped_items=result.SkippedItems,failed_items=result.FailedItems,protected_items=protectedItems});
            }catch(Exception){result.FailedItems++;Trace.TraceWarning("Official companion history summary could not be written.");}
            return result;
        }
        sealed class VersionRecord { internal string Name,Identity;internal long Retired; }
        sealed class State
        {
            internal string Current="",Previous="";
            internal readonly Dictionary<string,VersionRecord> Versions=new Dictionary<string,VersionRecord>(StringComparer.Ordinal);
        }
        internal static bool Managed(string name){return name!=null&&name.Length<=200&&Regex.IsMatch(name,@"\Av2-[a-zA-Z0-9.-]+-[a-f0-9]{32}-[a-f0-9]{32}\z");}
        static string Name(object raw)
        {
            string value=raw as string;
            if(value==null||value.Length>200||(value.Length!=0&&!Managed(value)&&!Regex.IsMatch(value,@"\Av-[a-zA-Z0-9.-]+-[a-f0-9]{64}\z")))throw new IOException("Unknown official version pointer.");
            return value;
        }
        static string PathFor(string role,string name){return OfficialCompanionStorage.SafePath(role,name);}
        internal static IDisposable Lock(string role)
        {return Lock(role,CancellationToken.None);}
        internal static IDisposable Lock(string role,CancellationToken cancel)
        {
            var clock=Stopwatch.StartNew();
            while(true){cancel.ThrowIfCancellationRequested();try{return OpenLock(role);}catch(IOException error){int code=error.HResult&65535;if(code!=32&&code!=33||clock.Elapsed>TimeSpan.FromSeconds(90))throw;cancel.WaitHandle.WaitOne(25);}}
        }
        static IDisposable OpenLock(string role)
        {
            CachePaths.Check(role);Directory.CreateDirectory(role);
            string path=PathFor(role,"versions.lock");
            return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        }
        internal static IDisposable LockCandidate(string dataRoot,string path)
        {
            return CachePaths.Category(dataRoot,path)=="pack-versions"?OpenLock(Path.GetDirectoryName(path)):null;
        }
        static string Pointer(string role)
        {
            string path=PathFor(role,"current.txt");if(!File.Exists(path))return "";
            if(new FileInfo(path).Length>240)throw new IOException("Oversized official pointer.");
            return Name(File.ReadAllText(path,new UTF8Encoding(false,true)));
        }
        static State Read(string role)
        {
            string path=PathFor(role,"versions.json");var state=new State();if(!File.Exists(path))return state;
            var data=OfficialJson.File(path,1024*1024);
            if(OfficialJson.Integer(data,"schema_version")!=1)throw new IOException("Unknown official lifecycle schema.");
            state.Current=Name(OfficialJson.Required(data,"current"));state.Previous=Name(OfficialJson.Required(data,"previous"));
            foreach(var raw in OfficialJson.Array(OfficialJson.Required(data,"versions"))){
                var item=OfficialJson.Object(raw);string name=Name(OfficialJson.Required(item,"name"));
                if(!Managed(name)||state.Versions.Count>=2048||state.Versions.ContainsKey(name))throw new IOException("Invalid managed version record.");
                long retired=OfficialJson.Integer(item,"retired_utc_ticks");if(retired<0||retired>DateTime.MaxValue.Ticks)throw new IOException("Invalid retirement date.");
                state.Versions.Add(name,new VersionRecord{Name=name,Identity=OfficialJson.Text(item,"identity",200),Retired=retired});
            }
            return state;
        }
        static void Write(string role,string filename,object value)
        {
            string temporary=PathFor(role,".versions-"+Guid.NewGuid().ToString("N"));
            try{File.WriteAllBytes(temporary,OfficialJson.Bytes(value));AtomicFile.Commit(temporary,PathFor(role,filename));}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        static void Save(string role,State state)
        {
            Write(role,"versions.json",new{schema_version=1,current=state.Current,previous=state.Previous,versions=state.Versions.Values.OrderBy(x=>x.Name,StringComparer.Ordinal).Select(x=>new{name=x.Name,identity=x.Identity,retired_utc_ticks=x.Retired}).ToArray()});
        }
        internal static void Begin(OfficialCompanionStorage store,string id,string next,string stage)
        {
            string role=store.CharacterRoot(id);Recover(store,id);var state=Read(role);string current=Pointer(role);
            var verified=store.Installed(id);
            if(state.Current.Length!=0&&state.Current!=current&&(!current.StartsWith("v-",StringComparison.Ordinal)||verified==null))throw new IOException("Lifecycle and pointer disagree.");
            // Only a verified current may displace the existing recovery candidate.
            string previous=verified!=null?current:state.Previous;
            Write(role,"version-transaction.json",new{schema_version=1,state_current=state.Current,old_current=current,old_previous=state.Previous,keep_previous=previous,new_current=next,new_identity=CachePaths.Identity(stage),started_utc_ticks=DateTime.UtcNow.Ticks});
        }
        internal static void Recover(OfficialCompanionStorage store,string id)
        {
            string role=store.CharacterRoot(id),path=PathFor(role,"version-transaction.json");if(!File.Exists(path))return;
            var tx=OfficialJson.File(path,16384);if(OfficialJson.Integer(tx,"schema_version")!=1)throw new IOException("Unknown publication transaction.");
            string old=Name(OfficialJson.Required(tx,"old_current")),priorState=Name(OfficialJson.Required(tx,"state_current")),next=Name(OfficialJson.Required(tx,"new_current")),keep=Name(OfficialJson.Required(tx,"keep_previous")),pointer=Pointer(role);
            Name(OfficialJson.Required(tx,"old_previous"));
            if(!Managed(next))throw new IOException("Invalid managed transaction root.");
            if(pointer==old){File.Delete(path);return;}
            if(pointer!=next)throw new IOException("Publication transaction has an unknown outcome.");
            string destination=PathFor(role,next),identity=OfficialJson.Text(tx,"new_identity",200);
            if(CachePaths.Identity(destination)!=identity||store.ReadVersion(destination,id,null)==null)throw new IOException("Published transaction root changed.");
            var state=Read(role);
            if(state.Current.Length!=0&&state.Current!=priorState&&state.Current!=next)throw new IOException("Publication state changed.");
            foreach(var name in state.Versions.Keys.ToArray())if(!Directory.Exists(PathFor(role,name)))state.Versions.Remove(name);
            state.Versions[next]=new VersionRecord{Name=next,Identity=identity,Retired=0};
            long now=DateTime.UtcNow.Ticks;
            foreach(var record in state.Versions.Values){if(record.Name==next||record.Name==keep)record.Retired=0;else if(record.Retired==0)record.Retired=now;}
            state.Current=next;state.Previous=keep;Save(role,state);
            CacheResourceLease.RegisterInstalled(Path.GetDirectoryName(store.Root),destination);
            File.Delete(path);
        }
        internal static string RetirementProof(string dataRoot,string directory,DateTime now,CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            if(!Managed(Path.GetFileName(directory)))return null;
            string role=Path.GetDirectoryName(directory),transaction=PathFor(role,"version-transaction.json");if(File.Exists(transaction))return null;
            var state=Read(role);string current=Pointer(role),name=Path.GetFileName(directory);VersionRecord record;
            if(current.Length==0||state.Current!=current||name==current||name==state.Previous||!state.Versions.TryGetValue(name,out record)||record.Retired==0||record.Retired>now.AddDays(-7).Ticks||record.Identity!=CachePaths.Identity(directory))return null;
            var store=new OfficialCompanionStorage(dataRoot);string id=Path.GetFileName(role);
            // If recovery resources are damaged, older material may be the only usable copy.
            if(store.InstalledCancelable(id,cancel)==null||state.Previous.Length!=0&&store.ReadVersionCancelable(PathFor(role,state.Previous),id,null,cancel)==null||store.ReadVersionCancelable(directory,id,null,cancel)==null)return null;
            return OfficialCompanionStorage.HashFile(PathFor(role,"versions.json"))+"|"+current+"|"+record.Identity;
        }
    }
}
