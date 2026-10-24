using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Dafeiyu;

namespace DeskPet.Desktop;

internal static class RelationshipCardRenderer
{
    internal static Control Create(RelationshipCardData data,PetSkin skin,SpriteAssets assets)
    {
        var content=new Grid{Width=800,Height=480,Background=new SolidColorBrush(Color.FromRgb(244,247,252))};
        var panel=new StackPanel{Margin=new Thickness(42,32,42,30)};
        var title=Text(TextResources.Get("relationship.card.title"),20);title.Foreground=new SolidColorBrush(Color.FromRgb(53,99,160));panel.Children.Add(title);
        var row=new Grid{ColumnDefinitions=new ColumnDefinitions("200,*"),Margin=new Thickness(0,28,0,24)};
        row.Children.Add(new AvatarView{Assets=assets,Skin=skin,Width=168,Height=168,HorizontalAlignment=HorizontalAlignment.Left});
        var words=new StackPanel{VerticalAlignment=VerticalAlignment.Center};words.Children.Add(Text(data.Name,34));words.Children.Add(Text(data.StageName,27));Grid.SetColumn(words,1);row.Children.Add(words);panel.Children.Add(row);
        panel.Children.Add(Text(data.DateText,20));panel.Children.Add(Text(TextResources.Get("relationship.card.footer"),16));content.Children.Add(panel);return content;
    }
    static TextBlock Text(string value,double size)=>new(){Text=value,FontSize=size,FontFamily=new FontFamily("Microsoft YaHei UI, PingFang SC, Noto Sans CJK SC, Noto Sans, Segoe UI"),Foreground=new SolidColorBrush(Color.FromRgb(29,44,68)),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
    internal static byte[] Render(RelationshipCardData data,PetSkin skin,SpriteAssets assets)
    {
        var visual=Create(data,skin,assets);visual.Measure(new Size(800,480));visual.Arrange(new Rect(0,0,800,480));
        using var bitmap=new RenderTargetBitmap(new PixelSize(800,480),new Vector(96,96));bitmap.Render(visual);
        using var memory=new MemoryStream();bitmap.Save(memory,new PngBitmapEncoderOptions());return memory.ToArray();
    }
}
