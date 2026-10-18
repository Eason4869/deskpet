using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class AgentRunRecoveryTests
    {
        static int count;static void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;Console.WriteLine("PASS "+why);}
        static AgentToolInvocation Invocation(string call){return new AgentToolInvocation("write_file",new Dictionary<string,object>{{"path","file.txt"},{"content","append"}},new AgentToolExecutionContext{CallId=call,SessionId="session",ProjectId="project",TaskId="task"});}
        public static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("Agent run recovery tests: "+count+" passed");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
        static async Task Run(){string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"run-recovery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);int effects=0;var journal=new AgentRunJournal(root);string result=await journal.ExecuteAsync(Invocation("done"),before=>{before();effects++;return Task.FromResult("written");});Check(result=="written"&&effects==1,"completed side effect recorded");journal=new AgentRunJournal(root);await journal.ExecuteAsync(Invocation("done"),before=>{effects++;return Task.FromResult("bad");});Check(effects==1,"reopen does not replay completed side effect");
            journal=new AgentRunJournal(root);journal.BeforeSave=r=>{if(r.State=="completed")throw new IOException("fixture storage failure");};bool failed=false;try{await journal.ExecuteAsync(Invocation("crash"),before=>{before();effects++;return Task.FromResult("written");});}catch(IOException){failed=true;}Check(failed&&effects==2&&journal.ReconciliationRequired,"post-effect completion-save failure is marked for reconciliation");
            journal=new AgentRunJournal(root);Check(journal.HasPending("task"),"restart detects interrupted side effect");try{await journal.ExecuteAsync(Invocation("crash"),before=>{effects++;return Task.FromResult("bad");});}catch(ApiException){}Check(effects==2,"interrupted call cannot blindly repeat");journal.Acknowledge("task");Check(!journal.HasPending("task"),"explicit user reconciliation clears pending gate");
            journal=new AgentRunJournal(root);journal.BeforeSave=r=>{throw new IOException("before-effect save failure");};try{await journal.ExecuteAsync(Invocation("before"),before=>{effects++;return Task.FromResult("bad");});}catch(IOException){}Check(effects==2,"intent-save failure prevents all effects");
            string[] files=Directory.GetFiles(root,"*.run");Check(files.Length>0&&!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(files[0])).Contains("written"),"execution journal is protected at rest");
        }
    }
}
