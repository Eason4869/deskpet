using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class ThemeModePicker : UserControl
{
    private readonly Dictionary<string, Button> choices = new();
    private string value = "system";
    internal event Action<string>? Changed;
    internal string Value
    {
        get => value;
        set
        {
            this.value = ThemeColors.Normalize(value);
            foreach (var pair in choices) pair.Value.BorderBrush = pair.Key == this.value ? DesktopTheme.Brush("#456DE6") : Brushes.Transparent;
        }
    }
    internal ThemeModePicker(string initial)
    {
        Name = "ThemeModePicker";
        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        layout.Children.Add(new TextBlock { Text = "模式", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var mode in new[] { "system", "light", "dark" })
        {
            var title = mode == "system" ? "跟随系统" : mode == "light" ? "浅色" : "暗色";
            var button = new Button { Name = "ThemeChoice" + mode, Content = Preview(mode), Width = 84, Height = 64, Padding = new Thickness(4), Background = Brushes.Transparent, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(10) };
            button.Classes.Add("theme-choice");
            ToolTip.SetTip(button, title); Avalonia.Automation.AutomationProperties.SetName(button, title);
            button.Click += (_, _) => { Value = mode; Changed?.Invoke(mode); }; choices.Add(mode, button); row.Children.Add(button);
        }
        Grid.SetColumn(row, 1); layout.Children.Add(row);
        Content = new Border { CornerRadius = new CornerRadius(16), Background = DesktopTheme.Brush("#FDFEFF"), BorderBrush = DesktopTheme.Brush("#DCE5F4"), BorderThickness = new Thickness(1), Padding = new Thickness(14, 8), Child = layout };
        Value = initial;
    }
    private static Control Preview(string mode)
    {
        var grid = new Grid { Width = 72, Height = 52, Clip = new RectangleGeometry(new Rect(0, 0, 72, 52), 6, 6) };
        if (mode != "dark") grid.Children.Add(Sketch(false));
        if (mode != "light")
        {
            var dark = Sketch(true);
            if (mode == "system") dark.Clip = new RectangleGeometry(new Rect(36, 0, 36, 52));
            grid.Children.Add(dark);
        }
        return grid;
    }
    private static Canvas Sketch(bool dark)
    {
        var canvas = new Canvas { Width = 72, Height = 52, Background = Fixed(dark ? "#192332" : "#FFFFFF") };
        Block(canvas, 0, 0, 11, 52, dark ? "#111A27" : "#F0F2F5", 0); Block(canvas, 11, 0, 12, 52, dark ? "#222E40" : "#F7F8FA", 0);
        Block(canvas, 4, 6, 4, 4, dark ? "#75839A" : "#BBC3CF", 2); Block(canvas, 4, 15, 4, 2, dark ? "#53627A" : "#D6DBE3", 1);
        Block(canvas, 27, 7, 38, 2, dark ? "#7295D6" : "#7198D8", 1); Block(canvas, 39, 14, 26, 2, dark ? "#53647D" : "#D9DFE9", 1);
        Block(canvas, 29, 20, 30, 2, dark ? "#718098" : "#C3CDDC", 1); Block(canvas, 40, 27, 25, 2, "#4887EF", 1);
        Block(canvas, 27, 34, 38, 2, dark ? "#53647D" : "#D9DFE9", 1); Block(canvas, 27, 42, 39, 6, dark ? "#2A384C" : "#EDF1F7", 2); Block(canvas, 60, 44, 3, 3, "#4887EF", 1.5);
        return canvas;
    }
    private static SolidColorBrush Fixed(string color) => new(Color.Parse(color));
    private static void Block(Canvas canvas, double x, double y, double width, double height, string color, double radius)
    {
        var block = new Border { Width = width, Height = height, Background = Fixed(color), CornerRadius = new CornerRadius(radius) }; Canvas.SetLeft(block, x); Canvas.SetTop(block, y); canvas.Children.Add(block);
    }
}
