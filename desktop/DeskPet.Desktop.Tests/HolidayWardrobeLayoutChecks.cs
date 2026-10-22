using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;

internal static class HolidayWardrobeLayoutChecks
{
    internal static int RunSourceSwitch(string directory)
    {
        using var fixture = new HolidayReviewChecks.Fixture(directory, "e-bao", null, false); fixture.EnterHoliday();
        var owner = fixture.Owner;
        Complete(owner.SelectSkinAsync(owner.Skins.Single(s => s.BuiltinCharacterId == "dafeiyu")));
        var custom = ImportCustom(owner, fixture.Root);
        if (File.Exists(Path.Combine(custom.Folder!, "spritesheet.png"))) throw new Exception("Fixture accidentally contains the old filename");
        Complete(owner.SelectSkinAsync(custom));
        var sprite = (SpriteView)typeof(PetWindow).GetField("sprite", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(owner.Pet)!;
        if (!ReferenceEquals(sprite.Skin, custom) || sprite.Clip?.File != "custom-atlas.png" || custom.BuiltinCharacterId != null || owner.Holidays.EffectiveOutfit != null) throw new Exception("New custom source was not atomically rebound without holiday trust");
        Complete(owner.SelectSkinAsync(owner.Skins.Single(s => s.BuiltinCharacterId == "e-bao")));
        if (sprite.Skin.BuiltinCharacterId != "e-bao" || sprite.Clip!.File.Contains("custom-atlas")) throw new Exception("Returning to built-in still reads the custom source");
        return 3;
    }
    private static PetSkin ImportCustom(DesktopController owner, string root)
    {
        var source = Path.Combine(root, "custom-source"); Directory.CreateDirectory(source);
        using (var input = SpriteAssets.OpenAsset("spritesheet.png")) using (var output = File.Create(Path.Combine(source, "custom-atlas.png"))) input.CopyTo(output);
        File.WriteAllText(Path.Combine(source, "pet.json"), "{\"version\":1,\"name\":\"自定义伙伴\",\"atlas\":\"custom-atlas.png\"}");
        return SpriteAssets.Import(Path.Combine(source, "pet.json"), owner.DataDirectory);
    }
    internal static int Run(string directory, string artifacts)
    {
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new Exception("FAIL wardrobe layout: " + name); checks++; }
        using var fixture = new HolidayReviewChecks.Fixture(directory, "e-bao", null, false, "蝙蝠小披风");
        fixture.EnterHoliday(); var owner = fixture.Owner;
        var window = new SettingsWindow(owner, new SpriteAssets(name => File.OpenRead(Path.Combine(fixture.Root, name.Substring("Dafeiyu.Holidays.".Length)))));
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var panel = window.GetLogicalDescendants().OfType<HolidayOutfitPanel>().Distinct().Single();
            var partner = window.GetLogicalDescendants().OfType<ComboBox>().Distinct().Single(c => ReferenceEquals(c.ItemsSource, owner.Skins));
            var wardrobe = panel.GetLogicalDescendants().OfType<ComboBox>().Distinct().Single();
            var prompt = window.GetLogicalDescendants().OfType<TextBox>().First(t => t.AcceptsReturn);
            prompt.Text = "未保存的人格草稿，解锁衣柜时应当保留。";
            Check(!panel.IsVisible, "undiscovered wardrobe reserves no visible selector");
            Directory.CreateDirectory(artifacts); Capture(window, Path.Combine(artifacts, "avalonia-before-unlock.png"));
            double unsplitWidth = partner.Bounds.Width;
            var ticket = owner.Holidays.TryBeginHover(0)!;
            var playback = new HolidayPlayback(); playback.Start(ticket); playback.MarkFirstFrameRendered(); owner.Holidays.MarkHoverFirstFrame(ticket);
            for (int i = 0; i < 8; i++) playback.Advance(.25);
            Check(owner.Holidays.CompleteHover(ticket), "valid complete hover discovers wardrobe while settings stays open");
            Dispatcher.UIThread.RunJobs();
            Check(panel.IsVisible && Math.Abs(partner.Bounds.Width - wardrobe.Bounds.Width) < 1 && partner.Bounds.Width < unsplitWidth * .65, "discovery splits selector area into equal-width dropdowns live");
            Check(prompt.Text == "未保存的人格草稿，解锁衣柜时应当保留。", "live discovery preserves unsaved persona form");
            Check(wardrobe.Items.Count == 2 && wardrobe.Items[0]!.ToString() == "默认", "wardrobe contains default plus current character's owned outfit");
            foreach (var option in wardrobe.Items)
            {
                var row = wardrobe.ItemTemplate!.Build(option)!;
                Check(row.GetLogicalDescendants().OfType<Control>().Any(c => c.Name is "HolidayDefaultThumbnail" or "HolidayOutfitThumbnail"), "every wardrobe option has a thumbnail");
                var preview = row.GetLogicalDescendants().OfType<HolidayOutfitThumbnail>().SingleOrDefault();
                if (preview != null) Check(RendersPreview(preview), "outfit thumbnail renders its own decoded preview rather than an empty tile");
            }
            var import = window.GetLogicalDescendants().OfType<Button>().Distinct().Single(b => b.Name == "ImportCompanion");
            Check(import.TranslatePoint(default, window)!.Value.X > wardrobe.TranslatePoint(default, window)!.Value.X + wardrobe.Bounds.Width, "import remains to the right of both selectors");
            wardrobe.SelectedIndex = 0; wardrobe.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            Check(owner.Holidays.PreferredOutfitId == fixture.Outfit.OutfitId && prompt.Text == "未保存的人格草稿，解锁衣柜时应当保留。", "wardrobe selection updates current character preference without changing persona draft");
            Check(fixture.HolidayStore.Load().ForCharacter("e-bao").PreferredOutfitId == fixture.Outfit.OutfitId, "wardrobe choice is durably saved for its own companion");
            wardrobe.SelectedIndex = 0; Dispatcher.UIThread.RunJobs();
            Directory.CreateDirectory(artifacts);
            foreach (var mode in new[] { "light", "dark" })
            {
                DesktopTheme.Apply(mode); window.Width = 790; Dispatcher.UIThread.RunJobs(); Capture(window, Path.Combine(artifacts, "avalonia-wardrobe-" + mode + ".png"));
                window.Width = 570; Dispatcher.UIThread.RunJobs();
                Check(Math.Abs(partner.Bounds.Width - wardrobe.Bounds.Width) < 1 && partner.Bounds.Width >= 100 && wardrobe.Bounds.Width >= 100, mode + " narrow layout keeps equal usable dropdowns");
                Capture(window, Path.Combine(artifacts, "avalonia-wardrobe-" + mode + "-narrow.png"));
            }
            wardrobe.SelectedIndex = 1; Dispatcher.UIThread.RunJobs();
            var restored = new HolidayController(new HolidayCalendar(), new HolidayCatalog(fixture.Root), fixture.HolidayStore);
            restored.Refresh(new HolidayContext { PersonaId = "e-bao", CharacterId = "e-bao", LocalTime = new DateTime(2026, 11, 1), ResourceVersion = "1" });
            Check(restored.PreferredOutfitId == fixture.Outfit.OutfitId && restored.EffectiveOutfit?.OutfitId == fixture.Outfit.OutfitId, "selected outfit survives reload and applies outside the holiday");
            Complete(owner.SelectSkinAsync(owner.Skins.Single(s => s.BuiltinCharacterId == "dafeiyu"))); Dispatcher.UIThread.RunJobs();
            Check(panel.IsVisible && wardrobe.Items.Count == 1 && wardrobe.Items[0]!.ToString() == "默认", "other companion keeps a default-only wardrobe without borrowing ownership");
            var defaultRow = wardrobe.ItemTemplate!.Build(wardrobe.Items[0])!;
            Check(defaultRow.GetLogicalDescendants().OfType<AvatarView>().Single().Skin == owner.Skin, "default thumbnail follows the newly selected companion");
            var imported = ImportCustom(owner, fixture.Root);
            var custom = new PetSkin { Id = "e-bao", Name = "自定义E包", Prompt = "", Folder = imported.Folder, Atlas = imported.Atlas };
            Complete(owner.SelectSkinAsync(custom)); Dispatcher.UIThread.RunJobs();
            Check(panel.IsVisible && wardrobe.Items.Count == 1 && wardrobe.Items[0]!.ToString() == "默认", "custom matching identity cannot inherit builtin wardrobe choices");
            Complete(owner.SelectSkinAsync(owner.Skins.Single(s => s.BuiltinCharacterId == "e-bao"))); Dispatcher.UIThread.RunJobs();
            Check(wardrobe.Items.Count == 2 && wardrobe.SelectedIndex == 1 && owner.Holidays.PreferredOutfitId == fixture.Outfit.OutfitId, "returning companion restores its independently selected outfit");
        }
        finally { window.Close(); DesktopTheme.Apply("system"); }
        return checks;
    }
    private static void Complete(Task task)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!task.IsCompleted) { if (watch.Elapsed.TotalSeconds > 10) throw new TimeoutException(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        task.GetAwaiter().GetResult();
    }
    private static void Capture(Window window, string path)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame() ?? throw new Exception("No rendered wardrobe frame"); frame.Save(path, new PngBitmapEncoderOptions());
    }
    private static bool RendersPreview(Control thumbnail)
    {
        thumbnail.Measure(new Size(32, 32)); thumbnail.Arrange(new Rect(0, 0, 32, 32));
        using var target = new RenderTargetBitmap(new PixelSize(32, 32));
        using (var context = target.CreateDrawingContext()) thumbnail.Render(context);
        var pixels = new byte[32 * 32 * 4]; var pin = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { target.CopyPixels(new PixelRect(0, 0, 32, 32), pin.AddrOfPinnedObject(), pixels.Length, 32 * 4); }
        finally { pin.Free(); }
        for (int i = 0; i < pixels.Length; i += 4) if (pixels[i] > 100 && pixels[i + 1] < 50 && pixels[i + 2] < 50 && pixels[i + 3] > 16) return true;
        return false;
    }
}
