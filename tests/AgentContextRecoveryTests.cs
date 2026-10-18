using System;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class AgentContextRecoveryTests
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        public static int Main()
        {
            try
            {
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"context-recovery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                var model=new AgentSessionModel("session-1","project-1","conversation-1");var store=new AgentSessionStore(Path.Combine(root,"sessions"));var audit=new AgentAuditLog(Path.Combine(root,"audit"));var claims=new ExecutionClaimStore(Path.Combine(root,"claims"));
                var workspace=new AgentWorkspace(root);workspace.ConfigureKernel(AgentPermissionRuntime.DefaultWorkspacePolicy(),audit,null,claims,"task-1",model);var task=new AgentTaskState{Id="task-1",Workspace=root,Goal="compact"};workspace.BeginTask(task);
                workspace.ContextProgress("compacting");workspace.RecordCompaction(8123,"summary");workspace.FinishTask("complete","done");store.Save(model);
                var loaded=store.Load("project-1","conversation-1");
                Check(loaded.Events.Exists(e=>e.Type=="context.compacted") && loaded.Events.Exists(e=>e.Type=="task.completed"),"context compaction and task completion are durable session events");
                Check(workspace.TaskState.ContextSummary=="summary" && workspace.TaskState.ContextCompactions==1,"task checkpoint retains recovered context facts");
                Check(!claims.Interrupted.Contains("task-1"),"context recovery releases the task execution claim");
                Console.WriteLine("Agent context recovery tests: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
    }
}
