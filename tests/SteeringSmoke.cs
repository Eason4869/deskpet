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
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class SteeringSmoke
    {
        static int passed;
        [STAThread] public static int Main(string[] args)
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
            try
            {
                string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"steering-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
                string configured=Environment.GetEnvironmentVariable("DESKPET_STEERING_SETTINGS");bool real=args.Contains("--real");
                if(real && String.IsNullOrEmpty(configured))throw new InvalidOperationException("--real requires DESKPET_STEERING_SETTINGS to point to an isolated model configuration.");
                var cfg=new PetSettings{AgentEnabled=false,AutoUpdate=false,ReadReplies=false,SaveChatHistory=false,FollowCursor=false,Wander=false,HideOnFullscreen=false,GreetingEnabled=false,SystemPrompt="你正在执行桌宠交互验收。请严格遵循最新用户消息，回答简短。"};
                foreach(string action in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[action]="";
                cfg.Text=real?new SettingsStore(configured).Load().Text.Copy():new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="steering-fixture"};cfg.Text.StreamReplies=true;
                Console.WriteLine(real?"INFO isolated test uses configured text model; no personal history or audio.":"INFO deterministic streaming transport checks button behavior and request payloads.");
                var api=new Recorder(real);var session=new ConversationSession(api,new SilentSpeech(),()=>null);
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(folder,"settings.json")),null);session.SelectPersona(pet.Skin.Id);typeof(PetWindow).GetField("conversation",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,session);
                pet.Show();var fixtureChat=new ChatWindow(pet);typeof(PetWindow).GetField("chat",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(pet,fixtureChat);fixtureChat.Show();var sharedQuick=new QuickChatWindow(pet);typeof(PetWindow).GetField("quick",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(pet,sharedQuick);sharedQuick.Closed+=delegate{typeof(PetWindow).GetField("quick",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(pet,null);};sharedQuick.Show();Pump();
                foreach(Window window in new Window[]{app.Windows.OfType<ChatWindow>().Single(),app.Windows.OfType<QuickChatWindow>().Single()})
                {
                    window.Show();window.UpdateLayout();Pump();bool quick=window is QuickChatWindow;string prefix=quick?"quick":"full";var input=Find<MarkdownEditor>(window,quick?"QuickMessageInput":"MessageInput");var send=Find<Button>(window,quick?"SendQuickMessage":"SendMessage");int baseline=api.Requests.Count;
                    api.PrepareOriginal();input.Text="请逐行写出从 1 到 200 的数字，每行一个，不要省略。";Click(send);
                    Until(()=>session.History.Messages.Any(m=>m.Role=="assistant" && m.Status=="pending" && m.Content.Length>0),90);
                    Check(session.IsBusy,prefix+": original reply is still streaming before guide click");
                    input.Text="只回复 QUEUE_OK。";Click(send);input.Text="停止列举数字，只回复 STEER_OK。";Click(send);Pump();
                    Check(pet.InputQueue.Items.Count==2,prefix+": both pending messages are visible before steering");
                    var buttons=All(window).OfType<Button>().Where(b=>b.Name=="GuideQueuedMessage").ToArray();Check(buttons.Length==2,prefix+": actual queue rows expose guide controls");Click(buttons[1]);
                    Pump();Check(session.IsBusy && !api.OriginalToken.IsCancellationRequested && api.Requests.Count==baseline+1,prefix+": guide preserves the active reply and does not start a concurrent model request");
                    Check(pet.InputQueue.Items.Count==2 && pet.InputQueue.Items[0].IsGuidance && pet.InputQueue.Items[0].Text.Contains("STEER_OK"),prefix+": guide is retained ahead of ordinary queued messages until the safe boundary");
                    Check(session.History.Messages.Count(m=>m.Role=="user" && m.Content.Contains("STEER_OK"))==1,prefix+": guide appears in history immediately");
                    Check(All(window).OfType<Button>().Count(b=>b.Name=="GuideQueuedMessage")==1,prefix+": submitted guidance leaves the ordinary queue UI");
                    api.ReleaseOriginal();
                    Until(()=>!pet.InputQueue.IsRunning && !session.IsBusy,90);Pump();
                    Check(pet.InputQueue.Items.Count==0 && !pet.InputQueue.IsPaused,prefix+": guide completes and original queue resumes");
                    var requests=api.Requests.Skip(baseline).ToArray();Check(requests.Length==3 && requests[1].Last().Content.Contains("STEER_OK") && requests[2].Last().Content.Contains("QUEUE_OK"),prefix+": request order is original, steered, then previously queued");
                    Check(session.History.Messages.Count(m=>m.Role=="user" && m.Content.Contains("STEER_OK"))==1,prefix+": delivered guidance does not duplicate its receipt");
                    var replies=session.History.Messages.Where(m=>m.Role=="assistant").ToArray();
                    Check(replies.Length==3 && replies[0].Status=="complete" && replies[0].Content.Length>0 && replies[1].Status=="complete" && replies[1].Content.Contains("STEER_OK") && replies[2].Status=="complete" && replies[2].Content.Contains("QUEUE_OK"),prefix+": original reply completes naturally and both queued instructions complete");
                    Check(requests[1].Any(m=>m.Role=="user" && m.Content.Contains("200")) && requests[1].Any(m=>m.Role=="assistant" && m.Content==replies[0].Content) && !requests[1].Any(m=>m.Content.Contains("未完成回复")),prefix+": guidance receives the completed original exchange without an interruption marker");
                    Check(!api.OriginalToken.IsCancellationRequested && api.Maximum==1,prefix+": requests remain sequential without cancelling the original reply");session.Clear();
                }
                Console.WriteLine("Steering smoke: "+passed+" passed; transport="+(real?"configured model":"fixture"));return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL steering: "+e.GetType().Name+": "+e.Message);return 1;}
            finally{if(pet!=null)pet.Close();Pump();app.Shutdown();}
        }
        sealed class Recorder:IConversationApi,IStreamingConversationApi
        {
            readonly ApiClient real;int active;TaskCompletionSource<bool> releaseOriginal;public CancellationToken OriginalToken;public int Maximum;public readonly List<IList<ChatMessage>> Requests=new List<IList<ChatMessage>>();
            public Recorder(bool external){if(external)real=new ApiClient(TimeSpan.FromSeconds(90));}
            public void PrepareOriginal(){releaseOriginal=new TaskCompletionSource<bool>();OriginalToken=CancellationToken.None;}
            public void ReleaseOriginal(){releaseOriginal.TrySetResult(true);}
            public async Task<string> ChatStreamAsync(ConnectionSettings cfg,IList<ChatMessage> messages,Action<string> delta,CancellationToken token)
            {
                int count=Interlocked.Increment(ref active);Maximum=Math.Max(Maximum,count);Requests.Add(messages.Select(m=>m.Copy()).ToList());
                try
                {
                    string latest=messages.Last().Content;bool original=!latest.Contains("STEER_OK") && !latest.Contains("QUEUE_OK");
                    if(original)OriginalToken=token;
                    string reply;
                    if(real!=null)reply=await real.ChatStreamAsync(cfg,messages,delta,token);
                    else if(latest.Contains("STEER_OK")){delta("STEER_OK");reply="STEER_OK";}
                    else if(latest.Contains("QUEUE_OK")){delta("QUEUE_OK");reply="QUEUE_OK";}
                    else{delta("1\n2\n3\n");reply="1\n2\n3\nORIGINAL_DONE";}
                    if(original)using(token.Register(()=>releaseOriginal.TrySetCanceled())){await releaseOriginal.Task;token.ThrowIfCancellationRequested();}
                    return reply;
                }
                finally{Interlocked.Decrement(ref active);}
            }
            public Task<string> ChatAsync(ConnectionSettings c,IList<ChatMessage> m,CancellationToken t){return ChatStreamAsync(c,m,_=>{},t);}public Task<string> TranscribeAsync(ConnectionSettings c,byte[] b,CancellationToken t){throw new NotSupportedException();}public Task<byte[]> SpeechAsync(ConnectionSettings c,string v,string s,CancellationToken t){throw new NotSupportedException();}public void Dispose(){if(real!=null)real.Dispose();}
        }
        sealed class SilentSpeech:IReplySpeech{public Task SpeakAsync(string s,PetSettings p,CancellationToken t){return Task.FromResult(0);}public void Stop(){}public void SetVolume(int v){}public void Dispose(){}}
        static void Check(bool condition,string text){if(!condition)throw new Exception(text);passed++;Console.WriteLine("PASS "+text);}
        static void Click(Button button){if(button==null || !button.IsEnabled)throw new Exception("Button unavailable");button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        static T Find<T>(DependencyObject root,string name)where T:FrameworkElement{return All(root).OfType<T>().FirstOrDefault(e=>e.Name==name);}
        static IEnumerable<DependencyObject> All(DependencyObject root){yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All(VisualTreeHelper.GetChild(root,i)))yield return child;}
        static void Until(Func<bool> ready,int seconds){var end=DateTime.UtcNow.AddSeconds(seconds);while(!ready() && DateTime.UtcNow<end)Pump();if(!ready())throw new Exception("Timed out waiting for streaming or queue completion");}
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
    }
}
