using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dafeiyu;
using DeskPet.Desktop;

internal static class ComposerLayoutUiChecks
{
    public static int Run(string directory)
    {
        int assertions = 0;
        void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); assertions++; }
        using var owner = new DesktopController(new ClassicDesktopStyleApplicationLifetime(), new TestRuntime(directory));
        var provider = owner.Settings.TextProviders.First();
        const string longModel = "organization/very-long-model-identifier-with-version-and-capability-suffix-2026-11-02";
        provider.Models.Add(longModel);
        owner.Settings.SelectTextModel(provider.Id, longModel);
        var window = new ChatWindow(owner, true) { Height = 620 };
        string originalTheme = owner.Settings.ThemeMode;
        string originalLanguage = TextResources.CurrentLanguage;
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            T Named<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
            var toolbar = Named<Grid>("ComposerToolbar");
            var model = Named<ComboBox>("ChatModels");
            var fallback = Named<Button>("ImageDescriptionMenu");
            var mode = Named<Button>("ChatModeAction");
            var mic = Named<Button>("ChatDictation");
            var send = Named<Button>("PrimaryChatAction");
            Control Slot(Control child) => child.GetVisualAncestors().TakeWhile(v => v != toolbar).OfType<Control>().LastOrDefault() ?? child;
            Check(Grid.GetColumn(Slot(model)) == 2 && Grid.GetColumn(Slot(fallback)) == 2 && toolbar.ColumnDefinitions[2].Width.IsStar,
                "text and image-description pickers share the flexible outer model slot");
            Check(model.SelectedItem?.ToString()?.Contains(longModel) == true, "geometry fixture displays the actual long selected provider model");
            Rect Bounds(Control child) => new(child.TranslatePoint(default, toolbar)!.Value, child.Bounds.Size);
            var samples = new List<(string Theme, int Width, Rect Mode, Rect Model, Rect Fallback, Rect Mic, Rect Send)>();
            foreach (string theme in new[] { "light", "dark" })
            {
                DesktopTheme.Apply(theme);
                foreach (int width in new[] { 470, 390 })
                {
                    window.Width = width; window.Measure(new Size(width, 620)); window.Arrange(new Rect(0, 0, width, 620));
                    Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    var sample = (theme, width, Bounds(mode), Bounds(model), Bounds(fallback), Bounds(mic), Bounds(send));
                    samples.Add(sample);
                    Console.WriteLine($"COMPOSER {theme} window={width} toolbar={toolbar.Bounds.Width} mode={sample.Item3} model={sample.Item4} fallback={sample.Item5} mic={sample.Item6} send={sample.Item7}");
                    var artifacts = Path.Combine(AppContext.BaseDirectory, "artifacts"); Directory.CreateDirectory(artifacts);
                    using var frame = window.CaptureRenderedFrame();
                    Check(frame != null, "composer renders " + theme + " at " + width);
                    frame!.Save(Path.Combine(artifacts, $"chat-composer-long-{width}-{theme}.png"), new PngBitmapEncoderOptions());
                }
            }
            foreach (var s in samples)
            {
                Check(s.Mode.Width == 104 && s.Mic.Width == 32 && s.Send.Width == 32, "fixed composer controls retain their widths " + s.Theme + " " + s.Width);
                Check(s.Model.Width > 0 && s.Fallback.Width > 0 && s.Mode.Right <= s.Model.Left + 1 && s.Model.Right <= s.Fallback.Left + 1 && s.Fallback.Right <= s.Mic.Left + 1,
                    "long model and fallback do not overlap fixed composer controls " + s.Theme + " " + s.Width);
                Check(s.Width != 390 || s.Model.Width >= 60 && s.Fallback.Width == 32,
                    "minimum-width composer reserves a useful primary model label and compact fallback target " + s.Theme);
            }
            Check(samples[1].Model.Width < samples[0].Model.Width && samples[3].Model.Width < samples[2].Model.Width,
                "selected long model shrinks when the composer reaches its minimum width");
            var menu = fallback.ContextMenu;
            Check(fallback.Content is Avalonia.Controls.Shapes.Path && fallback.Bounds.Height == 32,
                "compact fallback uses a themed glyph in a 32 pixel target");
            foreach (string language in new[] { "en_US", "zh_CN" })
            {
                TextResources.ApplyLanguage(TextResources.PrepareLanguage(language)); Dispatcher.UIThread.RunJobs();
                Check(AutomationProperties.GetName(fallback) == TextResources.Get("imageDescription.title") && ToolTip.GetTip(fallback)?.ToString() == TextResources.Get("imageDescription.title"),
                    "compact fallback keeps its live localized accessible name and tooltip " + language);
            }
            fallback.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
            Check(ReferenceEquals(fallback.ContextMenu, menu) && menu!.IsOpen && menu.Items.Count >= 2,
                "compact fallback opens the existing per-conversation menu");
            menu!.Close();
            window.Width = 470; window.Measure(new Size(470, 620)); window.Arrange(new Rect(0, 0, 470, 620)); Dispatcher.UIThread.RunJobs();
            Check(fallback.Content is TextBlock caption && caption.Text == TextResources.Get("imageDescription.title") && ReferenceEquals(fallback.ContextMenu, menu),
                "widening restores the translated full caption without replacing the fallback menu");
            return assertions;
        }
        finally
        {
            window.Close(); owner.Pet.Close(); DesktopTheme.Apply(originalTheme);
            TextResources.ApplyLanguage(TextResources.PrepareLanguage(originalLanguage)); Dispatcher.UIThread.RunJobs();
        }
    }
}
