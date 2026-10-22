using Dafeiyu;
using DeskPet.Desktop;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.Headless;
using System.Runtime.InteropServices;

internal static class HolidayUiChecks
{
    internal static int RunResources(string directory)
    {
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL holiday resources: " + message); checks++; }
        var catalog = new HolidayCatalog();
        Check(catalog.Outfits.Count == 5 && catalog.Outfits.All(o => o.Ready), "all five holiday outfits load ready from the delivered assembly");
        var skins = SpriteAssets.LoadSkins(directory);
        foreach (var outfit in catalog.Outfits)
        {
            using var assets = new SpriteAssets();
            var skin = skins.Single(s => s.BuiltinCharacterId == outfit.CharacterId);
            foreach (var definition in outfit.Clips)
            {
                var original = definition.Variant == "default";
                var clip = assets.Resolve(skin, definition.Action, original, outfit);
                var source = assets.Resolve(skin, definition.Action, original);
                Check(clip.File.Contains(definition.ResourceName) && clip.Loop == source.Loop && Math.Abs(clip.Frames / clip.Fps - source.Frames / source.Fps) < 1e-8, "action/variant uses outfit pixels with source duration and loop: " + outfit.CharacterId + "/" + definition.Action + "/" + definition.Variant);
                Check(assets.Get(skin, clip.File).PixelSize.Width == clip.Columns * clip.Width, "embedded outfit image geometry");
                bool opaque = false, transparent = false;
                for (int y = 0; y < 20; y++) for (int x = 0; x < 20; x++) { var hit = assets.Hit(skin, clip, 0, x / 20d, y / 20d); opaque |= hit; transparent |= !hit; }
                Check(opaque && transparent, "outfit alpha mask contains character and transparent margin");
            }
            Check(assets.Gaze(skin, outfit).Frames == 16 && assets.Gaze(skin, outfit).Columns == 4, "outfit gaze preserves sixteen directions");
        }
        var data = Path.Combine(directory, "lifecycle");
        var controller = new DesktopController(new ClassicDesktopStyleApplicationLifetime(), new TestRuntime(data));
        try
        {
            var pet = controller.Pet; var date = new DateTime(2026, 10, 31, 12, 0, 0); double now = 100;
            controller.Settings.GreetingEnabled = false; controller.Settings.Wander = false; controller.Settings.FollowCursor = false;
            pet.HolidayLocalNow = () => date; pet.HolidayMonotonicNow = () => now; pet.HolidayHoverRandom = () => 0;
            pet.ProbeHolidayForTest();
            Check(pet.CommittedHolidayOutfit != null && !controller.Holidays.WardrobeVisible, "hidden startup commits temporary holiday outfit without revealing wardrobe");
            using var panel = new HolidayOutfitPanel(controller.Holidays);
            pet.Show(); Dispatcher.UIThread.RunJobs(); pet.ProbeHolidayForTest();
            var begin = typeof(PetWindow).GetMethod("TryHolidayHover", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var advance = typeof(PetWindow).GetMethod("AdvanceHoliday", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var sprite = (SpriteView)typeof(PetWindow).GetField("sprite", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(pet)!;
            Check((bool)begin.Invoke(pet, null)!, "hover can begin prepared holiday egg");
            // Draw the actual committed first frame; the renderer acknowledges the controller ticket.
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (var render = pet.CaptureRenderedFrame()) Check(render != null, "holiday first frame is actually rendered in Headless");
            typeof(PetWindow).GetField("lastTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(pet, -100d);
            typeof(PetWindow).GetMethod("Tick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(pet, null);
            var egg = (HolidayPlayback)typeof(PetWindow).GetField("holidayPlayback", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(pet)!;
            Check(!egg.Active && !controller.Holidays.WardrobeVisible, "resume gap interrupts before capped animation delta and never unlocks");
            typeof(PetWindow).GetMethod("LeaveHolidayHover", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(pet, null);
            now += 61; pet.ProbeHolidayForTest();
            Check((bool)begin.Invoke(pet, null)!, "reentry after interrupted cooldown can begin a new egg");
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (var render = pet.CaptureRenderedFrame()) Check(render != null && egg.FirstFrameRendered, "new egg first frame is acknowledged only by renderer");
            for (int i = 0; i < 8; i++) { now += .25; advance.Invoke(pet, new object[] { .25 }); }
            Check(controller.Holidays.WardrobeVisible && panel.IsVisible && controller.Holidays.OwnedOutfits.Count == 1, "complete rendered hover unlocks only current character and refreshes open panel");
            Check(new HolidayStateStore(Path.Combine(data, "holiday-state.json")).Load().WardrobeDiscovered, "discovery and unlock are durable in actual data directory");
            date = date.AddDays(1); now += 1; pet.ProbeHolidayForTest();
            Check(pet.CommittedHolidayOutfit == null, "after holiday returns to original without preferred outfit");
            controller.Holidays.SelectOwnedOutfit(catalog.Outfits.First(o => o.CharacterId == controller.Skin.BuiltinCharacterId).OutfitId);
            advance.Invoke(pet, new object[] { .4 });
            Check(pet.CommittedHolidayOutfit != null && controller.Session.PersonaId == controller.Skin.Id, "owned outfit selection preserves conversation identity");
        }
        finally
        {
            controller.Pet.Close(); controller.Dispose();
            while (!controller.ImageServicesShutdown.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
            controller.ImageServicesShutdown.GetAwaiter().GetResult();
        }
        return checks;
    }
    internal static int Run(string directory)
    {
        int checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL holiday: " + message); checks++; }
        var skins = SpriteAssets.LoadSkins(directory);
        Check(skins.Count(s => s.BuiltinCharacterId != null) == 5, "all five embedded characters carry trusted provenance");
        Check(new PetSkin { Id = "e-bao", Name = "E包", Prompt = "", Folder = directory }.BuiltinCharacterId == null, "a matching user identity never grants trusted provenance");
        bool swapped = false;
        var player = new InteractionPlayback((action, original) => new ClipSpec("test", 8, swapped ? 4 : 8, 8, action == "idle"));
        player.Play("feeding", true); player.Advance(.125); player.Advance(.125);
        swapped = true; player.RefreshAppearance();
        Check(player.Action == "feeding" && player.Original && player.Frame == 1, "appearance rebind retains action, variant and normalized progress");
        for (int i = 0; i < 3; i++) player.Advance(.125);
        Check(player.Action == "happy" && player.Original, "feeding still completes into happy after changing appearance");
        var entry = new HolidayHoverEntry();
        Check(entry.TryEnter(true, true), "first alpha hit gets one opportunity");
        Check(!entry.TryEnter(true, false) && !entry.TryEnter(true, true), "transparent pixel jitter never rearms entry");
        entry.Leave(); Check(entry.TryEnter(true, true), "leaving viewport rearms opportunity");
        entry.Leave(); Check(!entry.TryEnter(true, false) && entry.TryEnter(true, true), "transparent margin does not consume opportunity before first alpha hit");
        var timed = SpriteAssets.MatchSourceTiming(new ClipSpec("outfit", 8, 8, 5, true), new ClipSpec("source", 8, 5, 8, false));
        Check(!timed.Loop && Math.Abs(timed.Frames / timed.Fps - 5d / 8) < 1e-9, "outfit keeps source duration and nonloop completion even when frame count differs");
        Directory.CreateDirectory(directory);
        using (var bitmap = new WriteableBitmap(new PixelSize(8, 8), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul))
        {
            var bytes = new byte[8 * 8 * 4]; bytes[(2 * 8 + 1) * 4 + 3] = 255;
            using (var buffer = bitmap.Lock()) Marshal.Copy(bytes, 0, buffer.Address, bytes.Length);
            bitmap.Save(Path.Combine(directory, "alpha.png"), new PngBitmapEncoderOptions());
        }
        using (var assets = new SpriteAssets())
        {
            var skin = new PetSkin { Id = "alpha", Name = "alpha", Prompt = "", Folder = directory };
            var sprite = new SpriteView { Assets = assets, Skin = skin, Clip = new ClipSpec("alpha.png", 1, 1, 1, true, 8, 8), Frame = 0 };
            sprite.Measure(new Size(80, 80)); sprite.Arrange(new Rect(0, 0, 80, 80));
            Check(sprite.Hits(new Point(15, 25)) && !sprite.Hits(new Point(5, 5)), "hit uses the committed frame alpha");
            sprite.MirrorX = true; Check(sprite.Hits(new Point(65, 25)) && !sprite.Hits(new Point(15, 25)), "mirroring transforms visual alpha consistently");
            sprite.MirrorX = false; sprite.Clip = sprite.Clip with { OffsetX = 2 };
            Check(sprite.Hits(new Point(35, 25)) && !sprite.Hits(new Point(15, 25)), "alignment offsets transform the same alpha hit");
        }
        var state = new HolidayState(); state.Unlock("e-bao", "unavailable-test-outfit");
        var data = Path.Combine(directory, "state"); Directory.CreateDirectory(data);
        new HolidayStateStore(Path.Combine(data, "holiday-state.json")).Save(state);
        var controller = new DesktopController(new ClassicDesktopStyleApplicationLifetime(), new TestRuntime(data));
        try
        {
            var date = new DateTime(2026, 10, 30, 23, 59, 59); double monotonic = 10;
            controller.Pet.HolidayLocalNow = () => date; controller.Pet.HolidayMonotonicNow = () => monotonic;
            controller.Pet.Paused = true;
            var before = controller.Holidays.Revision;
            date = date.AddSeconds(2); monotonic += 1.01;
            typeof(PetWindow).GetMethod("Tick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(controller.Pet, null);
            Check(controller.Holidays.Revision > before, "date refresh runs while both hidden and paused");
            before = controller.Holidays.Revision; monotonic += 1.01;
            typeof(PetWindow).GetMethod("Tick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(controller.Pet, null);
            Check(controller.Holidays.Revision == before, "unchanged date probes preserve pending request revision");
            using var panel = new HolidayOutfitPanel(controller.Holidays);
            Check(panel.IsVisible && panel.GetLogicalDescendants().OfType<ComboBox>().Single().IsEnabled, "global discovery keeps a default-only wardrobe visible for the current character");
            Check(panel.GetLogicalDescendants().OfType<ComboBox>().Single().Items.Single()!.ToString() == "默认", "missing owned content safely retains the default choice");
            controller.Settings.Text.BaseUrl = "https://example.test/v1"; controller.Settings.Text.Model = "fake"; controller.Settings.Text.ApiKey = "test";
            controller.Queue.Hold(); controller.Queue.Enqueue("keep this queued");
            var queued = controller.Queue.Items.Single().Id;
            controller.Session.SelectPersona("other-persona");
            Check(controller.Queue.Items.Count == 0 && controller.HolidayBusinessBusy, "queues belonging to another persona still block holiday playback");
            controller.Session.SelectPersona(controller.Skin.Id); controller.Queue.Remove(queued);
            using (var cancel = new CancellationTokenSource())
            {
                var question = controller.Questions.AskAsync(new AgentQuestionRequest { SessionId = "session", TaskId = "task", CallId = "call", Question = "Which?" }, cancel.Token);
                Check(controller.HolidayBusinessBusy, "pending work question blocks holiday playback");
                cancel.Cancel(); try { question.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
            }
            typeof(ConversationSession).GetProperty("RuntimeAction")!.SetValue(controller.Session, "review");
            typeof(ConversationSession).GetProperty("TerminalRevision")!.SetValue(controller.Session, 1);
            controller.Session.ReportNotice("completed"); Dispatcher.UIThread.RunJobs();
            var playback = (InteractionPlayback)typeof(PetWindow).GetField("playback", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(controller.Pet)!;
            Check(playback.Action == "review", "completion feedback receives ordinary playback ownership before holiday animation");
        }
        finally
        {
            controller.Pet.Close(); controller.Dispose();
            while (!controller.ImageServicesShutdown.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
            controller.ImageServicesShutdown.GetAwaiter().GetResult();
        }
        var hidden = new HolidayController(new HolidayCalendar(), new HolidayCatalog(), new HolidayState(), _ => { });
        hidden.Refresh(new HolidayContext { PersonaId = "e-bao", CharacterId = "e-bao", LocalTime = new DateTime(2026, 10, 31), ResourceVersion = "test" });
        using (var panel = new HolidayOutfitPanel(hidden)) Check(!panel.IsVisible, "detected holiday without unlock never reveals wardrobe");
        return checks;
    }
}
