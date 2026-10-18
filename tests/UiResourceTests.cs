using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class UiResourceTests
    {
        static int checks;
        static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Check(bool value, string text) { if (!value) throw new Exception(text); checks++; Console.WriteLine("PASS " + text); }
        static void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        static void Receive(MessageUpdatePump pump, string id, string text)
        { typeof(MessageUpdatePump).GetMethod("Receive", Hidden).Invoke(pump, new object[] { Snapshot(id, text) }); }
        static ChatMessage Snapshot(string id, string text) { var message = new ChatMessage("assistant", text); typeof(ChatMessage).GetProperty("Id").SetValue(message, id, null); return message; }
        static DispatcherTimer Clock(MessageUpdatePump pump) { return (DispatcherTimer)typeof(MessageUpdatePump).GetField("timer", Hidden).GetValue(pump); }
        [STAThread]
        public static int Main()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; PetWindow pet = null; Window host = null;
            try
            {
                var cfg = new PetSettings { AutoUpdate = false, GreetingEnabled = false, ReadReplies = false, ShowPredictedMessages = false };
                foreach (var key in cfg.Hotkeys.Keys.ToArray()) cfg.Hotkeys[key] = "";
                pet = new PetWindow(null, cfg, new SettingsStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resource-" + Guid.NewGuid().ToString("N"), "settings.json")), null);
                var received = new List<ChatMessage>(); var pump = new MessageUpdatePump(pet.Conversation, received.Add);
                Check(!Clock(pump).IsEnabled, "message pump has no idle polling timer");
                Task.Run(delegate { for (int i = 0; i < 100; i++) Receive(pump, "one", i.ToString()); Receive(pump, "two", "other"); }).Wait();
                Pump(160); Check(received.Count == 2 && received.Single(m => m.Id == "one").Content == "99", "worker bursts coalesce to the latest snapshot for each message");
                Check(!Clock(pump).IsEnabled, "drained message pump returns to idle without polling");
                Receive(pump, "one", "next"); Pump(160); Check(received.Count == 3 && received.Last().Content == "next", "new activity restarts delivery after idle");
                Receive(pump, "one", "discard"); pump.Dispose(); Pump(160); Check(received.Count == 3 && !Clock(pump).IsEnabled, "dispose cancels scheduled delivery and timer");
                var navigation = new AgentWorkspaceNavigation(pet, delegate { }); host = new Window { Content = navigation, Width = 300, Height = 600, ShowInTaskbar = false }; host.Show(); Pump(50);
                var sidebar = (ProjectSidebar)typeof(AgentWorkspaceNavigation).GetField("projectNav", Hidden).GetValue(navigation);
                var popup = sidebar.HoverDetails; popup.Child = new Border(); popup.PlacementTarget = sidebar; popup.IsOpen = true; Pump(30); navigation.Dispose();
                Check(!popup.IsOpen && popup.Child == null && popup.PlacementTarget == null, "disposing navigation closes popup and releases its visual references");
                Check(typeof(AgentWorkspaceNavigation).GetField("project", Hidden) == null, "obsolete invisible project label is removed");
                var art = new AdditionalInteractions(); var skin = pet.Catalog.Find("e-bao"); var first = art.Get(skin, "lifted");
                var frame = first.Frame(0); int stride = frame.PixelWidth * 4; var pixels = new byte[stride * frame.PixelHeight]; frame.CopyPixels(pixels, stride, 0);
                foreach (string action in AdditionalInteractions.Actions) art.Get(skin, action);
                var cache = (System.Collections.IDictionary)typeof(AdditionalInteractions).GetField("clips", Hidden).GetValue(art);
                Check(cache.Count <= 3, "trying every action retains at most three decoded clips");
                var reloaded = art.Get(skin, "lifted"); var actual = new byte[pixels.Length]; reloaded.Frame(0).CopyPixels(actual, stride, 0);
                Check(!ReferenceEquals(first, reloaded) && pixels.SequenceEqual(actual), "evicted action reloads with identical original pixels");
                Check(ReferenceEquals(reloaded, art.Get(skin, "lifted")), "repeated frames reuse the current decoded action");
                art.Get(new SkinDefinition("e-bao", "custom", "", null, false), "idle");
                Check(cache.Count == 0, "switching to a custom skin releases built-in clips even without a matching action");
                pet.Show(); Pump(40); var tail = new System.Windows.Shapes.Polygon();
                BubblePlacement.Follow(host, pet, tail); var geometry = tail.Points; BubblePlacement.Follow(host, pet, tail);
                Check(ReferenceEquals(geometry, tail.Points), "stationary quick chat reuses its tail geometry");
                var windowHandle = new System.Windows.Interop.WindowInteropHelper(host).Handle;
                var petHandle = new System.Windows.Interop.WindowInteropHelper(pet).Handle;
                var area = DesktopInterop.WorkArea(petHandle); DesktopInterop.Move(petHandle, new Point(area.Left + area.Width * .45, area.Top + area.Height * .65));
                BubblePlacement.Follow(host, pet, tail); var before = DesktopInterop.Bounds(windowHandle);
                DesktopInterop.Move(petHandle, new Point(area.Left + area.Width * .6, area.Top + area.Height * .65)); BubblePlacement.Follow(host, pet, tail);
                Check(Math.Abs(DesktopInterop.Bounds(windowHandle).Left - before.Left) > 10, "quick chat still follows an actual pet movement");
                Console.WriteLine("UI resource tests: " + checks + " passed"); return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
            finally { if (host != null) host.Close(); if (pet != null) pet.Close(); app.Shutdown(); }
        }
    }
}
