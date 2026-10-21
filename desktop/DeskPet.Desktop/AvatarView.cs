using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DeskPet.Desktop;

// The containing window owns Assets; all avatars share its cached bitmaps.
internal sealed class AvatarView : Control
{
    private PetSkin? skin;
    public required SpriteAssets Assets { get; init; }
    public required PetSkin Skin
    {
        get => skin!;
        set { skin = value; InvalidateVisual(); }
    }
    public AvatarView() { Width = Height = 32; IsHitTestVisible = false; }
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (skin == null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        var target = new Rect((Bounds.Width - size) / 2, (Bounds.Height - size) / 2, size, size);
        var radius = size * .2;
        using var clip = context.PushGeometryClip(new RectangleGeometry(target, radius, radius));
        context.DrawRectangle(DesktopTheme.Brush("#F2F3F5"), null, target, radius, radius);
        if (Assets.GetAvatar(skin) is { } avatar)
        {
            var sourceSize = avatar.PixelSize;
            var edge = Math.Min(sourceSize.Width, sourceSize.Height);
            context.DrawImage(avatar, new Rect((sourceSize.Width - edge) / 2d, (sourceSize.Height - edge) / 2d, edge, edge), target);
            return;
        }
        var idle = Assets.Resolve(skin, "idle", false);
        var source = SpriteAssets.Rect(idle, 0);
        var scale = size / Math.Max(source.Width, source.Height);
        var width = source.Width * scale; var height = source.Height * scale;
        context.DrawImage(Assets.Get(skin, idle.File), new Rect(source.X, source.Y, source.Width, source.Height),
            new Rect(target.Center.X - width / 2, target.Center.Y - height / 2, width, height));
    }
}
