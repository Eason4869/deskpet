using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Dafeiyu;
namespace DeskPet.Desktop;

internal sealed class GeneratedImageGroupCard : Grid, IDisposable
{
    sealed class Slot
    {
        internal readonly Button Button;
        internal string? AssetId;
        internal bool Initialized;
        internal Bitmap? Bitmap;
        internal Slot(Button button) { Button = button; }
        internal void ReleaseImage()
        {
            Button.Content = null;
            Bitmap?.Dispose();
            Bitmap = null;
        }
    }

    readonly DesktopController owner;
    readonly Window host;
    readonly GeneratedImageGroupState state = new();
    readonly ContentControl main = new();
    readonly StackPanel rail = new();
    readonly ScrollViewer scroll;
    readonly Dictionary<string, Slot> slots = new();
    ChatMessage? message;
    bool attached;
    string? displayedId, displayedAssetId;
    bool displayedHasTask;
    GeneratedImageCard? selected;
    internal string? SelectedId => state.SelectedId;

    internal GeneratedImageGroupCard(DesktopController controller, Window window)
    {
        owner = controller; host = window; Name = "GeneratedImageGroupCard";
        ColumnDefinitions = new("*,Auto"); RowDefinitions = new("*,Auto");
        scroll = new() { Content = rail, MaxHeight = 360 };
        Children.Add(main); Children.Add(scroll);
        SizeChanged += (_, _) => Layout();
        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => Dispose();
    }

    void Attach()
    {
        if (attached) return;
        attached = true;
        owner.ImageTasks.Changed += Changed;
        Refresh();
    }

    void Changed(ImageGenerationTaskSnapshot task)
    {
        if (message?.ImageGenerationTaskIds?.Contains(task.Id) == true)
            Dispatcher.UIThread.Post(() => { if (attached) Refresh(); });
    }

    internal void Update(ChatMessage value) { message = value; Refresh(); }

    void Refresh()
    {
        if (message == null) return;
        state.Update(message.ImageGenerationTaskIds, message.GeneratedImages ?? new(), id =>
        {
            var saved = owner.ImageStore.FindByJobId(id);
            var task = owner.ImageTasks.Get(id);
            return saved ?? (string.IsNullOrEmpty(task?.ResultImageId) ? null : owner.ImageStore.Get(task.ResultImageId));
        });
        foreach (var id in slots.Keys.Where(id => !state.Keys.Contains(id)).ToArray())
        {
            var slot = slots[id];
            rail.Children.Remove(slot.Button);
            slot.ReleaseImage();
            slots.Remove(id);
        }
        for (int index = 0; index < state.Keys.Count; index++)
        {
            var id = state.Keys[index];
            if (!slots.TryGetValue(id, out var slot))
            {
                var button = new Button { Name = "GeneratedImageGroupSlot", Tag = id, Width = 64, Height = 64,
                    Margin = new(4), Padding = new(2), Background = DesktopTheme.Surface };
                button.Click += (_, _) => { state.Select(id); ShowSelected(); UpdateSelection(); };
                slot = new Slot(button); slots.Add(id, slot);
            }
            if (index >= rail.Children.Count || !ReferenceEquals(rail.Children[index], slot.Button))
            {
                rail.Children.Remove(slot.Button);
                rail.Children.Insert(index, slot.Button);
            }
            UpdateSlot(slot, state.ImageFor(id), index);
        }
        UpdateSelection(); ShowSelected();
        IsVisible = state.Keys.Count > 0; Layout();
    }

    void UpdateSlot(Slot slot, GeneratedImage? asset, int index)
    {
        if (!slot.Initialized || slot.AssetId != asset?.Id)
        {
            slot.ReleaseImage();
            slot.Initialized = true; slot.AssetId = asset?.Id;
            slot.Button.Content = Ui.Text((index + 1).ToString(), 12);
            if (asset != null)
            {
                try
                {
                    using var stream = new MemoryStream(owner.ImageStore.Read(asset.Id));
                    slot.Bitmap = Bitmap.DecodeToWidth(stream, 80);
                    slot.Button.Content = new Image { Source = slot.Bitmap, Stretch = Stretch.UniformToFill };
                }
                catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException) { }
            }
        }
        if (slot.Button.Content is TextBlock text) text.Text = (index + 1).ToString();
        ToolTip.SetTip(slot.Button, asset != null ? TextResources.Format("generatedImageGroupCard.updateSlot.toolTipSetTip",(index + 1)) : ImageGenerationMotion.Label(owner.ImageTasks.Get((string)slot.Button.Tag!)));
    }

    void UpdateSelection()
    {
        foreach (var pair in slots)
        {
            bool active = pair.Key == state.SelectedId;
            pair.Value.Button.BorderThickness = new(active ? 2 : 1);
            pair.Value.Button.BorderBrush = DesktopTheme.Brush(active ? "#456DE6" : "#DCE5F4");
        }
    }

    void ShowSelected()
    {
        var asset = state.SelectedImage;
        bool hasTask = asset == null && state.SelectedId != null && owner.ImageTasks.Get(state.SelectedId) != null;
        // Pending controls consume their own snapshots; unrelated task states never replace the main view.
        if (displayedId == state.SelectedId && displayedAssetId == asset?.Id && displayedHasTask == hasTask) return;
        main.Content = null; selected?.Dispose(); selected = null;
        displayedId = state.SelectedId; displayedAssetId = asset?.Id; displayedHasTask = hasTask;
        if (asset != null) { selected = new(owner, host, asset); main.Content = selected; }
        else if (state.SelectedId != null)
            main.Content = hasTask ? (Control)new ImageGenerationPendingCard(owner, host, state.SelectedId) : Ui.Text(TextResources.Get("generatedImageGroupCard.showSelected.text"), 12);
    }

    void Layout()
    {
        bool wide = Bounds.Width >= 480;
        SetColumn(scroll, wide ? 1 : 0); SetRow(scroll, wide ? 0 : 1);
        rail.Orientation = wide ? Orientation.Vertical : Orientation.Horizontal;
        scroll.IsVisible = state.Keys.Count > 1;
        scroll.HorizontalScrollBarVisibility = wide ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        scroll.VerticalScrollBarVisibility = wide ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
    }

    public void Dispose()
    {
        if (attached) owner.ImageTasks.Changed -= Changed;
        attached = false;
        main.Content = null; selected?.Dispose(); selected = null;
        rail.Children.Clear();
        foreach (var slot in slots.Values) slot.ReleaseImage();
        slots.Clear();
        displayedId = null; displayedAssetId = null; displayedHasTask = false;
    }
}
