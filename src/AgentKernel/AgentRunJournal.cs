using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    public sealed class AgentRunRecord
    {
        public int Version=1;public string ExecutionId,SessionId,ProjectId,TaskId,CallId,ToolName,PayloadHash,State,Result;
        public Dictionary<string,string> BeforeHashes=new Dictionary<string,string>(),AfterHashes=new Dictionary<string,string>();
    }
    public sealed class AgentRunJournal
    {
        readonly string directory;readonly Func<IDisposable> lease;internal Action<AgentRunRecord> BeforeSave {get;set;}
        public bool ReconciliationRequired {get;private set;}
        public AgentRunJournal(string directory,Func<IDisposable> lease=null){this.directory=Path.GetFullPath(directory);this.lease=lease;}
        string FilePath(string key){using(var hash=SHA256.Create())return Path.Combine(directory,BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-","")+".run");}
        AgentRunRecord Read(string path){using(lease==null?null:lease())return ReadCore(path);}
        AgentRunRecord ReadCore(string path){AgentInstructions.RejectLinks(path);byte[] bytes=File.ReadAllBytes(path);if(bytes.Length>1024*1024)throw new IOException(TextResources.Get("agentRunJournal.readCore.text"));var record=new JavaScriptSerializer{MaxJsonLength=1024*1024}.Deserialize<AgentRunRecord>(Encoding.UTF8.GetString(SecretProtection.Unprotect(bytes)));if(record==null||record.Version!=1||String.IsNullOrEmpty(record.ExecutionId))throw new IOException(TextResources.Get("agentRunJournal.readCore.text.alternativeA"));return record;}
        void Save(string path,AgentRunRecord record){using(lease==null?null:lease())SaveCore(path,record);}
        void SaveCore(string path,AgentRunRecord record)
        {
            if(BeforeSave!=null)BeforeSave(record);AgentInstructions.RejectLinks(directory);Directory.CreateDirectory(directory);AgentInstructions.RejectLinks(directory);string temporary=Path.Combine(directory,Guid.NewGuid().ToString("N")+".tmp");
            try{File.WriteAllBytes(temporary,SecretProtection.Protect(Encoding.UTF8.GetBytes(new JavaScriptSerializer{MaxJsonLength=1024*1024}.Serialize(record))));AtomicFile.Commit(temporary,path);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        public bool HasPending(string taskId)
        {if(!Directory.Exists(directory))return false;AgentInstructions.RejectLinks(directory);var files=Directory.EnumerateFiles(directory,"*.run").Take(4097).ToArray();if(files.Length>4096)throw new IOException(TextResources.Get("agentRunJournal.hasPending.text"));ReconciliationRequired=files.Select(Read).Any(r=>r.TaskId==taskId&&(r.State=="prepared"||r.State=="started"||r.State=="needs_reconciliation"));return ReconciliationRequired;}
        public async Task<string> ExecuteAsync(AgentToolInvocation invocation,Func<Action,Task<string>> execute,Func<Dictionary<string,string>> hashes=null)
        {
            string path=FilePath(invocation.Key);if(File.Exists(path)){
                var old=Read(path);if(old.PayloadHash!=invocation.PayloadHash)throw new InvalidOperationException(AgentJobTools.IsJobTool(invocation.ToolName)?"job_idempotency_conflict":TextResources.Get("agentRunJournal.executeAsync.text"));
                if(old.State=="completed"||old.State=="failed"||old.State=="cancelled")return AgentJobTools.IsJobTool(invocation.ToolName)?(old.State=="cancelled"?AgentJobTools.Error("start_failed"):AgentJobTools.Replay(old.Result)):old.Result??TextResources.Get("agentRunJournal.executeAsync.text.alternativeA");
                if(old.State=="reconciled")return AgentJobTools.IsJobTool(invocation.ToolName)?AgentJobTools.Error("execution_reconciliation_required"):TextResources.Get("agentRunJournal.executeAsync.text.alternativeB");
                if(AgentJobTools.IsJobTool(invocation.ToolName)){ReconciliationRequired=true;return AgentJobTools.Error("execution_reconciliation_required");}
                ReconciliationRequired=true;throw new ApiException(TextResources.Get("agentRunJournal.executeAsync.text.alternativeC"));
            }
            var record=new AgentRunRecord{ExecutionId=Guid.NewGuid().ToString("N"),SessionId=invocation.SessionId,ProjectId=invocation.ProjectId,TaskId=invocation.TaskId,CallId=invocation.CallId,ToolName=invocation.ToolName,PayloadHash=invocation.PayloadHash,State="prepared",BeforeHashes=hashes==null?new Dictionary<string,string>():hashes()};
            Save(path,record);bool started=false;
            try{
                string result=await execute(delegate{record.State="started";Save(path,record);started=true;}).ConfigureAwait(false);
                record.AfterHashes=hashes==null?new Dictionary<string,string>():hashes();record.Result=(result??"");if(record.Result.Length>24000&&!AgentJobTools.IsJobTool(invocation.ToolName))record.Result=TextResources.Format("agentRunJournal.executeAsync.recordResultSubstring",record.Result.Substring(0,24000));record.State=(AgentJobTools.IsJobTool(invocation.ToolName)?AgentJobTools.IsFailure(record.Result):record.Result.StartsWith("错误："))?"failed":"completed";
                try{Save(path,record);}catch(Exception){ReconciliationRequired=started;throw;}return result;
            }catch(OperationCanceledException){if(!started){record.State="cancelled";record.Result=TextResources.Get("agentRunJournal.executeAsync.recordResult");Save(path,record);}else ReconciliationRequired=true;throw;}
            catch(Exception){if(started)ReconciliationRequired=true;throw;}
        }
        public void Acknowledge(string taskId)
        {
            if(!Directory.Exists(directory))return;foreach(string path in Directory.EnumerateFiles(directory,"*.run")){var record=Read(path);if(record.TaskId==taskId&&(record.State=="prepared"||record.State=="started"||record.State=="needs_reconciliation")){record.State="reconciled";record.Result=TextResources.Get("agentRunJournal.acknowledge.recordResult");Save(path,record);}}ReconciliationRequired=false;
        }
    }
    public sealed partial class AgentWorkspace
    {
        AgentRunJournal runJournal;
        public bool NeedsReconciliation {get{return runJournal!=null&&runJournal.ReconciliationRequired||jobsJournal!=null&&jobsJournal.ReconciliationRequired;}}
        Task<string> ExecuteRecordedAsync(AgentToolInvocation invocation,CancellationToken token)
        {
            bool effect=AgentToolCatalog.Definitions.Concat(dynamicTools).Any(t=>t.Name==invocation.ToolName&&!t.ReadOnly);
            if(runJournal==null||!effect)return ExecuteAuthorizedAsync(invocation,token,null);
            return Task.Run(()=>runJournal.ExecuteAsync(invocation,before=>ExecuteAuthorizedAsync(invocation,token,before),()=>InvocationHashes(invocation)),token);
        }
        Dictionary<string,string> InvocationHashes(AgentToolInvocation invocation)
        {
            var values=new Dictionary<string,string>();if(invocation.ToolName!="write_file"&&invocation.ToolName!="edit_file"&&invocation.ToolName!="restore_file")return values;object path;var args=invocation.ReadArguments();if(args.TryGetValue("path",out path)&&path is string){string full=Resolve((string)path);if(File.Exists(full))using(LockDirectories(Path.GetDirectoryName(full),false))using(var hash=SHA256.Create())values[full]=BitConverter.ToString(hash.ComputeHash(ReadBytes(full,CancellationToken.None,DocumentFormats.MaxDocumentBytes))).Replace("-","");else values[full]="missing";}return values;
        }
        internal static string RunJournalPath(string root){return Path.Combine(root,".deskpet-agent-kernel","runs");}
    }
}
