using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
 public static class DesktopObservationHostTests
 {
  static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
  [STAThread] public static int Main()
  {
   var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"observation-host-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);PetWindow pet=null;
   try{
    var settings=new PetSettings{GreetingEnabled=true,AiGreetingEnabled=true,DesktopObservationEnabled=false,AutoUpdate=false,FollowCursor=false,Wander=false,OnboardingState="completed",Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="test",ImageSupport=CapabilitySupport.Supported}};foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
    pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(root,"settings.json")),null);var capture=new Capture();var api=new Api();uint tick=1;pet.ObservationCaptureFactory=()=>capture;pet.ObservationApiFactory=()=>api;pet.ObservationInputTick=()=>tick;pet.Show();Pump();
    Func<PetSettings,Task<ProactiveDecision>> run=s=>pet.GenerateObservationAsync(s,pet.Skin.Id,pet.Skin.Name,null,null,CancellationToken.None,DesktopObservationMode.ManualTest);
    using(var preview=Wait(pet.CaptureLocalObservationAsync())){Check(preview!=null && capture.Calls==1 && api.Calls==0,"local preview captures once without creating an API request");}capture.Calls=0;capture.ThrowCurrent=true;var broken=pet.CaptureLocalObservationAsync();try{Wait(broken);}catch(InvalidOperationException){}Check(capture.Last!=null && capture.Last.IsDisposed,"failed preview currentness clears captured bytes");capture.ThrowCurrent=false;capture.Calls=0;
    using(var off=Wait(run(settings.Copy())))Check(off.Reason==ObservationReason.Disabled && capture.Calls==0 && api.Calls==0,"disabled feature never captures or requests");
    settings.DesktopObservationEnabled=true;using(var noConsent=Wait(run(settings.Copy())))Check(noConsent.Reason==ObservationReason.ConsentRequired && capture.Calls==0 && api.Calls==0,"missing consent never captures");
    settings.DesktopObservationConsentVersion=DesktopObservationConsent.Version;settings.DesktopObservationConsentDestination=DesktopObservationConsent.Destination(settings);
    capture.Block=true;var duringCapture=run(settings.Copy());PumpUntil(()=>capture.Calls==1);tick++;capture.Release.SetResult(capture.Frame());Check(Wait(duringCapture)==null && api.Calls==0,"same-window input before upload denies request");capture.Block=false;
    var old=run(settings.Copy());PumpUntil(()=>api.Calls==1);pet.InvalidateObservation(true);api.Answer.SetResult("{\"speak\":true,\"message\":\"Hello\"}");Check(Wait(old)==null,"privacy invalidation drops late result");
    api.Answer=new TaskCompletionSource<string>();var stale=run(settings.Copy());PumpUntil(()=>api.Calls==2);tick++;api.Answer.SetResult("{\"speak\":true,\"message\":\"Hello\"}");Check(Wait(stale)==null,"same-window user input drops stale result");
    var when=new DateTime(2026,11,2,0,0,0,DateTimeKind.Utc);Check(pet.TryBeginManualObservation(when) && !pet.TryBeginManualObservation(when.AddSeconds(9)) && pet.TryBeginManualObservation(when.AddSeconds(10)),"manual upload tests debounce for ten seconds");
    var events=(ProactiveEventStore)typeof(PetWindow).GetField("observationEvents",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(pet);var frame=capture.Frame();events.Register(new ProactiveEvent("clear",pet.Skin.Id,"hello",frame.EncodedPng,DateTime.UtcNow,null,null));frame.Dispose();pet.SetObservationPaused(true);Check(events.TryGet("clear").Png==null,"pause immediately clears retained observation image");pet.SetObservationPaused(false);
    api.Answer=new TaskCompletionSource<string>();var cancel=new CancellationTokenSource();var cancelled=pet.GenerateObservationAsync(settings.Copy(),pet.Skin.Id,pet.Skin.Name,null,null,cancel.Token,DesktopObservationMode.ManualTest);PumpUntil(()=>api.Calls==3);cancel.Cancel();using(var stopped=Wait(cancelled)){}var idle=pet.ObservationService.WaitForIdleAsync();bool maintenanceRejected=false;try{pet.DataMaintenance.BeginAsync();}catch(InvalidOperationException){maintenanceRejected=true;}Check(maintenanceRejected && !idle.IsCompleted,"maintenance remains blocked while timed-out model work is owned");api.Answer.SetResult("{\"speak\":false,\"message\":\"\"}");PumpUntil(()=>idle.IsCompleted);Pump();var lease=pet.DataMaintenance.BeginAsync();PumpUntil(()=>lease.IsCompleted);lease.GetAwaiter().GetResult().Dispose();cancel.Dispose();Check(true,"maintenance proceeds after real model work drains");
    var notice=typeof(PetWindow).GetField("relationshipNotice",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(pet) as Window;if(notice!=null)notice.Close();pet.PlayAction("idle",0);var stage=new TaskCompletionSource<RelationshipExperienceContent>();pet.GreetingRelationshipReader=(id,pack,locale)=>stage.Task;
    int capturesBefore=capture.Calls, callsBefore=api.Calls;InvokeGreeting(pet);Check(GreetingPending(pet),"automatic greeting enters delayed relationship read");
    settings.Text.Model="changed";pet.InvalidateObservation();stage.SetResult(null);PumpUntil(()=>!GreetingPending(pet));
    Check(capture.Calls==capturesBefore && api.Calls==callsBefore,"invalidation during relationship read prevents capture and API admission");
    settings.Text.Model="test";settings.DesktopObservationConsentDestination=DesktopObservationConsent.Destination(settings);pet.GreetingRelationshipReader=(id,pack,locale)=>Task.FromResult<RelationshipExperienceContent>(null);
    api.Answer=new TaskCompletionSource<string>();capture.LastCheck=new ManualResetEventSlim(false);capture.CurrentChecks=0;capture.Current=true;InvokeGreeting(pet);PumpUntil(()=>api.Calls==callsBefore+1);var epochField=typeof(PetWindow).GetField("observationEpoch",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);long finalEpoch=(long)epochField.GetValue(pet);uint finalTick=tick;
    Task.Run(()=>api.Answer.SetResult("{\"speak\":true,\"message\":\"Fresh observation\"}"));
    if(!capture.LastCheck.Wait(TimeSpan.FromSeconds(10)))throw new Exception("service currentness check timed out");capture.Current=false;
    PumpUntil(()=>!GreetingPending(pet));Pump();
    var bubble=typeof(PetWindow).GetField("greeting",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(pet) as Window;
    var recent=(Dictionary<string,List<string>>)typeof(PetWindow).GetField("recentGreetings",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(pet);
    var stored=(System.Collections.IDictionary)typeof(ProactiveEventStore).GetField("events",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(events);
    Check((long)epochField.GetValue(pet)==finalEpoch && tick==finalTick && (bubble==null || !bubble.IsVisible) && !recent[pet.Skin.Id].Contains("Fresh observation") && stored.Count==1 && capture.Last.IsDisposed,"changed native target with unchanged epoch and input does not show or register a greeting, and clears the frame");
    Console.WriteLine("DesktopObservationHostTests: "+checks+" assertions passed");return 0;
   }catch(Exception error){Console.WriteLine("FAIL "+error);return 1;}finally{if(pet!=null){pet.Close();Pump();}app.Shutdown();}
  }
  static T Wait<T>(Task<T> task){PumpUntil(()=>task.IsCompleted);return task.GetAwaiter().GetResult();}
  static void PumpUntil(Func<bool> done){var until=DateTime.UtcNow.AddSeconds(12);while(!done() && DateTime.UtcNow<until)Pump();if(!done())throw new Exception("fixture timed out");}
  static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(30)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
  static void InvokeGreeting(PetWindow pet){typeof(PetWindow).GetMethod("ShowGreeting",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(pet,null);}
  static bool GreetingPending(PetWindow pet){return typeof(PetWindow).GetField("greetingRequest",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(pet)!=null;}
  sealed class Capture:IDesktopObservationCapture
  {
   public int Calls;public bool ThrowCurrent;public volatile bool Current=true;public int CurrentChecks;public ManualResetEventSlim LastCheck;public DesktopObservationFrame Last;public bool Block;public TaskCompletionSource<DesktopObservationFrame> Release=new TaskCompletionSource<DesktopObservationFrame>();public DesktopCaptureStatus GetStatus(){return DesktopCaptureStatus.Ready;}public bool IsCurrent(DesktopObservationFrame f){if(ThrowCurrent)throw new InvalidOperationException("fixture");if(Interlocked.Increment(ref CurrentChecks)==3 && LastCheck!=null)LastCheck.Set();return Current;}
   public Task<DesktopObservationFrame> CaptureForegroundAsync(DesktopCaptureOptions options,CancellationToken cancel){Calls++;return Block?Release.Task:Task.FromResult(Frame());}
   public DesktopObservationFrame Frame(){var image=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[]{255,0,0,255,0,255,0,255,0,0,255,255,255,255,255,255},8);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));byte[] bytes;using(var stream=new MemoryStream()){encoder.Save(stream);bytes=stream.ToArray();}return Last=new DesktopObservationFrame(bytes,DateTimeOffset.UtcNow,new DesktopObservationIdentity(1,2,3,4,"app","","",0,0,2,2));}
  }
  sealed class Api:IConversationApi
  {
   public int Calls;public TaskCompletionSource<string> Answer=new TaskCompletionSource<string>();public Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,CancellationToken cancel){Interlocked.Increment(ref Calls);return Answer.Task;}
   public Task<string> TranscribeAsync(ConnectionSettings settings,byte[] bytes,CancellationToken cancel){throw new NotSupportedException();}public Task<byte[]> SpeechAsync(ConnectionSettings settings,string voice,string text,CancellationToken cancel){throw new NotSupportedException();}public void Dispose(){}
  }
 }
}
