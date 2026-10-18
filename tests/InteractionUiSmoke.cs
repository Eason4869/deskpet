using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class InteractionUiSmoke
    {
        private static int passed;
        [DllImport("user32.dll", SetLastError=true)] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint {public int X,Y;}
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);

        [STAThread] public static int Main()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            PetWindow pet = null;
            Point originalCursor = DesktopInterop.Cursor();
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "interaction-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var settings = new PetSettings { FollowCursor = false, GreetingEnabled = false, HideOnFullscreen = false, Wander = false, AutoUpdate = false, QuietMode = true, StumbleEnabled=false };
                foreach (string key in settings.Hotkeys.Keys.ToArray()) settings.Hotkeys[key] = "";
                pet = new PetWindow(null, settings, new SettingsStore(Path.Combine(folder, "settings.json")), null);
                pet.Show(); Pump(.2);
                foreach (string id in new[] { "e-bao", "dafeiyu", "marfu", "xiaobei" })
                {
                    Check(pet.SelectSkin(pet.Catalog.Find(id)), "select " + id);
                    var supplemental = (InteractionAtlas)Field(pet, "interactionAtlas");
                    Check(supplemental != null || pet.Skin.Illustrated, id + " interaction artwork available");
                    foreach (string name in new[] { "lifted", "landing", "petting", "poked", "feeding", "happy", "sleeping", "special", "stumble", "special2" })
                    {
                        pet.PlayInteraction(name);
                        var interaction = State(pet);
                        Check(interaction.Active && interaction.Name == name, id + " starts " + name);
                        var clip=(MotionClip)Field(pet,"shownMotion");
                        var expected=clip!=null?clip.Frame((int)Field(pet,"shownSample")):supplemental.GetFrame(interaction.Row,interaction.Frame);
                        Check(Object.ReferenceEquals(((Image)pet.Content).Source,expected), id + " shows correct original interaction " + name);
                        Check(((Image)pet.Content).RenderTransform.Value.IsIdentity, id + " supplemental pose has no inherited alignment or mirror");
                        Capture(pet, Path.Combine(folder, id + "-" + name + ".png"));
                    }
                    pet.PlayAction("running-left", 0);
                    Check(!State(pet).Active, "ordinary action cancels interaction");
                    CheckAlignedHitTest(pet);
                    pet.PlayInteraction("sleeping"); Pump(.1);
                    pet.PreviewGreeting();
                    Check(State(pet).IsSleeping && !(bool)Field(pet, "walking"), "sleep suppresses greeting and wandering");
                    pet.SetPaused(true);
                    Check(!State(pet).Active, "pause clears interaction");
                    pet.PlayInteraction("petting");
                    Check(!pet.Paused && State(pet).Name == "petting", "explicit interaction resumes paused pet in DND");
                    pet.HidePet();
                    Check(!State(pet).Active, "hide clears interaction");
                    pet.RestorePet(); Pump(.05);
                }
                var interactionMenu = pet.ContextMenu.Items.OfType<MenuItem>().First(item => (string)item.Header == "互动");
                pet.ContextMenu.IsOpen=true;Pump(.04);pet.ContextMenu.IsOpen=false;
                Check(interactionMenu.Items.OfType<MenuItem>().Select(item => (string)item.Header).SequenceEqual(new[] { "摸摸头", "戳一戳", "喂零食", "小憩", pet.Skin.SpecialAction,AdditionalInteractions.SpecialName(pet.Skin) }), "interaction menu uses concrete action names");
                interactionMenu.Items.OfType<MenuItem>().First(item => (string)item.Header == "喂零食").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(State(pet).Name == "feeding", "menu feeds pet");
                pet.PlayInteraction("sleeping");
                pet.ContextMenu.IsOpen = true; Pump(.08);
                Check(interactionMenu.Items.OfType<MenuItem>().Any(item => (string)item.Header == "叫醒"), "sleep menu becomes wake command");
                pet.ContextMenu.IsOpen = false;
                pet.ReturnHome();
                Check(!State(pet).Active, "return home clears sleep");
                ConversationPriority(pet);
                NativeHover(pet);
                NativeGestures(pet);
                string legacy = Path.Combine(folder, "legacy.png");
                using (var input = typeof(SpriteAtlas).Assembly.GetManifestResourceStream("Dafeiyu.Atlas")) using (var output = File.Create(legacy)) input.CopyTo(output);
                Check(pet.SelectSkin(pet.Catalog.Import(legacy)), "select imported artwork");
                Check(Field(pet, "interactionAtlas") == null, "imported pet has no supplemental artwork");
                pet.PlayInteraction("lifted");
                Check(pet.Animation.State != "running-left" && pet.Animation.State != "running-right", "imported lifted fallback never runs");
                Check(((Image)pet.Content).RenderTransform.Value.IsIdentity, "imported artwork has no builtin offset");
                pet.PlayInteraction("sleeping");
                pet.SelectSkin(pet.Catalog.Find("e-bao"));
                Check(!State(pet).Active, "switching skin clears interaction");
                Console.WriteLine("Interaction UI smoke: " + passed + " passed; previews: " + folder);
                return 0;
            }
            catch (Exception error) { Console.WriteLine("FAIL interaction UI: " + error); return 1; }
            finally
            {
                mouse_event(4, 0, 0, 0, UIntPtr.Zero);
                SetCursorPos((int)originalCursor.X, (int)originalCursor.Y);
                if (pet != null) pet.Close();
                Pump(.2); app.Shutdown();
            }
        }

        private static void NativeHover(PetWindow pet)
        {
            var originalRandom=Field(pet,"random");typeof(PetWindow).GetField("random",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(pet,new JumpRandom());
            pet.SelectSkin(pet.Catalog.Find("e-bao"));pet.Settings.FollowCursor=true;pet.PlayAction("idle",0);
            var bounds=DesktopInterop.Bounds(new WindowInteropHelper(pet).Handle);var area=DesktopInterop.WorkArea(new WindowInteropHelper(pet).Handle);
            SetCursorPos((int)area.Left+5,(int)area.Top+5);Pump(.15);
            var point=OpaquePoint(pet,false,false);SetCursorPos((int)point.X,(int)point.Y);Pump(.15);
            Check(pet.Animation.State=="jumping","native hover starts a single jump");Pump(1.1);
            Check(pet.Animation.State=="idle" && !(bool)Field(pet,"gazeActive"),"stationary hover returns to idle without repeating or following gaze");
            SetCursorPos((int)area.Left+5,(int)area.Top+5);Pump(.15);
            Check((bool)Field(pet,"gazeActive"),"leaving pet resumes configured gaze following");
            pet.Settings.FollowCursor=false;pet.PlayAction("idle",0);point=OpaquePoint(pet,false,false);SetCursorPos((int)point.X,(int)point.Y);Pump(.15);
            Check(pet.Animation.State=="jumping","reentering triggers jump even with gaze disabled");Pump(.8);
            SetCursorPos((int)area.Left+5,(int)area.Top+5);Pump(.1);pet.PlayAction("idle",0);
            typeof(PetWindow).GetField("random",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(pet,originalRandom);
        }
        private sealed class JumpRandom:Random{public override int Next(int maximum){return 0;}}
        private static void NativeGestures(PetWindow pet)
        {
            pet.PlayAction("idle", 0); Pump(.1);
            Point head = OpaquePoint(pet, true);
            if(!SetCursorPos((int)head.X,(int)head.Y))throw new InvalidOperationException("Native mouse fixture cannot access the desktop (Win32 " + Marshal.GetLastWin32Error() + "). Run this test outside the restricted sandbox.");
            Pump(.05);
            Check(WindowFromPoint(new NativePoint {X=(int)head.X,Y=(int)head.Y}) == new WindowInteropHelper(pet).Handle, "native input target belongs to this pet fixture");
            Click(head); Pump(System.Windows.Forms.SystemInformation.DoubleClickTime / 1000d + .12);
            Check(State(pet).Name == "petting", "native head click pets after double-click grace period");
            pet.PlayAction("idle", 0); Pump(.1);
            Point body = OpaquePoint(pet, false);
            Click(body); Pump(System.Windows.Forms.SystemInformation.DoubleClickTime / 1000d + .12);
            Check(State(pet).Name == "poked", "native body click pokes");
            pet.PlayAction("idle", 0); Pump(.1);
            head = OpaquePoint(pet, true);
            SetCursorPos((int)head.X, (int)head.Y); Pump(.05);
            mouse_event(2, 0, 0, 0, UIntPtr.Zero); Pump(.05);
            SetCursorPos((int)head.X - 50, (int)head.Y - 20); Pump(.12);
            Check(State(pet).Name == "lifted" && (bool)Field(pet, "moved"), "native drag enters lifted after threshold");
            mouse_event(4, 0, 0, 0, UIntPtr.Zero); Pump(.06);
            Check(State(pet).Name == "landing", "native drag release lands");
            Pump(1.15); Check(!State(pet).Active, "eight-pose landing finishes without a deferred click");
            pet.PlayAction("idle", 0); Pump(.1);
            head = OpaquePoint(pet, true);
            SetCursorPos((int)head.X, (int)head.Y); Pump(.05);
            mouse_event(2, 0, 0, 0, UIntPtr.Zero); Pump(.05);
            SetCursorPos((int)head.X - 35, (int)head.Y); Pump(.1);
            ((Image)pet.Content).ReleaseMouseCapture(); Pump(.03);
            Check(!State(pet).Active && !(bool)Field(pet, "dragging"), "capture loss cancels lifted state");
            mouse_event(4, 0, 0, 0, UIntPtr.Zero); Pump(.1);
            pet.SetPaused(true); Pump(System.Windows.Forms.SystemInformation.DoubleClickTime / 1000d + .1); head = OpaquePoint(pet, true);
            // Queue a genuine double-click without unrelated dispatcher work stretching
            // the gap beyond the user's configured Windows double-click interval.
            SetCursorPos((int)head.X,(int)head.Y);Pump(.05);
            mouse_event(2,0,0,0,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);
            mouse_event(2,0,0,0,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);Pump(.2);
            var quick = (QuickChatWindow)Field(pet, "quick");
            Check(quick != null && quick.IsVisible, "native double-click opens quick chat while paused");
            Pump(System.Windows.Forms.SystemInformation.DoubleClickTime / 1000d + .1);
            Check(!State(pet).Active, "double-click cancels pending single-click interaction");
            quick.Hide();
            int frozenFrame = pet.Animation.Frame;
            ImageSource frozenSource = ((Image)pet.Content).Source;
            Rect pausedBounds = DesktopInterop.Bounds(new WindowInteropHelper(pet).Handle);
            head = OpaquePoint(pet, true);
            SetCursorPos((int)head.X, (int)head.Y); Pump(.05);
            mouse_event(2, 0, 0, 0, UIntPtr.Zero); Pump(.05);
            SetCursorPos((int)head.X - 35, (int)head.Y - 10); Pump(.1);
            Check(pet.Paused && !State(pet).Active && pet.Animation.Frame == frozenFrame && Object.ReferenceEquals(frozenSource, ((Image)pet.Content).Source), "paused native drag preserves frozen artwork");
            Check(DesktopInterop.Bounds(new WindowInteropHelper(pet).Handle).Left != pausedBounds.Left, "paused native drag still moves the pet");
            mouse_event(4, 0, 0, 0, UIntPtr.Zero); Pump(.1);
            Check(pet.Paused && !State(pet).Active, "paused drag release does not start landing or resume animation");
            pet.SetPaused(false);
            pet.PlayAction("idle", 0); Pump(.1); head = OpaquePoint(pet, true);
            SetCursorPos((int)head.X, (int)head.Y); Pump(.05);
            mouse_event(2, 0, 0, 0, UIntPtr.Zero); Pump(.05);
            SetCursorPos((int)head.X - 35, (int)head.Y); Pump(.1);
            pet.Settings.LockPosition = true; pet.ApplySettings();
            Check(!State(pet).Active && !(bool)Field(pet, "dragging"), "locking position during drag cancels lifted state");
            mouse_event(4, 0, 0, 0, UIntPtr.Zero); Pump(.1);
            pet.Settings.LockPosition = false; pet.ApplySettings();
        }

        private static void ConversationPriority(PetWindow pet)
        {
            var session = new ConversationSession(new WaitingApi(), new SilentSpeech(), delegate { throw new InvalidOperationException("No recording in this fixture"); });
            session.SelectPersona(pet.Skin.Id);
            session.StateChanged += (Action)Delegate.CreateDelegate(typeof(Action), pet, typeof(PetWindow).GetMethod("ConversationChanged", BindingFlags.NonPublic | BindingFlags.Instance));
            typeof(PetWindow).GetField("conversation", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(pet, session);
            pet.Settings.Text = new ConnectionSettings { BaseUrl = "http://127.0.0.1:1", Model = "fixture", StreamReplies = false };
            pet.Settings.ReadReplies = false;
            pet.PlayInteraction("sleeping");
            Task request = session.SendTextAsync("busy fixture", pet.Settings, pet.Skin.Name);
            Check(session.IsBusy && !State(pet).Active, "starting real conversation state cancels sleep");
            pet.PlayInteraction("petting");
            Check(State(pet).Active && State(pet).Name == "petting", "busy conversation preserves explicit interaction");
            Task stop = session.StopAsync();
            for (int i = 0; i < 100 && (!stop.IsCompleted || !request.IsCompleted); i++) Pump(.02);
            Check(stop.IsCompleted && request.IsCompleted && !session.IsBusy, "conversation fixture stops cleanly");
            pet.PlayInteraction("sleeping");
            typeof(PetWindow).GetMethod("ConversationChanged", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(pet, null);
            Check(State(pet).IsSleeping, "idle conversation notifications preserve explicit sleep");
            pet.PlayAction("idle", 0);
        }

        private sealed class WaitingApi : IConversationApi
        {
            public async Task<string> ChatAsync(ConnectionSettings cfg, IList<ChatMessage> messages, CancellationToken cancel) { await Task.Delay(-1, cancel); return ""; }
            public Task<string> TranscribeAsync(ConnectionSettings cfg, byte[] wav, CancellationToken cancel) { throw new InvalidOperationException(); }
            public Task<byte[]> SpeechAsync(ConnectionSettings cfg, string voice, string text, CancellationToken cancel) { throw new InvalidOperationException(); }
            public void Dispose() { }
        }
        private sealed class SilentSpeech : IReplySpeech
        {
            public Task SpeakAsync(string reply, PetSettings settings, CancellationToken cancel) { return Task.FromResult(0); }
            public void SetVolume(int volume) { } public void Stop() { } public void Dispose() { }
        }

        private static void CheckAlignedHitTest(PetWindow pet)
        {
            var atlas = (SpriteAtlas)Field(pet, "atlas");
            int row = (int)Field(pet, "shownRow"), col = (int)Field(pet, "shownCol");
            bool mirrored = (bool)Field(pet, "shownMirrored");
            Point offset = (Point)Field(pet,"shownOffset");
            var motion=(MotionClip)Field(pet,"shownMotion");int sample=(int)Field(pet,"shownSample");
            int width=motion!=null?motion.Width:atlas.CellWidth,height=motion!=null?motion.Height:atlas.CellHeight;
            IntPtr handle = new WindowInteropHelper(pet).Handle; Rect bounds = DesktopInterop.Bounds(handle);
            bool checkedBody = false, checkedBackground = false;
            for (int y = 0; y < (int)bounds.Height; y += 3) for (int x = 0; x < (int)bounds.Width; x += 3)
            {
                int sx = (int)Math.Floor(x / bounds.Width * width - offset.X*width/480), sy = (int)Math.Floor(y / bounds.Height * height - offset.Y*height/520);
                if (mirrored) sx = width - 1 - sx;
                bool expected = (motion!=null?motion.AlphaAt(sample,sx,sy):atlas.AlphaAt(row, col, sx, sy)) >= 12;
                if (expected ? checkedBody : checkedBackground) continue;
                int px = (int)bounds.Left + x, py = (int)bounds.Top + y;
                bool transparent = SendMessage(handle, 0x84, IntPtr.Zero, new IntPtr((py << 16) | (px & 0xffff))).ToInt32() == -1;
                Check(transparent != expected, "aligned native hit testing " + pet.Skin.Id + (expected ? " body" : " margin"));
                if (expected) checkedBody = true; else checkedBackground = true;
            }
            Check(checkedBody && checkedBackground, "aligned hit test samples body and margin");
        }

        private static Point OpaquePoint(PetWindow pet, bool head,bool primeHover=true)
        {
            var motion=(MotionClip)Field(pet,"shownMotion");int sample=(int)Field(pet,"shownSample");
            if(motion!=null) {
                Rect b=DesktopInterop.Bounds(new WindowInteropHelper(pet).Handle);
                for(int y=head?60:165;y<(head?130:225);y+=3)for(int x=85;x<155;x+=3) {
                    bool solid=true;for(int dy=-5;dy<=5 && solid;dy+=2)for(int dx=-5;dx<=5;dx+=2)if(motion.AlphaAt(sample,(x+dx)*motion.Width/240,(y+dy)*motion.Height/260)<240)solid=false;
                    if(solid){var point=new Point(b.Left+x/240d*b.Width,b.Top+y/260d*b.Height);if(WindowFromPoint(new NativePoint{X=(int)point.X,Y=(int)point.Y})!=new WindowInteropHelper(pet).Handle)continue;if(primeHover)((HoverInteraction)Field(pet,"hover")).Update(true,true,false);return point;}
                }
            }
            var atlas = (SpriteAtlas)Field(pet, "atlas");
            int row = (int)Field(pet, "shownRow"), col = (int)Field(pet, "shownCol");
            Rect bounds = DesktopInterop.Bounds(new WindowInteropHelper(pet).Handle);
            for (int y = head ? 120 : 330; y < (head ? 260 : 450); y += 3) for (int x = 170; x < 310; x += 3)
            {
                bool solid = true;
                for(int dy = -8; dy <= 8 && solid; dy += 4) for(int dx = -8; dx <= 8; dx += 4) if(atlas.AlphaAt(row, col, x + dx, y + dy) < 240) {solid = false; break;}
                if (solid) {var offset=(Point)Field(pet,"shownOffset");bool mirrored=(bool)Field(pet,"shownMirrored");var point=new Point(bounds.Left + ((mirrored?479-x:x)+offset.X) / 480d * bounds.Width, bounds.Top + (y+offset.Y) / 520d * bounds.Height);if(WindowFromPoint(new NativePoint{X=(int)point.X,Y=(int)point.Y})!=new WindowInteropHelper(pet).Handle)continue;if(primeHover)((HoverInteraction)Field(pet,"hover")).Update(true,true,false);return point;}
            }
            throw new Exception("No opaque gesture target");
        }
        private static void Click(Point point) { SetCursorPos((int)point.X, (int)point.Y); Pump(.03); mouse_event(2, 0, 0, 0, UIntPtr.Zero); Pump(.025); mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
        private static InteractionAnimation State(PetWindow pet) { return (InteractionAnimation)Field(pet, "interaction"); }
        private static object Field(PetWindow pet, string name) { return typeof(PetWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(pet); }
        private static void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        private static void Capture(PetWindow pet, string path)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(pet.Width), (int)Math.Ceiling(pet.Height), 96, 96, PixelFormats.Pbgra32); bitmap.Render(pet);
            using (var stream = File.Create(path)) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(stream); }
        }
        private static void Pump(double seconds) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) }; timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
    }
}
