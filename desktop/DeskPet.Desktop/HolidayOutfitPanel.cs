using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Dafeiyu;

namespace DeskPet.Desktop;

// The right half of the companion selector row shares the existing Fluent controls.
internal sealed class HolidayOutfitPanel : StackPanel, IDisposable
{
    private readonly HolidayController controller;
    private readonly Func<PetSkin?> currentSkin;
    private readonly SpriteAssets avatars;
    private readonly bool ownsAvatars;
    private readonly CheckBox enabled;
    private readonly ComboBox choices;
    private readonly TextBlock status;
    private bool refreshing, disposed;
    private sealed record Choice(string? Id, string Label, bool Available, PetSkin? Skin, OutfitDefinition? Outfit) { public override string ToString() => Label; }
    internal HolidayOutfitPanel(HolidayController holidays, Func<PetSkin?>? skin = null, SpriteAssets? assets = null)
    {
        controller = holidays; currentSkin = skin ?? (() => null); avatars = assets ?? new SpriteAssets(); ownsAvatars = assets == null;
        Name = "HolidayOutfitPanel"; Spacing = 6;
        enabled = new CheckBox { Name = "HolidayOutfitsEnabled", Content = "启用特殊服饰", FontSize = 12, Foreground = DesktopTheme.Brush("#61738B"), MinHeight = 24 };
        choices = new ComboBox { Name = "HolidayOutfitPicker", MinWidth = 0, MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        choices.ItemTemplate = new FuncDataTemplate<Choice>((choice, _) => ItemRow(choice));
        status = Ui.Text(""); status.Name = "HolidayOutfitStatus"; status.FontSize = 12; status.Foreground = DesktopTheme.Brush("#61738B");
        Children.Add(Ui.Text("衣柜")); Children.Add(choices); Children.Add(enabled); Children.Add(status);
        enabled.IsCheckedChanged += (_, _) => { if (!refreshing) controller.SetEnabled(enabled.IsChecked == true); };
        choices.SelectionChanged += (_, _) => { if (!refreshing && choices.SelectedItem is Choice { Available: true } choice) controller.SelectOwnedOutfit(choice.Id); };
        choices.ContainerPrepared += ItemPrepared;
        controller.Changed += Changed; Refresh();
    }
    private Control ItemRow(Choice? choice)
    {
        if (choice == null) return new TextBlock();
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*"), Name = "HolidayWardrobeItem" };
        Control thumbnail = choice.Outfit != null && choice.Skin != null
            ? new HolidayOutfitThumbnail(avatars, choice.Skin, choice.Outfit) { Name = "HolidayOutfitThumbnail", Width = 32, Height = 32 }
            : choice.Skin != null ? new AvatarView { Assets = avatars, Skin = choice.Skin, Name = "HolidayDefaultThumbnail" }
            : new Border { Name = "HolidayDefaultThumbnail", Width = 32, Height = 32, CornerRadius = new CornerRadius(6), Background = DesktopTheme.Brush("#F2F3F5") };
        var label = new TextBlock { Text = choice.Label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        row.Children.Add(thumbnail); Grid.SetColumn(label, 1); row.Children.Add(label); return row;
    }
    private void Changed(object? sender, EventArgs args)
    {
        if (disposed) return;
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) Refresh(); else Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (!disposed) Refresh(); });
    }
    private void Refresh()
    {
        refreshing = true;
        try
        {
            IsVisible = controller.WardrobeVisible; enabled.IsChecked = controller.Enabled;
            var skin = currentSkin(); var owned = controller.OwnedOutfits;
            enabled.IsEnabled = skin == null || skin.BuiltinCharacterId != null;
            var options = new List<Choice> { new(null, "默认", true, skin, null) };
            options.AddRange(owned.Select(x => new Choice(x.OutfitId, x.Name + (x.Ready ? "" : "（暂不可用）"), x.Ready, skin, x)));
            choices.ItemsSource = options;
            var current = controller.EffectiveOutfit;
            choices.SelectedItem = options.FirstOrDefault(x => x.Id == (controller.Enabled ? current?.OutfitId : controller.PreferredOutfitId));
            choices.IsEnabled = controller.Enabled || skin is { BuiltinCharacterId: null };
            choices.PlaceholderText = "当前体验尚未解锁";
            status.Text = !string.IsNullOrEmpty(controller.PersistenceWarning) ? controller.PersistenceWarning :
                current != null && !owned.Any(x => x.OutfitId == current.OutfitId) ? "当前体验尚未解锁" : "";
            status.IsVisible = !string.IsNullOrEmpty(status.Text);
        }
        finally { refreshing = false; }
    }
    private void ItemPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is ComboBoxItem item && item.DataContext is Choice choice) item.IsEnabled = choice.Available;
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; controller.Changed -= Changed; choices.ContainerPrepared -= ItemPrepared;
        if (ownsAvatars) avatars.Dispose();
    }
}

internal sealed class HolidayOutfitThumbnail(SpriteAssets assets, PetSkin skin, OutfitDefinition outfit) : Control
{
    public override void Render(DrawingContext context)
    {
        base.Render(context); if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var size = Math.Min(32, Math.Min(Bounds.Width, Bounds.Height));
        var target = new Rect((Bounds.Width - size) / 2, (Bounds.Height - size) / 2, size, size);
        context.DrawRectangle(DesktopTheme.Brush("#F2F3F5"), null, target, 6, 6);
        if (outfit.Preview == null) return;
        try
        {
            var resource = "Dafeiyu.Holidays." + outfit.Preview.Replace('/', '.');
            var bitmap = assets.Get(skin, "holiday|" + outfit.CharacterId + ":" + outfit.OutfitId + ":" + outfit.Version + "|" + resource);
            var factor = size / Math.Max(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
            var width = bitmap.PixelSize.Width * factor; var height = bitmap.PixelSize.Height * factor;
            context.DrawImage(bitmap, new Rect(bitmap.Size), new Rect(target.Center.X - width / 2, target.Center.Y - height / 2, width, height));
        }
        catch (Exception error) when (error is InvalidDataException or IOException or ArgumentException or InvalidOperationException) { }
    }
}
