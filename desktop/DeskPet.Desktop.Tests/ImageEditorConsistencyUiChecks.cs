using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;

internal static class ImageEditorConsistencyUiChecks
{
    internal static int Run(DesktopController owner)
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception("FAIL: image editor consistency " + label); checks++; }
        void Render() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
        var host = new Window { Width = 570, Height = 920 }; using var text = new ProviderEditor(owner, "text"); using var image = new ImageProviderEditor(owner, host);
        var scroll = new ScrollViewer { Content = image }; host.Content = new Border { Padding = new Thickness(20), Child = scroll }; host.Show(); Render();
        Check(image.Spacing == text.Spacing && image.Spacing == 12, "form uses conversation editor spacing");
        var textRow = (WrapPanel)text.Children[0]; var imageRow = (WrapPanel)image.Children[0];
        Check(imageRow.Orientation == textRow.Orientation && imageRow.Orientation == Orientation.Horizontal && imageRow.ItemSpacing == textRow.ItemSpacing && imageRow.LineSpacing == textRow.LineSpacing && imageRow.Children.Select(c=>c.Margin).SequenceEqual(textRow.Children.Select(c=>c.Margin)), "provider selector and actions share the same wrapping row spacing");
        Check(((ComboBox)imageRow.Children[0]).MinWidth == ((ComboBox)textRow.Children[0]).MinWidth && imageRow.Children.OfType<Button>().Select(b => b.Content as string).SequenceEqual(textRow.Children.OfType<Button>().Select(b => b.Content as string)), "selector width and add/delete labels match conversation settings");
        Check(((TextBlock)image.Children[1]).Text == ((TextBlock)text.Children[1]).Text && ((TextBlock)image.Children[1]).FontSize == ((TextBlock)text.Children[1]).FontSize, "draft guidance has matching text and typography");
        Check(((ComboBox)image.Children[2]).PlaceholderText == ((ComboBox)text.Children[2]).PlaceholderText, "both editors use the same service preset prompt");
        var labels = image.Children.OfType<StackPanel>().Where(p => p.Children.FirstOrDefault() is TextBlock).Select(p => ((TextBlock)p.Children[0]).Text).ToArray();
        Check(labels.SequenceEqual(new[] { "厂商名称", "服务地址", "API Key（保存在系统密钥保护下）", "协议", "代理地址", "模型 ID（可手填）" }), "common fields follow conversation naming and order");
        Check(image.Children.Last() is Button imageSave && text.Children.Last() is Button textSave && imageSave.Content as string == textSave.Content as string, "save/enable action matches text model footer");
        Check(image.Children.OfType<WrapPanel>().Any(row => row.Children.OfType<CheckBox>().Select(c => c.Content as string).SequenceEqual(new[] { "此厂商使用独立代理", "支持参考图编辑" })), "independent proxy and reference capability share the options row");
        var parameters = image.GetLogicalDescendants().OfType<Expander>().Distinct().Single(e => e.Name == "ImageProviderParameters");
        Check(!parameters.IsExpanded && parameters.Header is TextBlock { Text: "图片生成设置" }, "image specific defaults are grouped and initially collapsed");
        var before = owner.Settings.ImageProviders.Count; var drafts = image.GetLogicalDescendants().OfType<ComboBox>().Distinct().Single(c => c.Name == "ImageProviderList"); var count = drafts.Items.Count;
        imageRow.Children.OfType<Button>().Single(b => b.Name == "AddImageProvider").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(drafts.Items.Count == count + 1 && owner.Settings.ImageProviders.Count == before, "add uses isolated draft like conversation editor");
        imageRow.Children.OfType<Button>().Single(b => b.Name == "DeleteImageProvider").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Render();var confirmation=host.OwnedWindows.Single();Check(confirmation.GetLogicalDescendants().OfType<TextBlock>().Any(t=>t.Text!=null&&t.Text.Contains("0 个未完成")),"provider delete confirms unfinished task count");confirmation.GetLogicalDescendants().OfType<Button>().Single(b=>b.Content as string=="取消任务并删除").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Render(); Check(drafts.Items.Count == count && owner.Settings.ImageProviders.Count == before, "delete uses isolated draft like conversation editor");
        var preset = image.GetLogicalDescendants().OfType<ComboBox>().Distinct().Single(c => c.Name == "ImageServicePreset"); preset.SelectedItem = preset.ItemsSource!.Cast<ImageServicePreset>().Single(p => p.Protocol == "gemini");
        var artifacts = Path.Combine(AppContext.BaseDirectory, "artifacts"); Directory.CreateDirectory(artifacts);
        foreach (var theme in new[] { "light", "dark" })
        {
            DesktopTheme.Apply(theme); scroll.Content = image; scroll.Offset = default; Render(); using (var screenshot = host.CaptureRenderedFrame()) { Check(screenshot != null && screenshot.PixelSize.Width == 570, "narrow image provider form renders in " + theme); screenshot!.Save(Path.Combine(artifacts, "image-provider-consistent-570-" + theme + ".png")); }
            scroll.Content = null; scroll.Content = text; scroll.Offset = default; Render(); using (var reference = host.CaptureRenderedFrame()) reference!.Save(Path.Combine(artifacts, "text-provider-reference-570-" + theme + ".png"));
            scroll.Content = null;
        }
        scroll.Content = image; parameters.IsExpanded = true; Render();
        Check(parameters.GetLogicalDescendants().OfType<NumericUpDown>().Any(c => c.Name == "ImageProviderTimeout") && parameters.GetLogicalDescendants().OfType<ComboBox>().Distinct().Count() == 2 && parameters.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Name == "ImagePresetNotes"), "expanded image settings retain timeout size quality and native service notes");
        scroll.Offset = new Vector(0, scroll.Extent.Height); Render(); using (var expanded = host.CaptureRenderedFrame()) expanded!.Save(Path.Combine(artifacts, "image-provider-settings-expanded-570-dark.png"));
        DesktopTheme.Apply(owner.Settings.ThemeMode); host.Close(); Render(); return checks;
    }
}
