using Dafeiyu;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;

namespace DeskPet.Desktop;

internal static class Ui
{
    public static StackPanel Column(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 10 };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }
    public static StackPanel Row(params Control[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }
    public static TextBlock Text(string value, double size = 14) => new() { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap };
    public static TextBox Input(string value = "", string? hint = null) => new() { Text = value, PlaceholderText = hint, MinWidth = 200 };
    public static Button Button(string label, Action action)
    {
        var button = new Button { Content = label };
        button.Click += (_, _) => action();
        return button;
    }
    public static Button AsyncButton(string label, Func<Task> action, Action<Exception> error)
    {
        var button = new Button { Content = label };
        button.Click += async (_, _) => { button.IsEnabled = false; try { await action(); } catch (OperationCanceledException) { } catch (Exception e) { error(e); } finally { button.IsEnabled = true; } };
        return button;
    }
    public static CheckBox Check(string label, bool value, Action<bool> changed)
    {
        var check = new CheckBox { Content = label, IsChecked = value };
        check.IsCheckedChanged += (_, _) => changed(check.IsChecked == true);
        return check;
    }
    public static Window Window(string title, Control content, double width = 650, double height = 620) => new()
    {
        Title = title, Width = width, Height = height, MinWidth = Math.Min(360, width), MinHeight = 280,
        Content = new Border { Padding = new Thickness(20), Child = content }, WindowStartupLocation = WindowStartupLocation.CenterScreen
    };
    public static Control Field(string label, Control input) => Column(Text(label), input);
    public static void ReadOnlyMenu(SelectableTextBlock text)
    {
        var copy = LocalizedUi.With(new MenuItem { Header = TextResources.Get("common.copy"), InputGesture = new KeyGesture(Key.C, KeyModifiers.Control) },"Header",()=>(TextResources.Get("common.copy"))); copy.Click += (_, _) => text.Copy();
        var all = LocalizedUi.With(new MenuItem { Header = TextResources.Get("ui.readOnlyMenu.header"), InputGesture = new KeyGesture(Key.A, KeyModifiers.Control) },"Header",()=>(TextResources.Get("ui.readOnlyMenu.header"))); all.Click += (_, _) => text.SelectAll();
        var menu = new ContextMenu { ItemsSource = new object[] { copy, all } };
        menu.Opening += (_, _) => { copy.IsEnabled = text.CanCopy; all.IsEnabled = !string.IsNullOrEmpty(text.Text) || text.Inlines?.Count > 0; }; text.ContextMenu = menu;
    }
public static TextBlock Text(System.Func<string> value, double size = 14){return LocalizedUi.With(Text(value(),size),"Text",value);}
        public static Button Button(System.Func<string> label, Action action){return LocalizedUi.With(Button(label(),action),"Content",label);}
        public static Button AsyncButton(System.Func<string> label, Func<Task> action, Action<Exception> error){return LocalizedUi.With(AsyncButton(label(),action,error),"Content",label);}
        public static CheckBox Check(System.Func<string> label, bool value, Action<bool> changed){return LocalizedUi.With(Check(label(),value,changed),"Content",label);}}
