using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class ReleaseNotesWindow : Window
{
    internal ReleaseNotesWindow(ReleaseNotes notes, Action<Exception> onError)
    {
        Name = "ReleaseNotesWindow";
        Title = notes.Title;
        Width = 520;
        Height = 570;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var language = TextResources.CurrentLanguage;
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var header = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 15) };
        var channel = SettingsVisual.Text(notes.ChannelName, 12);
        channel.Foreground = notes.Current.IsPreview ? SettingsVisual.Accent : SettingsVisual.Muted;
        channel.FontWeight = FontWeight.SemiBold;
        header.Children.Add(channel);
        header.Children.Add(SettingsVisual.Text(notes.Subtitle, 13, true));
        root.Children.Add(header);

        var history = new StackPanel { Spacing = 13, Margin = new Thickness(0, 0, 9, 0) };
        foreach (var entry in notes.Entries(language))
        {
            var section = new StackPanel { Spacing = 6 };
            var heading = new DockPanel();
            if (entry.Details != null)
            {
                var details = Ui.AsyncButton(TextResources.Get("releaseNotes.details"), () => Launcher.LaunchUriAsync(entry.Details), onError);
                details.Margin = new Thickness(8, 0, 0, 0);
                DockPanel.SetDock(details, Dock.Right);
                heading.Children.Add(details);
            }
            var version = SettingsVisual.Text("v" + entry.Version.Canonical, 14);
            version.FontWeight = FontWeight.SemiBold;
            version.VerticalAlignment = VerticalAlignment.Center;
            heading.Children.Add(version);
            section.Children.Add(heading);

            foreach (var item in entry.Items)
            {
                var row = new DockPanel();
                var bullet = SettingsVisual.Text("•", 13, true);
                bullet.Margin = new Thickness(0, 0, 9, 0);
                DockPanel.SetDock(bullet, Dock.Left);
                row.Children.Add(bullet);
                row.Children.Add(SettingsVisual.Text(item, 13));
                section.Children.Add(row);
            }
            history.Children.Add(section);
        }
        if (history.Children.Count == 0) history.Children.Add(SettingsVisual.Text(TextResources.Get("releaseNotes.empty"), 13, true));
        var scroll = new ScrollViewer { Name = "ReleaseNotesHistory", Content = history };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var full = Ui.AsyncButton(TextResources.Get("releaseNotes.full"), () => Launcher.LaunchUriAsync(notes.FullLog(language)), onError);
        var close = Ui.Button(TextResources.Get("releaseNotes.dismiss"), Close);
        close.Classes.Add("accent");
        close.Name = "DismissReleaseNotes";
        close.IsDefault = true;
        var footer = SettingsVisual.Wrap(full, close);
        footer.HorizontalAlignment = HorizontalAlignment.Right;
        footer.Margin = new Thickness(0, 12, 0, 0);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        DialogUi.ApplyTitled(this, root);
        Opened += (_, _) => close.Focus();
    }
}
