using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Dafeiyu
{
    internal static class AppCardHeader
    {
        static BitmapSource LoadGmailLogo()
        {
            using (var stream = typeof(AppCardHeader).Assembly.GetManifestResourceStream("Dafeiyu.GmailAppLogo"))
            {
                var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                return decoder.Frames.OrderByDescending(frame => frame.PixelWidth).First();
            }
        }

        internal static FrameworkElement GitHub()
        {
            var image = UiIcons.Image("github", ThemeManager.Brush("#181818", "#FFFFFF"));
            image.Width = image.Height = 26;
            return Create("GitHub", () => TextResources.Get("gitHubApp.description"), image);
        }

        internal static FrameworkElement Gmail()
        {
            var image = new Image { Source = LoadGmailLogo(), Width = 22, Height = 22 };
            return Create("Gmail", () => TextResources.Get("apps.gmail.description"), image);
        }

        static FrameworkElement Create(string name, System.Func<string> description, FrameworkElement icon)
        {
            var row = new DockPanel();
            var frame = new Border { Name = name + "AppLogo", Width = 36, Height = 36, CornerRadius = new CornerRadius(9),
                Background = ThemeManager.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
                Child = icon, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 0),
                IsHitTestVisible = false };
            DockPanel.SetDock(frame, Dock.Left);
            row.Children.Add(frame);
            var labels = new StackPanel();
            var heading = Ui.Text(name, 15, Ui.PageInk);
            heading.FontWeight = FontWeights.SemiBold;
            labels.Children.Add(heading);
            labels.Children.Add(LocalizedUi.Text(description, 12, Ui.Muted));
            row.Children.Add(labels);
            return row;
        }
    }
}
