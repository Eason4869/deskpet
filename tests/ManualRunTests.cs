using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class ManualRunTests
    {
        static int passed;
        sealed class HeldApi:IConversationApi
        {
            readonly TaskCompletionSource<string> reply=new TaskCompletionSource<string>();
            public async Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,CancellationToken cancellation){using(cancellation.Register(()=>reply.TrySetCanceled()))return await reply.Task;}
            public Task<string> TranscribeAsync(ConnectionSettings settings,byte[] wave,CancellationToken cancellation){return Task.FromResult("");}
            public Task<byte[]> SpeechAsync(ConnectionSettings settings,string voice,string text,CancellationToken cancellation){return Task.FromResult(new byte[0]);}
            public void Dispose(){}
        }
        sealed class SilentSpeech:IReplySpeech{public Task SpeakAsync(string text,PetSettings settings,CancellationToken cancellation){return Task.FromResult(0);}public void SetVolume(int value){}public void Stop(){}public void Dispose(){}}
        static void Check(bool value,string label){if(!value)throw new Exception(label);passed++;Console.WriteLine("PASS "+label);}
        static void Pump(double seconds){var until=DateTime.UtcNow.AddSeconds(seconds);while(DateTime.UtcNow<until){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);System.Threading.Thread.Sleep(8);}}
        static System.Collections.Generic.IEnumerable<T> All<T>(DependencyObject root) where T:DependencyObject {if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in All<T>(VisualTreeHelper.GetChild(root,i)))yield return child;}
        [STAThread] public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
            try
            {
                string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"manual-run-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
                var cfg=new PetSettings{FollowCursor=false,GreetingEnabled=false,HideOnFullscreen=false,Wander=false,AutoUpdate=false,QuietMode=true,ReadReplies=false,SaveChatHistory=false};foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(folder,"settings.json")),null);pet.Show();Pump(.2);
                IntPtr handle=new WindowInteropHelper(pet).Handle;Rect area=DesktopInterop.WorkArea(handle),bounds=DesktopInterop.Bounds(handle);Point center=new Point(area.Left+(area.Width-bounds.Width)/2,area.Top+(area.Height-bounds.Height)/2);DesktopInterop.Move(handle,center);
                // Exercise the real context-menu dispatch, with automatic wandering disabled.
                var actions=pet.ContextMenu.Items.OfType<MenuItem>().Single(m=>Convert.ToString(m.Header)=="做个动作");
                actions.Items.OfType<MenuItem>().Single(m=>Convert.ToString(m.Header)=="向右跑").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump(.35);
                Rect right=DesktopInterop.Bounds(handle);Check(right.Left>center.X+15&&Math.Abs(right.Top-center.Y)<2,"context-menu run moves right horizontally even with wandering off and quiet mode on");
                pet.PlayAction("waving",2);double stopped=DesktopInterop.Bounds(handle).Left;Pump(.25);Check(Math.Abs(DesktopInterop.Bounds(handle).Left-stopped)<2,"switching action stops displacement");
                var palette=new ActionPaletteWindow(pet);palette.ShowForPet();Pump(.1);All<Button>(palette).Single(b=>b.Name=="PaletteBasicTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                All<Button>(palette).Single(b=>b.Name=="PaletteAction_running_left").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(.35);Check(!palette.IsVisible&&DesktopInterop.Bounds(handle).Left<stopped-15,"wheel dispatch closes and moves left");
                pet.SetPaused(true);stopped=DesktopInterop.Bounds(handle).Left;Pump(.2);Check(Math.Abs(DesktopInterop.Bounds(handle).Left-stopped)<2,"pause stops displacement");pet.SetPaused(false);
                DesktopInterop.Move(handle,center);pet.PlayAction("running-right",.25);Pump(.6);stopped=DesktopInterop.Bounds(handle).Left;Check(stopped>center.X+10&&stopped<center.X+100,"run uses its requested finite duration");Pump(.25);Check(Math.Abs(DesktopInterop.Bounds(handle).Left-stopped)<2,"run remains stopped after duration expires");Check(pet.Store.Load().X.HasValue&&Math.Abs(pet.Store.Load().X.Value-stopped)<2,"final run position is persisted");
                DesktopInterop.Move(handle,new Point(area.Right-bounds.Width-4,center.Y));pet.PlayAction("running-right",2);Pump(.3);Check(DesktopInterop.Bounds(handle).Right<=area.Right+1&&pet.Animation.State=="idle","right screen edge clamps movement and stops running");
                DesktopInterop.Move(handle,new Point(area.Left+4,center.Y));pet.PlayAction("running-left",2);Pump(.3);Check(DesktopInterop.Bounds(handle).Left>=area.Left-1&&pet.Animation.State=="idle","left screen edge clamps movement and stops running");
                DesktopInterop.Move(handle,center);cfg.LockPosition=true;pet.PlayAction("running-right",2);Pump(.25);Check(Math.Abs(DesktopInterop.Bounds(handle).Left-center.X)<2,"position lock preserves in-place action");cfg.LockPosition=false;
                pet.PlayAction("running-right",2);Pump(.15);typeof(PetWindow).GetMethod("BeginDrag",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,new object[]{pet,new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonDownEvent}});stopped=DesktopInterop.Bounds(handle).Left;Pump(.2);Check(Math.Abs(DesktopInterop.Bounds(handle).Left-stopped)<2,"starting a drag immediately stops running");pet.PlayAction("idle",0);
                pet.PlayAction("running-right",2);Pump(.15);pet.PlayInteraction("petting");stopped=DesktopInterop.Bounds(handle).Left;Pump(.2);Check(Math.Abs(DesktopInterop.Bounds(handle).Left-stopped)<2,"starting an interaction stops running");
                pet.PlayAction("running-right",2);Pump(.15);pet.HidePet();stopped=DesktopInterop.Bounds(handle).Left;pet.RestorePet();Pump(.2);Check(Math.Abs(DesktopInterop.Bounds(handle).Left-stopped)<2,"hide and restore do not resume stale movement");
                pet.Conversation.Dispose();var session=new ConversationSession(new HeldApi(),new SilentSpeech(),()=>{throw new Exception("unused live session");});session.SelectPersona(pet.Skin.Id);typeof(PetWindow).GetField("conversation",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,session);cfg.Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:9876",Model="held-fixture",StreamReplies=false};var request=session.SendTextAsync("busy",cfg,pet.Skin.Name);Pump(.1);DesktopInterop.Move(handle,center);pet.PlayAction("running-right",2);Pump(.3);Check(pet.DialogueBusy&&!request.IsCompleted&&DesktopInterop.Bounds(handle).Left>center.X+15&&pet.Animation.State=="running-right","active conversation preserves manual running animation and real movement");pet.PlayAction("idle",0);var stop=session.StopAsync();Pump(.1);stop.GetAwaiter().GetResult();
                Console.WriteLine("Manual run tests: "+passed+" passed");return 0;
            }
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
            finally{foreach(var window in app.Windows.OfType<Window>().ToArray())window.Close();if(pet!=null)pet.Close();Pump(.2);app.Shutdown();}
        }
    }
}
