using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
namespace Dafeiyu.Tests
{
    public static class WorkspacePermissionTests
    {
        static int checks;
        static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;}
        sealed class User:ICancellableAgentApprovalProvider{public int Calls;public bool Allow=true;public Task<bool> ApproveAsync(AgentApprovalRequest r){return ApproveAsync(r,CancellationToken.None);}public Task<bool> ApproveAsync(AgentApprovalRequest r,CancellationToken ct){Calls++;return Task.FromResult(Allow);}}
        sealed class WaitingUser:IAgentApprovalProvider
        {
            internal readonly TaskCompletionSource<bool> Answer=new TaskCompletionSource<bool>();
            internal readonly TaskCompletionSource<AgentApprovalRequest> Requested=new TaskCompletionSource<AgentApprovalRequest>();
            public Task<bool> ApproveAsync(AgentApprovalRequest request){Requested.TrySetResult(request);return Answer.Task;}
        }
        sealed class Reviewer:IAgentRiskReviewer{public int Calls;public AgentReviewVerdict Verdict;public bool Fail;public Task<AgentReviewResult> ReviewAsync(AgentApprovalRequest r,CancellationToken ct){Calls++;if(Fail)throw new IOException("fixture unavailable");return Task.FromResult(new AgentReviewResult{Verdict=Verdict,Reason="fixture review"});}}
        sealed class ChildModel:IAgentConversationApi{public async Task<string> ChatAgentAsync(ConnectionSettings s,IList<ChatMessage> messages,AgentWorkspace w,Action<string> delta,Action<string> reasoning,Action<AgentToolEvent> tool,bool stream,CancellationToken cancel){return await w.ExecuteAsync("read_file",new JavaScriptSerializer().Serialize(new{path=messages.Last().Content}),cancel);}}
        sealed class Silent:IReplySpeech{public Task SpeakAsync(string s,PetSettings p,CancellationToken ct){return Task.FromResult(0);}public void Stop(){}public void SetVolume(int n){}public void Dispose(){}}
        sealed class Plain:IConversationApi{public string Reply="";public IList<ChatMessage> Messages;public Task<string> ChatAsync(ConnectionSettings s,IList<ChatMessage> m,CancellationToken ct){Messages=m;return Task.FromResult(Reply);}public Task<string> TranscribeAsync(ConnectionSettings s,byte[] w,CancellationToken ct){return Task.FromResult("");}public Task<byte[]> SpeechAsync(ConnectionSettings s,string v,string t,CancellationToken ct){return Task.FromResult(new byte[0]);}public void Dispose(){}}
        static AgentToolInvocation Call(string tool,object args){return new AgentToolInvocation(tool,new JavaScriptSerializer().DeserializeObject(new JavaScriptSerializer().Serialize(args)) as Dictionary<string,object>,new AgentToolExecutionContext{CallId=Guid.NewGuid().ToString("N"),SessionId="s",ProjectId="p",TaskId="t"});}
        static AgentPermissionEffect Decide(PetSettings cfg,AgentToolInvocation call){return new AgentAccessPolicy(cfg).Decide(call,AgentPermissionRuntime.DefaultWorkspacePolicy().Decide(call.Action,call.Resource)).Effect;}
        public static int Main(){try{
            string root=Path.Combine(Directory.GetCurrentDirectory(),"work","permissions","policy-project");Directory.CreateDirectory(root);string outside=Path.Combine(Path.GetDirectoryName(root),"outside.txt");
            var fresh=new PetSettings();Check(fresh.AgentApprovalMode==AgentApprovalMode.Auto&&fresh.AgentSandboxEnabled&&fresh.AgentTerminalPolicy==AgentTerminalPolicy.Disabled,"new settings default to automatic review with sandbox and terminal still disabled");
            Check(fresh.Copy().AgentApprovalMode==AgentApprovalMode.Auto&&fresh.Copy().AgentSandboxEnabled,"new automatic-review default survives request snapshots");
            Check(new SettingsStore(Path.Combine(root,"missing-"+Guid.NewGuid().ToString("N")+".json")).Load().AgentApprovalMode==AgentApprovalMode.Auto,"missing settings file receives the new default");
            var freshProject=ProjectCatalogStore.CreateProject(new ProjectCatalog(),"new project",root);Check(freshProject.ApprovalMode==AgentApprovalMode.Auto&&freshProject.SandboxEnabled&&freshProject.TerminalPolicy==AgentTerminalPolicy.Disabled,"new projects inherit automatic review with sandbox and terminal disabled");
            var oldProject=(ProjectDefinition)new DataContractJsonSerializer(typeof(ProjectDefinition)).ReadObject(new MemoryStream(Encoding.UTF8.GetBytes("{\"FullAccess\":false,\"TerminalPolicy\":1}")));Check(oldProject.ApprovalMode==AgentApprovalMode.Legacy&&!oldProject.SandboxEnabled&&!oldProject.FullAccess&&oldProject.TerminalPolicy==AgentTerminalPolicy.ConfirmEach,"old project JSON without permission fields retains legacy boundaries and terminal confirmation");
            string legacy=Path.Combine(root,"legacy.json");File.WriteAllText(legacy,"{\"AgentTerminalPolicy\":1,\"AgentFullAccess\":false}");var old=new SettingsStore(legacy).Load();Check(old.AgentApprovalMode==AgentApprovalMode.Legacy&&!old.AgentSandboxEnabled&&old.AgentTerminalPolicy==AgentTerminalPolicy.ConfirmEach&&!old.AgentFullAccess,"legacy settings retain exact terminal/file behavior");
            Check(AgentAccessPolicy.DisplayMode(old)==AgentApprovalMode.Ask&&AgentAccessPolicy.Label(old)=="请求批准 · 沙箱关闭"&&old.AgentApprovalMode==AgentApprovalMode.Legacy,"restricted legacy settings display one of the three names without changing runtime mode");
            new SettingsStore(legacy).Save(old);var oldSaved=new SettingsStore(legacy).Load();Check(oldSaved.AgentApprovalMode==AgentApprovalMode.Legacy&&!oldSaved.AgentSandboxEnabled&&!oldSaved.AgentFullAccess&&oldSaved.AgentTerminalPolicy==AgentTerminalPolicy.ConfirmEach,"saving existing legacy settings retains their exact permissions and terminal confirmation");
            string unrestricted=Path.Combine(root,"legacy-full.json");File.WriteAllText(unrestricted,"{\"AgentFullAccess\":true}");var oldFull=new SettingsStore(unrestricted).Load();Check(oldFull.AgentApprovalMode==AgentApprovalMode.Legacy&&!oldFull.AgentSandboxEnabled&&oldFull.AgentTerminalPolicy==AgentTerminalPolicy.Disabled&&AgentAccessPolicy.Label(oldFull)=="完全访问权限 · 沙箱关闭","legacy unrestricted files retain disabled terminal and use the full-access display name");
            string defaultPath=Path.Combine(root,"fresh-default.json");new SettingsStore(defaultPath).Save(fresh);var freshSaved=new SettingsStore(defaultPath).Load();Check(freshSaved.AgentApprovalMode==AgentApprovalMode.Auto&&freshSaved.AgentSandboxEnabled&&freshSaved.AgentTerminalPolicy==AgentTerminalPolicy.Disabled,"explicit new default roundtrips while omitted old fields stay legacy");
            var cfg=new PetSettings{AgentWorkspace=root,AgentApprovalMode=AgentApprovalMode.Ask,AgentSandboxEnabled=true,AgentTerminalPolicy=AgentTerminalPolicy.ConfirmEach};
            Check(cfg.Copy().AgentApprovalMode==AgentApprovalMode.Ask&&cfg.Copy().AgentSandboxEnabled,"permission fields survive snapshot");
            foreach(string tool in new[]{"github_read_file","github_list_files","github_search_code","github_read_issue"})Check(Decide(cfg,Call(tool,new{query="private project text",path="README.md",number=1}))==AgentPermissionEffect.Ask,"GitHub read requires network approval in ask mode: "+tool);
            var legacyNetwork=cfg.Copy();legacyNetwork.AgentApprovalMode=AgentApprovalMode.Legacy;Check(Decide(legacyNetwork,Call("github_search_code",new{query="legacy"}))==AgentPermissionEffect.Allow,"legacy GitHub read keeps previous behavior");
            var fullNetwork=cfg.Copy();fullNetwork.AgentApprovalMode=AgentApprovalMode.Full;Check(Decide(fullNetwork,Call("github_search_code",new{query="full"}))==AgentPermissionEffect.Allow,"explicit full access permits GitHub read");
            new SettingsStore(Path.Combine(root,"saved.json")).Save(cfg);var saved=new SettingsStore(Path.Combine(root,"saved.json")).Load();Check(saved.AgentSandboxEnabled&&saved.AgentApprovalMode==AgentApprovalMode.Ask,"permission fields survive disk roundtrip");
            Check(Decide(cfg,Call("write_file",new{path="inside.txt",content="ok"}))==AgentPermissionEffect.Allow,"project write is allowed");
            Check(Decide(cfg,Call("write_file",new{path=outside,content="ok"}))==AgentPermissionEffect.Ask,"outside write requires approval");
            Check(Decide(cfg,Call("read_files",new{paths=new[]{"inside.txt",outside}}))==AgentPermissionEffect.Ask,"batch read cannot hide outside path");
            Check(Decide(cfg,Call("apply_patch",new{edits=new[]{new{path=outside,content="ok"}}}))==AgentPermissionEffect.Ask,"batch edit cannot hide outside path");
            Check(Decide(cfg,Call("exec_command",new{command="Get-ChildItem"}))==AgentPermissionEffect.Ask,"arbitrary sandbox commands require an exact approval rather than a blacklist exemption");
            Check(Decide(cfg,Call("exec_command",new{command="Remove-Item -Recurse ."}))==AgentPermissionEffect.Ask,"destructive sandbox command requires confirmation");
            foreach(var mode in new[]{AgentApprovalMode.Ask,AgentApprovalMode.Auto})
            {
                string deniedRoot=Path.Combine(root,"denied-commands-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(deniedRoot);string victim=Path.Combine(deniedRoot,"victim.txt");File.WriteAllText(victim,"keep-me");
                var deniedSettings=cfg.Copy();deniedSettings.AgentWorkspace=deniedRoot;deniedSettings.AgentApprovalMode=mode;deniedSettings.Text.ToolSupport=CapabilitySupport.Supported;deniedSettings.Text.Model="gpt-fixture";var refusing=new User{Allow=false};
                using(var workspace=ConversationSession.OpenAgentWorkspace(deniedSettings,refusing))
                {
                    foreach(string command in new[]{"ri -LiteralPath (Join-Path $PWD.ProviderPath 'victim.txt') -Force","Microsoft.PowerShell.Management\\Remove-Item -LiteralPath (Join-Path $PWD.ProviderPath 'victim.txt')","[System.IO.File]::Delete([System.IO.Path]::Combine($PWD.ProviderPath,'victim.txt'))","& ('Rem'+'ove-Item') -LiteralPath (Join-Path $PWD.ProviderPath 'victim.txt')","Write-Output ready; ri -LiteralPath (Join-Path $PWD.ProviderPath 'victim.txt')"})
                    {
                        Check(Decide(deniedSettings,Call("exec_command",new{command}))==AgentPermissionEffect.Ask,"all command spellings require approval: "+mode+" / "+command);
                        int priorCalls=refusing.Calls;string rejected=workspace.ExecuteAsync("exec_command",new JavaScriptSerializer().Serialize(new{command}),CancellationToken.None).GetAwaiter().GetResult();
                        Check(rejected.StartsWith("错误：")&&refusing.Calls==priorCalls+1&&File.ReadAllText(victim)=="keep-me"&&workspace.CommandResults.Count==0,"refused command never executes or deletes its fixture: "+mode+"; calls="+refusing.Calls+" prior="+priorCalls+"; result="+rejected);
                    }
                }
                var waiting=new WaitingUser();using(var workspace=ConversationSession.OpenAgentWorkspace(deniedSettings,waiting))using(var cancellation=new CancellationTokenSource())
                {
                    var execution=workspace.ExecuteAsync("exec_command","{\"command\":\"Get-ChildItem\"}",cancellation.Token);
                    Check(waiting.Requested.Task.Wait(5000),"terminal waits for its exact approval before process start");cancellation.Cancel();bool stopped=false;
                    try{execution.GetAwaiter().GetResult();}catch(OperationCanceledException){stopped=true;}finally{waiting.Answer.TrySetResult(false);}
                    Check(stopped&&workspace.CommandResults.Count==0&&File.ReadAllText(victim)=="keep-me","cancelling a pending approval never starts a command");
                }
            }
            cfg.AgentSandboxEnabled=false;Check(Decide(cfg,Call("exec_command",new{command="Get-ChildItem"}))==AgentPermissionEffect.Ask,"unsandboxed command requires confirmation");
            cfg.AgentApprovalMode=AgentApprovalMode.Full;Check(Decide(cfg,Call("write_file",new{path=outside,content="ok"}))==AgentPermissionEffect.Allow,"explicit full mode permits outside files");
            Check(Decide(cfg,Call("exec_command",new{command="Get-ChildItem"}))==AgentPermissionEffect.Allow,"explicit full mode retains its selected command authority");
            var approvedSettings=cfg.Copy();approvedSettings.AgentApprovalMode=AgentApprovalMode.Ask;approvedSettings.AgentSandboxEnabled=true;approvedSettings.Text.ToolSupport=CapabilitySupport.Supported;approvedSettings.Text.Model="gpt-fixture";approvedSettings.AgentWorkspace=Path.Combine(root,"approved-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(approvedSettings.AgentWorkspace);var approving=new User();
            using(var workspace=ConversationSession.OpenAgentWorkspace(approvedSettings,approving))
            {
                string result=workspace.ExecuteAsync("exec_command",new JavaScriptSerializer().Serialize(new{command="[IO.File]::WriteAllText((Join-Path $PWD.ProviderPath 'approved.txt'),'ok'); Write-Output 'APPROVED_SANDBOX'"}),CancellationToken.None).GetAwaiter().GetResult();
                Check(approving.Calls==1&&result.Contains("APPROVED_SANDBOX")&&result.Contains("AppContainer")&&File.ReadAllText(Path.Combine(approvedSettings.AgentWorkspace,"approved.txt"))=="ok","approved command executes once through the actual AppContainer path");
            }
            cfg.AgentApprovalMode=AgentApprovalMode.Auto;cfg.AgentSandboxEnabled=true;
            var user=new User();var reviewer=new Reviewer{Verdict=AgentReviewVerdict.Allow};var provider=new AgentPolicyApprovalProvider(user,reviewer,new AgentAccessPolicy(cfg,"读取指定外部文档"));var request=new AgentApprovalRequest{Action="read",ToolName="read_file",Arguments="{}"};
            Check(provider.ApproveAsync(request).Result&&reviewer.Calls==1&&user.Calls==0,"auto review can approve exact bounded request");
            reviewer.Verdict=AgentReviewVerdict.Deny;Check(!provider.ApproveAsync(request).Result&&user.Calls==0,"review rejection is not automatically overridden");
            reviewer.Verdict=AgentReviewVerdict.Ask;Check(provider.ApproveAsync(request).Result&&user.Calls==1,"uncertainty transfers to user");
            reviewer.Fail=true;Check(provider.ApproveAsync(request).Result&&user.Calls==2,"review failure transfers to user");
            request.Action="mcp";Check(provider.ApproveAsync(request).Result&&reviewer.Calls==4&&user.Calls==3,"unknown MCP effects remain manual");
            request.Action="shell";Check(provider.ApproveAsync(request).Result&&reviewer.Calls==4&&user.Calls==4,"sensitive shell exception remains manual");
            cfg.AgentSandboxEnabled=false;provider=new AgentPolicyApprovalProvider(user,reviewer,new AgentAccessPolicy(cfg));request.Action="read";Check(provider.ApproveAsync(request).Result&&reviewer.Calls==4,"auto review without sandbox remains conservative");
            var rules=AgentPermissionRuntime.DefaultWorkspacePolicy();rules.AccessPolicy=new AgentAccessPolicy(cfg);rules.Add(new AgentPermissionRule{Action="write",Resource="**",Effect=AgentPermissionEffect.Deny});Check(new AgentAuthorizationService(rules,user).AuthorizeAsync(Call("write_file",new{path="x",content="x"}),CancellationToken.None).Result.Effect==AgentPermissionEffect.Deny,"explicit deny survives profile selection");
            using(var workspace=ConversationSession.OpenAgentWorkspace(new PetSettings{AgentWorkspace=root,AgentApprovalMode=AgentApprovalMode.Ask,AgentSandboxEnabled=true,AgentPlanMode=true},user)){Check(workspace.ReadOnly,"plan mode remains readonly");Check(workspace.ExecuteAsync("write_file","{\"path\":\"no.txt\",\"content\":\"bad\"}",CancellationToken.None).Result.StartsWith("错误：")&&!File.Exists(Path.Combine(root,"no.txt")),"plan mode blocks actual write");}
            using(var workspace=ConversationSession.OpenAgentWorkspace(new PetSettings{AgentWorkspace=root,AgentApprovalMode=AgentApprovalMode.Ask,AgentSandboxEnabled=true},user)){workspace.ConfigureProvider("openai","gpt-fixture");int before=user.Calls;Check(!workspace.ExecuteAsync("write_file",new JavaScriptSerializer().Serialize(new{path=outside,content="approved"}),CancellationToken.None).Result.StartsWith("错误：")&&File.ReadAllText(outside)=="approved"&&user.Calls==before+1,"approved outside file exception executes once; user calls="+user.Calls+" before="+before+" file="+File.Exists(outside));}
            cfg.AgentApprovalMode=AgentApprovalMode.Ask;cfg.AgentSandboxEnabled=true;cfg.Text.ToolSupport=CapabilitySupport.Supported;cfg.Text.Model="gpt-fixture";File.WriteAllText(outside,"child-private-sentinel");
            using(var workspace=ConversationSession.OpenAgentWorkspace(cfg,user)){workspace.BeginTask(new AgentTaskState{Id="parent-read",Workspace=root,Goal="read"});var runner=typeof(AgentWorkspace).GetField("childWorkers",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(workspace);typeof(AgentSubagentRunner).GetField("factory",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(runner,(Func<IAgentConversationApi>)(()=>new ChildModel()));var json=new JavaScriptSerializer();var spawned=(Dictionary<string,object>)json.DeserializeObject(workspace.ExecuteAsync("spawn_subagent",json.Serialize(new{profile="explore",input=outside}),CancellationToken.None).Result);string result=workspace.ExecuteAsync("wait_subagents",json.Serialize(new{ids=new[]{(string)spawned["Id"]},timeout_seconds=5}),CancellationToken.None).Result;Check(!result.Contains("child-private-sentinel"),"actual child worker cannot bypass parent outside-read approval");}
            using(var workspace=ConversationSession.OpenAgentWorkspace(cfg,user)){string records=Path.Combine(root,".deskpet-agent-kernel");Directory.CreateDirectory(records);File.WriteAllText(Path.Combine(records,"private-record.txt"),"protected-journal-sentinel");string result=workspace.ExecuteAsync("search_files","{\"path\":\".\",\"query\":\"protected-journal-sentinel\"}",CancellationToken.None).Result;Check(!result.Contains("protected-journal-sentinel"),"ancestor search must not disclose protected run records");}
            var global=cfg.Copy();global.AgentApprovalMode=AgentApprovalMode.Legacy;global.AgentSandboxEnabled=false;var project=ProjectCatalogStore.UnassignedProject(new ProjectCatalog(),global);var definition=new ConversationDefinition{Id=Guid.NewGuid().ToString("N"),Mode="work"};using(var opened=ConversationWorkspace.Create(project,definition,Path.Combine(root,"global"),"work",()=>new Plain(),()=>new Silent(),()=>null,()=>global,()=>false)){global.AgentApprovalMode=AgentApprovalMode.Ask;global.AgentSandboxEnabled=true;Check(opened.EffectiveSettings().AgentApprovalMode==AgentApprovalMode.Ask&&opened.EffectiveSettings().AgentSandboxEnabled,"already-open unassigned workspace follows saved global permissions");}
            var api=new Plain{Reply="{\"decision\":\"allow\",\"reason\":\"授权读取\"}"};request.UserIntent="读取我指定的文档";var realReviewer=new ApiAgentRiskReviewer(cfg.Text,()=>api);Check(realReviewer.ReviewAsync(request,CancellationToken.None).Result.Verdict==AgentReviewVerdict.Allow&&api.Messages.Count==2&&api.Messages[0].Role=="system"&&api.Messages[1].Content.Contains(request.UserIntent),"independent plain-model reviewer receives original intent and exact operation");
            api.Reply="{\"decision\":true}";Check(realReviewer.ReviewAsync(request,CancellationToken.None).Result==null,"malformed reviewer verdict cannot grant permission");
            cfg.AgentApprovalMode=AgentApprovalMode.Auto;cfg.AgentSandboxEnabled=true;reviewer.Fail=false;reviewer.Verdict=AgentReviewVerdict.Allow;int userBefore=user.Calls;bool auditFailed=false;provider=new AgentPolicyApprovalProvider(user,reviewer,new AgentAccessPolicy(cfg),(r,verdict)=>{throw new IOException("audit fixture failure");});try{provider.ApproveAsync(request).GetAwaiter().GetResult();}catch(IOException){auditFailed=true;}Check(auditFailed&&user.Calls==userBefore,"audit-save failure cannot turn auto approval into an unrecorded manual fallback");
            using(var stopped=new CancellationTokenSource()){stopped.Cancel();bool canceled=false;try{provider.ApproveAsync(request,stopped.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){canceled=true;}Check(canceled&&user.Calls==userBefore,"canceled approval cannot show or grant a request");}
            Console.WriteLine("Workspace permission tests: "+checks+" passed");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    }
}
