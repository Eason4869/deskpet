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
 public static class ProactiveConversationUiTests
 {
  static int checks;static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
  [STAThread] public static int Main()
  {
   var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"proactive-reply-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);PetWindow pet=null;
   try{
    var settings=new PetSettings{GreetingEnabled=true,AiGreetingEnabled=false,AutoUpdate=false,FollowCursor=false,Wander=false,SaveChatHistory=false,OnboardingState="completed",AgentEnabled=true,ReadReplies=false,Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="fixture"}};foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
    pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(root,"settings.json")),null);pet.Show();Pump();pet.PlayAction("idle",0);
    typeof(PetWindow).GetMethod("ShowGreeting",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,null);Pump();
    var id=(string)typeof(PetWindow).GetField("lastProactiveEventId",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet);Check(!String.IsNullOrEmpty(id),"shown ordinary greeting has a reply event");
    pet.ShowQuickChat();Pump();var old=app.Windows.OfType<QuickChatWindow>().Single(w=>w.IsVisible);var oldInput=Find<MarkdownEditor>(old,"QuickMessageInput");oldInput.Text="existing draft";
    var oldComposer=(ConversationComposer)typeof(QuickChatWindow).GetField("composer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(old);oldComposer.AddAttachments(new[]{ChatAttachment.FromText("old.txt","old attachment")});
    var oldSession=(ConversationSession)typeof(QuickChatWindow).GetField("session",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(old);var pendingApi=new PendingApi();typeof(ConversationSession).GetField("api",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(oldSession,pendingApi);var pending=oldSession.SendTextAsync("existing task",pet.ConversationSettings(oldSession),pet.Skin.Name);PumpUntil(()=>pendingApi.Calls==1);
    pet.OpenProactiveReply(id);Pump();var windows=app.Windows.OfType<QuickChatWindow>().Where(w=>w.IsVisible).ToArray();Check(windows.Length==2 && oldInput.Text=="existing draft" && oldComposer.SnapshotPendingAttachments().Count==1 && !pending.IsCompleted,"old quick draft, attachment and running task remain available");
    var reply=windows.Single(w=>!ReferenceEquals(w,old));var replySession=(ConversationSession)typeof(QuickChatWindow).GetField("session",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(reply);Check(replySession.History.Messages.Count(m=>m.Source=="proactive")==1,"reply shows original assistant once");
    pet.OpenProactiveReply(id);Pump();Check(app.Windows.OfType<QuickChatWindow>().Count(w=>w.IsVisible)==2,"repeated click reuses reply window");Check(settings.AgentEnabled,"opening reply does not change global work mode");
    var composer=(ConversationComposer)typeof(QuickChatWindow).GetField("composer",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(reply);composer.AddAttachments(new[]{ChatAttachment.FromText("reply.txt","reply attachment")});Find<Button>(reply,"ExpandChat").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
    var full=app.Windows.OfType<ChatWindow>().Single(w=>w.IsVisible);var fullSession=(ConversationSession)typeof(ChatWindow).GetField("session",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(full);Check(ReferenceEquals(replySession,fullSession),"expanded chat keeps the same session");Check(full.SnapshotPendingAttachments(replySession).Count==1,"expansion retains reply attachment");Check(oldInput.Text=="existing draft" && settings.AgentEnabled,"expansion preserves other draft and global mode");
    full.Close();pendingApi.Answer.SetResult("done");PumpUntil(()=>pending.IsCompleted);pending.GetAwaiter().GetResult();old.Close();Pump();Check(!Directory.Exists(Path.Combine(root,"conversations",replySession.KernelSession.SessionId)),"unanswered reply leaves no conversation directory");
    settings.SaveChatHistory=true;pet.OpenProactiveReply(id);Pump();var acceptedWindow=app.Windows.OfType<QuickChatWindow>().Single(w=>w.IsVisible);var acceptedSession=(ConversationSession)typeof(QuickChatWindow).GetField("session",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(acceptedWindow);var acceptedApi=new PendingApi();typeof(ConversationSession).GetField("api",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(acceptedSession,acceptedApi);var input=Find<MarkdownEditor>(acceptedWindow,"QuickMessageInput");input.Text="real reply";var beforeSave=pet.ProjectStore.BeforeSave;pet.ProjectStore.BeforeSave=delegate{throw new IOException("fixture failure");};Find<Button>(acceptedWindow,"SendQuickMessage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Pump();Check(input.Text=="real reply" && acceptedApi.Calls==0 && acceptedSession.History.Messages.Count==1,"failed admission preserves draft and avoids model and history writes");
    pet.ProjectStore.BeforeSave=beforeSave;Find<Button>(acceptedWindow,"SendQuickMessage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));PumpUntil(()=>acceptedApi.Calls==1);Pump();Check(input.Text=="" && acceptedSession.History.Messages.Any(m=>m.Role=="user" && m.Content=="real reply"),"successful admission preserves real user turn");Check(acceptedApi.Messages.Any(m=>m.Role=="assistant" && m.Source=="proactive" && m.TurnId=="proactive-"+id),"model request contains the exact original proactive event");
    var definition=pet.ProjectStore.Load().UnassignedConversations.Single(c=>c.Id==acceptedSession.KernelSession.SessionId);Check(definition.Origin=="proactive" && settings.AgentEnabled,"admission stores proactive owner without switching global mode");acceptedApi.Answer.SetResult("answer");PumpUntil(()=>!acceptedSession.IsBusy);var flush=acceptedSession.FlushHistoryAsync();PumpUntil(()=>flush.IsCompleted);flush.GetAwaiter().GetResult();var saved=new HistoryStore(Path.Combine(root,"conversations",definition.Id,"history")).Load(definition.PetId);Check(saved.Any(m=>m.Source=="proactive" && m.TurnId=="proactive-"+id) && saved.Any(m=>m.Role=="user" && m.Content=="real reply"),"accepted reply persists original event and actual user message; saved="+saved.Count+" warning="+acceptedSession.HistoryWarning);acceptedWindow.Close();Pump();
    Console.WriteLine("ProactiveConversationUiTests: "+checks+" assertions passed");return 0;
   }catch(Exception error){Console.WriteLine("FAIL "+error);return 1;}finally{if(pet!=null){pet.Close();Pump();}app.Shutdown();}
  }
  sealed class PendingApi:IConversationApi
  {
   public int Calls;public IList<ChatMessage> Messages;public TaskCompletionSource<string> Answer=new TaskCompletionSource<string>();
   public Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,CancellationToken cancel){Messages=messages.Select(m=>m.Copy()).ToList();Interlocked.Increment(ref Calls);return Answer.Task;}
   public Task<string> TranscribeAsync(ConnectionSettings settings,byte[] audio,CancellationToken cancel){throw new NotSupportedException();}
   public Task<byte[]> SpeechAsync(ConnectionSettings settings,string voice,string text,CancellationToken cancel){throw new NotSupportedException();}public void Dispose(){}
  }
  static void PumpUntil(Func<bool> done){var limit=DateTime.UtcNow.AddSeconds(10);while(!done() && DateTime.UtcNow<limit)Pump();if(!done())throw new Exception("fixture timed out");}  static T Find<T>(DependencyObject root,string name)where T:FrameworkElement{if(root is T && ((T)root).Name==name)return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var found=Find<T>(VisualTreeHelper.GetChild(root,i),name);if(found!=null)return found;}return null;}
  static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(150)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
 }
}
