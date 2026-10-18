using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Runtime.InteropServices;
namespace Dafeiyu.Tests
{
    public static class PetRuntimeTests
    {
        static int count;
        static void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;Console.WriteLine("PASS "+why);}
        static object Field(object o,string name){return o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
        static void Pump(){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(delegate{}));}
        static void Until(Func<bool> done){var end=DateTime.UtcNow.AddSeconds(4);while(!done()&&DateTime.UtcNow<end){Pump();Thread.Sleep(10);}Check(done(),"runtime transition completes");}
        [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
        [DllImport("user32.dll")]static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
        [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(NativePoint point);
        [StructLayout(LayoutKind.Sequential)]struct NativePoint{public int X,Y;}
        static Point OpaquePoint(PetWindow pet){var image=(Image)pet.Content;var bitmap=(BitmapSource)image.Source;int stride=bitmap.PixelWidth*4;byte[] pixels=new byte[stride*bitmap.PixelHeight];bitmap.CopyPixels(pixels,stride,0);for(int y=bitmap.PixelHeight/4;y<bitmap.PixelHeight*3/4;y+=4)for(int x=bitmap.PixelWidth/4;x<bitmap.PixelWidth*3/4;x+=4)if(pixels[y*stride+x*4+3]>240){var point=image.PointToScreen(new Point((x+.5)*image.ActualWidth/bitmap.PixelWidth,(y+.5)*image.ActualHeight/bitmap.PixelHeight));if(WindowFromPoint(new NativePoint{X=(int)point.X,Y=(int)point.Y})==new WindowInteropHelper(pet).Handle)return point;}throw new Exception("fixture has no opaque native mouse target");}
        sealed class HeldApi:IConversationApi,IReasoningConversationApi
        {
            internal TaskCompletionSource<string> Reply=new TaskCompletionSource<string>();
            internal Action<string> Reason,Delta;
            public Task<string> ChatWithReasoningAsync(ConnectionSettings s,IList<ChatMessage> m,Action<string> delta,Action<string> reasoning,bool stream,CancellationToken c){Reason=reasoning;Delta=delta;return ChatAsync(s,m,c);}
            public async Task<string> ChatAsync(ConnectionSettings s,IList<ChatMessage> m,CancellationToken c){using(c.Register(()=>Reply.TrySetCanceled()))return await Reply.Task;}
            public Task<string> TranscribeAsync(ConnectionSettings s,byte[] w,CancellationToken c){return Task.FromResult("");}
            public Task<byte[]> SpeechAsync(ConnectionSettings s,string v,string t,CancellationToken c){return Task.FromResult(new byte[0]);}public void Dispose(){}
        }
        sealed class Speech:IReplySpeech{public Task SpeakAsync(string t,PetSettings s,CancellationToken c){return Task.FromResult(0);}public void SetVolume(int n){}public void Stop(){}public void Dispose(){}}
        [STAThread] public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;Point originalCursor=DesktopInterop.Cursor();bool pressed=false;
            try{
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"pet-runtime-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                var cfg=new PetSettings{ReadReplies=false,AutoUpdate=false,GreetingEnabled=false,SaveChatHistory=false,Wander=false,FollowCursor=false,HideOnFullscreen=false,StumbleEnabled=false,AutoSleepEnabled=false};foreach(var key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";cfg.Text.BaseUrl="http://127.0.0.1:1";cfg.Text.Model="fixture";
                var api=new HeldApi();pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"settings.json")),null);
                var manager=new ConversationWorkspaceManager(root,"e-bao",()=>api,()=>new Speech(),()=>null,()=>cfg,()=>false);
                typeof(PetWindow).GetField("workspaces",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,manager);
                pet.Show();Pump();
                var workspace=manager.CreateWorkspace(new ProjectDefinition{Id="p",Workspace=root},new ConversationDefinition{Id="c",Mode="chat"});
                pet.PlayInteraction("sleeping");var send=workspace.Session.SendTextAsync("hello",cfg,"pet");Pump();
                typeof(PetWindow).GetMethod("ConversationChanged",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,null);
                Check(pet.DialogueBusy && pet.Animation.State=="running","background workspace thinking selects thinking action");
                Check(!((InteractionAnimation)Field(pet,"interaction")).IsSleeping,"new work wakes a pre-existing nap");pet.PlayInteraction("sleeping");typeof(PetWindow).GetMethod("ConversationChanged",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,null);Check(((InteractionAnimation)Field(pet,"interaction")).IsSleeping,"sleep chosen during work remains an explicit interaction");
                pet.PlayInteraction("petting");Check(((InteractionAnimation)Field(pet,"interaction")).Active,"petting remains available while agent is busy");
                typeof(PetWindow).GetMethod("ConversationChanged",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,null);
                Check(((InteractionAnimation)Field(pet,"interaction")).Name=="petting","runtime events preserve user interaction");
                var down=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonDownEvent};
                typeof(PetWindow).GetMethod("BeginDrag",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,new object[]{pet,down});
                Check((bool)Field(pet,"dragging"),"drag capture starts during thinking");
                typeof(PetWindow).GetMethod("ConversationChanged",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,null);Check((bool)Field(pet,"dragging"),"runtime events do not release drag capture");
                typeof(PetWindow).GetMethod("CancelInteraction",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,null);
                typeof(PetWindow).GetMethod("ConversationChanged",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,null);
                Check(pet.Animation.State=="running" && !send.IsCompleted,"interaction restores thinking without cancelling task");
                api.Delta("answer draft");Until(()=>pet.Animation.State=="review");api.Reason("more reasoning");Until(()=>pet.Animation.State=="running");
                DesktopInterop.Move(new WindowInteropHelper(pet).Handle,new Point(100,100));Pump();var point=OpaquePoint(pet);SetCursorPos((int)point.X,(int)point.Y);Pump();mouse_event(2,0,0,0,UIntPtr.Zero);pressed=true;Until(()=>(bool)Field(pet,"dragging"));var before=DesktopInterop.Bounds(new WindowInteropHelper(pet).Handle);SetCursorPos((int)point.X+42,(int)point.Y+26);Until(()=>(bool)Field(pet,"moved"));var after=DesktopInterop.Bounds(new WindowInteropHelper(pet).Handle);Check((after.TopLeft-before.TopLeft).Length>20&&!send.IsCompleted,"native mouse drag moves busy pet while task stays active");api.Delta("while held");Pump();Check((bool)Field(pet,"dragging"),"streaming reply does not interrupt native drag");mouse_event(4,0,0,0,UIntPtr.Zero);pressed=false;Until(()=>!(bool)Field(pet,"dragging"));Until(()=>!((InteractionAnimation)Field(pet,"interaction")).Active);api.Reason("continue");Until(()=>pet.Animation.State=="running");
                api.Reply.SetResult("done");while(!send.IsCompleted)Pump();send.GetAwaiter().GetResult();Pump();
                typeof(PetWindow).GetMethod("ConversationChanged",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,null);
                Check(pet.Animation.State=="review","task completion briefly selects review action");
                api.Reply=new TaskCompletionSource<string>();var failed=workspace.Session.SendTextAsync("fail",cfg,"pet");api.Reply.SetException(new ApiException("fixture failure"));Until(()=>failed.IsCompleted);try{failed.GetAwaiter().GetResult();}catch(ApiException){}Until(()=>pet.Animation.State=="failed");
                api.Reply=new TaskCompletionSource<string>();var cancelled=workspace.Session.SendTextAsync("cancel",cfg,"pet");var stop=workspace.Session.StopAsync();Until(()=>stop.IsCompleted&&cancelled.IsCompleted);try{cancelled.GetAwaiter().GetResult();}catch(OperationCanceledException){}Until(()=>pet.Animation.State=="idle");
                Console.WriteLine("Pet runtime tests: "+count+" passed");return 0;
            }catch(Exception e){Console.Error.WriteLine(e);return 1;}finally{if(pressed)mouse_event(4,0,0,0,UIntPtr.Zero);SetCursorPos((int)originalCursor.X,(int)originalCursor.Y);if(pet!=null)pet.Close();app.Shutdown();}
        }
    }
}
