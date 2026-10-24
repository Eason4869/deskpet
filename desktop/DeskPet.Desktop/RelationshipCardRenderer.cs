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
        {return Create(data,skin,assets,DesktopTheme.IsDark);}
        internal static Control Create(RelationshipCardData data,PetSkin skin,SpriteAssets assets,bool dark)
    {
        Func<string,double,TextBlock> text=(value,size)=>Text(value,size,dark);
        var content=new Grid{Width=800,Height=480,Background=new SolidColorBrush(dark?Color.FromRgb(43,53,69):Color.FromRgb(244,247,252))};
        var panel=new StackPanel{Margin=new Thickness(42,32,42,30)};
        var title=text(TextResources.Get("relationship.card.title"),20);title.Foreground=new SolidColorBrush(dark?Color.FromRgb(157,187,255):Color.FromRgb(53,99,160));panel.Children.Add(title);
        var row=new Grid{ColumnDefinitions=new ColumnDefinitions("200,*"),Margin=new Thickness(0,28,0,24)};
        row.Children.Add(new AvatarView{Assets=assets,Skin=skin,Width=168,Height=168,HorizontalAlignment=HorizontalAlignment.Left});
        var words=new StackPanel{VerticalAlignment=VerticalAlignment.Center};words.Children.Add(text(data.Name,34));words.Children.Add(text(data.StageName,27));Grid.SetColumn(words,1);row.Children.Add(words);panel.Children.Add(row);
        panel.Children.Add(text(data.DateText,20));panel.Children.Add(text(TextResources.Get("relationship.card.footer"),16));content.Children.Add(panel);return content;
    }
    static TextBlock Text(string value,double size,bool dark)=>new(){Text=value,FontSize=size,FontFamily=new FontFamily("Microsoft YaHei UI, PingFang SC, Noto Sans CJK SC, Noto Sans, Segoe UI"),Foreground=new SolidColorBrush(dark?Color.FromRgb(229,234,242):Color.FromRgb(29,44,68)),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};
    internal static byte[] Render(RelationshipCardData data,PetSkin skin,SpriteAssets assets)
        {return Render(data,skin,assets,DesktopTheme.IsDark);}
        internal static byte[] Render(RelationshipCardData data,PetSkin skin,SpriteAssets assets,bool dark)
    {
        var visual=Create(data,skin,assets,dark);visual.Measure(new Size(800,480));visual.Arrange(new Rect(0,0,800,480));
        using var bitmap=new RenderTargetBitmap(new PixelSize(800,480),new Vector(96,96));bitmap.Render(visual);
        using var memory=new MemoryStream();bitmap.Save(memory,new PngBitmapEncoderOptions());return memory.ToArray();
    }
}
