using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

// The workspace owns only its view. The application queue continues after it closes.
internal sealed class ImageWorkspacePreview : Border, IDisposable
{
    private readonly DesktopController owner;
    private readonly Window host;
    private readonly Action<GeneratedImage> repeat;
    private string? taskId, displayedAsset;
    private GeneratedImageCard? card;
    private bool attached, disposed;

    internal ImageWorkspacePreview(DesktopController controller, Window window, Action<GeneratedImage> regenerate)
    {
        owner = controller; host = window; repeat = regenerate;
        Name = "ImageWorkspacePreview"; Background = DesktopTheme.Brush("#F2F4F7"); CornerRadius = new CornerRadius(18); Padding = new Thickness(14); ClipToBounds = true;
        Child = LocalizedUi.With(new TextBlock { Text = TextResources.Get("imageWorkspacePreview.imageWorkspacePreview.text"), FontSize = 20, Foreground = DesktopTheme.Brush("#777F89"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },"Text",()=>(TextResources.Get("imageWorkspacePreview.imageWorkspacePreview.text")));
        AttachedToVisualTree += (_, _) => { if (disposed) return; attached = true; owner.ImageTasks.Changed += Changed; Update(); };
        DetachedFromVisualTree += (_, _) => { attached = false; owner.ImageTasks.Changed -= Changed; ReleaseCard(); };
    }
    internal void ShowTask(string id)
    {
        taskId = id; displayedAsset = null; ReleaseCard();
        Child = new ImageGenerationPendingCard(owner, host, id, repeat) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Update();
    }
    private void Changed(ImageGenerationTaskSnapshot task)
    {
        if (task.Id == taskId) Dispatcher.UIThread.Post(() => { if (attached && !disposed) Update(); });
    }
    private void Update()
    {
        if (disposed || !attached || taskId == null) return;
        var task = owner.ImageTasks.Get(taskId);
        if (task?.State != ImageGenerationTaskState.Succeeded || string.IsNullOrEmpty(task.ResultImageId) || displayedAsset == task.ResultImageId) return;
        try
        {
            var image = owner.ImageStore.Get(task.ResultImageId) ?? throw new FileNotFoundException(TextResources.Get("imageWorkspacePreview.update.ownerImageStoreGet"));
            ReleaseCard(); card = new GeneratedImageCard(owner, host, image, repeat, workspacePreview: true); Child = card; displayedAsset = image.Id;
        }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException)
        {
            Child = LocalizedUi.Text(()=>(TextResources.Get("imageWorkspacePreview.update.text")));
        }
    }
    private void ReleaseCard() { if (card == null) return; Child = null; card.Dispose(); card = null; displayedAsset = null; }
    public void Dispose() { disposed = true; owner.ImageTasks.Changed -= Changed; Child = null; ReleaseCard(); }
}
