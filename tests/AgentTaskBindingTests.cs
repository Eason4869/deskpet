using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class AgentTaskBindingTests
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        sealed class Api:IConversationApi
        {
            public Task<string> ChatAsync(ConnectionSettings s,IList<ChatMessage> m,CancellationToken c){return Task.FromResult("done");}
            public Task<string> TranscribeAsync(ConnectionSettings s,byte[] w,CancellationToken c){return Task.FromResult("");}
            public Task<byte[]> SpeechAsync(ConnectionSettings s,string v,string t,CancellationToken c){return Task.FromResult(new byte[0]);}
            public void Dispose(){}
        }
        sealed class Speech:IReplySpeech{public Task SpeakAsync(string t,PetSettings s,CancellationToken c){return Task.FromResult(0);}public void SetVolume(int n){}public void Stop(){}public void Dispose(){}}
        sealed class Approval:IAgentApprovalProvider
        {
            public int Calls;public Task<bool> ApproveAsync(AgentApprovalRequest request){Calls++;return Task.FromResult(true);}
        }
        public static int Main()
        {
            try
            {
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"task-binding-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                var session=new ConversationSession(new Api(),new Speech(),()=>null);var model=new AgentSessionModel("chat-1","project-1","conversation-1");var sessionStore=new AgentSessionStore(Path.Combine(root,"sessions"));session.ConfigureKernelSession(model,sessionStore);
                PetSettings cfg=new PetSettings{ReadReplies=false,SaveChatHistory=false,AgentEnabled=true,AgentWorkspace=root};cfg.Text.BaseUrl="http://127.0.0.1:1";cfg.Text.Model="fixture";
                session.SendTextAsync("hello",cfg,"pet").GetAwaiter().GetResult();
                Check(model.Projection.DeliveredCount==1 && model.Projection.Step==1,"real conversation input is admitted and closes one kernel step");
                Check(sessionStore.Load("project-1","conversation-1").Projection.DeliveredCount==1,"real conversation input is persisted in the kernel session store");
                var workspace=new AgentWorkspace(root);var task=new AgentTaskState{Id="task-1",Workspace=root,Goal="write"};var audit=new AgentAuditLog(Path.Combine(root,"audit"));var claims=new ExecutionClaimStore(Path.Combine(root,"claims"));var approval=new Approval();var permissions=new AgentPermissionRuntime();permissions.Add(new AgentPermissionRule{Action="write",Resource="**",Effect=AgentPermissionEffect.Ask});workspace.ConfigureKernel(permissions,audit,approval,claims,"task-1");
                workspace.BeginTask(task);string result=workspace.ExecuteAsync("write_file",new JavaScriptSerializer().Serialize(new{path="bound.txt",content="ok"}),CancellationToken.None).GetAwaiter().GetResult();
                Check(!result.StartsWith("错误：") && approval.Calls==1,"approval provider authorizes a real task write");
                Check(audit.Events.Count==1 && audit.Events[0].TaskId=="task-1","real task audit records task identity");
                workspace.FinishTask("complete","done");
                Check(!claims.Interrupted.Contains("task-1"),"completing a real task releases its execution claim");
                Console.WriteLine("Agent task binding tests: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
    }
}
