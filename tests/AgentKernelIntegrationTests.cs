using System;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class AgentKernelIntegrationTests
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        public static int Main()
        {
            try
            {
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"kernel-integration-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var workspace=new AgentWorkspace(root);
                var permissions=new AgentPermissionRuntime();permissions.Add(new AgentPermissionRule{Action="write",Resource="**",Effect=AgentPermissionEffect.Deny});var audit=new AgentAuditLog(root);
                workspace.ConfigureKernel(permissions,audit);
                string denied=workspace.ExecuteAsync("write_file",new JavaScriptSerializer().Serialize(new{path="blocked.txt",content="no"}),CancellationToken.None).GetAwaiter().GetResult();
                Check(denied.StartsWith("错误：权限拒绝") && !File.Exists(Path.Combine(root,"blocked.txt")),"workspace denies writes before touching files");
                Check(audit.Events.Count==1 && audit.Events[0].Result=="denied","permission denial is audited with tool identity");
                permissions=new AgentPermissionRuntime();permissions.Add(new AgentPermissionRule{Action="write",Resource="**",Effect=AgentPermissionEffect.Allow});audit=new AgentAuditLog(Path.Combine(root,"allowed"));workspace.ConfigureKernel(permissions,audit);
                string allowed=workspace.ExecuteAsync("write_file",new JavaScriptSerializer().Serialize(new{path="ok.txt",content="yes"}),CancellationToken.None).GetAwaiter().GetResult();
                Check(!allowed.StartsWith("错误：") && File.ReadAllText(Path.Combine(root,"ok.txt"))=="yes","workspace executes allowed writes");
                Check(audit.Events.Count==1 && audit.Events[0].Result=="completed" && audit.Events[0].CallId.Length>0,"allowed tool execution records stable audit identity");
                Console.WriteLine("Agent kernel integration tests: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
    }
}
