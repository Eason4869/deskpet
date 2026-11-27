using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Dafeiyu
{
    internal sealed class ReleaseNotesWindow : Window
    {
        internal ReleaseNotesWindow(ReleaseNotes notes)
        {
            Name = "ReleaseNotesWindow";
            Title = notes.Title;
            Width = 520;
            Height = 570;
            Topmost = true;

            var language = TextResources.CurrentLanguage;
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };
            var channel = Ui.Text(notes.ChannelName, 12, notes.Current.IsPreview ? Ui.Blue : Ui.Muted);
            channel.FontWeight = FontWeights.SemiBold;
            header.Children.Add(channel);
            var subtitle = Ui.Text(notes.Subtitle, 13, Ui.Muted);
            subtitle.Margin = new Thickness(0, 3, 0, 0);
            header.Children.Add(subtitle);
            root.Children.Add(header);

            var history = new StackPanel { Margin = new Thickness(0, 0, 9, 0) };
            foreach (var entry in notes.Entries(language))
            {
                var section = new StackPanel { Margin = new Thickness(0, 0, 0, 13) };
                var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                if (entry.Details != null)
                {
                    var details = Ui.Button(TextResources.Get("releaseNotes.details"));
                    details.Margin = new Thickness(8, 0, 0, 0);
                    details.MinHeight = 28;
                    details.Padding = new Thickness(8, 3, 8, 3);
                    details.Click += delegate { Open(entry.Details); };
                    DockPanel.SetDock(details, Dock.Right);
                    heading.Children.Add(details);
                }
                var version = Ui.Text("v" + entry.Version.Canonical, 14, Ui.Ink);
                version.FontWeight = FontWeights.SemiBold;
                version.VerticalAlignment = VerticalAlignment.Center;
                heading.Children.Add(version);
                section.Children.Add(heading);

                foreach (var item in entry.Items)
                {
                    var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                    var bullet = Ui.Text("•", 13, Ui.Muted);
                    bullet.Margin = new Thickness(0, 0, 9, 0);
                    DockPanel.SetDock(bullet, Dock.Left);
                    row.Children.Add(bullet);
                    row.Children.Add(Ui.Text(item, 13));
                    section.Children.Add(row);
                }
                history.Children.Add(section);
            }
            if (history.Children.Count == 0) history.Children.Add(Ui.Text(TextResources.Get("releaseNotes.empty"), 13, Ui.Muted));
            var scroll = Ui.Scroll(history);
            scroll.Name = "ReleaseNotesHistory";
            Grid.SetRow(scroll, 1);
            root.Children.Add(scroll);

            var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var full = Ui.Button(TextResources.Get("releaseNotes.full"));
            full.Click += delegate { Open(notes.FullLog(language)); };
            footer.Children.Add(full);
            var close = Ui.Button(TextResources.Get("releaseNotes.dismiss"), true);
            close.Name = "DismissReleaseNotes";
            close.IsDefault = true;
            close.Click += delegate { Close(); };
            footer.Children.Add(close);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            DialogUi.ApplyTitled(this, root);
            ((FrameworkElement)Content).PreviewMouseLeftButtonDown += delegate(object sender, System.Windows.Input.MouseButtonEventArgs e)
            {
                if(e.GetPosition(this).Y >= 64)return;
                var source=e.OriginalSource as DependencyObject;
                while(source!=null && source!=this)
                {
                    if(source is System.Windows.Controls.Primitives.ButtonBase)return;
                    source=System.Windows.Media.VisualTreeHelper.GetParent(source);
                }
                e.Handled=true;
                try { DragMove(); } catch(InvalidOperationException) { }
            };
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Loaded += delegate { close.Focus(); };
        }

        static void Open(Uri address)
        {
            try { Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception error) { RuntimeDiagnostics.Record(error); }
        }
    }
}
