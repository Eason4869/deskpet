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
        var shapes = new[] { (320, 560), (600, 320), (400, 400), (400, 610), (640, 360), (420, 520), (460, 340) };
        for (int i = 0; i < shapes.Length; i++) owner.ImageStore.Save(Png(shapes[i].Item1, shapes[i].Item2, i), new GeneratedImage { Prompt = "layout fixture " + i, Model = "layout-model" }, true);
        Render(); page.RefreshGallery(); Render();
        Button Toggle(string name) => page.GetLogicalDescendants().OfType<Button>().Distinct().Single(b => b.Name == name);
        Toggle("GalleryMasonryLayout").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.Width = 960; Render();
        Check(owner.Settings.ImageGalleryLayout == "masonry" && page.GalleryCount == shapes.Length, "masonry is selected and includes every filtered thumbnail");
        var tiles = page.GalleryPanel.Children.OfType<GeneratedImageCard>().ToArray();
        Check(tiles.All(t => t.IsGalleryTile && t.GetLogicalDescendants().OfType<Image>().Single().Stretch == Stretch.Uniform), "masonry uses full image aspect ratios");
        Check(tiles.All(t => !t.GetLogicalDescendants().OfType<TextBlock>().Any(text => !string.IsNullOrEmpty(text.Text) && text.Text != "⋯") && !t.GetLogicalDescendants().OfType<WrapPanel>().Any() && t.GetLogicalDescendants().OfType<Button>().Distinct().Count() == 2), "gallery contains image tiles and ellipsis only without metadata or full action rows");
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
        Render(); Check(page.GalleryCount == 40, "gallery initially attaches forty filtered tiles");
        Toggle("LoadMoreGalleryImages").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Render(); Check(page.GalleryCount == 43, "load more exposes remaining images");
        page.Search.Text = "layout fixture 42"; Render(); Check(page.GalleryCount == 1, "filter resets pagination and finds older assets");
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
