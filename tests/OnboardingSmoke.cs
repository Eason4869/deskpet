using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class OnboardingSmoke
    {
        private static int passed;
        [STAThread] public static int Main(string[] args)
        {
            if(args.Length>0 && (args.Length!=1 || args[0]!="--simulated")){Console.Error.WriteLine("Usage: OnboardingSmoke.exe [--simulated]");return 2;}
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            PetWindow pet = null;
            Point originalCursor=DesktopInterop.Cursor();
            bool simulated=args.Contains("--simulated") || Environment.GetEnvironmentVariable("DESKPET_TEST_SIMULATED_GESTURES")=="1";if(simulated)Console.WriteLine("INFO gesture completion uses explicit fixtures; native mouse input is NOT verified by this run.");
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "onboarding-smoke-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                Check(new PetSettings().OnboardingState == "new", "fresh settings request first-run guide");
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}")))
                {
                    var legacy = (PetSettings)new DataContractJsonSerializer(typeof(PetSettings)).ReadObject(stream); legacy.Normalize();
                    Check(legacy.OnboardingState == "completed", "legacy settings never auto-open a new guide");
                }
                var settings = new PetSettings { AutoUpdate = false, FollowCursor = false, Wander = false, GreetingEnabled = false, HideOnFullscreen = false };
                foreach (string key in settings.Hotkeys.Keys.ToArray()) settings.Hotkeys[key] = "";
                pet = new PetWindow(null, settings, new SettingsStore(Path.Combine(folder, "settings.json")), null);
                pet.ContextMenu.Opened+=delegate{Console.WriteLine("NATIVE menu OPEN "+DateTime.UtcNow.ToString("HH:mm:ss.fff"));};pet.ContextMenu.Closed+=delegate{Console.WriteLine("NATIVE menu CLOSED "+DateTime.UtcNow.ToString("HH:mm:ss.fff"));};
                pet.AddHandler(UIElement.PreviewMouseRightButtonDownEvent,new System.Windows.Input.MouseButtonEventHandler((s,e)=>Console.WriteLine("NATIVE right DOWN "+DateTime.UtcNow.ToString("HH:mm:ss.fff")+" hit="+e.OriginalSource)),true);
                pet.AddHandler(UIElement.PreviewMouseRightButtonUpEvent,new System.Windows.Input.MouseButtonEventHandler((s,e)=>Console.WriteLine("NATIVE right UP "+DateTime.UtcNow.ToString("HH:mm:ss.fff")+" hit="+e.OriginalSource)),true);
                pet.Show(); Pump(.15);
                Check(Field(pet, "guide") == null, "constructing a pet does not auto-popup outside Program startup");
                pet.ShowOnboarding(); Pump(.25);
                var guide = (OnboardingWindow)Field(pet, "guide");
                var preferences = (SettingsPage)Field(pet, "preferences");
                Check(guide.IsVisible && guide.Owner == preferences.HostWindow && !guide.ShowInTaskbar, "guide floats over the real settings host as owned window");
                Check(guide.CurrentLessonId=="welcome" && String.IsNullOrEmpty(guide.TargetName) && !guide.HighlightVisible,"introduction precedes configuration without highlighting a target");
                Press(guide,"GuideNext");
                Check(guide.CurrentLessonId=="network.intro","GitHub acceleration is the first configuration step");Go(guide,"network.save");Press(guide,"GuideNext");
                Check(guide.CurrentLessonId=="pet.select" && guide.Step == 0 && guide.TargetName == "SkinPicker" && guide.HighlightVisible, "starting the guide outlines the actual skin picker");
                CheckTargetUncovered(guide,preferences);
                Capture(preferences,Path.Combine(folder,"guide-partner-settings.png"));Capture(guide,Path.Combine(folder,"guide-partner-card.png"));
                Check(!preferences.FindControl("SettingsNavigation").IsEnabled && !((UIElement)pet.Content).IsEnabled,"only current settings target remains enabled");
                Check(!pet.CanApplyUpdate(true),"guide protects setup from automatic exit");
                var skins=(ComboBox)preferences.FindControl("SkinPicker");skins.SelectedItem=pet.Catalog.Find("dafeiyu");Check(pet.Skin.Id=="dafeiyu","actual partner selection works while navigation is locked");
                Go(guide,"persona.edit");var personaDeadline=DateTime.UtcNow.AddSeconds(2);while(guide.Step!=1 && DateTime.UtcNow<personaDeadline)Pump(.05);Check(guide.Step==1,"persona has its own detailed step");
                var prompt=(TextBox)preferences.FindControl("PersonaPrompt");string originalPrompt=prompt.Text;prompt.Clear();Pump(.2);Check(!Find<Button>(guide,"GuideNext").IsEnabled,"empty persona disables next");prompt.Text=originalPrompt;Press(guide,"GuideNext");Press(guide,"GuideNext");
                Check(guide.Step==2,"behavior has its own step");((Slider)preferences.FindControl("SizeSlider")).Value=1.1;Go(guide,"gesture.drag");
                Check(guide.Step==3 && !Find<Button>(guide,"GuideNext").IsEnabled && ((UIElement)pet.Content).IsEnabled,"drag step unlocks pet but requires actual movement");
                Point point=new Point();
                if(simulated)pet.NotifyGuideInteraction("drag");else{PlacePetUncovered(pet);point=MoveToPet(pet);mouse_event(2,0,0,0,UIntPtr.Zero);Pump(.06);MoveCursor(new Point(point.X-70,point.Y-40));Pump(.15);mouse_event(4,0,0,0,UIntPtr.Zero);Pump(.2);}
                Check(Find<Button>(guide,"GuideNext").IsEnabled,"drag completion unlocks drag lesson");Press(guide,"GuideNext");
                Check(guide.Step==4 && !Find<Button>(guide,"GuideNext").IsEnabled,"right menu lesson starts incomplete");if(simulated){pet.ContextMenu.IsOpen=true;Pump(.2);}else{WaitForInteraction(pet);point=MoveToPet(pet);NativeClick(pet,System.Windows.Input.MouseButton.Right,8,16);}
                Pump(.6);
                Check(pet.ContextMenu.IsOpen && pet.ContextMenu.IsEnabled,"guide menu stays open long enough to inspect");
                Check(Find<Button>(guide,"GuideNext").IsEnabled,"opening real menu completes menu lesson");pet.ContextMenu.IsOpen=false;Press(guide,"GuideNext");
                Check(guide.Step==5 && !Find<Button>(guide,"GuideNext").IsEnabled,"wheel lesson requires selecting an action");if(simulated){pet.ShowActionPalette();Pump(.2);}else{point=MoveToPet(pet);NativeClick(pet,System.Windows.Input.MouseButton.Middle,32,64);}
                var wheel=app.Windows.OfType<ActionPaletteWindow>().Single();Check(wheel.IsVisible && !Find<Button>(guide,"GuideNext").IsEnabled,"opening wheel alone cannot complete lesson");Find<Button>(wheel,"PaletteInteraction_feeding").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(.2);Check(Find<Button>(guide,"GuideNext").IsEnabled,"choosing wheel action completes lesson");Press(guide,"GuideNext");
                Check(guide.Step==6 && !Find<Button>(guide,"GuideNext").IsEnabled,"click lesson starts incomplete");if(simulated)pet.NotifyGuideInteraction("click");else{WaitForInteraction(pet);point=MoveToPet(pet);NativeClick(pet,System.Windows.Input.MouseButton.Left,2,4);Pump(System.Windows.Forms.SystemInformation.DoubleClickTime/1000d+.3);}Check(Find<Button>(guide,"GuideNext").IsEnabled,"click completion unlocks interaction lesson");Press(guide,"GuideNext");
                Check(guide.Step==7 && guide.TargetName=="GuidePet" && !Find<Button>(guide,"GuideNext").IsEnabled,"double click lesson follows single click and requires practice");
                if(simulated)pet.ShowQuickChat();else
                {
                    WaitForInteraction(pet);point=MoveToPet(pet);
                    NativeClick(pet,System.Windows.Input.MouseButton.Left,2,4);Pump(System.Windows.Forms.SystemInformation.DoubleClickTime/1000d+.2);
                    Check(!Find<Button>(guide,"GuideNext").IsEnabled,"single click cannot complete double click lesson");
                    point=MoveToPet(pet);
                    mouse_event(2,0,0,0,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);Pump(.06);mouse_event(2,0,0,0,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);
                }
                var quickWait=DateTime.UtcNow.AddSeconds(3);while(Field(pet,"quick")==null && DateTime.UtcNow<quickWait)Pump(.06);Pump(.15);var quick=(QuickChatWindow)Field(pet,"quick");
                Check(quick!=null && quick.IsVisible,"double click opens actual quick chat during guide");
                if(simulated)pet.NotifyGuideInteraction("double-click");
                Check(Find<Button>(guide,"GuideNext").IsEnabled,"opening quick chat by double click completes lesson");
                Check(!Find<Button>(quick,"SendQuickMessage").IsEnabled && !Find<Button>(quick,"VoiceRecord").IsEnabled && Find<Button>(quick,"CloseQuickChat").IsEnabled,"quick chat preview allows closing but blocks send and recording");
                Capture(quick,Path.Combine(folder,"guide-quick-chat.png"));Capture(guide,Path.Combine(folder,"guide-double-click.png"));Press(guide,"GuideNext");
                Check(!quick.IsVisible,"leaving quick chat lesson closes its preview");
                Go(guide,"text.save");Check(!Find<Button>(guide,"GuideNext").IsEnabled,"missing text service blocks saving but not introductory lessons");
                Choose(preferences,"AddTextProvider","custom");var url=(TextBox)preferences.FindControl("TextServiceUrl");url.Text="bad address";Pump(.2);Check(!Find<Button>(guide,"GuideNext").IsEnabled,"invalid address remains blocked");
                preferences.HostWindow.Width=700;preferences.HostWindow.Height=430;Pump(.2);CheckTargetUncovered(guide,preferences);Capture(preferences,Path.Combine(folder,"guide-text-minimum-settings.png"));Capture(guide,Path.Combine(folder,"guide-text-minimum-card.png"));preferences.HostWindow.Width=850;preferences.HostWindow.Height=790;Pump(.2);
                Press(guide,"GuideSkipChapter");Check(guide.Step==9&&guide.IsVisible&&preferences.HasUnsavedEdits,"skipping a chapter retains its settings draft");
                Go(guide,"asr.save");Check(!Find<Button>(guide,"GuideNext").IsEnabled,"ASR requires configuration before saving");Press(guide,"GuideSkipChapter");Check(guide.Step==10,"ASR and TTS remain separate chapters");
                ((CheckBox)preferences.FindControl("UseCloudVoice")).IsChecked=false;Go(guide,"speech.save");Press(guide,"GuideNext");Check(guide.CurrentLessonId=="speech.test"&&!pet.Settings.UseCloudVoice,"system TTS saves independently without auto-testing");
                Press(guide,"GuideSkipChapter");Go(guide,"live.save");Check(!Find<Button>(guide,"GuideNext").IsEnabled,"Live requires complete setup before saving");Choose(preferences,"AddLiveProvider","custom");((TextBox)preferences.FindControl("LiveServiceUrl")).Text="ws://127.0.0.1/live";((TextBox)preferences.FindControl("LiveServiceModel")).Text="guide-model";((PasswordBox)preferences.FindControl("LiveServiceKey")).Password="fixture-key";((ComboBox)preferences.FindControl("LiveVoice")).Text="Kore";Pump(.2);Press(guide,"GuideNext");
                Check(guide.CurrentLessonId=="live.test"&&pet.Settings.Live.Model=="guide-model"&&!pet.LiveActive,"Live save reaches an explicit test without connecting");Press(guide,"GuideSkipChapter");
                Go(guide,"greeting.interval");((TextBox)preferences.FindControl("GreetingMinMinutes")).Text="100";((TextBox)preferences.FindControl("GreetingMaxMinutes")).Text="10";Pump(.2);Check(!Find<Button>(guide,"GuideNext").IsEnabled,"invalid greeting interval disables save");((TextBox)preferences.FindControl("GreetingMaxMinutes")).Text="120";Press(guide,"GuideNext");
                Go(guide,"shortcut.quick");((TextBox)preferences.FindControl("Hotkeyquick")).Text="not-a-shortcut";Pump(.2);Check(!Find<Button>(guide,"GuideNext").IsEnabled,"invalid shortcut disables save");((TextBox)preferences.FindControl("Hotkeyquick")).Clear();Press(guide,"GuideNext");
                Go(guide,"chat.open");Check(app.Windows.OfType<ChatWindow>().Any(w=>w.IsVisible),"conversation guidance opens the actual full window without sending");Capture(guide,Path.Combine(folder,"guide-conversation.png"));
                Go(guide,"work.access");var workPanel=(WorkSettingsPanel)preferences.FindControl("WorkSettingsPanel");Check(workPanel.IsEnabled&&preferences.FindControl("AgentApprovalMode").IsEnabled&&!preferences.FindControl("SettingsNavigation").IsEnabled,"approval lesson unlocks the actual three-mode setting while locking unrelated navigation");
                Go(guide,"work.save");((TextBox)preferences.FindControl("AgentContextTokenLimit")).Text="0";Press(guide,"GuideNext");Check(guide.CurrentLessonId=="work.save","invalid work settings cannot advance or silently save");
                ((TextBox)preferences.FindControl("AgentContextTokenLimit")).Text="16384";((CheckBox)preferences.FindControl("AgentPlanMode")).IsChecked=true;Press(guide,"GuideNext");Check(guide.CurrentLessonId=="work.prompt"&&pet.Settings.AgentContextTokenLimit==16384&&pet.Settings.AgentPlanMode,"work settings save before the dedicated prompt lesson");
                Go(guide,"network.save");var network=(Expander)preferences.FindControl("UpdateNetwork");Check(network.IsExpanded&&network.IsEnabled&&!preferences.FindControl("CheckUpdates").IsEnabled&&!preferences.FindControl("UninstallApplication").IsEnabled,"network lesson blocks installation and uninstall");
                ((RadioButton)preferences.FindControl("UpdateMirror")).IsChecked=true;((RadioButton)preferences.FindControl("CustomUpdateMirror")).IsChecked=true;((TextBox)preferences.FindControl("UpdateMirrorAddress")).Text="invalid";Pump(.2);Check(!Find<Button>(guide,"GuideNext").IsEnabled,"invalid network address blocks saving");
                var about=(AboutPanel)Field(preferences,"about");about.MirrorProbe=(address,token)=>System.Threading.Tasks.Task.FromResult(0);var probe=about.TestMirrorsAsync();while(!probe.IsCompleted)Pump(.05);probe.GetAwaiter().GetResult();((RadioButton)preferences.FindControl("BuiltinUpdateMirror1")).IsChecked=true;Press(guide,"GuideNext");Check(guide.CurrentLessonId=="pet.select","network save precedes partner selection");Go(guide,"finish.summary");Press(guide,"GuideNext");
                Check(guide.CurrentLessonId=="finish.congratulations","summary enters congratulations before completion");Press(guide,"GuideNext");Check(settings.OnboardingState=="completed"&&Field(pet,"guide")==null&&preferences.FindControl("SettingsNavigation").IsEnabled&&((UIElement)pet.Content).IsEnabled,"completion restores settings and pet interaction");Check(!network.IsExpanded&&!pet.Updates.IsBusy&&!pet.LiveActive&&!pet.Conversation.IsBusy&&pet.Conversation.History.Messages.Count==0,"guide never automatically sends, records, calls or updates");
                pet.ShowOnboarding();Pump(.2);guide=(OnboardingWindow)Field(pet,"guide");Check(guide.CurrentLessonId=="welcome","completed guide can restart");Press(guide,"GuideLater");Check(guide.CurrentLessonId=="network.intro"&&pet.Store.Load().OnboardingLessonId=="network.intro","skipping a small step persists a stable resume ID");guide.Close();Pump(.2);Check(settings.OnboardingState=="deferred"&&((UIElement)pet.Content).IsEnabled,"closing defers progress and releases all locks");
                Console.WriteLine("Onboarding smoke: " + passed + " passed; screenshot: " + folder);
                return 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL onboarding: " + error); return 1; }
            finally { mouse_event(4|16|64,0,0,0,UIntPtr.Zero);if(!simulated)SetCursorPos((int)originalCursor.X,(int)originalCursor.Y);if (pet != null) pet.Close(); Pump(.2); app.Shutdown(); }
        }
        static void Choose(SettingsPage settings,string add,string brand){Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate{var picker=Application.Current.Windows.OfType<ProviderPickerWindow>().Single();Find<Button>(picker,"ChooseProvider_"+brand).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}));Find<Button>(settings,add).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(.2);}
        static void Go(OnboardingWindow guide,string id){typeof(OnboardingWindow).GetField("index",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(guide,Array.FindIndex(OnboardingLessons.All,l=>l.Id==id));typeof(OnboardingWindow).GetMethod("Navigate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(guide,null);Pump(.2);}
        [System.Runtime.InteropServices.DllImport("user32.dll",SetLastError=true)]static extern bool SetCursorPos(int x,int y);
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]struct NativePoint{public int X,Y;}
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr WindowFromPoint(NativePoint point);
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr w,IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
        static void MoveCursor(Point point){if(!SetCursorPos((int)Math.Round(point.X),(int)Math.Round(point.Y)))throw new InvalidOperationException("SetCursorPos failed; Win32 "+System.Runtime.InteropServices.Marshal.GetLastWin32Error());}
        static void WaitForInteraction(PetWindow pet)
        {var deadline=DateTime.UtcNow.AddSeconds(5);while(((InteractionAnimation)Field(pet,"interaction")).Active && DateTime.UtcNow<deadline)Pump(.04);if(((InteractionAnimation)Field(pet,"interaction")).Active)throw new Exception("Previous interaction did not finish before native gesture");}
        static Point MoveToPet(PetWindow pet)
        {
            PlacePetUncovered(pet);

            Point point=OpaquePoint(pet);MoveCursor(point);Pump(.03);
            if(!HitsPet(pet,point))throw new Exception("Native gesture aborted: opaque hit target is not the fixture");
            uint foregroundProcess;GetWindowThreadProcessId(GetForegroundWindow(),out foregroundProcess);
            Console.WriteLine("NATIVE verified current frame hit at "+point+" foregroundPid="+foregroundProcess+" fixturePid="+System.Diagnostics.Process.GetCurrentProcess().Id+" buttons="+System.Windows.Input.Mouse.LeftButton+"/"+System.Windows.Input.Mouse.RightButton);return point;
        }
        static void NativeClick(PetWindow pet,System.Windows.Input.MouseButton button,uint down,uint up,double settle=.25)
        {
            bool received=false;
            System.Windows.Input.MouseButtonEventHandler observe=(sender,args)=>{if(args.ChangedButton==button)received=true;};
            pet.AddHandler(UIElement.PreviewMouseDownEvent,observe,true);
            try {
                mouse_event(down,0,0,0,UIntPtr.Zero);
                var deadline=DateTime.UtcNow.AddSeconds(2);
                while(!received && DateTime.UtcNow<deadline)Pump(.02);
            } finally {
                mouse_event(up,0,0,0,UIntPtr.Zero);
                pet.RemoveHandler(UIElement.PreviewMouseDownEvent,observe);
            }
            Check(received,"native "+button+" down reaches fixture before release");if(settle>0)Pump(settle);
        }
        static void PlacePetUncovered(PetWindow pet)
        {
            IntPtr hwnd=new WindowInteropHelper(pet).Handle;Rect area=DesktopInterop.WorkArea(hwnd),bounds=DesktopInterop.Bounds(hwnd);
            var blockers=Application.Current.Windows.OfType<Window>().Where(w=>w!=pet && w.IsVisible && w.WindowState!=WindowState.Minimized).Select(w=>DesktopInterop.Bounds(new WindowInteropHelper(w).Handle)).ToArray();
            for(double y=area.Top+100;y+bounds.Height<area.Bottom-50;y+=bounds.Height+35)for(double x=area.Left+100;x+bounds.Width<area.Right-50;x+=bounds.Width+35)
            {
                var candidate=new Rect(x,y,bounds.Width,bounds.Height);var safe=candidate;safe.Inflate(45,45);if(blockers.Any(b=>b.IntersectsWith(safe)))continue;
                DesktopInterop.Move(hwnd,candidate.TopLeft);Pump(.12);Console.WriteLine("NATIVE unobstructed pet bounds "+DesktopInterop.Bounds(hwnd));return;
            }
            throw new Exception("No unobstructed native pet fixture position outside settings and guide");
        }
        static bool HitsPet(PetWindow pet,Point point)
        {
            int x=(int)Math.Round(point.X),y=(int)Math.Round(point.Y);IntPtr hwnd=new WindowInteropHelper(pet).Handle;
            return WindowFromPoint(new NativePoint{X=x,Y=y})==hwnd && SendMessage(hwnd,0x84,IntPtr.Zero,new IntPtr((y<<16)|(x&0xffff))).ToInt32()!=-1;
        }
        static Point OpaquePoint(PetWindow pet)
        {
            // Sample the bitmap actually displayed, including new illustrated
            // MotionClip frames; legacy atlas coordinates may now be transparent.
            var image=(Image)pet.Content;var bitmap=(BitmapSource)image.Source;int width=bitmap.PixelWidth,height=bitmap.PixelHeight;
            var converted=new FormatConvertedBitmap(bitmap,PixelFormats.Pbgra32,null,0);var pixels=new byte[width*height*4];converted.CopyPixels(pixels,width*4,0);
            for(int y=height/3;y<height*2/3;y+=3)for(int x=width/3;x<width*2/3;x+=3)
            {
                bool solid=true;for(int dy=-12;dy<=12 && solid;dy+=3)for(int dx=-12;dx<=12;dx+=3)if(pixels[((y+dy)*width+x+dx)*4+3]<240){solid=false;break;}
                if(solid){Point point=image.PointToScreen(new Point((x+.5)/width*image.ActualWidth,(y+.5)/height*image.ActualHeight));if(HitsPet(pet,point))return point;}
            }
            throw new Exception("No opaque target");
        }
        private static object Field(object target, string name) { return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target); }
        private static void CheckTargetUncovered(OnboardingWindow guide,SettingsPage settings)
        {
            var target=settings.FindControl(guide.TargetName);Point a=target.PointToScreen(new Point()),b=target.PointToScreen(new Point(target.ActualWidth,target.ActualHeight));
            Rect targetBounds=new Rect(a,b),guideBounds=DesktopInterop.Bounds(new WindowInteropHelper(guide).Handle);
            Check(Rect.Intersect(targetBounds,guideBounds).IsEmpty,"floating guide leaves actual target uncovered");
            Check(DesktopInterop.WorkArea(new WindowInteropHelper(settings.HostWindow).Handle).Contains(guideBounds),"floating guide stays inside current monitor work area");
        }
        private static T Find<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            var page=root as SettingsPage;if(page!=null)return page.FindControl(name) as T;
            var match = root as T; if (match != null && match.Name == name) return match;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = Find<T>(VisualTreeHelper.GetChild(root, i), name); if (child != null) return child; }
            return null;
        }
        private static void Press(Window window, string name)
        {
            Pump(.15);
            var button = Find<Button>(window, name);
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (button != null && !button.IsEnabled && DateTime.UtcNow < deadline) Pump(.02);
            Check(button != null && button.IsEnabled, "guide action available " + name);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); passed++; Console.WriteLine("PASS " + message); }
        private static void Capture(FrameworkElement window, string path) { window.UpdateLayout();var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(window); using (var stream = File.Create(path)) { var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); png.Save(stream); } }
        private static void Pump(double seconds) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) }; timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
    }
}
