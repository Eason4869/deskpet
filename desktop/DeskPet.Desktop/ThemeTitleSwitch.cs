using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Dafeiyu;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace DeskPet.Desktop;

internal sealed class ThemeTitleSwitch : Grid
{
    private const double OptionSize = 28;
    private readonly Button lightButton, darkButton;
    private readonly ShapePath systemIcon, lightIcon, darkIcon;
    private readonly TranslateTransform thumbOffset = new();
    private readonly Transitions slide = new()
    {
        new DoubleTransition
        {
            Property = TranslateTransform.XProperty,
            Duration = TimeSpan.FromMilliseconds(190),
            Easing = new CubicEaseOut(),
        },
    };
    private string mode = "system";
    private bool dark;

    internal Button SystemButton { get; }
    internal Border Capsule { get; }
    internal event Action<string, Button>? Selected;

    internal ThemeTitleSwitch()
    {
        Name = "AppThemeMode";
        VerticalAlignment = VerticalAlignment.Center;
        ColumnDefinitions = new ColumnDefinitions("Auto,Auto");
        ColumnSpacing = 4;
        LocalizedUi.Automation(this, () => TextResources.Get("quietPanel.quietPanel.text"));

        SystemButton = Option("system", "AppThemeSystem", out systemIcon);
        lightButton = Option("light", "AppThemeLight", out lightIcon);
        darkButton = Option("dark", "AppThemeDark", out darkIcon);
        var thumb = new Border
        {
            Name = "AppThemeThumb",
            Width = OptionSize,
            Height = OptionSize,
            CornerRadius = new CornerRadius(OptionSize / 2),
            Background = DesktopTheme.CardSurface,
            BorderBrush = DesktopTheme.CardBorder,
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            RenderTransform = thumbOffset,
        };
        var options = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
            Children = { thumb, lightButton, darkButton },
        };
        SetColumn(darkButton, 1);
        Capsule = new Border
        {
            Background = DesktopTheme.Brush("#E8EBF0"),
            BorderBrush = DesktopTheme.CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(17),
            Padding = new Thickness(2),
            Child = options,
        };
        SetColumn(Capsule, 1);
        Children.Add(SystemButton);
        Children.Add(Capsule);
        Refresh(DesktopTheme.Mode, DesktopTheme.IsDark, false);
    }

    private Button Option(string choice, string name, out ShapePath icon)
    {
        icon = new ShapePath
        {
            Data = Geometry.Parse(ThemeSwitch.Icon(choice)),
            StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
        };
        var button = new Button
        {
            Name = name,
            Width = OptionSize,
            Height = OptionSize,
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(OptionSize / 2),
            Content = new Viewbox
            {
                Width = 16,
                Height = 16,
                IsHitTestVisible = false,
                Child = new Canvas { Width = 24, Height = 24, Children = { icon } },
            },
        };
        Func<string> caption = choice == "system" ? SystemCaption : () => ThemeSwitch.Label(choice);
        LocalizedUi.Tooltip(button, caption);
        LocalizedUi.Automation(button, caption);
        button.Click += (_, _) => Selected?.Invoke(choice, button);
        return button;
    }

    internal void Refresh(string mode, bool dark, bool animate)
    {
        this.mode = ThemeColors.Normalize(mode);
        this.dark = dark;
        bool system = this.mode == "system";
        bool darkSide = this.mode == "dark" || (system && dark);
        thumbOffset.Transitions = animate ? slide : null;
        thumbOffset.X = darkSide ? OptionSize : 0;

        var ink = DesktopTheme.Brush("#203754");
        var muted = DesktopTheme.Brush("#777F89");
        SystemButton.Background = system ? DesktopTheme.Brush("#EDF3FF") : Brushes.Transparent;
        systemIcon.Stroke = system ? ink : muted;
        lightIcon.Stroke = darkSide ? muted : ink;
        darkIcon.Stroke = darkSide ? ink : muted;

        LocalizedUi.Tooltip(SystemButton, SystemCaption);
        LocalizedUi.Automation(SystemButton, SystemCaption);
        ItemStatus(SystemButton, "system");
        ItemStatus(lightButton, "light");
        ItemStatus(darkButton, "dark");
    }

    private string SystemCaption()
    {
        string caption = ThemeSwitch.Label("system");
        return mode == "system" ? caption + " · " + ThemeSwitch.Label(dark ? "dark" : "light") : caption;
    }

    private void ItemStatus(Button button, string choice)
    {
        LiveTextBindings.Bind(button, "AutomationItemStatus",
            () => mode == choice ? TextResources.Get("themeModePicker.value.automationAutomationPropertiesSetItemStatus") :
                TextResources.Get("themeModePicker.value.automationAutomationPropertiesSetItemStatus.alternativeA"),
            target => AutomationProperties.GetItemStatus((StyledElement)target),
            (target, value) => AutomationProperties.SetItemStatus((StyledElement)target, (string)value));
    }
}
