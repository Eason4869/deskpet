using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class ConversationRepeatSmoke
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        [STAThread] public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;ChatWindow chat=null;string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"repeat-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                var cfg=new PetSettings{AgentEnabled=false,SaveChatHistory=false,GreetingEnabled=false,ReadReplies=false,AutoUpdate=false};foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";cfg.Text.BaseUrl="http://127.0.0.1:1";cfg.Text.Model="fixture";
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"settings.json")),null);var api=new PendingApi();
                var project=new ProjectDefinition{Id="p",Name="p",Workspace=root};var firstDef=new ConversationDefinition{Id="a",Name="A"};var secondDef=new ConversationDefinition{Id="b",Name="B"};
                var first=ConversationWorkspace.Create(project,firstDef,root,"pet",()=>api,()=>new Silent(),()=>null,()=>cfg,()=>true);
                var second=ConversationWorkspace.Create(project,secondDef,root,"pet",()=>api,()=>new Silent(),()=>null,()=>cfg,()=>true);
                chat=new ChatWindow(pet,first);chat.Show();Pump();
                Send(chat,"第一轮");Wait(()=>api.Requests.Count==1);api.Complete(0);Wait(()=>!first.Session.IsBusy && !first.Queue.IsRunning);
                Check(first.Session.History.Messages.Count(m=>m.Role=="user")==1,"first completed conversation remains sendable state");
                chat.SwitchWorkspace(second);Pump();Send(chat,"第二轮");Wait(()=>api.Requests.Count==2);api.Complete(1);Wait(()=>!second.Session.IsBusy && !second.Queue.IsRunning);
                chat.SwitchWorkspace(first);Pump();Send(chat,"第三轮");Wait(()=>api.Requests.Count==3);api.Complete(2);Wait(()=>!first.Session.IsBusy && !first.Queue.IsRunning);
                Check(first.Session.History.Messages.Count(m=>m.Role=="user")==2 && api.Requests.Count==3,"a completed conversation can be sent again after switching projects and sessions");
                Check(second.Session.History.Messages.Count(m=>m.Role=="user")==1,"other session history remains isolated");
                Console.WriteLine("Conversation repeat smoke: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}finally{if(chat!=null)chat.Close();if(pet!=null)pet.Close();}
        }
        static void Send(ChatWindow chat,string text){var input=All<TextBox>(chat).Single(t=>t.Name=="MessageInput");input.Text=text;var send=All<Button>(chat).Single(b=>b.Name=="SendMessage");if(!send.IsEnabled)throw new Exception("send disabled");send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();}
        static void Wait(Func<bool> ready){var end=DateTime.UtcNow.AddSeconds(5);while(!ready()&&DateTime.UtcNow<end)Pump();if(!ready())throw new Exception("Timed out waiting for conversation state");}
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(40)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in All<T>(VisualTreeHelper.GetChild(root,i)))yield return item;}
        sealed class PendingApi:IConversationApi
        {
            public readonly List<TaskCompletionSource<string>> Requests=new List<TaskCompletionSource<string>>();
            public Task<string> ChatAsync(ConnectionSettings s,IList<ChatMessage> m,CancellationToken t){var request=new TaskCompletionSource<string>();Requests.Add(request);t.Register(()=>request.TrySetCanceled());return request.Task;}
            public void Complete(int index){Requests[index].TrySetResult("done");}public Task<string> TranscribeAsync(ConnectionSettings s,byte[] w,CancellationToken t){return Task.FromResult("");}public Task<byte[]> SpeechAsync(ConnectionSettings s,string v,string x,CancellationToken t){return Task.FromResult(new byte[0]);}public void Dispose(){}
        }
        sealed class Silent:IReplySpeech{public Task SpeakAsync(string s,PetSettings p,CancellationToken t){return Task.FromResult(0);}public void SetVolume(int n){}public void Stop(){}public void Dispose(){}}
    }
}
