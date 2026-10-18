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
            public Task<string> ChatAgentAsync(ConnectionSettings s,IList<ChatMessage> m,AgentWorkspace w,Action<string> delta,Action<string> reasoning,Action<AgentToolEvent> tool,bool stream,CancellationToken cancel){AgentCalls++;throw new Exception("side chat must never use agent tools");}
            public Task<string> TranscribeAsync(ConnectionSettings s,byte[] w,CancellationToken c){throw new Exception("unexpected microphone request");}
            public Task<byte[]> SpeechAsync(ConnectionSettings s,string voice,string text,CancellationToken c){throw new Exception("unexpected speech request");}
            public void Dispose(){Disposed=true;}
        }
        static Type PaneType;
        static object Create(PetWindow pet,Window host,PetSettings settings,string project,FakeApi api)
        {return Activator.CreateInstance(PaneType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{pet,host,settings,project,api},null);}
        static ConversationSession Session(object pane){return (ConversationSession)PaneType.GetProperty("Session",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pane,null);}
        static Task Send(object pane,string text){return (Task)PaneType.GetMethod("SendAsync",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pane,new object[]{text});}
        static IEnumerable<T> All<T>(DependencyObject parent)where T:DependencyObject
        {if(parent is T)yield return (T)parent;for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)foreach(var item in All<T>(VisualTreeHelper.GetChild(parent,i)))yield return item;}
        static string DiskSnapshot(string root){return String.Join("|",Directory.GetFiles(root,"*",SearchOption.AllDirectories).OrderBy(p=>p).Select(p=>p+":"+Convert.ToBase64String(File.ReadAllBytes(p))));}
        static async Task Run()
        {
            PaneType=typeof(PetWindow).Assembly.GetType("Dafeiyu.SideChatPane");Check(PaneType!=null,"temporary project chat pane exists");
            string root=Path.Combine(Directory.GetCurrentDirectory(),"work","permissions","sidechat-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            var cfg=new PetSettings{AutoUpdate=false,GreetingEnabled=false,ShowPredictedMessages=false,SaveChatHistory=false,ReadReplies=false,OnboardingState="completed",AgentEnabled=true,AgentWorkspace=root,AgentSystemPrompt="项目工作提示词 {pet_name}"};
            foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";cfg.Text.BaseUrl="http://127.0.0.1:1";cfg.Text.Model="fixture-original";cfg.Text.ToolSupport=CapabilitySupport.Supported;
            var pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"settings.json")),null);var host=new Window();object first=null,second=null,blocked=null;
            try
            {
                pet.Conversation.AddLiveTurn("","main-private-input","main-private-reply");await pet.Conversation.FlushHistoryAsync();string main=pet.Conversation.ExportHistory();int workspaceCount=pet.Workspaces.OpenCount;string disk=DiskSnapshot(root);
                var apiA=new FakeApi();var apiB=new FakeApi();first=Create(pet,host,cfg,"测试项目",apiA);second=Create(pet,host,cfg,"另一个项目",apiB);
                Check(!Object.ReferenceEquals(Session(first),Session(second))&&!Object.ReferenceEquals(Session(first),pet.Conversation),"panes own sessions separate from each other and main chat");
                cfg.AgentWorkspace=Path.Combine(root,"changed-after-open");cfg.AgentSystemPrompt="changed-prompt";cfg.Text.Model="changed-model";cfg.ReadReplies=true;cfg.AgentEnabled=true;
                await Send(first,"side-alpha");await Send(second,"side-beta");await Send(first,"side-followup");await Task.Delay(100);
                Check(apiA.AgentCalls==0&&apiB.AgentCalls==0,"even an agent-capable transport receives only ordinary chat requests");
                Check(apiA.Requests[0].Count==2&&!apiA.Requests[0].Any(m=>m.Content.Contains("main-private")),"first request contains no main conversation history");
                Check(apiA.Requests[1].Any(m=>m.Content=="side-alpha")&&!apiA.Requests[1].Any(m=>m.Content.Contains("side-beta"))&&apiB.Requests[0].Count==2,"each pane retains only its own conversation context");
                string prompt=apiA.Requests[0][0].Content;Check(prompt.Contains("项目工作提示词")&&prompt.Contains("测试项目")&&prompt.Contains(root)&&!prompt.Contains("changed-prompt")&&!prompt.Contains("changed-after-open"),"project prompt and directory are frozen when the pane opens");
                Check(apiA.Models.All(m=>m.Model=="fixture-original"),"model connection is copied rather than following global edits");
                Check(prompt.Contains("不")&&prompt.Contains("文件")&&prompt.Contains("工具"),"system context states that this chat cannot automatically inspect project files");
                Check(pet.Conversation.ExportHistory()==main&&pet.Workspaces.OpenCount==workspaceCount,"temporary messages never change main history or register a workspace");
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
    }
}
