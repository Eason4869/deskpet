using Dafeiyu;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Media;

namespace DeskPet.Desktop;

internal static class ProcessExpander
{
    public static void Apply(Expander expander)
    {
        expander.Template = new FuncControlTemplate<Expander>((owner, scope) =>
        {
            var toggle = new ToggleButton { Name = "ProcessToggle", Padding = new Thickness(0, 3), Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Left };
            Avalonia.Automation.AutomationProperties.SetName(toggle, TextResources.Get("processExpander.apply.automationAutomationPropertiesSetName"));
            toggle.Bind(ContentControl.ContentProperty, new Binding(nameof(Expander.Header)) { Source = owner });
            toggle.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(Expander.IsExpanded)) { Source = owner, Mode = BindingMode.TwoWay });
            toggle.Template = new FuncControlTemplate<ToggleButton>((button, _) =>
            {
                var content = new ContentPresenter(); content.Bind(ContentPresenter.ContentProperty, new TemplateBinding(ContentControl.ContentProperty));
                var chevron = new Avalonia.Controls.Shapes.Path { Name = "ProcessChevron", Data = Geometry.Parse("M 0,0 L 4,4 L 0,8"), Width = 6, Height = 10, Stroke = DesktopTheme.Brush("#969BA3"), StrokeThickness = 1.2, Margin = new Thickness(6, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0, RenderTransformOrigin = RelativePoint.Center };
                void Refresh() { chevron.Opacity = button.IsPointerOver ? 1 : 0; chevron.RenderTransform = new RotateTransform(button.IsChecked == true ? 90 : 0); }
                button.PropertyChanged += (_, e) => { if (e.Property == InputElement.IsPointerOverProperty || e.Property == InputElement.IsKeyboardFocusWithinProperty || e.Property == ToggleButton.IsCheckedProperty) Refresh(); }; Refresh();
                return new Border { Background = Brushes.Transparent, Padding = button.Padding, Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { content, chevron } } };
            });
            var details = new ContentPresenter { Name = "ProcessContent", Margin = new Thickness(0, 5, 0, 0) };
            details.Bind(ContentPresenter.ContentProperty, new Binding(nameof(Expander.Content)) { Source = owner });
            details.Bind(Visual.IsVisibleProperty, new Binding(nameof(Expander.IsExpanded)) { Source = owner });
            return new StackPanel { Children = { toggle, details } };
        });
    }
}
