using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class ProactiveGreetingSpeechTests
    {
        static int checks;
        static Type serviceType;
        static PropertyInfo preference;
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        static object Service(PetSettings settings,Func<IReplySpeech> factory,Func<bool> busy=null)
        {return Activator.CreateInstance(serviceType,new object[]{new Func<PetSettings>(()=>settings),factory,busy});}
        static Task Speak(object service,string text){return (Task)serviceType.GetMethod("SpeakAsync").Invoke(service,new object[]{text});}
        static void Stop(object service){serviceType.GetMethod("Stop").Invoke(service,null);}
        static void Refresh(object service){serviceType.GetMethod("Refresh").Invoke(service,null);}
        [STAThread] public static int Main()
        {
            try{Run().GetAwaiter().GetResult();Host();Console.WriteLine("Proactive greeting speech: "+checks+" passed");return 0;}
            catch(Exception e){Console.WriteLine("FAIL proactive greeting speech: "+e);return 1;}
        }
        static async Task Run()
        {
            preference=typeof(PetSettings).GetProperty("GreetingAutoReadEnabled");
            Check(preference!=null,"independent auto-read preference exists");
            Check((bool)preference.GetValue(new PetSettings(),null),"auto-read defaults on");
            using(var stream=new MemoryStream(Encoding.UTF8.GetBytes("{}")))
                Check((bool)preference.GetValue(new DataContractJsonSerializer(typeof(PetSettings)).ReadObject(stream),null),"old settings enable proactive speech when global TTS is enabled");
            var settings=new PetSettings();preference.SetValue(settings,false,null);
            Check(!(bool)preference.GetValue(settings.Copy(),null),"copy keeps independent opt-out");
            string root=Path.Combine("work","proactive-speech-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try{
                var store=new SettingsStore(Path.Combine(root,"settings.json"));store.Save(settings);
                Check(!(bool)preference.GetValue(store.Load(),null),"opt-out persists across restart");
                typeof(SettingsStore).GetMethod("CopyPreferences",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{new PetSettings(),settings});
                Check((bool)preference.GetValue(settings,null),"reset restores auto-read default");
            }finally{Directory.Delete(root,true);}
            serviceType=typeof(PetSettings).Assembly.GetType("Dafeiyu.ProactiveGreetingSpeech");
            Check(serviceType!=null,"shared dedicated proactive speech owner exists");
            await Policy();await Lifecycle();
        }
        static async Task Policy()
        {
            var settings=new PetSettings{Volume=34};int creates=0;bool busy=false;FakeSpeech output=null;
            var service=Service(settings,()=>{creates++;return output=new FakeSpeech();},()=>busy);
            try{
                Check(creates==0,"speech output opens lazily");
                settings.ReadReplies=false;await Speak(service,"global off");settings.ReadReplies=true;
                preference.SetValue(settings,false,null);await Speak(service,"auto-read off");preference.SetValue(settings,true,null);
                settings.QuietMode=true;await Speak(service,"quiet");settings.QuietMode=false;
                settings.Volume=0;await Speak(service,"muted");settings.Volume=34;
                busy=true;await Speak(service,"reply or microphone active");busy=false;
                await Speak(service,"  ");Check(creates==0,"global TTS, independent opt-out, DND, mute, busy and blank text suppress output");
                var task=Speak(service,"visible screen-aware greeting");
                Check(creates==1&&output.Text=="visible screen-aware greeting"&&output.Settings.Volume==34,"only actual shown greeting reaches speech with configured volume");
                settings.Volume=55;Check(output.Settings.Volume==34,"active speech owns an immutable settings snapshot");
                output.Completion.SetResult(true);await task;Check(output.Disposals==1,"successful utterance releases its dedicated output");
                Check(creates==1,"leaving quiet or unmuting never replays suppressed greetings");
            }finally{((IDisposable)service).Dispose();}
        }
        static async Task Lifecycle()
        {
            var settings=new PetSettings();FakeSpeech current=null;int creates=0;bool busy=false;
            var service=Service(settings,()=>{creates++;return current=new FakeSpeech();},()=>busy);
            var first=Speak(service,"first");var previous=current;
            var second=Speak(service,"replacement");
            Check(previous.Token.IsCancellationRequested&&previous.Stops>0,"replacement cancels previous utterance before beginning another");
            await first;Check(previous.Disposals==1,"uncooperative cancelled output is released without waiting for completion");
            settings.QuietMode=true;Refresh(service);await second;
            Check(current.Token.IsCancellationRequested&&current.Stops>0&&current.Disposals==1,"entering DND immediately interrupts and releases active speech");
            settings.QuietMode=false;Refresh(service);Check(creates==2,"leaving DND does not restart speech");
            var active=Speak(service,"global switch");settings.ReadReplies=false;Refresh(service);await active;
            Check(current.Token.IsCancellationRequested,"turning global TTS off interrupts proactive speech");settings.ReadReplies=true;
            active=Speak(service,"independent switch");preference.SetValue(settings,false,null);Refresh(service);await active;
            Check(current.Token.IsCancellationRequested,"turning proactive auto-read off interrupts speech");preference.SetValue(settings,true,null);
            active=Speak(service,"microphone starts");busy=true;Refresh(service);await active;
            Check(current.Token.IsCancellationRequested,"reply, dictation or live audio takes priority over proactive speech");busy=false;
            active=Speak(service,"companion changed or bubble dismissed");Stop(service);await active;
            Check(current.Token.IsCancellationRequested,"explicit owner stop cancels pending or playing speech");
            active=Speak(service,"shutdown");((IDisposable)service).Dispose();await active;
            Check(current.Token.IsCancellationRequested&&current.Disposals==1,"shutdown cancels and releases active output");
            await Speak(service,"after shutdown");Check(creates==7,"shutdown rejects future playback");
            previous.Completion.TrySetException(new InvalidOperationException("late backend failure"));
        }
        static void Host()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"proactive-speech-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);PetWindow pet=null;
            try{
                var settings=new PetSettings{GreetingEnabled=true,AiGreetingEnabled=false,ReadReplies=true,AutoUpdate=false,FollowCursor=false,Wander=false,HideOnFullscreen=false,SaveChatHistory=false,OnboardingState="completed",Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="fixture",ImageSupport=CapabilitySupport.Supported}};
                foreach(var key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
                pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(root,"settings.json")),null);pet.Show();Pump();
                pet.GreetingRelationshipReader=(id,pack,locale)=>Task.FromResult<RelationshipExperienceContent>(null);
                int creates=0;FakeSpeech output=null;var speech=new ProactiveGreetingSpeech(()=>settings,()=>{creates++;return output=new FakeSpeech();},()=>false);
                typeof(PetWindow).GetField("proactiveGreetingSpeech",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,speech);
                int history=pet.Conversation.History.Messages.Count;
                pet.PlayAction("idle",0);pet.PreviewGreeting();PumpUntil(()=>creates==1);
                var bubble=app.Windows.OfType<GreetingBubbleWindow>().Single();
                string shown=((TextBlock)LogicalTreeHelper.FindLogicalNode(bubble,"GreetingMessage")).Text;
                Check(bubble.IsVisible&&shown==output.Text,"actual preset preview reads its displayed bubble text");
                pet.ShowSettings();Pump();var panel=(SettingsPage)typeof(PetWindow).GetField("preferences",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet);panel.SelectCompanionSection(2);Pump();
                var enabled=(CheckBox)panel.FindControl("GreetingEnabled");var autoRead=(CheckBox)panel.FindControl("GreetingAutoReadEnabled");var ai=(CheckBox)panel.FindControl("AiGreetingEnabled");var parent=(Panel)LogicalTreeHelper.GetParent(enabled);
                Check(parent.Children.IndexOf(enabled)<parent.Children.IndexOf(autoRead)&&parent.Children.IndexOf(autoRead)<parent.Children.IndexOf(ai),"auto-read switch sits between proactive and AI switches");
                pet.PlayAction("idle",0);pet.PreviewGreeting();PumpUntil(()=>creates==2);var speaking=output;
                autoRead.IsChecked=false;Pump();
                Check(bubble.IsVisible&&speaking.Token.IsCancellationRequested&&!pet.Store.Load().GreetingAutoReadEnabled,"turning actual UI auto-read off stops speech and retains the bubble");
                pet.PlayAction("idle",0);pet.PreviewGreeting();Pump();Check(creates==2&&bubble.IsVisible,"disabled auto-read keeps subsequent bubble greetings silent");
                autoRead.IsChecked=true;Pump();Check(creates==2,"enabling UI auto-read does not replay prior greeting");
                var api=new GreetingApi{Reply="An AI greeting just for you."};pet.GreetingGenerator=new GreetingService(()=>api,TimeSpan.FromSeconds(1));ai.IsChecked=true;
                pet.PlayAction("idle",0);pet.PreviewGreeting();PumpUntil(()=>creates==3);
                Check(api.Calls==1&&output.Text=="An AI greeting just for you."&&pet.Conversation.History.Messages.Count==history,"AI greeting speaks once without an extra model request or history turn");
                var lifetime=(DispatcherTimer)typeof(GreetingBubbleWindow).GetField("lifetime",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(bubble);lifetime.Interval=TimeSpan.FromMilliseconds(30);
                Pump();Pump();Check(bubble.IsVisible&&!output.Token.IsCancellationRequested,"automatic bubble expiry waits while synthesis or playback is pending");
                output.Completion.SetResult(true);PumpUntil(()=>!bubble.IsVisible);Check(output.Disposals==1,"expired bubble closes promptly once speech finishes");
                api.Reply="Another greeting for the close button.";pet.PlayAction("idle",0);pet.PreviewGreeting();PumpUntil(()=>creates==4);
                Check(lifetime.Interval==TimeSpan.FromSeconds(12),"a replacement greeting receives its full initial bubble lifetime");
                ((Button)LogicalTreeHelper.FindLogicalNode(bubble,"DismissGreeting")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(output.Token.IsCancellationRequested&&!bubble.IsVisible,"actual bubble close button interrupts owned speech");
                panel.HostWindow.Close();Pump();pet.PlayAction("idle",0);
                var capture=new GreetingCapture();api.Reply="{\"speak\":true,\"message\":\"Your screen-aware greeting.\"}";pet.ObservationCaptureFactory=()=>capture;pet.ObservationApiFactory=()=>api;pet.ObservationInputTick=()=>1;
                settings.DesktopObservationEnabled=true;settings.DesktopObservationConsentVersion=DesktopObservationConsent.Version;settings.DesktopObservationConsentDestination=DesktopObservationConsent.Destination(settings);
                var notice=typeof(PetWindow).GetField("relationshipNotice",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet) as Window;if(notice!=null)notice.Close();
                typeof(PetWindow).GetMethod("ShowGreeting",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,null);PumpUntil(()=>creates==5);
                Check(capture.Calls==1&&output.Text=="Your screen-aware greeting."&&bubble.IsVisible,"automatic screen observation reads the actual visible greeting");
                pet.SetQuietMode(true);Pump();Check(output.Token.IsCancellationRequested,"actual DND action interrupts screen-aware speech");pet.SetQuietMode(false);Pump();Check(creates==5,"leaving actual DND does not replay screen-aware speech");
                api.Reply="{\"speak\":true,\"message\":\"Raw manual test description.\"}";
                var test=pet.GenerateObservationAsync(settings.Copy(),pet.Skin.Id,pet.Skin.Name,null,null,CancellationToken.None,DesktopObservationMode.ManualTest);PumpUntil(()=>test.IsCompleted);using(var decision=test.GetAwaiter().GetResult()){}
                Check(creates==5,"manual observation test results are never automatically read aloud");
            }finally{if(pet!=null){pet.Close();Pump();}app.Shutdown();}
        }
        static void PumpUntil(Func<bool> done){var until=DateTime.UtcNow.AddSeconds(10);while(!done()&&DateTime.UtcNow<until)Pump();if(!done())throw new Exception("greeting speech UI timed out");}
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(80)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        sealed class GreetingApi:IConversationApi
        {
            public string Reply;public int Calls;
            public Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,CancellationToken cancel){Calls++;return Task.FromResult(Reply);}
            public Task<string> TranscribeAsync(ConnectionSettings s,byte[] w,CancellationToken c){throw new NotSupportedException();}
            public Task<byte[]> SpeechAsync(ConnectionSettings s,string v,string t,CancellationToken c){throw new NotSupportedException();}public void Dispose(){}
        }
        sealed class GreetingCapture:IDesktopObservationCapture
        {
            public int Calls;public DesktopCaptureStatus GetStatus(DesktopCaptureOptions options=null){return DesktopCaptureStatus.Ready;}public bool IsCurrent(DesktopObservationFrame frame){return true;}
            public Task<DesktopObservationFrame> CaptureForegroundAsync(DesktopCaptureOptions options,CancellationToken cancel)
            {
                Calls++;var image=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[]{255,0,0,255,0,255,0,255,0,0,255,255,255,255,255,255},8);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var stream=new MemoryStream()){encoder.Save(stream);return Task.FromResult(new DesktopObservationFrame(stream.ToArray(),DateTimeOffset.UtcNow,new DesktopObservationIdentity(1,2,3,4,"fixture","","",0,0,2,2)));}
            }
        }
        sealed class FakeSpeech:IReplySpeech
        {
            public string Text;public PetSettings Settings;public CancellationToken Token;public int Stops,Disposals;
            public readonly TaskCompletionSource<bool> Completion=new TaskCompletionSource<bool>();
            public Task SpeakAsync(string text,PetSettings settings,CancellationToken cancel){Text=text;Settings=settings;Token=cancel;return Completion.Task;}
            public void Stop(){Stops++;}public void SetVolume(int value){}public void Dispose(){Disposals++;}
        }
    }
}
