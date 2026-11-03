using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dafeiyu.Tests
{
    public static class SideChatTests
    {
        static int checks;
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        sealed class FakeApi:IConversationApi,IAgentConversationApi
        {
            internal readonly List<IList<ChatMessage>> Requests=new List<IList<ChatMessage>>();
            internal readonly List<ConnectionSettings> Models=new List<ConnectionSettings>();
            internal bool Block,Cancelled,Disposed;internal int AgentCalls;
            public async Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,CancellationToken cancel)
            {
                Models.Add(settings.Copy());Requests.Add(messages.Select(m=>m.Copy()).ToArray());
                if(Block)try{await Task.Delay(Timeout.Infinite,cancel).ConfigureAwait(false);}catch(OperationCanceledException){Cancelled=true;throw;}
                return "**临时回复**："+messages.Last().Content;
            }
            public async Task<string> ChatAgentAsync(ConnectionSettings s,IList<ChatMessage> m,AgentWorkspace w,Action<string> delta,Action<string> reasoning,Action<AgentToolEvent> tool,bool stream,CancellationToken cancel){AgentCalls++;Check(w.ReadOnly,"side chat project tools use a read-only workspace");string read=await w.ExecuteAsync("read_file","{\"path\":\"project-note.txt\"}",cancel);Check(read.Contains("PROJECT_FILE_CONTENT"),"side chat actually reads its bound project file");string write=await w.ExecuteAsync("write_file","{\"path\":\"should-not-exist.txt\",\"content\":\"bad\"}",cancel);Check(TextResources.IsToolError(write)&&!File.Exists(Path.Combine(w.Root,"should-not-exist.txt")),"side chat cannot write project files");return await ChatAsync(s,m,cancel);}
            public Task<string> TranscribeAsync(ConnectionSettings s,byte[] w,CancellationToken c){throw new Exception("unexpected microphone request");}
            public Task<byte[]> SpeechAsync(ConnectionSettings s,string voice,string text,CancellationToken c){throw new Exception("unexpected speech request");}
            public void Dispose(){Disposed=true;}
        }
        static Type PaneType;
        static object Create(PetWindow pet,Window host,PetSettings settings,string project,FakeApi api)
        {return new SideChatPane(pet,host,settings,project,api);}
        static ConversationSession Session(object pane){return (ConversationSession)PaneType.GetProperty("Session",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pane,null);}
        static Task Send(object pane,string text){return (Task)PaneType.GetMethod("SendAsync",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pane,new object[]{text});}
        static IEnumerable<T> All<T>(DependencyObject parent)where T:DependencyObject
        {if(parent is T)yield return (T)parent;for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)foreach(var item in All<T>(VisualTreeHelper.GetChild(parent,i)))yield return item;}
        static string DiskSnapshot(string root)
        {
            // Agent audit bookkeeping is distinct from user project content.
            return String.Join("|",Directory.GetFiles(root,"*",SearchOption.AllDirectories).Where(p=>!p.StartsWith(Path.Combine(root,".deskpet-agent-kernel")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)).OrderBy(p=>p).Select(p=>p+":"+Convert.ToBase64String(File.ReadAllBytes(p))));
        }
        static async Task Run()
        {
            PaneType=typeof(PetWindow).Assembly.GetType("Dafeiyu.SideChatPane");Check(PaneType!=null,"temporary project chat pane exists");
            string root=Path.Combine(Directory.GetCurrentDirectory(),"work","permissions","sidechat-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var cfg=new PetSettings{AutoUpdate=false,GreetingEnabled=false,ShowPredictedMessages=false,SaveChatHistory=false,ReadReplies=false,OnboardingState="completed",AgentEnabled=true,AgentWorkspace=root,AgentSystemPrompt="项目工作提示词 {pet_name}"};
            foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";cfg.Text.BaseUrl="http://127.0.0.1:1";cfg.Text.Model="fixture-original";cfg.Text.ToolSupport=CapabilitySupport.Supported;
            var pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"settings.json")),null);var host=new Window();object first=null,second=null,blocked=null;
            try
            {
                Check(await pet.AgentJobs.InitializeAsync(),"snapshot baseline starts after the application task-store lease is ready");
                pet.Conversation.AddLiveTurn(pet.Conversation.PersonaId,"main-private-input","main-private-reply");await pet.Conversation.FlushHistoryAsync();string main=pet.Conversation.ExportHistory();int workspaceCount=pet.Workspaces.OpenCount;string disk=DiskSnapshot(root);
                File.WriteAllText(Path.Combine(root,"project-note.txt"),"PROJECT_FILE_CONTENT");disk=DiskSnapshot(root);
                var apiA=new FakeApi();var apiB=new FakeApi();first=Create(pet,host,cfg,"测试项目",apiA);second=Create(pet,host,cfg,"另一个项目",apiB);
                Check(!Object.ReferenceEquals(Session(first),Session(second))&&!Object.ReferenceEquals(Session(first),pet.Conversation),"panes own sessions separate from each other and main chat");
                cfg.AgentWorkspace=Path.Combine(root,"changed-after-open");cfg.AgentSystemPrompt="changed-prompt";cfg.Text.Model="changed-model";cfg.ReadReplies=true;cfg.AgentEnabled=true;
                await Send(first,"side-alpha");
                pet.Conversation.AddLiveTurn(pet.Conversation.PersonaId,"latest-main-input","latest-main-reply");var latest=pet.Conversation.History.Messages.Last();pet.Conversation.History.Update(latest.Id,latest.Content,"complete",reasoning:"PRIVATE_REASONING");main=pet.Conversation.ExportHistory();
                await Send(second,"side-beta");await Send(first,"side-followup");await Task.Delay(100);
                Check(apiA.AgentCalls==2&&apiB.AgentCalls==1,"project-bound side chat uses tools to read its project");
                Check(apiA.Requests[0].Any(m=>m.Content=="main-private-input")&&apiA.Requests[0].Any(m=>m.Content=="main-private-reply"),"first request inherits the owning main conversation history");
                Check(!apiA.Requests[0].Any(m=>m.Content=="latest-main-input")&&apiA.Requests[1].Any(m=>m.Content=="latest-main-input")&&apiA.Requests[1].All(m=>String.IsNullOrEmpty(m.Reasoning)&&!m.Content.Contains("PRIVATE_REASONING")),"each send refreshes parent context without copying reasoning");
                Check(apiA.Requests[1].Any(m=>m.Content=="side-alpha")&&!apiA.Requests[1].Any(m=>m.Content.Contains("side-beta"))&&!apiB.Requests[0].Any(m=>m.Content=="side-alpha"),"side panes share parent context without sharing each other's messages");
                string prompt=apiA.Requests[0][0].Content;Check(prompt.Contains("项目工作提示词")&&prompt.Contains("测试项目")&&prompt.Contains(root)&&!prompt.Contains("changed-prompt")&&!prompt.Contains("changed-after-open"),"project prompt and directory are frozen when the pane opens");
                Check(apiA.Models.All(m=>m.Model=="fixture-original"),"model connection is copied rather than following global edits");
                Check(prompt.Contains("read-only")&&prompt.Contains("project"),"system context describes read-only project access");
                Check(pet.Conversation.ExportHistory()==main&&pet.Workspaces.OpenCount==workspaceCount,"temporary messages never change main history or register a workspace");
                using(var otherParent=new ConversationSession(new FakeApi(),new SilentSpeech(),()=>null)){
                    otherParent.AddLiveTurn(otherParent.PersonaId,"OTHER_PARENT_INPUT","OTHER_PARENT_REPLY");var otherApi=new FakeApi();using(var otherPane=new SideChatPane(pet,host,cfg,"其他会话",otherApi,parent:otherParent,allowProjectRead:false)){
                        await otherPane.SendAsync("other-side");Check(otherApi.AgentCalls==0&&otherApi.Requests[0].Any(m=>m.Content=="OTHER_PARENT_INPUT")&&!otherApi.Requests[0].Any(m=>m.Content.Contains("main-private")),"a pane follows its explicit owning conversation and stays usable without a project");
                    }
                }
                var editorA=All<MarkdownEditor>((DependencyObject)first).Single();var editorB=All<MarkdownEditor>((DependencyObject)second).Single();editorA.Text="first unsent draft";editorB.Text="second unsent draft";
                Check(editorA.Text=="first unsent draft"&&editorB.Text=="second unsent draft","temporary chat drafts are independent");
                var firstView=(FrameworkElement)first;firstView.Measure(new Size(600,800));firstView.Arrange(new Rect(0,0,600,800));firstView.UpdateLayout();
                Check(All<MarkdownView>((DependencyObject)first).Any(v=>v.Text.Contains("临时回复")&&v.HasFormatting),"assistant replies render using the Markdown view");
                var waiting=new FakeApi{Block=true};blocked=Create(pet,host,cfg,"取消项目",waiting);Task pending=Send(blocked,"wait-for-close");Check(Session(blocked).IsBusy,"fake in-flight request is active before close");((IDisposable)blocked).Dispose();bool cancelled=false;try{await pending;}catch(OperationCanceledException){cancelled=true;}
                Check(cancelled&&waiting.Cancelled&&waiting.Disposed&&!Session(blocked).IsBusy,"closing cancels the in-flight request and releases its transport");
                ((IDisposable)first).Dispose();((IDisposable)first).Dispose();((IDisposable)second).Dispose();
                Check(apiA.Disposed&&apiB.Disposed&&DiskSnapshot(root)==disk,"closing is idempotent and creates no history, project or other files");
                Check(pet.Conversation.ExportHistory()==main,"main history remains unchanged after all temporary panes close");
                Console.WriteLine("Side chat tests: "+checks+" passed");
            }
            finally{foreach(var pane in new[]{first,second,blocked})if(pane!=null)((IDisposable)pane).Dispose();host.Close();pet.Close();}
        }
        [STAThread]public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int result=1;
            app.Dispatcher.BeginInvoke(new Action(async delegate{try{await Run();result=0;}catch(Exception error){Console.Error.WriteLine(error);}finally{app.Shutdown();}}));app.Run();return result;
        }
        sealed class SilentSpeech:IReplySpeech{public Task SpeakAsync(string text,PetSettings settings,CancellationToken cancel){return Task.FromResult(0);}public void SetVolume(int volume){}public void Stop(){}public void Dispose(){}}
    }
}
