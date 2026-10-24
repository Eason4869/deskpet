using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Dafeiyu;
namespace DeskPet.Desktop;

internal static class DesktopTheme
{
    private static readonly Dictionary<string, SolidColorBrush> colors = new(StringComparer.OrdinalIgnoreCase);
    private static bool installed;
    private static readonly SolidColorBrush caret = new(Color.Parse("#203754"));
    internal static IBrush Caret => caret;
    internal static bool IsDark => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
    internal static IBrush Surface => Brush("#FFFFFF");
    internal static SolidColorBrush Brush(string light)
    {
        if (!colors.TryGetValue(light, out var brush)) colors.Add(light, brush = new SolidColorBrush(Color.Parse(ThemeColors.Resolve(light, IsDark))));
        return brush;
    }
    internal static void Apply(string mode)
    {
        var app = Application.Current;
        if (app == null) return;
        if (!installed) { app.ActualThemeVariantChanged += (_, _) => Refresh(); installed = true; }
        app.RequestedThemeVariant = ThemeColors.Normalize(mode) switch { "light" => ThemeVariant.Light, "dark" => ThemeVariant.Dark, _ => ThemeVariant.Default };
        Refresh();
    }
    private static void Refresh()
    {
        caret.Color = Color.Parse(IsDark ? "#FFFFFF" : "#203754");
        foreach (var pair in colors) pair.Value.Color = Color.Parse(ThemeColors.Resolve(pair.Key, IsDark));
    }
}
