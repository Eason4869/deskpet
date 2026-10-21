using Avalonia;
using Avalonia.Controls;
using Dafeiyu;

namespace DeskPet.Desktop;

// Both native clients use the shared placement calculation, so resize and
// aspect ratios follow the same rules on Windows and macOS.
internal sealed class ImageGalleryPanel : Panel
{
    private IList<GeneratedImage> images = Array.Empty<GeneratedImage>();
    private string layoutMode = "masonry";
    internal string LayoutMode
    {
        get => layoutMode;
        set { if (layoutMode == value) return; layoutMode = value; InvalidateMeasure(); }
    }
    internal void SetImages(IList<GeneratedImage> values) { images = values; InvalidateMeasure(); }
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) && availableSize.Width > 0 ? availableSize.Width : Math.Max(220, Bounds.Width);
        var result = ImageGalleryLayout.Calculate(images, width, layoutMode);
        for (int i = 0; i < Children.Count && i < result.Items.Count; i++) Children[i].Measure(new Size(result.Items[i].Width, result.Items[i].Height));
        return new Size(width, result.Height);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = ImageGalleryLayout.Calculate(images, finalSize.Width, layoutMode);
        for (int i = 0; i < Children.Count && i < result.Items.Count; i++) { var p = result.Items[i]; Children[i].Arrange(new Rect(p.X, p.Y, p.Width, p.Height)); }
        return finalSize;
    }
}
