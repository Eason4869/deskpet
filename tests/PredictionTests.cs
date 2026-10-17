using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class PredictionTests
    {
        static int checks;
        static void Check(bool value,string reason){if(!value)throw new Exception(reason);checks++;Console.WriteLine("PASS "+reason);}
        [STAThread] public static int Main(string[] args){try{Run().GetAwaiter().GetResult();if(args.Contains("--ui"))RunUi();Console.WriteLine("Prediction tests: "+checks+" passed");return 0;}catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}}
        static Task Request(ConversationSession session,PetSettings settings){return session.RequestPredictionsAsync(settings);}
        static IList<string> Suggestions(ConversationSession session){return session.Predictions;}
        static void Cancel(ConversationSession session){session.CancelPredictions();}
        static void Block(ConversationSession session,object owner,bool value){session.UpdatePredictionInput(owner,value);}
        static PetSettings Settings(bool enabled)
        {
            var settings=new PetSettings{ReadReplies=false,Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="test-model"}};
            settings.ShowPredictedMessages=enabled;return settings;
        }
        static ConversationSession Session(HeldApi api){var session=new ConversationSession(api,new SilentSpeech(),delegate{return null;});session.History.Commit("我想做一个桌面工具。","你希望先确定用途还是界面？");return session;}
        static async Task Run()
        {
            var api=new HeldApi();using(var session=Session(api)){
                await Request(session,Settings(false));Check(api.Calls==0 && Suggestions(session).Count==0,"disabled suggestions never issue a model request");
            }
            api=new HeldApi();using(var session=Session(api)){
                session.History.Attach(session.History.Messages.First().Id,new[]{ChatAttachment.FromText("private.txt","ATTACHMENT-CONTENT-MUST-NOT-BE-RESENT")});
                var settings=Settings(true);int history=session.History.Messages.Count;var first=Request(session,settings);var second=Request(session,settings);
                Check(api.Calls==1 && !session.IsBusy,"two windows share one prediction without occupying conversation work");
                api.Complete("[\"先聊聊用途\",\"给我界面方案\",\"先聊聊用途\",\"最后一个\",\"第五个\"]");await Task.WhenAll(first,second);
                Check(Suggestions(session).SequenceEqual(new[]{"先聊聊用途","给我界面方案","最后一个"}),"only three distinct model suggestions are published");
                await Request(session,settings);Check(api.Calls==1 && session.History.Messages.Count==history,"completed request is cached and does not create a conversation turn");
                Check(api.Messages.Count<=13 && api.Messages.All(m=>(m.Attachments==null || m.Attachments.Count==0) && !m.Content.Contains("ATTACHMENT-CONTENT-MUST-NOT-BE-RESENT")),"prediction context is bounded text without attachment content or metadata");
                var dismiss=typeof(ConversationSession).GetMethod("DismissPredictions",BindingFlags.Instance|BindingFlags.NonPublic);
                Check(dismiss!=null,"prediction dismissal is available without changing the conversation");dismiss.Invoke(session,new object[]{settings});await Request(session,settings);
                Check(api.Calls==1 && Suggestions(session).Count==0 && session.History.Messages.Count==history,"Escape dismissal suppresses cached predictions for this completed reply");
            }
            foreach(string change in new[]{"persona","model","credential","provider","draft","cancel","disable","latest reply","dispose"}){
                api=new HeldApi();using(var session=Session(api)){
                    var settings=Settings(true);var pending=Request(session,settings);
                    if(change=="persona")session.SelectPersona("another");else if(change=="model")settings.Text.Model="changed";else if(change=="credential")settings.Text.EncryptedKey="changed";else if(change=="provider")settings.ActiveTextProviderId="changed";else if(change=="draft")Block(session,new object(),true);else if(change=="disable")settings.ShowPredictedMessages=false;else if(change=="latest reply")session.History.Commit("another user message","another answer");else if(change=="dispose")session.Dispose();else Cancel(session);
                    api.Complete("[\"旧建议不能出现\"]");await pending;
                    Check(Suggestions(session).Count==0,"late result is discarded after "+change);
                    if(change=="draft" || change=="cancel" || change=="persona" || change=="dispose")Check(api.Cancel.IsCancellationRequested,"prediction token cancelled after "+change);
                }
            }
            foreach(string response in new[]{"not JSON", "try [\"invented\"] please", "{\"suggestions\":[\"wrong shape\"]}", "[42,true,null]", "[\"\",\""+new string('长',121)+"\"]"}){
                api=new HeldApi();using(var session=Session(api)){var request=Request(session,Settings(true));api.Complete(response);await request;Check(Suggestions(session).Count==0,"malformed or invalid output produces no invented fallback");}
            }
            api=new HeldApi();using(var session=Session(api)){
                var blocker=new object();Block(session,blocker,true);await Request(session,Settings(true));Check(api.Calls==0,"shared draft or pending queue prevents a prediction request");
                Block(session,blocker,false);var request=Request(session,Settings(true));api.Fail(new ApiException("service unavailable"));await request;
                Check(Suggestions(session).Count==0 && session.LastNotice=="","prediction failure stays silent and leaves chat notice alone");
            }
            api=new HeldApi();using(var session=Session(api)){
                session.History.Commit(new string('用',8000),new string('答',50000));var request=Request(session,Settings(true));
                Check(api.Messages.Sum(m=>m.Content.Length)<18000,"large conversations cannot create an unbounded prediction prompt");api.Complete("[]");await request;
            }
            api=new HeldApi();using(var session=Session(api)){
                var settings=Settings(true);var prediction=Request(session,settings);var predictionToken=api.Cancel;
                var work=session.SendTextAsync("开始真正的工作",settings,"伙伴");var workToken=api.Cancel;
                Check(predictionToken.IsCancellationRequested && !workToken.IsCancellationRequested && session.IsBusy,"new conversation work cancels only the optional prediction");
                await Request(session,settings);Check(api.Calls==2,"active conversation work prevents extra suggestion requests");
                api.Complete("真实的工作回复");await Task.WhenAll(prediction,work);
                Check(session.History.Messages.Last().Content=="真实的工作回复" && Suggestions(session).Count==0,"actual chat completes normally while obsolete predictions stay hidden");
            }
            api=new HeldApi();using(var session=Session(api)){
                ChatMessage user;session.History.BeginTurn("未完成的问题","text",out user);await Request(session,Settings(true));
                Check(api.Calls==0,"an incomplete latest assistant turn cannot generate suggestions");
            }
        }
        static void RunUi()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;Window first=null,second=null;
            try
            {
                var settings=Settings(false);settings.SaveChatHistory=false;settings.GreetingEnabled=false;settings.AgentEnabled=false;foreach(string action in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[action]="";
                string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"prediction-ui-"+Guid.NewGuid().ToString("N"));
                pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(directory,"settings.json")),null);
                var api=new HeldApi();var session=Session(api);session.SelectPersona(pet.Skin.Id);session.History.Commit("我想做一个工具。","先从需求开始吧。");
                typeof(PetWindow).GetField("conversation",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,session);
                first=new Window{Width=380,Height=380,Title="Prediction full composer"};second=new Window{Width=380,Height=380,Title="Prediction quick composer"};
                var firstInput=Ui.Field("",true);firstInput.Name="FirstInput";var firstComposer=new ConversationComposer(pet,first,firstInput,UiIcons.Button("mic","听写"),Ui.Text("",11,Ui.Muted),false);first.Content=firstComposer;
                var secondInput=Ui.Field("",true);secondInput.Name="SecondInput";var secondComposer=new ConversationComposer(pet,second,secondInput,UiIcons.Button("mic","听写"),Ui.Text("",11,Ui.Muted),true,true);second.Content=secondComposer;
                first.Show();second.Show();Pump(650);
                Check(All<Button>(first).Any(b=>b.Name=="ConversationModePicker") && !All<Button>(second).Any(b=>b.Name=="ConversationModePicker"),"only the full composer keeps the mode switch");
                Check(api.Calls==0 && GhostText(firstInput)=="和伙伴一起工作" && GhostText(secondInput)=="和伙伴一起工作","default-off prediction issues no request and keeps the plain placeholder");
                settings.ShowPredictedMessages=true;firstComposer.RefreshModels();secondComposer.RefreshModels();Pump(700);
                Check(api.Calls==1,"two real composers debounce into one shared provider call");
                firstInput.Text="已有草稿";api.Complete("[\"不会覆盖草稿\"]");Pump(150);
                Check(session.Predictions.Count==0 && GhostText(firstInput)=="和伙伴一起工作" && GhostText(secondInput)=="和伙伴一起工作","draft invalidates the pending response in both windows");
                api.Reset();firstInput.Clear();Pump(700);
                Check(api.Calls==2,"cleared draft permits a fresh shared request");api.Complete("[\"请给我一个具体的方案\",\"先比较两种做法\"]");Pump(150);
                string ghost="请给我一个具体的方案";
                Check(session.Predictions.Count==2,"valid model output publishes shared suggestions in order");
                Check(GhostText(firstInput)==ghost && GhostVisible(firstInput),"empty full composer shows the first suggestion as ghost text inside the input");
                Check(GhostText(secondInput)==ghost && GhostVisible(secondInput),"the conversation-only quick composer shows the same ghost");
                int calls=api.Calls,history=session.History.Messages.Count;
                var confirm=Key(firstInput,System.Windows.Input.Key.Enter,System.Windows.Input.Keyboard.PreviewKeyDownEvent);Pump(80);
                Check(confirm.Handled && firstInput.Text==ghost && api.Calls==calls && session.History.Messages.Count==history && pet.InputQueue.Items.Count==0,"first Enter only confirms the ghost into the draft");
                Check(GhostText(secondInput)=="和伙伴一起工作","confirming one draft collapses the shared ghost");
                var repeat=Key(firstInput,System.Windows.Input.Key.Enter,System.Windows.Input.Keyboard.PreviewKeyDownEvent);Pump(80);
                Check(repeat.Handled && api.Calls==calls && pet.InputQueue.Items.Count==0 && session.History.Messages.Count==history,"held Enter repeats cannot send the confirmed draft");
                Key(firstInput,System.Windows.Input.Key.Enter,System.Windows.Input.Keyboard.PreviewKeyUpEvent);Pump(40);api.Reset();
                var send=Key(firstInput,System.Windows.Input.Key.Enter,System.Windows.Input.Keyboard.PreviewKeyDownEvent);Pump(250);
                Check(send.Handled && api.Calls==calls+1 && session.History.Messages.Any(m=>m.Role=="user" && m.Content==ghost) && firstInput.Text.Length==0,"the next Enter sends the confirmed draft through the shared queue");
                api.Complete("收到。");Pump(250);Check(!session.IsBusy,"sent draft completes as a normal conversation turn");
                api.Reset();Pump(700);api.Complete("[\"总结一下当前进度\"]");Pump(150);
                Check(GhostText(firstInput)=="总结一下当前进度" && GhostVisible(firstInput),"a new completed reply generates a fresh ghost");
                int afterGhost=api.Calls;
                var escape=Key(firstInput,System.Windows.Input.Key.Escape,System.Windows.Input.Keyboard.PreviewKeyDownEvent);Pump(80);
                Check(escape.Handled && session.Predictions.Count==0 && GhostText(firstInput)=="和伙伴一起工作" && GhostText(secondInput)=="和伙伴一起工作","Escape dismisses the shared ghost in both composers");
                firstComposer.RefreshModels();secondComposer.RefreshModels();Pump(700);
                Check(api.Calls==afterGhost && session.Predictions.Count==0,"the dismissed suggestion is not regenerated for the same reply");
                first.Close();second.Close();first=second=null;session.Dispose();
            }
            finally{if(first!=null)first.Close();if(second!=null)second.Close();if(pet!=null)pet.Close();app.Shutdown();}
        }
        static string GhostText(TextBox input){var placeholder=ChatUi.GetComposerPlaceholder(input);return placeholder==null?"":placeholder.Text;}
        static bool GhostVisible(TextBox input){var placeholder=ChatUi.GetComposerPlaceholder(input);return placeholder!=null && placeholder.IsVisible && placeholder.Text!="和伙伴一起工作";}
        static System.Windows.Input.KeyEventArgs Key(TextBox input,System.Windows.Input.Key key,System.Windows.RoutedEvent routed)
        {var args=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(input),Environment.TickCount,key){RoutedEvent=routed};input.RaiseEvent(args);return args;}
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in All<T>(System.Windows.Media.VisualTreeHelper.GetChild(root,i)))yield return item;}        static void Pump(int milliseconds)
        {
            var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(milliseconds)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
        }
        sealed class HeldApi:IConversationApi
        {
            TaskCompletionSource<string> response=new TaskCompletionSource<string>();public int Calls;public CancellationToken Cancel;public IList<ChatMessage> Messages;
            public Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,CancellationToken cancel){Calls++;Cancel=cancel;Messages=messages;return response.Task;}
            public void Complete(string value){response.TrySetResult(value);}public void Fail(Exception error){response.TrySetException(error);}
            public void Reset(){response=new TaskCompletionSource<string>();}
            public Task<string> TranscribeAsync(ConnectionSettings settings,byte[] wav,CancellationToken cancel){throw new NotSupportedException();}
            public Task<byte[]> SpeechAsync(ConnectionSettings settings,string voice,string text,CancellationToken cancel){throw new NotSupportedException();}
            public void Dispose(){}
        }
        sealed class SilentSpeech:IReplySpeech
        {
            public Task SpeakAsync(string text,PetSettings settings,CancellationToken cancel){return Task.FromResult(0);}public void SetVolume(int value){}public void Stop(){}public void Dispose(){}
        }
    }
}
