using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dafeiyu;
using DeskPet.Desktop;

internal static class CompanionWindowRevealChecks
{
    internal static int Run(string directory,bool capture)
    {
        int checks=0;
        void Check(bool value,string description)
        {if(!value)throw new Exception(description);checks++;}

        var content=new Border{Background=DesktopTheme.Surface};
        var host=new Grid{Children={content}};
        var window=new Window{Content=host,Width=680,Height=430};
        string key="old",effect="ripple";
        bool eligible=true,reduced=false;
        TimeSpan now=TimeSpan.Zero;
        try
        {
            window.Show();Dispatcher.UIThread.RunJobs();
            using var reveal=new CompanionWindowReveal(window,host,content,()=>eligible,()=>key,()=>effect,
                ()=>new Point(20,22),()=>reduced,()=>now);
            bool Overlay()=>host.Children.Any(control=>control.Name=="CompanionWindowOverlay");
            eligible=false;reveal.Prepare(1,key,"new");
            Check(!Overlay(),"ineligible chat view shows no overlay");
            eligible=true;reduced=true;reveal.Prepare(2,key,"new");
            Check(!Overlay(),"reduced motion shows no overlay");
            reduced=false;effect="off";reveal.Prepare(3,key,"new");
            Check(!Overlay(),"off shows no overlay");
            effect="ripple";reveal.Prepare(4,key,"new");
            var layer=host.Children.Single(control=>control.Name=="CompanionWindowOverlay");
            Check(!layer.IsHitTestVisible&&!layer.Focusable,"prepared frame cannot take focus or pointer input");
            reveal.OnContentRefreshed();Dispatcher.UIThread.RunJobs();
            Check(Overlay(),"a refresh before commit cannot start the prepared reveal");
            reveal.Prepare(5,key,"other");reveal.AbortPending(4);
            Check(Overlay(),"new requests own their own overlay");
            key="other";reveal.MarkCommitted(5,key);
            now=TimeSpan.FromMilliseconds(121);
            reveal.OnContentRefreshed();Dispatcher.UIThread.RunJobs();
            Check(!Overlay(),"late refresh releases a stale snapshot");
            effect="zipper";reveal.Prepare(6,key,"next");
            key="next";reveal.MarkCommitted(6,key);reveal.OnContentRefreshed();
            Dispatcher.UIThread.RunJobs();
            Check(Overlay(),"the committed snapshot waits for native animation frames");
            now+=TimeSpan.FromMilliseconds(600);AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Check(Overlay(),"zipper retains both layers during the animation");
            now+=TimeSpan.FromMilliseconds(120);AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var deadline=DateTime.UtcNow.AddSeconds(2);
            while(Overlay()&&DateTime.UtcNow<deadline)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Thread.Sleep(1);
            }
            Check(!Overlay(),"completion removes the overlay");
            reveal.Prepare(7,key,"last");
            key="last";reveal.MarkCommitted(7,key);
            window.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Tab});
            Check(!Overlay(),"input interrupts a committed overlay");
        }
        finally{window.Close();}
        return checks+CheckMainWindow(directory,capture);
    }

    private static int CheckMainWindow(string directory,bool capture)
    {
        int checks=0;
        void Check(bool value,string description)
        {if(!value)throw new Exception(description);checks++;}
        void Until(Func<bool> ready)
        {
            var deadline=DateTime.UtcNow.AddSeconds(8);
            while(!ready()&&DateTime.UtcNow<deadline)
            {
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Thread.Sleep(2);
            }
            if(!ready())throw new TimeoutException("companion main window fixture");
        }

        using var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new TestRuntime(directory));
        try
        {
            owner.Settings.OnboardingState="completed";
            owner.Settings.AgentEnabled=false;
            owner.Settings.AutoUpdate=false;
            owner.Settings.CompanionTransitionEffect="zipper";
            owner.SetThemeMode("light");
            owner.ShowChat(false);Dispatcher.UIThread.RunJobs();
            var chat=owner.GuideMainWindow as ChatWindow??throw new InvalidOperationException("main chat window missing");
            var reveal=chat.CompanionReveal??throw new InvalidOperationException("main reveal controller missing");
            // Headless windows do not own the OS foreground. Only the fixture
            // supplies it so the real role-commit and chat-refresh paths are exercised.
            typeof(CompanionWindowReveal).GetField("eligible",BindingFlags.Instance|BindingFlags.NonPublic)!
                .SetValue(reveal,new Func<bool>(()=>chat.CurrentPage=="chat"));
            typeof(CompanionWindowReveal).GetField("reducedMotion",BindingFlags.Instance|BindingFlags.NonPublic)!
                .SetValue(reveal,new Func<bool>(()=>false));
            TimeSpan now=TimeSpan.Zero;
            typeof(CompanionWindowReveal).GetField("now",BindingFlags.Instance|BindingFlags.NonPublic)!
                .SetValue(reveal,new Func<TimeSpan>(()=>now));
            var quick=new ChatWindow(owner,true);
            Check(quick.CompanionReveal==null,"quick chat has no role overlay");
            quick.Close();
            var target=owner.Skins.First(skin=>skin.OfficialCharacterId=="marfu"&&!skin.IsPlaceholder);
            var task=owner.SelectSkinAsync(target,CompanionSelectionOrigin.MainWindow);
            Until(()=>task.IsCompleted);
            task.GetAwaiter().GetResult();
            bool Overlay()=>chat.GetVisualDescendants().OfType<Control>().Any(element=>element.Name=="CompanionWindowOverlay");
            Until(Overlay);
            Check(owner.Skin.PickerKey==target.PickerKey,"the real role is committed before the animation");
            Dispatcher.UIThread.RunJobs();
            now+=TimeSpan.FromMilliseconds(340);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Check(Overlay(),"Avalonia chat refresh plays the committed zipper");
            if(capture)
            {
                using var frame=chat.CaptureRenderedFrame();
                var output=Path.Combine(Path.GetFullPath("work"),"companion-reveal-avalonia.png");
                frame!.Save(output,new PngBitmapEncoderOptions());
                Console.WriteLine("Avalonia capture "+output);
            }
            now+=TimeSpan.FromMilliseconds(500);
            Until(()=>!Overlay());
            Check(chat.CurrentPage=="chat"&&owner.Skin.PickerKey==target.PickerKey,"completed role switch leaves the live chat");
            chat.Close();
        }
        finally
        {
            owner.Pet.Close();owner.Dispose();Until(()=>owner.ImageServicesShutdown.IsCompleted);
        }
        return checks;
    }
}
