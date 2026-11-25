using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class AgentRunRecoveryTests
    {
        static int count;static void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;Console.WriteLine("PASS "+why);}
        static AgentToolInvocation Invocation(string call, string task="task", string content="append", string tool="write_file", string session="session")
        {
            return new AgentToolInvocation(tool,new Dictionary<string,object>{{"path","file.txt"},{"content",content}},new AgentToolExecutionContext{CallId=call,SessionId=session,ProjectId="project",TaskId=task});
        }
        public static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("Agent run recovery tests: "+count+" passed");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
        static async Task Run(){string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"run-recovery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);int effects=0;var journal=new AgentRunJournal(root);string result=await journal.ExecuteAsync(Invocation("done"),before=>{before();effects++;return Task.FromResult("written");});Check(result=="written"&&effects==1,"completed side effect recorded");journal=new AgentRunJournal(root);await journal.ExecuteAsync(Invocation("done"),before=>{effects++;return Task.FromResult("bad");});Check(effects==1,"reopen does not replay completed side effect");
            journal=new AgentRunJournal(root);journal.BeforeSave=r=>{if(r.State=="completed")throw new IOException("fixture storage failure");};bool failed=false;try{await journal.ExecuteAsync(Invocation("crash"),before=>{before();effects++;return Task.FromResult("written");});}catch(IOException){failed=true;}Check(failed&&effects==2&&journal.ReconciliationRequired,"post-effect completion-save failure is marked for reconciliation");
            journal=new AgentRunJournal(root);Check(journal.HasPending("task"),"restart detects interrupted side effect");try{await journal.ExecuteAsync(Invocation("crash"),before=>{effects++;return Task.FromResult("bad");});}catch(ApiException){}Check(effects==2,"interrupted call cannot blindly repeat");journal.Acknowledge("task");Check(!journal.HasPending("task"),"explicit user reconciliation clears pending gate");
            journal=new AgentRunJournal(root);journal.BeforeSave=r=>{throw new IOException("before-effect save failure");};try{await journal.ExecuteAsync(Invocation("before"),before=>{effects++;return Task.FromResult("bad");});}catch(IOException){}Check(effects==2,"intent-save failure prevents all effects");
            string[] files=Directory.GetFiles(root,"*.run",SearchOption.AllDirectories);Check(files.Length>0&&!Encoding.UTF8.GetString(File.ReadAllBytes(files[0])).Contains("written"),"execution journal is protected at rest");
            await LegacyMigration(Path.Combine(root,"legacy"));
            await InterruptedMigration(Path.Combine(root,"interrupted"));
            await ConcurrentExecution(Path.Combine(root,"concurrent"));
            await BackgroundJobMigration(Path.Combine(root,"background-jobs"));
            if(Environment.OSVersion.Platform==PlatformID.Win32NT)BackgroundJournalBlocksResume(root);
            Directory.Delete(IoPath(root),true);
        }

        static string Hash(string value)
        {
            using(var hash=SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","");
        }

        static string RecordPath(string directory, AgentToolInvocation invocation)
        {
            return IoPath(Path.Combine(directory,Hash(invocation.Key)+".run"));
        }

        static string IoPath(string path)
        {
            return Environment.OSVersion.Platform==PlatformID.Win32NT?Maintenance.CachePaths.NativePath(path):path;
        }

        static string TaskDirectory(string root,string task,string state)
        {
            return Path.Combine(root,"tasks",Hash(task),state);
        }

        static string WriteLegacy(string directory, AgentToolInvocation invocation, string state, string result=null)
        {
            Directory.CreateDirectory(directory);
            var record=new AgentRunRecord
            {
                ExecutionId=Guid.NewGuid().ToString("N"),SessionId=invocation.SessionId,ProjectId=invocation.ProjectId,
                TaskId=invocation.TaskId,CallId=invocation.CallId,ToolName=invocation.ToolName,
                PayloadHash=invocation.PayloadHash,State=state,Result=result
            };
            string path=RecordPath(directory,invocation);
            File.WriteAllBytes(path,SecretProtection.Protect(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(record))));
            return path;
        }

        static Task<string> UnexpectedEffect(Action before)
        {
            throw new Exception("the journal repeated an effect");
        }

        static async Task LegacyMigration(string root)
        {
            for(int i=0;i<4100;i++)WriteLegacy(root,Invocation("old-"+i,"other"),"completed","written");
            var original=Invocation("done");
            string oldPath=WriteLegacy(root,original,"completed","old result");
            byte[] oldBytes=File.ReadAllBytes(oldPath);
            WriteLegacy(root,Invocation("uncertain","other"),"started");
            var journal=new AgentRunJournal(root);
            Check(!journal.HasPending("task")&&journal.HasPending("other"),"legacy history beyond 4096 records does not block another task");
            Check(Directory.GetFiles(root,"*.run").Length==0&&oldBytes.SequenceEqual(File.ReadAllBytes(RecordPath(TaskDirectory(root,"task","archive"),original))),"migration moves the encrypted version 1 record without rewriting it");
            Check(await journal.ExecuteAsync(original,UnexpectedEffect)=="old result","migrated terminal result replays under the same invocation key");
            bool mismatch=false;
            try{await journal.ExecuteAsync(Invocation("done",content:"different"),UnexpectedEffect);}
            catch(InvalidOperationException){mismatch=true;}
            Check(mismatch,"migrated invocation rejects a different payload under the same key");
            string damaged=RecordPath(TaskDirectory(root,"other","pending"),Invocation("unrelated","other"));
            File.WriteAllText(damaged,"damaged");
            journal.Acknowledge("task");
            Check(!journal.HasPending("task"),"an unrelated task's damaged record cannot affect pending checks or acknowledgment");
            bool rejected=false;
            try{journal.HasPending("other");}
            catch(IOException){rejected=journal.ReconciliationRequired;}
            Check(rejected,"a damaged record marks its own task for reconciliation");
            rejected=false;
            try{journal.Acknowledge("other");}
            catch(IOException){rejected=true;}
            Check(rejected&&File.ReadAllText(damaged)=="damaged","acknowledgment refuses to overwrite a damaged record");
        }

        static async Task InterruptedMigration(string root)
        {
            var completed=Invocation("moved");
            var uncertain=Invocation("uncertain");
            string completedPath=WriteLegacy(root,completed,"completed","saved result");
            string archive=TaskDirectory(root,"task","archive");
            Directory.CreateDirectory(archive);
            File.Move(completedPath,RecordPath(archive,completed));
            string pendingPath=WriteLegacy(root,uncertain,"started");
            var journal=new AgentRunJournal(root);
            Check(journal.HasPending("task")&&!File.Exists(pendingPath),"migration resumes after a subset of flat records moved");
            bool blocked=false;
            try{await journal.ExecuteAsync(uncertain,UnexpectedEffect);}
            catch(ApiException){blocked=true;}
            Check(blocked,"a migrated started call requires reconciliation");
            string conflictingSource=WriteLegacy(root,completed,"completed","different result");
            byte[] source=File.ReadAllBytes(conflictingSource);
            string conflictingDestination=RecordPath(archive,completed);
            byte[] destination=File.ReadAllBytes(conflictingDestination);
            bool rejected=false;
            try{await journal.ExecuteAsync(Invocation("new"),UnexpectedEffect);}
            catch(IOException){rejected=true;}
            Check(rejected&&source.SequenceEqual(File.ReadAllBytes(conflictingSource))&&destination.SequenceEqual(File.ReadAllBytes(conflictingDestination)),"a migration conflict preserves both records and blocks new effects");
            File.Delete(conflictingSource);
            journal.Acknowledge("task");
            Check(!journal.HasPending("task")&&await journal.ExecuteAsync(completed,UnexpectedEffect)=="saved result","acknowledgment archives uncertain calls and preserves previous results");
            var beforeMove=Invocation("durable-before-move");
            string beforeMovePath=WriteLegacy(TaskDirectory(root,"task","pending"),beforeMove,"completed","durable result");
            Check(!journal.HasPending("task")&&await journal.ExecuteAsync(beforeMove,UnexpectedEffect)=="durable result"&&
                !File.Exists(beforeMovePath)&&File.Exists(RecordPath(archive,beforeMove)),
                "a terminal record left pending by an interrupted move replays and archives without a second effect");
        }

        static async Task ConcurrentExecution(string root)
        {
            var invocation=Invocation("same");
            var proceed=new TaskCompletionSource<string>();
            var primary=new AgentRunJournal(root);
            var competing=new AgentRunJournal(root);
            Task<string> execution=primary.ExecuteAsync(invocation,before=>{before();return proceed.Task;});
            try
            {
                bool blocked=false;
                try{competing.Acknowledge("task");}
                catch(IOException){blocked=true;}
                Check(blocked,"another journal cannot acknowledge a live execution");
                blocked=false;
                try{await competing.ExecuteAsync(Invocation("another",session:"other-session"),UnexpectedEffect);}
                catch(ApiException){blocked=true;}
                Check(blocked,"another conversation cannot start an effect on the task while its journal is active");
                Check(await competing.ExecuteAsync(Invocation("independent","other"),before=>{before();return Task.FromResult("independent result");})=="independent result","an unrelated task can execute concurrently");
            }
            finally{proceed.SetResult("saved");}
            await execution;
            Check(await competing.ExecuteAsync(invocation,UnexpectedEffect)=="saved","a competing journal replays the durable result after completion");
        }

        static async Task BackgroundJobMigration(string root)
        {
            var invocation=Invocation("stopped",tool:"stop_job");
            WriteLegacy(root,invocation,"started");
            var journal=new AgentRunJournal(root);
            Check(journal.HasPending("task")&&AgentJobTools.IsFailure(await journal.ExecuteAsync(invocation,UnexpectedEffect)),"background job migration preserves unresolved operations and job error format");
            journal.Acknowledge("task");
            Check(!journal.HasPending("task")&&AgentJobTools.IsFailure(await journal.ExecuteAsync(invocation,UnexpectedEffect)),"acknowledged background operations cannot run under their original key");
        }

        static void BackgroundJournalBlocksResume(string root)
        {
            string project=Path.Combine(root,new string('w',Math.Max(30,154-root.Length)));
            Directory.CreateDirectory(project);
            var state=new AgentTaskState{Id="task",Workspace=project};
            var invocation=Invocation("background-only",tool:"stop_job");
            string journal=AgentWorkspace.JobRunJournalPath(project);
            WriteLegacy(journal,invocation,"started");
            using(var workspace=new AgentWorkspace(project))
            {
                workspace.ConfigureDesktopSession();
                bool blocked=false;
                try{workspace.BeginTask(state);}
                catch(ApiException){blocked=true;}
                Check(blocked&&workspace.NeedsReconciliation&&workspace.TaskState.Status=="needs_reconciliation",
                    "background-only pending execution preserves reconciliation state in a deep Windows workspace");
                workspace.AcknowledgeRunJournals(state.Id);
                workspace.BeginTask(state);
                Check(!workspace.NeedsReconciliation&&workspace.TaskState.Status=="running",
                    "acknowledging the background journal allows the task to resume");
            }
        }
    }
}
