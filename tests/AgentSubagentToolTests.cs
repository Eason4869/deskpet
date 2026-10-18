using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class AgentSubagentToolTests
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        public static int Main()
        {
            try
            {
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"subagent-tool-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var workspace=new AgentWorkspace(root);var audit=new AgentAuditLog(root);var claims=new ExecutionClaimStore(Path.Combine(root,"claims"));
                workspace.ConfigureKernel(AgentPermissionRuntime.DefaultWorkspacePolicy(),audit,null,claims,"parent-1");workspace.ConfigureAgentProfile(new AgentProfileRegistry().Get("build"));
                var child=workspace.SubagentScheduler.Schedule("parent-1",new AgentProfileRegistry().Get("explore"),"inspect");
                Check(child.Status=="running" && workspace.Subagents.List("parent-1").Count==1,"internal scheduler retains child lifecycle API");
                string completed=workspace.ExecuteAsync("complete_subagent",new JavaScriptSerializer().Serialize(new{id=child.Id,result="forged"}),CancellationToken.None).GetAwaiter().GetResult();
                Check(completed.StartsWith("错误：") && workspace.SubagentScheduler.Complete(child.Id,"internal worker result"),"model completion is rejected while internal worker completion remains available");
                Console.WriteLine("Agent subagent tool tests: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
    }
}
