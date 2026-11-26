using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class CompanionWindowRevealUiTests
    {
        static int checks;
        static void Check(bool ok,string message)
        {if(!ok)throw new Exception(message);checks++;}

        static void Pump()
        {Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(delegate{}));}

        [STAThread] public static int Main(string[] args)
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
            var scroll=new ScrollViewer{Content=new Border{Height=1400},VerticalScrollBarVisibility=ScrollBarVisibility.Hidden};
            var content=new Border{Background=Ui.Blue,Child=scroll};
            var host=new Grid();
            host.Children.Add(content);
            var window=new Window{Content=host,Width=620,Height=410,ShowInTaskbar=false};
            string key="old",effect="ripple";
            bool eligible=true,reduced=false;
            var elapsed=TimeSpan.Zero;
            try
            {
                window.Show();Pump();
                using(var reveal=new CompanionWindowReveal(window,host,content,()=>eligible,()=>key,()=>effect,
                    ()=>new Point(27,294),()=>155,_=>null,()=>reduced,()=>elapsed))
                {
                    Func<FrameworkElement> overlay=()=>host.Children.OfType<FrameworkElement>().SingleOrDefault(x=>x.Name=="CompanionWindowOverlay");
                    reveal.Prepare(1,key,key);
                    Check(overlay()==null,"same-role selection does not cover the view");
                    eligible=false;
                    reveal.Prepare(2,key,"new");
                    Check(overlay()==null,"non-chat or inactive window stays live");
                    eligible=true;
                    effect="off";
                    reveal.Prepare(3,key,"new");
                    Check(overlay()==null,"Off leaves the view live");
                    effect="ripple";
                    reduced=true;
                    reveal.Prepare(4,key,"new");
                    Check(overlay()==null,"reduced motion stays live");
                    reduced=false;
                    reveal.Prepare(5,key,"new");
                    Check(overlay()!=null&&!overlay().IsHitTestVisible&&!overlay().Focusable,"prepared frame is non-interactive");
                    reveal.AbortPending(4);
                    Check(overlay()!=null,"a stale abort cannot remove a newer snapshot");
                    reveal.AbortPending(5);
                    Check(overlay()==null,"a failed request discards the prepared frame");
                    reveal.Prepare(6,key,"new");
                    key="new";reveal.MarkCommitted(6,key);
                    elapsed=TimeSpan.FromMilliseconds(121);
                    reveal.OnContentRefreshed();Pump();
                    Check(overlay()==null,"an expired refresh reveals the live window");
                    elapsed=TimeSpan.Zero;
                    effect="zipper";
                    reveal.Prepare(7,key,"other");
                    content.Background=Brushes.Goldenrod;
                    key="other";reveal.MarkCommitted(7,key);
                    reveal.AbortPending(7);
                    reveal.OnContentRefreshed();Pump();
                    Check(overlay()!=null,"a successful role change begins the animation after refresh");
                    scroll.ScrollToVerticalOffset(120);Pump();
                    Check(scroll.VerticalOffset>0,"the fixture applied a programmatic scroll");
                    Check(overlay()!=null,"a programmatic viewport update does not cancel the role reveal");
                    window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),0,Key.Tab)
                        {RoutedEvent=Keyboard.PreviewKeyDownEvent});
                    Check(overlay()!=null,"keyboard input leaves the role animation running");
                    elapsed+=TimeSpan.FromMilliseconds(345);
                    typeof(CompanionWindowReveal).GetMethod("Frame",BindingFlags.NonPublic|BindingFlags.Instance)
                        .Invoke(reveal,new object[]{null,EventArgs.Empty});
                    var previous=overlay();
                    var partial=Pixels(host);
                    reveal.Prepare(8,key,"latest");
                    Check(overlay()!=null&&overlay()!=previous,"a new role replaces the overlay with the visible composition");
                    host.UpdateLayout();
                    Check(partial.SequenceEqual(Pixels(host)),"the next role inherits the same native frame, including zipper hardware");
                    key="latest";reveal.MarkCommitted(8,key);
                    reveal.OnContentRefreshed();Pump();
                    Check(overlay()!=null,"the latest role starts its own reveal");
                    eligible=false;
                    window.Width+=40;Pump();
                    Check(overlay()!=null,"navigation and resize leave a playing reveal active");
                    reveal.Cancel();
                    Check(overlay()==null&&key=="latest","cleanup is repeatable and leaves the committed role");
                }
                window.Close();
                CheckMainWindow(app,args.Contains("--capture"));
                Console.WriteLine("WPF companion window reveal: "+checks+" passed");
                return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
            finally{if(window.IsVisible)window.Close();app.Shutdown();}
        }

        static byte[] Pixels(FrameworkElement element)
        {
            var bitmap=LiquidThemeReveal.Capture(element);
            int stride=bitmap.PixelWidth*4;
            var pixels=new byte[stride*bitmap.PixelHeight];
            bitmap.CopyPixels(pixels,stride,0);
            return pixels;
        }

        static bool HasOverlay(DependencyObject root)
        {
            var visual=root as FrameworkElement;
            if(visual!=null&&visual.Name=="CompanionWindowOverlay")return true;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
                if(HasOverlay(VisualTreeHelper.GetChild(root,i)))return true;
            return false;
        }

        static void CheckMainWindow(Application app,bool capture)
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var data=Path.Combine(Directory.GetCurrentDirectory(),"work","companion-window-ui-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(data);
            var cfg=new PetSettings{UiLanguage="zh_CN",ThemeMode="light",AgentEnabled=false,AutoUpdate=false,GreetingEnabled=false,
                ReadReplies=false,OnboardingState="completed",CompanionTransitionEffect="ripple"};
            foreach(var hotkey in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[hotkey]="";
            var pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(data,"settings.json")),null);
            try
            {
                pet.ShowChat();Pump();
                var chat=app.Windows.OfType<ChatWindow>().Single(window=>window.IsVisible);
                chat.Width=1100;chat.Height=720;chat.Left=50;chat.Top=45;
                chat.Activate();Pump();
                // The capture fixture can be launched by a background test process.
                // Only its synthetic window bypasses the OS foreground check.
                if(capture)
                    typeof(CompanionWindowReveal).GetField("eligible",BindingFlags.Instance|BindingFlags.NonPublic)
                        .SetValue(chat.CompanionReveal,new Func<bool>(()=>chat.CurrentPage=="home"));
                var original=pet.Skin;
                var next=pet.Catalog.Skins.First(s=>s.BuiltinCharacterId=="marfu"&&!s.IsPlaceholder);
                Action<CompanionSelectionUpdate> cancelPrepared=update=>{
                    if(update.Stage=="prepared")pet.CancelCompanionSelection(update.Request);
                };
                pet.CompanionSelectionChanged+=cancelPrepared;
                var cancelled=pet.RequestSkinAsync(next,CompanionSelectionOrigin.MainWindow,chat);
                var cancellationWait=Stopwatch.StartNew();
                while(!cancelled.IsCompleted&&cancellationWait.Elapsed<TimeSpan.FromSeconds(10)){Pump();Thread.Sleep(2);}
                pet.CompanionSelectionChanged-=cancelPrepared;
                Check(cancelled.IsCompleted&&!cancelled.GetAwaiter().GetResult()&&pet.Skin.PickerKey==original.PickerKey&&
                    cfg.SelectedCompanionKey==original.PickerKey&&!HasOverlay(chat),
                    "a cancellation at preparation completion cannot publish the new role or animation");
                bool committed=false,covered=false;
                pet.CompanionSelectionChanged+=update=>{
                    if(update.Stage!="committed")return;
                    committed=true;covered=HasOverlay(chat);
                };
                foreach(string effect in new[]{"ripple","zipper","off"})
                {
                    cfg.CompanionTransitionEffect=effect;
                    committed=covered=false;
                    var target=pet.Skin.PickerKey==original.PickerKey?next:original;
                    var folder=Path.Combine(data,effect);
                    if(capture&&effect!="off"){Directory.CreateDirectory(folder);Capture(chat,folder,0);}
                    var task=pet.RequestSkinAsync(target,CompanionSelectionOrigin.MainWindow,chat);
                    var stopwatch=Stopwatch.StartNew();
                    while(!task.IsCompleted&&stopwatch.Elapsed<TimeSpan.FromSeconds(10)){Pump();Thread.Sleep(2);}
                    Check(task.IsCompleted&&task.GetAwaiter().GetResult()&&pet.Skin.PickerKey==target.PickerKey&&committed,
                        "the main window commits the selected real role");
                    Check(covered==(effect!="off"&&SystemParameters.ClientAreaAnimation&&(capture||chat.IsActive)),
                        "only an eligible committed main window displays the selected effect");
                    int frame=1;
                    while(HasOverlay(chat)&&stopwatch.Elapsed<TimeSpan.FromSeconds(12))
                    {
                        Pump();
                        if(capture&&effect!="off"&&frame<=48)Capture(chat,folder,frame++);
                        Thread.Sleep(20);
                    }
                    Check(!HasOverlay(chat),"the overlay clears after the real role change");
                    if(capture&&effect!="off")Capture(chat,folder,frame);
                }
                if(capture)Console.WriteLine("Companion window captures="+data);
            }
            finally{pet.Close();Pump();}
        }

        static void Capture(Window window,string directory,int frame)
        {
            var image=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),
                96,96,PixelFormats.Pbgra32);
            image.Render(window);
            var encoder=new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using(var file=File.Create(Path.Combine(directory,frame.ToString("D3")+".png")))encoder.Save(file);
        }
    }
}
