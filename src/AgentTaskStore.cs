using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public sealed class AgentTaskStore
    {
        readonly string directory;readonly object gate=new object();Task worker=Task.FromResult(0);string warning="";readonly System.Collections.Generic.Dictionary<string,string> warnings=new System.Collections.Generic.Dictionary<string,string>();readonly System.Collections.Generic.HashSet<string> unreadable=new System.Collections.Generic.HashSet<string>();
        readonly System.Collections.Generic.Dictionary<string,AgentTaskState> pending=new System.Collections.Generic.Dictionary<string,AgentTaskState>();
        public event Action WarningChanged;
        public string Warning{get{lock(gate)return warning;}}
        public AgentTaskStore(string path){directory=Path.GetFullPath(path);}
        string FilePath(string persona,string workspace){using(var sha=SHA256.Create())return Path.Combine(directory,BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes((persona??"")+"\n"+Path.GetFullPath(workspace).TrimEnd('\\','/').ToUpperInvariant()))).Replace("-","").ToLowerInvariant()+".task");}
        void Warn(string path,string text){Action changed;lock(gate){if(String.IsNullOrEmpty(text))warnings.Remove(path);else warnings[path]=text;string merged=String.Join(" ",warnings.Values.Distinct());if(merged==warning)return;warning=merged;changed=WarningChanged;}if(changed!=null)changed();}
        internal bool HasWarning(string persona,string workspace){lock(gate)return warnings.ContainsKey(FilePath(persona,workspace));}
        public Task FlushAsync(){lock(gate)return worker;}
        internal static bool FitsCheckpoint(AgentTaskState state)
        {
            if(!Valid(state))return false;
            using(var memory=new MemoryStream()){new DataContractJsonSerializer(typeof(AgentTaskState)).WriteObject(memory,state);return memory.Length<=8*1024*1024-4096;}
        }
        static bool ValidGuidance(AgentGuidance guide)
        {
            if(guide==null || String.IsNullOrEmpty(guide.Id) || guide.Id.Length>100 || guide.Revision<0 || guide.Text==null || guide.Text.Length>8000)return false;
            try{ChatAttachment.Snapshot(guide.Attachments);return true;}catch(ApiException){return false;}
        }
        public Task SaveAsync(AgentTaskState state)
        {
            var copy=state.Copy();string path=FilePath(copy.PersonaId,copy.Workspace);if(!Valid(copy)){Warn(path,"任务检查点超出受支持的记录限制，最近变更尚未保存。");return Task.FromResult(0);}
            lock(gate){pending[path]=copy;worker=worker.ContinueWith(delegate{
                string temporary=null;try{
                    bool blocked;lock(gate)blocked=unreadable.Contains(path);if(blocked){Warn(path,"任务检查点无法读取，原文件已保留；请先清除任务记录后重试。");return;}
                    byte[] plain;using(var memory=new MemoryStream()){new DataContractJsonSerializer(typeof(AgentTaskState)).WriteObject(memory,copy);plain=memory.ToArray();}
                    if(plain.Length>8*1024*1024)throw new SerializationException();byte[] data=SecretProtection.Protect(plain);if(data.Length>8*1024*1024)throw new SerializationException();Directory.CreateDirectory(directory);temporary=Path.Combine(directory,Guid.NewGuid().ToString("N")+".tmp");
                    using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(data,0,data.Length);stream.Flush(true);}AtomicFile.Commit(temporary,path);temporary=null;lock(gate){AgentTaskState newest;if(pending.TryGetValue(path,out newest) && Object.ReferenceEquals(newest,copy))pending.Remove(path);}Warn(path,"");
                }catch(Exception e){if(!StorageError(e))throw;Warn(path,"任务检查点尚未保存，请检查数据目录权限或空间；暂时不要退出或更新。");}
                finally{if(temporary!=null)try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
            },TaskScheduler.Default);return worker;}
        }
        public AgentTaskState Load(string persona,string workspace)
        {
            FlushAsync().GetAwaiter().GetResult();string path=FilePath(persona,workspace);AgentTaskState newest;lock(gate)if(pending.TryGetValue(path,out newest))return Recovered(newest.Copy());if(!File.Exists(path))return null;
            // A concurrent checkpoint save replaces the file atomically, but a
            // reader can still hit a transient sharing violation mid-replace.
            // Retry briefly instead of treating the checkpoint as unreadable.
            for(int attempt=0;;attempt++)
            {
                try{
                    if(new FileInfo(path).Length>8*1024*1024)throw new SerializationException();byte[] plain=SecretProtection.Unprotect(File.ReadAllBytes(path));if(plain.Length>8*1024*1024)throw new SerializationException();AgentTaskState state;
                    using(var memory=new MemoryStream(plain))state=(AgentTaskState)new DataContractJsonSerializer(typeof(AgentTaskState)).ReadObject(memory);
                    if(!Valid(state) || state.PersonaId!=(persona??"") || !String.Equals(Path.GetFullPath(state.Workspace).TrimEnd('\\','/'),Path.GetFullPath(workspace).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase))throw new SerializationException();
                    Warn(path,"");return Recovered(state);
                }
                catch(Exception e){if(!StorageError(e))throw;if(e is IOException && attempt<6){System.Threading.Thread.Sleep(40*(attempt+1));continue;}lock(gate)unreadable.Add(path);Warn(path,"任务检查点无法读取，原文件已保留。");return null;}
            }
        }
        public Task RetryPendingAsync(){lock(gate){foreach(var state in pending.Values.ToArray())SaveAsync(state);return worker;}}
        public Task ClearAsync(string persona,string workspace)
        {
            string path=FilePath(persona,workspace);lock(gate){worker=worker.ContinueWith(delegate{try{if(Directory.Exists(directory))File.Delete(path);lock(gate){unreadable.Remove(path);pending.Remove(path);}Warn(path,"");}catch(Exception e){if(!StorageError(e))throw;Warn(path,"任务记录删除失败，请检查数据目录权限。");}},TaskScheduler.Default);return worker;}
        }
        static AgentTaskState Recovered(AgentTaskState state){if(state.Children!=null)foreach(var child in state.Children.Where(c=>c.Status=="running"||c.Status=="queued")){child.Status="interrupted";child.Result="上次独立 worker 已中断，未自动恢复。";}if(state.ContextStatus=="compacting")state.ContextStatus="cancelled";if(state.Status=="running"){state.Status="interrupted";state.Notice="上次任务在完成前中断；继续前会先核对项目现状。";foreach(var item in state.Tools.Where(t=>t.Status=="running"))item.Status="error";}return state;}
        static bool Valid(AgentTaskState state){return state!=null && state.Version==1 && (state.Children==null || state.Children.Count<=64&&state.Children.All(c=>c!=null&&c.Input!=null&&c.Input.Length<=12000&&(c.Result==null||c.Result.Length<=12100)&&c.ToolCalls>=0&&c.EstimatedTokens>=0)) && state.EstimatedTotalTokens>=0 && state.ChildEstimatedTotalTokens>=0 && state.ModelRequests>=0 && state.ContextCompactions>=0 && state.EstimatedContextTokens>=0 && (state.ContextSummary==null || state.ContextSummary.Length<=20000) && (state.ContextStatus==null || state.ContextStatus.Length<=80) && (state.Guidance==null || (state.Guidance.Count<=100 && state.Guidance.All(ValidGuidance))) && !String.IsNullOrEmpty(state.Workspace) && state.Plan!=null && state.Plan.Count<=20 && state.Plan.All(p=>p!=null && p.Text!=null && p.Text.Length<=250 && (p.Status=="pending" || p.Status=="in_progress" || p.Status=="complete")) && state.Changes!=null && state.Changes.Count<=256 && state.Changes.All(c=>c!=null && !String.IsNullOrEmpty(c.Path) && c.AfterHash!=null) && state.Commands!=null && state.Commands.Count<=8 && state.Commands.All(c=>c!=null && c.Output!=null && c.Output.Length<=256*1024) && state.Tools!=null && state.Tools.Count<=100 && state.Tools.All(t=>t!=null && t.Id!=null) && state.Outputs!=null && state.Outputs.Count<=32 && state.Outputs.All(o=>o.Value!=null && o.Value.Length<=24000);}
        static bool StorageError(Exception e){return e is IOException || e is UnauthorizedAccessException || e is CryptographicException || e is SerializationException || e is ArgumentException || e is System.Xml.XmlException;}
    }
}
