using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu
{
    internal sealed class AttachmentPreviewWindow : Window
    {
        readonly ChatAttachment file;
        readonly string dataRoot;
        readonly Action<FileReadResult> select;
        readonly ComboBox sections = new ComboBox { MinWidth = 190 };
        readonly TextBox start = Number("1"), count = Number("25"), column = Number("1"), columns = Number("32");
        readonly TextBox encoding = new TextBox { MinWidth = 110 };
        readonly CheckBox ocr = new CheckBox { Content = "OCR", Margin = new Thickness(5) };
        readonly ComboBox ocrLanguage = new ComboBox { ItemsSource = new[] { "en-US", "zh-Hans" }, SelectedIndex = 0 };
        readonly TextBox filterColumn = new TextBox { MinWidth = 90 }, filterValue = new TextBox { MinWidth = 90 };
        readonly TextBox output = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 240, FontFamily = new FontFamily("Consolas") };
        readonly TextBlock status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        readonly Image frame = new Image { MaxHeight = 240, Visibility = Visibility.Collapsed };
        readonly Button load, use;
        readonly CancellationTokenSource cancel = new CancellationTokenSource();
        FileReadResult loaded;
        bool closed, reading;

        internal AttachmentPreviewWindow(Window owner, ChatAttachment file, string dataRoot, Action<FileReadResult> select = null)
        {
            this.file = file;
            this.dataRoot = dataRoot;
            this.select = select;
            Owner = owner;
            Title = file.Name;
            Width = 760;
            Height = 630;
            MinWidth = 460;
            MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            var root = new DockPanel { Margin = new Thickness(16) };
            var controls = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            controls.Children.Add(sections);
            controls.Children.Add(Labeled(start, TextResources.Get("fileCompatibility.start")));
            controls.Children.Add(Labeled(count, TextResources.Get("fileCompatibility.count")));
            string ext = FileContentService.Extension(file.Name);
            if (ext == ".xlsx" || ext == ".xls" || ext == ".csv" || ext == ".tsv")
            {
                controls.Children.Add(Labeled(column, TextResources.Get("fileCompatibility.column")));
                controls.Children.Add(Labeled(columns, TextResources.Get("fileCompatibility.columns")));
            }
            if (FileContentService.IsSqlite(ext))
            {
                controls.Children.Add(Labeled(filterColumn, TextResources.Get("fileCompatibility.columnName")));
                controls.Children.Add(Labeled(filterValue, TextResources.Get("fileCompatibility.filter")));
            }
            if (!FileContentService.IsMedia(ext))
                controls.Children.Add(Labeled(encoding, "GB18030"));
            if (ext == ".pdf" && FileOcrService.Available)
            {
                controls.Children.Add(ocr);
                controls.Children.Add(ocrLanguage);
            }
            load = new Button { Content = TextResources.Get("fileCompatibility.preview"), Margin = new Thickness(5) };
            load.Click += async delegate { await Read(false); };
            controls.Children.Add(load);
            use = new Button { Content = TextResources.Get("fileCompatibility.useSelection"), IsEnabled = false, Margin = new Thickness(5) };
            use.Click += delegate { if (loaded != null && select != null) { select(loaded); Close(); } };
            if (select != null) controls.Children.Add(use);
            DockPanel.SetDock(controls, Dock.Top);
            root.Children.Add(controls);
            DockPanel.SetDock(status, Dock.Bottom);
            root.Children.Add(status);
            DockPanel.SetDock(frame, Dock.Top);
            root.Children.Add(frame);
            output.Text = file.Text ?? "";
            root.Children.Add(output);
            Content = root;
            Loaded += async delegate { await Read(true); };
            Closed += delegate { closed = true; cancel.Cancel(); if (!reading) cancel.Dispose(); };
        }

        async Task Read(bool inspect)
        {
            if (reading || closed) return;
            reading = true; load.IsEnabled = false; use.IsEnabled = false; loaded = null; status.Text = "";
            try
            {
                var token = cancel.Token;
                if (inspect)
                {
                    var manifest = await Task.Run(() => FileContentService.Inspect(file.ImportedFile, dataRoot, token));
                    if (closed) return;
                    sections.ItemsSource = manifest.Sections;
                    if (manifest.Sections.Count > 0) sections.SelectedIndex = 0;
                    if (!String.IsNullOrWhiteSpace(manifest.Detail)) status.Text = manifest.Detail;
                    return;
                }
                var section = sections.SelectedItem as FileSection;
                string selectedFilter = EmptyToNull(filterColumn.Text);
                var request = new FileReadRequest
                {
                    Section = section == null ? null : section.Id, Start = section != null && section.Id == "frame" ? 1 : Int(start.Text), Count = Int(count.Text),
                    StartColumn = Int(column.Text), ColumnCount = Int(columns.Text),
                    TimestampMs = section != null && section.Id == "frame" ? Milliseconds(start.Text) : 0,
                    FilterColumn = selectedFilter, FilterValue = selectedFilter == null ? null : filterValue.Text,
                    EncodingName = EmptyToNull(encoding.Text),
                    Operation = ocr.IsChecked == true ? "ocr" : null,
                    Language = ocrLanguage.SelectedItem as string
                };
                var result = await Task.Run(() => FileContentService.Read(file.ImportedFile, dataRoot, request, token));
                if (closed) return;
                loaded = result;
                output.Text = FileContentService.FormatResult(result);
                frame.Visibility = result.Image == null ? Visibility.Collapsed : Visibility.Visible;
                if (result.Image != null)
                {
                    using (var stream = new MemoryStream(result.Image, false))
                    {
                        var image = new BitmapImage();
                        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit();
                        frame.Source = image;
                    }
                }
                else frame.Source = null;
                use.IsEnabled = select != null;
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!FileContentService.IsReadFailure(error)) throw; if (!closed) status.Text = FileContentService.UserError(error); }
            finally { reading = false; if (closed) cancel.Dispose(); else load.IsEnabled = true; }
        }

        static string EmptyToNull(string text) { return String.IsNullOrWhiteSpace(text) ? null : text.Trim(); }
        static int Int(string text) { return checked((int)Milliseconds(text)); }
        static long Milliseconds(string text)
        {
            long value;
            if (!Int64.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) || value > Int32.MaxValue)
                throw new ArgumentException("text");
            return value;
        }

        static TextBox Number(string text) { return new TextBox { Text = text, MinWidth = 45 }; }
        static StackPanel Labeled(Control child, string label)
        {
            var panel = new StackPanel { Margin = new Thickness(5, 0, 5, 0) };
            panel.Children.Add(new TextBlock { Text = label }); panel.Children.Add(child);
            return panel;
        }
    }
}
