using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class AgentKernelTests
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        public static int Main()
        {
            try
            {
                var permission=new AgentPermissionRuntime();
                Check(permission.Decide("unknown","anything").Effect==AgentPermissionEffect.Deny,"unknown actions default to deny");
                permission.Add(new AgentPermissionRule{Action="read",Resource="**",Effect=AgentPermissionEffect.Allow});
                permission.Add(new AgentPermissionRule{Action="write",Resource="src/**",Effect=AgentPermissionEffect.Allow});
                permission.Add(new AgentPermissionRule{Action="write",Resource="src/secret/**",Effect=AgentPermissionEffect.Deny});
                Check(permission.Decide("write","src/secret/key.txt").Effect==AgentPermissionEffect.Deny && permission.Decide("write","src/app.cs").Effect==AgentPermissionEffect.Allow,"deny rules override allow rules by resource");
                permission.Add(new AgentPermissionRule{Action="network",Resource="https://example.test",Effect=AgentPermissionEffect.Ask});var approved=false;var approval=new DelegateApproval(request=>{approved=true;return Task.FromResult(true);});
                var decision=permission.AuthorizeAsync("network","https://example.test",approval).GetAwaiter().GetResult();
                Check(decision.Effect==AgentPermissionEffect.Allow && approved,"ask decisions require an approval provider");
                var audit=new AgentAuditLog(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"audit-"+Guid.NewGuid().ToString("N")));
                var registry=new AgentToolRuntime(permission,audit);
                registry.Register(new AgentToolDefinition{Name="echo",Description="echo",ReadOnly=true},(args,ctx,cancel)=>Task.FromResult("ok:"+args["text"]));
                var result=registry.ExecuteAsync("echo",new Dictionary<string,object>{{"text","hi"}},new AgentToolExecutionContext{SessionId="s",AgentId="a",MessageId="m",CallId="call-1"},CancellationToken.None).GetAwaiter().GetResult();
                Check(result=="ok:hi" && audit.Events.Count==1 && audit.Events[0].CallId=="call-1","tool runtime executes with stable call identity and audit");
                var claim=new ExecutionClaimStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"claims-"+Guid.NewGuid().ToString("N")));
                Check(claim.TryClaim("task-1","call-2"),"execution claim admits one owner");
                Check(!claim.TryClaim("task-1","call-2") && claim.Complete("task-1","call-2"),"duplicate claim is rejected and completion releases ownership");
                Check(claim.TryClaim("task-1","call-3"),"released execution claim can be reused");
                string claimRoot=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"claims-restart-"+Guid.NewGuid().ToString("N"));var persisted=new ExecutionClaimStore(claimRoot);persisted.TryClaim("task-2","call-9");var reloaded=new ExecutionClaimStore(claimRoot);Check(reloaded.Interrupted.Contains("task-2"),"execution claims survive restart and expose interrupted work");
                Console.WriteLine("Agent kernel tests: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
        sealed class DelegateApproval:IAgentApprovalProvider
        {
            readonly Func<AgentApprovalRequest,Task<bool>> handler;public DelegateApproval(Func<AgentApprovalRequest,Task<bool>> handler){this.handler=handler;}
            public Task<bool> ApproveAsync(AgentApprovalRequest request){return handler(request);}
        }
    }
}
