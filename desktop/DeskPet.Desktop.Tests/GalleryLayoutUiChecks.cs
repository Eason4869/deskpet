using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;

internal static class GalleryLayoutUiChecks
{
    internal static int Run(DesktopController owner, ChatWindow window)
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception("FAIL: gallery layout " + label); checks++; }
        void Render() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
        var page = window.Images; window.ShowImages(); page.SelectedIndex = 1; page.Search.Text = "layout fixture";
        var dateField = (TextBox)typeof(ImageWorkspacePage).GetField("toDate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(page)!;
        var dateStatus = (TextBlock)typeof(ImageWorkspacePage).GetField("galleryCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(page)!;
        dateField.Text = "9999-12-31"; page.RefreshGallery(); Check(dateStatus.Text == "日期格式应为 YYYY-MM-DD。", "maximum end date is rejected in native Avalonia event");
        dateField.Text = "malformed"; page.RefreshGallery(); Check(dateStatus.Text == "日期格式应为 YYYY-MM-DD。", "malformed end date is rejected in native Avalonia event");
        dateField.Text = "2026-10-22"; page.RefreshGallery(); dateField.Text = ""; page.RefreshGallery(); Check(dateField.Text == "", "normal and empty end dates pass native Avalonia event");
        bool TilesReady(int count, ISet<string>? expectedIds = null)
        {
            var cards = page.GalleryPanel.Children.OfType<GeneratedImageCard>().ToArray();
            return page.GalleryCount == count && cards.Length == count
                && (expectedIds == null || expectedIds.SetEquals(cards.Select(t => t.GalleryAssetId)))
                && cards.All(t => t.GetLogicalDescendants().OfType<Image>().Any(i => i.Source != null));
        }
        void AwaitTiles(int count, int timeoutMilliseconds = 5000, ISet<string>? expectedIds = null)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < timeoutMilliseconds) { Render(); if (TilesReady(count, expectedIds)) return; Thread.Sleep(10); }
            var cards = page.GalleryPanel.Children.OfType<GeneratedImageCard>().ToArray();
            throw new TimeoutException("gallery thumbnails did not settle: search=" + page.Search.Text
                + " expected=" + count + " cards=" + cards.Length
                + " decoded=" + cards.Count(t => t.GetLogicalDescendants().OfType<Image>().Any(i => i.Source != null))
                + " expectedIds=" + string.Join(",", expectedIds ?? new HashSet<string>())
                + " actualIds=" + string.Join(",", cards.Select(t => t.GalleryAssetId)));
        }
        Check(page.GetLogicalDescendants().OfType<Button>().Any(b => b.Name == "GallerySelectLoaded") && page.GetLogicalDescendants().OfType<Button>().Any(b => b.Name == "GalleryExportSelected") && page.GetLogicalDescendants().OfType<Button>().Any(b => b.Name == "GalleryCleanupOrphans"), "selection, original export and explicit orphan cleanup controls are visible");
        var shapes = new[] { (320, 560), (600, 320), (400, 400), (400, 610), (640, 360), (420, 520), (460, 340) };
        for (int i = 0; i < shapes.Length; i++) owner.ImageStore.Save(Png(shapes[i].Item1, shapes[i].Item2, i), new GeneratedImage { Prompt = "layout fixture " + i, Model = "layout-model" }, true);
        Render(); page.RefreshGallery(); AwaitTiles(shapes.Length);
        Button Toggle(string name) => page.GetLogicalDescendants().OfType<Button>().Distinct().Single(b => b.Name == name);
        Toggle("GalleryMasonryLayout").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.Width = 960; Render();
        Check(owner.Settings.ImageGalleryLayout == "masonry" && page.GalleryCount == shapes.Length, "masonry is selected and includes every filtered thumbnail");
        var tiles = page.GalleryPanel.Children.OfType<GeneratedImageCard>().ToArray();
        Check(!page.GetLogicalDescendants().OfType<WrapPanel>().First(p => p.Children.OfType<Button>().Any(b => b.Name == "GallerySelectLoaded")).IsVisible, "bulk actions are hidden until multi-select starts");
        Check(tiles.All(t => t.IsGalleryTile && t.GetLogicalDescendants().OfType<Image>().Single().Stretch == Stretch.Uniform), "masonry uses full image aspect ratios");
        Check(tiles.All(t => !t.GetLogicalDescendants().OfType<WrapPanel>().Any() && t.GetLogicalDescendants().OfType<Button>().Distinct().Count() == 4), "gallery contains preview and compact action overlays without metadata rows");
        var expected = ImageGalleryLayout.Calculate(owner.ImageStore.List().Where(i => i.Prompt.StartsWith("layout fixture")).ToList(), page.GalleryPanel.Bounds.Width, "masonry");
        Check(expected.Columns > 1 && tiles.Zip(expected.Items).All(p => Math.Abs(p.First.Bounds.Height - p.Second.Height) < 2 && Math.Abs(p.First.Bounds.Width - p.Second.Width) < 2), "masonry arranges native tiles at shared aspect preserving placements");
        Toggle("GalleryGridLayout").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Render();
        Check(ReferenceEquals(tiles[0], page.GalleryPanel.Children[0]) && tiles.All(t => Math.Abs(t.Bounds.Width - t.Bounds.Height) < 1 && t.GetLogicalDescendants().OfType<Image>().Single().Stretch == Stretch.UniformToFill), "grid reuses images and uses square cover cropping");
        Check(new SettingsStore(Path.Combine(owner.DataDirectory, "settings.json")).Load().ImageGalleryLayout == "grid" && owner.Settings.Copy().ImageGalleryLayout == "grid", "grid preference persists and copies");
        var first = tiles[0]; var more = first.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "GalleryTileActions");
        window.MouseMove(new Point(2, 2)); Render(); Check(more.Opacity == 0, "ellipsis is unobtrusive while tile is idle");
        var point = first.TranslatePoint(new Point(10, 10), window)!.Value; window.MouseMove(point); Render(); Check(more.Opacity == 1, "hover reveals image operations");
        Check(first.ContextMenu!.ItemsSource!.Cast<MenuItem>().Select(m => m.Header as string).Contains("复制图片") && first.ContextMenu.Items.Count == 6, "context menu retains preview save copy folder details and regenerate");
        first.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "GalleryImagePreview").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Render();
        var details = window.OwnedWindows.Single(w => w.Name == "GeneratedImagePreviewWindow");
        Check(details.GetLogicalDescendants().OfType<SelectableTextBlock>().Any(t => t.Name == "GalleryDetailPrompt" && t.Text!.StartsWith("layout fixture")) && details.GetLogicalDescendants().OfType<Button>().Any(b => b.Content as string == "打开目录"), "clicking tile opens large preview with prompt metadata and image actions");
        var viewport = details.GetLogicalDescendants().OfType<Border>().Single(b => b.Name == "ImagePreviewViewport"); Check(((Image)viewport.Child!).Width > 100, "details image fits measured viewport after window opens");
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "artifacts")); using (var screenshot = details.CaptureRenderedFrame()) screenshot!.Save(Path.Combine(AppContext.BaseDirectory, "artifacts", "image-gallery-details.png")); details.Close();
        var artifacts = Path.Combine(AppContext.BaseDirectory, "artifacts"); Directory.CreateDirectory(artifacts);
        foreach (var mode in new[] { "masonry", "grid" })
        {
            Toggle(mode == "grid" ? "GalleryGridLayout" : "GalleryMasonryLayout").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            foreach (var width in new[] { 430, 960 }) foreach (var theme in new[] { "light", "dark" })
            {
                window.Width = width; window.Height = 760; DesktopTheme.Apply(theme); Render(); using var screenshot = window.CaptureRenderedFrame();
                Check(screenshot != null && screenshot.PixelSize.Width == width, mode + " renders at " + width + " pixels in " + theme); screenshot!.Save(Path.Combine(artifacts, "image-gallery-" + mode + "-" + width + "-" + theme + ".png"));
            }
        }
        Toggle("GalleryMasonryLayout").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Render();
        Check(new SettingsStore(Path.Combine(owner.DataDirectory, "settings.json")).Load().ImageGalleryLayout == "masonry", "masonry preference roundtrips after switching back");
        for (int i = shapes.Length; i < 43; i++) owner.ImageStore.Save(Png(2, 3, i), new GeneratedImage { Prompt = "layout fixture " + i, Model = "layout-model" }, false);
        AwaitTiles(40); Check(page.GalleryCount == 40, "gallery initially attaches forty filtered tiles");
        var layoutIds = new ImageGalleryQueryService(owner.ImageStore).Query(new ImageGalleryQuery { Keyword = "layout fixture", Limit = 40 }).Items.Select(i => i.Id).ToHashSet();
        AwaitTiles(40, expectedIds: layoutIds);
        var large = Png(2048, 2048, 2); var largeIds = new HashSet<string>();
        for (int i = 0; i < 40; i++) largeIds.Add(owner.ImageStore.Save(large, new GeneratedImage { Prompt = "large acceptance " + i, Model = "large-image" }, false).Id);
        // Keep the old, equally sized result visible while the new query is blocked.
        var queryGate = typeof(GeneratedImageStore).GetField("gate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(owner.ImageStore)!;
        Monitor.Enter(queryGate);
        try
        {
            page.Search.Text = "large acceptance"; Render();
            Check(TilesReady(40, layoutIds), "pending query keeps the previous forty decoded tiles visible");
            Check(!TilesReady(40, largeIds), "matching tile count cannot satisfy a pending search for different asset IDs");
        }
        finally { Monitor.Exit(queryGate); }
        // Forty 2048px originals take about seven seconds on this host; retain full readiness assertions.
        AwaitTiles(40,15000,largeIds);Toggle("GalleryMultiSelect").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));AwaitTiles(40,15000,largeIds);
        var stableTiles=page.GalleryPanel.Children.OfType<GeneratedImageCard>().ToArray();var stableSources=stableTiles.Select(t=>t.GetLogicalDescendants().OfType<Image>().Single().Source).ToArray();var stablePick=stableTiles[0].GetLogicalDescendants().OfType<Button>().Single(b=>b.Name=="GalleryTileSelect");window.Activate();Check(stablePick.Focus(),"forty large-image overlay accepts focus");stablePick.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var selectionClock=System.Diagnostics.Stopwatch.StartNew();while(selectionClock.ElapsedMilliseconds<200){Render();Thread.Sleep(5);}
        Check(stableTiles.All(t=>page.GalleryPanel.Children.Contains(t))&&stableTiles.Select((t,i)=>ReferenceEquals(t.GetLogicalDescendants().OfType<Image>().Single().Source,stableSources[i])).All(v=>v),"selection reuses forty large-image tiles and thumbnails");Check(stablePick.IsFocused,"selection preserves overlay focus");
        Toggle("GalleryMultiSelect").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));page.Search.Text="layout fixture";AwaitTiles(40,expectedIds:layoutIds);
        Toggle("LoadMoreGalleryImages").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); AwaitTiles(43); Check(page.GalleryCount == 43, "load more exposes remaining images");
        page.Search.Text = "layout fixture 42"; AwaitTiles(1); Check(page.GalleryCount == 1, "filter resets pagination and finds older assets");
        Toggle("GalleryMultiSelect").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); AwaitTiles(1);
        var selectable = page.GalleryPanel.Children.OfType<GeneratedImageCard>().Single(); var selectedId = owner.ImageStore.List().Single(i => i.Prompt == "layout fixture 42").Id;
        var pick = selectable.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "GalleryTileSelect"); var overlayClock = System.Diagnostics.Stopwatch.StartNew(); while (overlayClock.ElapsedMilliseconds < 5000 && pick.Opacity != 1) { Thread.Sleep(10); Render(); selectable = page.GalleryPanel.Children.OfType<GeneratedImageCard>().Single(); pick = selectable.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "GalleryTileSelect"); } Check(pick.Focusable && pick.Opacity == 1, "multi-select exposes keyboard focusable selection overlay");
        int windowsBefore = window.OwnedWindows.Count; pick.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); var selectedClock = System.Diagnostics.Stopwatch.StartNew(); while (selectedClock.ElapsedMilliseconds < 5000 && page.GallerySelectedCount != 1) { Thread.Sleep(10); Render(); } Check(window.OwnedWindows.Count == windowsBefore && page.GallerySelectedCount == 1, "selection click changes stable ID without opening details");
        Render(); using (var selectedShot = window.CaptureRenderedFrame()) selectedShot!.Save(Path.Combine(artifacts, "image-gallery-selected-dark.png"));
        var star = page.GalleryPanel.Children.OfType<GeneratedImageCard>().Single().GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "GalleryTileFavorite"); star.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); AwaitTiles(1); Check(owner.ImageStore.Get(selectedId).IsFavorite && window.OwnedWindows.Count == windowsBefore, "favorite overlay updates asset without opening details");
        owner.ImageStore.MoveToTrash(new[] { selectedId }); var trashView = page.GetLogicalDescendants().OfType<CheckBox>().First(c => c.Content as string == "回收站"); trashView.IsChecked = true; var trashClock = System.Diagnostics.Stopwatch.StartNew(); while (trashClock.ElapsedMilliseconds < 5000 && !Toggle("GalleryClearTrash").IsVisible) { Thread.Sleep(10); Render(); } Check(Toggle("GalleryClearTrash").IsVisible, "trash exposes reference-protected clear action"); using (var trashShot = window.CaptureRenderedFrame()) trashShot!.Save(Path.Combine(artifacts, "image-gallery-trash-dark.png")); owner.ImageStore.Restore(new[] { selectedId }); trashView.IsChecked = false;
        page.Search.Text = "no image matches"; Render(); Check(page.GallerySelectedCount == 0, "filter clears loaded selection");
        page.Search.Text = ""; DesktopTheme.Apply(owner.Settings.ThemeMode); window.Width = 880; Render(); return checks;
    }
    private static byte[] Png(int width, int height, int seed)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
        using (var pixels = bitmap.Lock())
        {
            var row = new byte[pixels.RowBytes];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++) { var distance = Math.Sqrt(Math.Pow((x - width * .62) / width, 2) + Math.Pow((y - height * .36) / height, 2)); bool circle = distance < .24; row[x * 4] = (byte)(circle ? 226 : 95 + (seed * 29 + y * 40 / height) % 130); row[x * 4 + 1] = (byte)(circle ? 225 : 75 + (seed * 37 + x * 50 / width) % 145); row[x * 4 + 2] = (byte)(circle ? 238 : 100 + (seed * 43 + y * 40 / height) % 130); row[x * 4 + 3] = 255; }
                Marshal.Copy(row, 0, pixels.Address + y * pixels.RowBytes, row.Length);
            }
        }
        using var output = new MemoryStream(); bitmap.Save(output); return output.ToArray();
    }
}
