using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Dafeiyu;
using System.Globalization;

namespace DeskPet.Desktop;

internal sealed class AttachmentPreview : Window
{
    readonly ChatAttachment file;
    readonly string dataRoot;
    readonly Action<FileReadResult>? select;
    readonly ComboBox sections = new() { Name = "AttachmentPreviewSections", MinWidth = 170 };
    readonly TextBox start = new() { Name = "AttachmentPreviewStart", Text = "1", Width = 60 }, count = Number("25"), column = Number("1"), columns = Number("32");
    readonly TextBox encoding = new() { Width = 105 };
    readonly CheckBox ocr = new() { Content = "OCR" };
    readonly ComboBox ocrLanguage = new() { ItemsSource = new[] { "en-US", "zh-Hans" }, SelectedIndex = 0 };
    readonly TextBox filterColumn = new() { Width = 115 }, filterValue = new() { Width = 115 };
    readonly TextBlock status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    readonly TextBox output = new() { Name = "AttachmentPreviewText", IsReadOnly = true, AcceptsReturn = true, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    readonly Image image = new() { MaxHeight = 220, IsVisible = false };
    readonly CancellationTokenSource cancellation = new();
    readonly Button load, use;
    FileReadResult? selection;
    Bitmap? bitmap;
    bool closed, reading;

    internal AttachmentPreview(ChatAttachment file, string dataRoot, Action<FileReadResult>? select = null)
    {
        this.file = file;
        this.dataRoot = dataRoot;
        this.select = select;
        Title = file.Name;
        Name = "AttachmentPreviewDialog";
        Width = 760;
        Height = 630;
        MinHeight = 380;
        MinWidth = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var controls = new WrapPanel { ItemSpacing = 8, LineSpacing = 6, Margin = new Thickness(0, 0, 0, 8) };
        controls.Children.Add(sections);
        controls.Children.Add(Field(start, TextResources.Get("fileCompatibility.start")));
        controls.Children.Add(Field(count, TextResources.Get("fileCompatibility.count")));
        string ext = FileContentService.Extension(file.Name);
        if (ext is ".xlsx" or ".xls" or ".csv" or ".tsv")
        {
            controls.Children.Add(Field(column, TextResources.Get("fileCompatibility.column")));
            controls.Children.Add(Field(columns, TextResources.Get("fileCompatibility.columns")));
        }
        if (FileContentService.IsSqlite(ext))
        {
            controls.Children.Add(Field(filterColumn, TextResources.Get("fileCompatibility.columnName")));
            controls.Children.Add(Field(filterValue, TextResources.Get("fileCompatibility.filter")));
        }
        if (!FileContentService.IsMedia(ext)) controls.Children.Add(Field(encoding, "GB18030"));
        if (ext == ".pdf" && FileOcrService.Available)
        {
            controls.Children.Add(ocr);
            controls.Children.Add(ocrLanguage);
        }
        load = Ui.Button(() => TextResources.Get("fileCompatibility.preview"), () => _ = ReadAsync(false));
        load.Name = "AttachmentPreviewRead";
        use = Ui.Button(() => TextResources.Get("fileCompatibility.useSelection"),
            () => { if (selection != null && select != null) { select(selection); Close(); } });
        use.IsEnabled = false;
        use.Name = "AttachmentPreviewUse";
        controls.Children.Add(load);
        if (select != null) controls.Children.Add(use);
        controls.Children.Add(Ui.Button(() => TextResources.Get("common.save"), () => _ = ExportAsync()));
        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(controls, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        DockPanel.SetDock(image, Dock.Top);
        root.Children.Add(controls); root.Children.Add(status); root.Children.Add(image); root.Children.Add(output);
        output.Text = file.Text;
        Content = root;
        Opened += async (_, _) => await ReadAsync(true);
        Closed += (_, _) => { closed = true; cancellation.Cancel(); if (!reading) cancellation.Dispose(); ReplaceBitmap(null); };
    }

    async Task ReadAsync(bool inspect)
    {
        if (closed || reading) return;
        reading = true; load.IsEnabled = false; use.IsEnabled = false; selection = null; status.Text = "";
        try
        {
            var token = cancellation.Token;
            if (inspect)
            {
                var manifest = await Task.Run(() => FileContentService.Inspect(file.ImportedFile, dataRoot, token));
                if (closed) return;
                sections.ItemsSource = manifest.Sections;
                if (manifest.Sections.Count > 0) sections.SelectedIndex = 0;
                status.Text = manifest.Detail;
                return;
            }
            string? section = (sections.SelectedItem as FileSection)?.Id;
            string? selectedFilter = EmptyToNull(filterColumn.Text);
            var request = new FileReadRequest
            {
                Section = section,
                Start = section == "frame" ? 1 : Parse(start.Text),
                Count = Parse(count.Text),
                StartColumn = Parse(column.Text),
                ColumnCount = Parse(columns.Text),
                TimestampMs = section == "frame" ? Parse(start.Text) : 0,
                FilterColumn = selectedFilter,
                FilterValue = selectedFilter == null ? null : filterValue.Text ?? "",
                EncodingName = EmptyToNull(encoding.Text),
                Operation = ocr.IsChecked == true ? "ocr" : null,
                Language = ocrLanguage.SelectedItem as string
            };
            var result = await Task.Run(() => FileContentService.Read(file.ImportedFile, dataRoot, request, token));
            if (closed) return;
            selection = result;
            output.Text = FileContentService.FormatResult(result);
            if (result.Image == null) ReplaceBitmap(null);
            else
            {
                using var bytes = new MemoryStream(result.Image, false);
                ReplaceBitmap(new Bitmap(bytes));
            }
            use.IsEnabled = select != null;
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (FileContentService.IsReadFailure(error))
        { if (!closed) status.Text = FileContentService.UserError(error); }
        finally { reading = false; if (closed) cancellation.Dispose(); else load.IsEnabled = true; }
    }

    async Task ExportAsync()
    {
        try
        {
            var destination=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName=file.Name });
            if(destination?.TryGetLocalPath() is string path)
                await Task.Run(()=>FileContentService.Export(file.ImportedFile,dataRoot,path));
        }
        catch(Exception error) when(FileContentService.IsReadFailure(error))
        {if(!closed)status.Text=FileContentService.UserError(error);}
    }

    void ReplaceBitmap(Bitmap? next)
    {
        image.Source = next;
        image.IsVisible = next != null;
        bitmap?.Dispose();
        bitmap = next;
    }

    static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    static int Parse(string? text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)
        ? value : throw new ArgumentException(nameof(text));
    static TextBox Number(string initial) => new() { Text = initial, Width = 60 };
    static StackPanel Field(Control control, string label) => new() { Children = { new TextBlock { Text = label }, control } };
}
