using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Dafeiyu
{
    internal sealed class AppManagementWindow : Window
    {
        static readonly DataTemplate wrappingCaption = CreateWrappingCaption();

        internal AppManagementWindow(Window owner, string appName, FrameworkElement header, UIElement details,
            FrameworkElement feedback, double maxHeight)
        {
            Name = appName + "AppManagement";
            if (owner != null && owner.IsVisible) Owner = owner;
            double width = Owner == null ? 578 : Math.Max(326, Math.Min(578, Owner.ActualWidth - 24));
            LocalizedUi.Window(this, () => ManagementTitle(appName), width, maxHeight);
            MinWidth = 310;
            MinHeight = 270;
            MaxHeight = maxHeight;
            SizeToContent = SizeToContent.Height;

            var layout = new Grid { Margin = new Thickness(22, 20, 22, 20) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition());
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            header.Margin = new Thickness(0, 0, 26, 16);
            layout.Children.Add(header);

            var scroll = Ui.Scroll(details);
            scroll.Name = "AppManagementScroll";
            scroll.Padding = new Thickness(0, 0, 3, 0);
            Grid.SetRow(scroll, 1);
            layout.Children.Add(scroll);
            feedback.Margin = new Thickness(4, 10, 4, 0);
            Grid.SetRow(feedback, 2);
            layout.Children.Add(feedback);
            DialogUi.Apply(this, layout);

            var frame = (Border)Content;
            frame.Background = ThemeManager.Surface;
            frame.BorderBrush = ThemeManager.CardBorder;
            frame.CornerRadius = new CornerRadius(18);
            KeyboardNavigation.SetTabNavigation(frame, KeyboardNavigationMode.Cycle);
            KeyboardNavigation.SetControlTabNavigation(frame, KeyboardNavigationMode.Cycle);
            Closed += delegate { scroll.Content = null; layout.Children.Remove(feedback); };
        }

        static string ManagementTitle(string appName)
        {
            return TextResources.Format("apps.management.title", appName);
        }

        internal static Border CreateCard(string appName, FrameworkElement header, TextBlock status, out Button manage)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var summary = new StackPanel { Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            summary.Children.Add(header);
            status.Margin = new Thickness(46, 4, 0, 0);
            summary.Children.Add(status);
            row.Children.Add(summary);

            manage = LocalizedUi.Button(() => TextResources.Get("apps.manage"));
            manage.Name = "Manage" + appName + "App";
            manage.MinWidth = 72;
            manage.Margin = new Thickness(0);
            manage.VerticalAlignment = VerticalAlignment.Center;
            LocalizedUi.Automation(manage, () => ManagementTitle(appName));
            Grid.SetColumn(manage, 1);
            row.Children.Add(manage);
            return new Border
            {
                Child = row, Background = ThemeManager.CardSurface, BorderBrush = ThemeManager.CardBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
                Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 10)
            };
        }

        internal static StackPanel AddSection(Panel parent, Func<string> title)
        {
            var heading = LocalizedUi.Text(title, 13, Ui.PageInk);
            heading.FontWeight = FontWeights.SemiBold;
            heading.Margin = new Thickness(0, 0, 0, 5);
            var content = new StackPanel();
            var section = new StackPanel();
            section.Children.Add(heading);
            section.Children.Add(content);
            parent.Children.Add(new Border
            {
                Child = section, Background = ThemeManager.CardSurface, BorderBrush = ThemeManager.CardBorder,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 11, 14, 11), Margin = new Thickness(0, 0, 0, 10)
            });
            return content;
        }

        static DataTemplate CreateWrappingCaption()
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding());
            text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            return new DataTemplate { VisualTree = text };
        }

        internal static void AddActions(Panel parent, params Button[] buttons)
        {
            var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var button in buttons)
            {
                button.ContentTemplate = wrappingCaption;
                button.Margin = new Thickness(0, 0, 7, 5);
                actions.Children.Add(button);
            }
            actions.SizeChanged += delegate(object sender, SizeChangedEventArgs args)
            {
                foreach (var button in buttons) button.MaxWidth = Math.Max(1, args.NewSize.Width - button.Margin.Right);
            };
            parent.Children.Add(actions);
        }

        internal static Button WebLink(Func<string> caption, string name, string address, bool primary = false)
        {
            var uri = new Uri(address, UriKind.Absolute);
            var button = LocalizedUi.Button(caption, primary);
            button.Name = name;
            button.Tag = uri;
            button.ToolTip = uri.AbsoluteUri;
            button.Click += delegate { MarkdownLinkMenu.OpenExternally(uri); };
            return button;
        }
    }
}
