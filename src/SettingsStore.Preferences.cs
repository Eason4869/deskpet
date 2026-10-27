using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu
{
    internal sealed class PreferenceChangeSet
    {
        internal readonly long Order;
        internal readonly Dictionary<string,object> Values;
        internal readonly Dictionary<string,long> Orders;internal string Seed;
        internal PreferenceChangeSet(long order,Dictionary<string,object> values){Order=order;Values=values;Orders=values.Keys.ToDictionary(k=>k,k=>order);}
    }
    public sealed partial class SettingsStore
    {
        sealed class WriteState
        {
            internal readonly SemaphoreSlim Gate=new SemaphoreSlim(1,1);
            internal long Order,LastFull;internal string Language;internal int Resetting,Publishing;
            internal readonly Dictionary<string,long> Fields=new Dictionary<string,long>();
        }
        static readonly Dictionary<string,WeakReference> writeStates=new Dictionary<string,WeakReference>(StringComparer.OrdinalIgnoreCase);
        static WriteState GetWriteState(string path)
        {
            lock(writeStates){
                foreach(var key in writeStates.Where(p=>!p.Value.IsAlive).Select(p=>p.Key).ToArray())writeStates.Remove(key);
                WeakReference reference;var state=writeStates.TryGetValue(path,out reference)?reference.Target as WriteState:null;
                if(state==null){state=new WriteState();writeStates[path]=new WeakReference(state);}return state;
            }
        }
        internal IDisposable TryBeginExplicitSave()
        {
            if(Volatile.Read(ref writeState.Resetting)!=0||Interlocked.CompareExchange(ref writeState.Publishing,1,0)!=0)return null;
            if(Volatile.Read(ref writeState.Resetting)!=0){Volatile.Write(ref writeState.Publishing,0);return null;}
            return new ExplicitSaveLease(writeState);
        }
        sealed class ExplicitSaveLease:IDisposable
        {
            WriteState state;internal ExplicitSaveLease(WriteState value){state=value;}
            public void Dispose(){var current=Interlocked.Exchange(ref state,null);if(current!=null)Volatile.Write(ref current.Publishing,0);}
        }
        // Immutable scalar values only; no live settings graph crosses threads.
        internal PreferenceChangeSet CapturePreferences(PetSettings settings,params string[] fields)
        {
            var values=new Dictionary<string,object>();
            foreach(string field in fields)switch(field){
                case "Scale":values[field]=settings.Scale;break;case "Volume":values[field]=settings.Volume;break;
                case "X":values[field]=settings.X;break;case "Y":values[field]=settings.Y;break;
                default:throw new ArgumentException("Unsupported ordinary preference.");
            }
            var patch=new PreferenceChangeSet(Interlocked.Increment(ref writeState.Order),values);
            if(!File.Exists(path))using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(PetSettings)).WriteObject(stream,settings);patch.Seed=Encoding.UTF8.GetString(stream.ToArray());}
            return patch;
        }
        internal PreferenceChangeSet MergePreferences(PreferenceChangeSet previous,PreferenceChangeSet next)
        {
            if(previous==null)return next;
            var values=new Dictionary<string,object>(previous.Values);foreach(var item in next.Values)values[item.Key]=item.Value;
            var merged=new PreferenceChangeSet(next.Order,values){Seed=next.Seed??previous.Seed};
            foreach(var key in values.Keys)merged.Orders[key]=next.Orders.ContainsKey(key)?next.Orders[key]:previous.Orders[key];return merged;
        }
        // Test seam deliberately contains neither configuration nor credentials.
        internal Action BeforePreferenceCommit;
        internal Action BeforeExactSave;
        internal Action BeforeLanguageCommit;
        internal Task SavePreferencePatchAsync(PreferenceChangeSet patch)
        {
            return Task.Run(delegate{
                writes.Wait();try{
                    var applicable=patch.Values.Where(p=>patch.Orders[p.Key]>writeState.LastFull&&(!writeState.Fields.ContainsKey(p.Key)||patch.Orders[p.Key]>writeState.Fields[p.Key])).ToArray();
                    if(applicable.Length==0)return;
                    var json=new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=16*1024*1024,RecursionLimit=128};
                    Dictionary<string,object> values;
                    try{
                        string raw=File.ReadAllText(path,new UTF8Encoding(false,true));
                        using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(raw)))
                            if(new DataContractJsonSerializer(typeof(PetSettings)).ReadObject(stream)==null)throw new InvalidDataException("Empty settings.");
                        values=ReadLanguageSettings(raw,json);
                    }catch(FileNotFoundException){values=patch.Seed==null?DefaultLanguageSettings(json):ReadLanguageSettings(patch.Seed,json);}
                    catch(DirectoryNotFoundException){values=patch.Seed==null?DefaultLanguageSettings(json):ReadLanguageSettings(patch.Seed,json);}
                    foreach(var item in applicable)values[item.Key]=json.Serialize(item.Value);
                    Directory.CreateDirectory(DirectoryPath);string temporary=path+".prefs-"+Guid.NewGuid().ToString("N")+".tmp";
                    try{
                        using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                            byte[] bytes=new UTF8Encoding(false).GetBytes("{"+String.Join(",",values.Select(p=>json.Serialize(p.Key)+":"+(string)p.Value))+"}");
                            stream.Write(bytes,0,bytes.Length);stream.Flush(true);
                        }
                        if(BeforePreferenceCommit!=null)BeforePreferenceCommit();
                        AtomicFile.Commit(temporary,path);
                        foreach(var item in applicable)writeState.Fields[item.Key]=patch.Orders[item.Key];
                        Interlocked.Increment(ref completedSnapshotWrites);Warning="";
                    }finally{if(File.Exists(temporary))File.Delete(temporary);}
                }finally{writes.Release();}
            });
        }
    }
}
