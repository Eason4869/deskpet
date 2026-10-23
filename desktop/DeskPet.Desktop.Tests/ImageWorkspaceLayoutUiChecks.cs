using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using DeskPet.Desktop;

internal static class ImageWorkspaceLayoutUiChecks
{
    internal static int Capture(DesktopController owner, ChatWindow window, ImageWorkspacePage workspace, string phase)
    {
        int checks = 0;
        var output = Path.GetFullPath("work/image-workspace-avalonia"); Directory.CreateDirectory(output);
        workspace.SelectedIndex = 0;
        foreach (var width in new[] { 430, 960 }) foreach (var theme in new[] { "light", "dark" })
        {
            window.Width = width; window.Height = 760; DesktopTheme.Apply(theme); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var controls = workspace.GetLogicalDescendants().OfType<Control>().Distinct().ToArray();
            var preview = controls.Single(c => c.Name == "ImageWorkspacePreview");
            var parameters = controls.Single(c => c.Name == "ImageGenerationParameters");
            var composer = controls.Single(c => c.Name == "ImagePromptComposer");
            var upload = controls.OfType<Button>().Single(c => c.Name == "UploadImageReference");
            var submit = controls.OfType<Button>().Single(c => c.Name == "StartImageGeneration");
            var uploadPoint = upload.TranslatePoint(default, composer)!.Value; var submitPoint = submit.TranslatePoint(default, composer)!.Value;
            if (preview.Bounds.Height < 170 || uploadPoint.X >= submitPoint.X || submitPoint.X + submit.Bounds.Width > composer.Bounds.Width || composer.Bounds.Width > workspace.Bounds.Width) throw new Exception("FAIL image generation measured bounds " + phase + theme + width + " preview=" + preview.Bounds + " composer=" + composer.Bounds);
            if (width == 960 && preview.Bounds.Width <= parameters.Bounds.Width) throw new Exception("FAIL image preview must be larger than parameter rail");
            using var frame = window.CaptureRenderedFrame(); if (frame?.PixelSize.Width != width) throw new Exception("FAIL native image workspace frame");
            frame.Save(Path.Combine(output, "generation-" + phase + "-" + theme + "-" + width + ".png"), new PngBitmapEncoderOptions()); checks += 2;
        }
        window.Width = 880; DesktopTheme.Apply(owner.Settings.ThemeMode); Dispatcher.UIThread.RunJobs(); return checks;
    }
    internal static int CheckStructure(ImageWorkspacePage workspace)
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception("FAIL: image workspace layout " + label); checks++; }
        var controls = workspace.GetLogicalDescendants().OfType<Control>().Distinct().ToArray();
        Check(controls.Any(c => c.Name == "ImageGenerationParameters") && controls.Any(c => c.Name == "ImageWorkspacePreview"), "service parameters and large preview have dedicated surfaces");
        var composer = controls.OfType<Border>().Single(c => c.Name == "ImagePromptComposer");
        Check(composer.CornerRadius.TopLeft >= 20 && composer.GetLogicalDescendants().OfType<Button>().Any(b => b.Name == "UploadImageReference" && b.Content as string == "+") && composer.GetLogicalDescendants().OfType<Button>().Any(b => b.Name == "StartImageGeneration"), "rounded composer contains reference upload on left and submit on right");
        return checks;
    }
}
