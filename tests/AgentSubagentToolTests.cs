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
                string spawned=workspace.ExecuteAsync("spawn_subagent",new JavaScriptSerializer().Serialize(new{profile="explore",input="inspect"}),CancellationToken.None).GetAwaiter().GetResult();
                var spawnedObject=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(spawned);string id=spawnedObject.ContainsKey("id")?spawnedObject["id"].ToString():spawnedObject["Id"].ToString();
                Check(spawned.Contains("running") && workspace.Subagents.List("parent-1").Count==1,"spawn_subagent creates a running child lifecycle");
                string completed=workspace.ExecuteAsync("complete_subagent",new JavaScriptSerializer().Serialize(new{id=id,result="done"}),CancellationToken.None).GetAwaiter().GetResult();
                Check(completed.Contains("complete") && workspace.Subagents.List("parent-1")[0].Status=="complete","complete_subagent finalizes the child lifecycle");
                Console.WriteLine("Agent subagent tool tests: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
    }
}
