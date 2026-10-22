using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;

internal static class HolidayReviewChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Call(object instance, string method, params object[] args)
    {
        try { return instance.GetType().GetMethod(method, Private)!.Invoke(instance, args); }
        catch (TargetInvocationException error) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException!).Throw(); throw; }
    }
    private static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private)!.GetValue(instance)!;
    internal static int Run(string directory, string selected = "all")
    {
        int passed = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL holiday review: " + name); passed++; }
        foreach (var corrupted in new[] { "feeding", "gaze", "render", "hit", "idle", "egg" })
        {
            if (selected != "all" && selected != "decode") break;
            using var fixture = new Fixture(Path.Combine(directory, "bad-" + corrupted), "e-bao", corrupted is "render" or "hit" ? "feeding" : corrupted, true);
            var pet = fixture.Owner.Pet;
            Check(fixture.Outfit.Ready, "correct-CRC malformed-IDAT fixture passes structural catalog validation");
            fixture.EnterHoliday();
            Check(corrupted == "idle" ? pet.CommittedHolidayOutfit == null : pet.CommittedHolidayOutfit == fixture.Outfit, corrupted == "idle" ? "initial bad idle is rejected before commit" : "valid idle is committed before lazy " + corrupted + " decode");
            using var panel = new HolidayOutfitPanel(fixture.Owner.Holidays);
            var sprite = Field<SpriteView>(pet, "sprite");
            if (corrupted == "feeding") pet.Interact("feeding");
            else if (corrupted == "gaze") Call(pet, "PublishHolidayGaze", 0);
            else if (corrupted == "egg") Check(!(bool)Call(pet, "TryHolidayHover")!, "bad egg candidate cannot begin or start cooldown");
            else if (corrupted is "render" or "hit")
            {
                // Defense against a future producer accidentally bypassing frame preparation.
                sprite.Clip = Field<SpriteAssets>(pet, "assets").Resolve(fixture.Owner.Skin, "feeding", false, fixture.Outfit); sprite.Frame = 0;
                if (corrupted == "render") { using var target = new RenderTargetBitmap(new PixelSize(192, 208)); using var context = target.CreateDrawingContext(); sprite.Render(context); }
                else _ = sprite.Hits(new Point(32, 48));
            }
            // Also invoke real renderer and native-alpha path: neither may throw.
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); using var captured = pet.CaptureRenderedFrame(); _ = sprite.Hits(new Point(32, 48));
            Check(pet.CommittedHolidayOutfit == null && !sprite.Clip!.File.StartsWith("holiday|"), "lazy decode failure restores original pixels and alpha before publish");
            Check(!fixture.Outfit.Ready && fixture.Owner.Holidays.EffectiveOutfit == null && fixture.Owner.Holidays.OwnedOutfits.Count == 1, "runtime-unavailable propagates while preserving ownership");
            var picker = panel.GetLogicalDescendants().OfType<ComboBox>().Single();
            Check(picker.Items.OfType<object>().Any(x => x.ToString()!.Contains("暂不可用")), "open wardrobe reports the failed outfit unavailable");
            Check(fixture.Owner.Holidays.CreateGreetingContext(0).OutfitId == null, "AI context describes the fallback original");
        }
        if (selected is "all" or "midnight")
        {
            using var fixture = new Fixture(Path.Combine(directory, "midnight"), "e-bao", null, false);
            fixture.Date = new DateTime(2026, 10, 31, 23, 59, 58); fixture.EnterHoliday();
            var pet = fixture.Owner.Pet;
            Check((bool)Call(pet, "TryHolidayHover")!, "fixture egg begins from hover adapter");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); using (var frame = pet.CaptureRenderedFrame()) Check(frame != null, "fixture egg first frame renders");
            for (int i = 0; i < 7; i++) { fixture.Seconds += .25; Call(pet, "AdvanceHoliday", .25); }
            fixture.Date = new DateTime(2026, 10, 31, 23, 59, 59, 900); pet.ProbeHolidayForTest();
            fixture.Date = fixture.Date.AddMilliseconds(150); fixture.Seconds += .15;
            Call(pet, "AdvanceHoliday", .25);
            Check(!fixture.Owner.Holidays.WardrobeVisible && fixture.Owner.Holidays.OwnedOutfits.Count == 0, "completion inside date-probe throttle cannot unlock yesterday's event");
            Check(!Field<HolidayPlayback>(pet, "holidayPlayback").Active, "midnight refresh cancels original ticket without null callback failure");
        }
        if (selected is "all" or "mirror") foreach (var character in new[] { "dafeiyu", "marfu" })
        {
            using var fixture = new Fixture(Path.Combine(directory, "left-" + character), character, null, false);
            fixture.EnterHoliday(); var pet = fixture.Owner.Pet;
            fixture.Owner.Settings.Wander = true;
            Call(pet, "Play", "running-left", false);
            pet.GetType().GetField("direction", Private)!.SetValue(pet, -1);
            pet.GetType().GetField("runUntil", Private)!.SetValue(pet, double.PositiveInfinity);
            pet.GetType().GetField("motionX", Private)!.SetValue(pet, (double)pet.Position.X);
            Call(pet, "Tick");
            var sprite = Field<SpriteView>(pet, "sprite");
            Check(!sprite.MirrorX && sprite.Hits(new Point(32, 48)) && !sprite.Hits(new Point(160, 48)), character + " authored holiday left pixels and alpha remain left-oriented");
            fixture.Owner.Holidays.SetEnabled(false); Call(pet, "Tick");
            Check(sprite.MirrorX, character + " original running-left mirror rule is preserved");
        }
        return passed;
    }

    internal sealed class Fixture : IDisposable
    {
        internal string Root { get; }
        internal HolidayStateStore HolidayStore { get; }
        internal DesktopController Owner { get; }
        internal OutfitDefinition Outfit { get; }
        internal DateTime Date = new(2026, 10, 31, 12, 0, 0);
        internal double Seconds = 100;
        internal Fixture(string root, string character, string? corrupted, bool owned, string outfitName = "fixture")
        {
            Root = root;
            Directory.CreateDirectory(root);
            SavePng(Path.Combine(root, "normal.png"), 3840, 520, 8);
            SavePng(Path.Combine(root, "gaze.png"), 1920, 2080, 4);
            SavePng(Path.Combine(root, "preview.png"), 480, 520, 1);
            if (corrupted != null) File.WriteAllBytes(Path.Combine(root, "bad.png"), BadIdat(File.ReadAllBytes(Path.Combine(root, corrupted == "gaze" ? "gaze.png" : "normal.png"))));
            object Clip(string action, string variant, string path, int frames = 8, int columns = 8, bool loop = true) => new { action, variant, path, frame_count = frames, columns, fps = 4, loop, frame_width = 480, frame_height = 520, offset_x = 0, offset_y = 0 };
            var clips = new List<object>();
            foreach (var action in new[] { "idle", "running-right", "running-left", "waving", "jumping", "failed", "waiting", "running", "review", "lifted", "landing", "petting", "poked", "feeding", "happy", "sleeping", "special", "stumble", "special2" }) clips.Add(Clip(action, "default", action == corrupted ? "bad.png" : "normal.png"));
            foreach (var action in new[] { "lifted", "landing", "feeding", "happy", "sleeping", "special", "special2", "stumble" }.Concat(character is "e-bao" or "gugugaga" ? new[] { "jumping" } : Array.Empty<string>())) clips.Add(Clip(action, "alternate", action == corrupted ? "bad.png" : "normal.png"));
            File.WriteAllText(Path.Combine(root, "outfit.json"), JsonSerializer.Serialize(new { schema_version = 1, character_id = character, outfit_id = "halloween-2026", holiday_id = "halloween", name = outfitName, version = "1", clips, gaze = Clip("gaze", "default", corrupted == "gaze" ? "bad.png" : "gaze.png", 16, 4), easter_egg = Clip("egg", "default", corrupted == "egg" ? "bad.png" : "normal.png", loop: false), preview = "preview.png", action_name = "fixture egg" }));
            File.WriteAllText(Path.Combine(root, "catalog.json"), JsonSerializer.Serialize(new { schema_version = 1, outfits = new[] { new { character_id = character, outfit_id = "halloween-2026", holiday_id = "halloween", name = outfitName, version = "1", ready = true, greetings_ready = true, definition = "outfit.json" } } }));
            var catalog = new HolidayCatalog(root); Outfit = catalog.Outfits.Single();
            var state = new HolidayState(); if (owned) state.Unlock(character, Outfit.OutfitId);
            HolidayStore = new HolidayStateStore(Path.Combine(root, "holiday-state.json")); HolidayStore.Save(state);
            var holidays = new HolidayController(new HolidayCalendar(), catalog, HolidayStore);
            var data = Path.Combine(root, "data"); Directory.CreateDirectory(data);
            new SettingsStore(Path.Combine(data, "settings.json")).Save(new PetSettings { SkinId = character, GreetingEnabled = false, Wander = false, FollowCursor = false });
            Owner = new DesktopController(new ClassicDesktopStyleApplicationLifetime(), new TestRuntime(data), holidays: holidays,
                spriteAssetsFactory: () => new SpriteAssets(name => File.OpenRead(Path.Combine(root, name.Substring("Dafeiyu.Holidays.".Length)))));
            Owner.Pet.HolidayLocalNow = () => Date; Owner.Pet.HolidayMonotonicNow = () => Seconds; Owner.Pet.HolidayHoverRandom = () => 0;
        }
        internal void EnterHoliday() { Owner.Pet.ProbeHolidayForTest(); Owner.Pet.Show(); Dispatcher.UIThread.RunJobs(); Owner.Pet.ProbeHolidayForTest(); }
        public void Dispose()
        {
            Owner.Pet.Close(); Owner.Dispose();
            while (!Owner.ImageServicesShutdown.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
            Owner.ImageServicesShutdown.GetAwaiter().GetResult();
        }
    }
    private static void SavePng(string path, int width, int height, int columns)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        var pixels = new byte[width * height * 4];
        for (int row = 0; row < height / 520; row++) for (int col = 0; col < columns; col++)
        for (int y = 80; y < 160; y++) for (int x = 60; x < 110; x++) { var i = ((row * 520 + y) * width + col * 480 + x) * 4; pixels[i] = 180; pixels[i + 3] = 255; }
        using (var data = bitmap.Lock()) Marshal.Copy(pixels, 0, data.Address, pixels.Length);
        bitmap.Save(path, new PngBitmapEncoderOptions());
    }
    private static byte[] BadIdat(byte[] source)
    {
        using var output = new MemoryStream(); output.Write(source, 0, 8); bool written = false;
        for (int offset = 8; offset < source.Length;)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(offset, 4));
            string type = System.Text.Encoding.ASCII.GetString(source, offset + 4, 4);
            if (type == "IDAT")
            {
                if (!written) { Chunk(output, "IDAT", new byte[] { 0x78, 0x9c, 0xff, 0xff, 0xff, 0xff, 1, 2, 3 }); written = true; }
            }
            else output.Write(source, offset, length + 12);
            offset += length + 12;
        }
        return output.ToArray();
    }
    private static void Chunk(Stream output, string type, byte[] payload)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(type).Concat(payload).ToArray();
        uint crc = 0xffffffff; foreach (var b in bytes) { crc ^= b; for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? crc >> 1 ^ 0xedb88320 : crc >> 1; }
        Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(number, (uint)payload.Length); output.Write(number); output.Write(bytes); BinaryPrimitives.WriteUInt32BigEndian(number, crc ^ 0xffffffff); output.Write(number);
    }
}
