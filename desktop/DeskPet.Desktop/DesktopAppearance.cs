using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Layout;
using Avalonia.Styling;

namespace DeskPet.Desktop;

internal static class DesktopAppearance
{
    internal static readonly Color HoverColor = Color.Parse("#F0F1F3");
    internal static readonly IBrush Hover = DesktopTheme.Brush("#F0F1F3");
    private static readonly CornerRadius Radius = new(8);
    public static void Install(Application app)
    {
        app.Resources["ControlCornerRadius"] = Radius;
        Add(app.Styles, s => s.OfType<TextBox>(), new Setter(TextBox.CaretBrushProperty, DesktopTheme.Caret));
        Add(app.Styles, s => s.OfType<Window>(), new Setter(TemplatedControl.BackgroundProperty, DesktopTheme.Surface));
        app.Resources["OverlayCornerRadius"] = new CornerRadius(10);
        app.Resources["ControlFocusVisualPrimaryThickness"] = new Thickness(0);
        app.Resources["ControlFocusVisualSecondaryThickness"] = new Thickness(0);
        app.Resources["TextControlBorderBrushFocused"] = DesktopTheme.Brush("#D5D9DF");
        app.Resources["TextControlBorderBrushPointerOver"] = DesktopTheme.Brush("#D5D9DF");
        app.Resources["TextControlBorderThemeThicknessFocused"] = new Thickness(1);
        app.Resources["ScrollBarSize"] = 12d;
        foreach (var key in new[] { "ScrollBarBackground", "ScrollBarBackgroundPointerOver", "ScrollBarBorderBrush", "ScrollBarTrackFill", "ScrollBarTrackStroke", "ScrollBarTrackFillPointerOver", "ScrollBarTrackStrokePointerOver" }) app.Resources[key] = Brushes.Transparent;
        foreach (var key in new[] { "ScrollBarPanningThumbBackground", "ScrollBarThumbBackgroundColor" }) app.Resources[key] = DesktopTheme.Brush("#C5C9D0");
        app.Resources["ScrollBarThumbFillPointerOver"] = DesktopTheme.Brush("#3B3B3B");
        app.Resources["ScrollBarThumbFillPressed"] = DesktopTheme.Brush("#222222");
        app.Resources["VerticalSmallScrollThumbScaleTransform"] = new ScaleTransform(1, 1);
        app.Resources["HorizontalSmallScrollThumbScaleTransform"] = new ScaleTransform(1, 1);
        var styles = new Styles();
        Add(styles, s => s.Is<Control>(), new Setter(Control.FocusAdornerProperty, null));
        Add(styles, s => s.Is<Button>(), new Setter(TemplatedControl.CornerRadiusProperty, Radius), new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)));
        Add(styles, s => s.Is<ComboBox>(), new Setter(TemplatedControl.CornerRadiusProperty, Radius), new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)));
        Add(styles, s => s.OfType<ContextMenu>(), new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(10)), new Setter(TemplatedControl.PaddingProperty, new Thickness(4)), new Setter(TemplatedControl.BackgroundProperty, DesktopTheme.Surface));
        Add(styles, s => s.Is<MenuItem>(), new Setter(TemplatedControl.CornerRadiusProperty, Radius), new Setter(TemplatedControl.PaddingProperty, new Thickness(10, 6)));
        Add(styles, s => s.Is<ScrollBar>(), new Setter(ScrollBar.AllowAutoHideProperty, false), new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent), new Setter(TemplatedControl.BorderBrushProperty, Brushes.Transparent));
        foreach (var name in new[] { "PART_LineUpButton", "PART_LineDownButton" }) Add(styles, s => s.Is<ScrollBar>().Template().OfType<RepeatButton>().Name(name), new Setter(Visual.IsVisibleProperty, false));
        Add(styles, s => s.Is<ScrollBar>().Class(":vertical").Template().OfType<Thumb>(), new Setter(TemplatedControl.TemplateProperty, ThumbTemplate(true)));
        Add(styles, s => s.Is<ScrollBar>().Class(":horizontal").Template().OfType<Thumb>(), new Setter(TemplatedControl.TemplateProperty, ThumbTemplate(false)));
        Add(styles, s => s.Is<ScrollBar>().Class(":pointerover").Template().OfType<Thumb>().Template().OfType<Border>(), new Setter(Border.BackgroundProperty, DesktopTheme.Brush("#3B3B3B")));
        Add(styles, s => s.Is<ScrollBar>().Template().OfType<Thumb>().Class(":pressed").Template().OfType<Border>(), new Setter(Border.BackgroundProperty, DesktopTheme.Brush("#222222")));
        foreach (var state in new[] { ":pointerover", ":pressed", ":focus-visible" })
        {
            Add(styles, s => s.Is<Button>().Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
                new Setter(ContentPresenter.BackgroundProperty, Hover), new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent), new Setter(ContentPresenter.CornerRadiusProperty, Radius), new Setter(ContentPresenter.ForegroundProperty, new TemplateBinding(TemplatedControl.ForegroundProperty)));
            Add(styles, s => s.Is<Button>().Class("theme-choice").Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"),
                new Setter(ContentPresenter.BorderBrushProperty, new TemplateBinding(TemplatedControl.BorderBrushProperty)), new Setter(ContentPresenter.CornerRadiusProperty, new CornerRadius(10)));
            Add(styles, s => s.Is<ComboBox>().Class(state).Template().OfType<Border>().Name("Background"),
                new Setter(Border.BackgroundProperty, Hover), new Setter(Border.BorderBrushProperty, Brushes.Transparent), new Setter(Border.CornerRadiusProperty, Radius));
            Add(styles, s => s.Is<ComboBox>().Class(state).Template().OfType<Border>().Name("HighlightBackground"),
                new Setter(Border.BackgroundProperty, Hover), new Setter(Border.BorderBrushProperty, Brushes.Transparent), new Setter(Border.CornerRadiusProperty, Radius));
            Add(styles, s => s.Is<ComboBox>().Class(state).Template().OfType<ContentControl>().Name("ContentPresenter"), new Setter(TemplatedControl.ForegroundProperty, new TemplateBinding(TemplatedControl.ForegroundProperty)));
            Add(styles, s => s.Is<ComboBoxItem>().Class(state).Template().OfType<ContentPresenter>(), new Setter(ContentPresenter.BackgroundProperty, Hover), new Setter(ContentPresenter.BorderBrushProperty, Brushes.Transparent), new Setter(ContentPresenter.CornerRadiusProperty, Radius));
            Add(styles, s => s.Is<CheckBox>().Class(state).Template().OfType<Border>().Name("PART_Border"), new Setter(Border.BackgroundProperty, Hover), new Setter(Border.BorderBrushProperty, Brushes.Transparent), new Setter(Border.CornerRadiusProperty, Radius));
            Add(styles, s => s.Is<RadioButton>().Class(state).Template().OfType<Border>().Name("PART_Border"), new Setter(Border.BackgroundProperty, Hover), new Setter(Border.BorderBrushProperty, Brushes.Transparent), new Setter(Border.CornerRadiusProperty, Radius));
            Add(styles, s => s.Is<ToggleSwitch>().Class(state).Template().OfType<Border>().Name("OuterBorder"), new Setter(Border.BackgroundProperty, Hover), new Setter(Border.BorderBrushProperty, Brushes.Transparent));
            Add(styles, s => s.Is<MenuItem>().Class(state).Template().OfType<Border>().Name("PART_LayoutRoot"), new Setter(Border.BackgroundProperty, Hover), new Setter(Border.BorderBrushProperty, Brushes.Transparent), new Setter(Border.CornerRadiusProperty, Radius));
            // The send action keeps its established semantic blue in every interaction state.
            Add(styles, s => s.OfType<Button>().Name("PrimaryChatAction").Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"), new Setter(ContentPresenter.BackgroundProperty, DesktopTheme.Brush("#357FF5")), new Setter(ContentPresenter.CornerRadiusProperty, new CornerRadius(16)));
        }
        Add(styles, s => s.Is<MenuItem>().Class(":selected").Template().OfType<Border>().Name("PART_LayoutRoot"), new Setter(Border.BackgroundProperty, Hover), new Setter(Border.BorderBrushProperty, Brushes.Transparent), new Setter(Border.CornerRadiusProperty, Radius));
        app.Styles.Add(styles);
    }
    private static void Add(Styles styles, Func<Selector?, Selector> selector, params Setter[] setters)
    {
        var style = new Style(selector); foreach (var setter in setters) style.Setters.Add(setter); styles.Add(style);
    }
    private static FuncControlTemplate<Thumb> ThumbTemplate(bool vertical) => new((thumb, _) =>
    {
        var border = new Border { Name = "SlimScrollThumb", CornerRadius = new CornerRadius(3), HorizontalAlignment = vertical ? HorizontalAlignment.Center : HorizontalAlignment.Stretch, VerticalAlignment = vertical ? VerticalAlignment.Stretch : VerticalAlignment.Center, Width = vertical ? 6 : double.NaN, Height = vertical ? double.NaN : 6 };
        border.Bind(Border.BackgroundProperty, new TemplateBinding(TemplatedControl.BackgroundProperty));
        return new Panel { Name = "SlimScrollHitArea", Background = Brushes.Transparent, Children = { border } };
    });
}
