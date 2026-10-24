using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Dafeiyu;
namespace DeskPet.Desktop;
internal sealed class CompanionCardVisual:Canvas,IDisposable
{
    internal CompanionPortraitSource? OwnedPortrait;
    public void Dispose(){OwnedPortrait?.Dispose();OwnedPortrait=null;}
}
internal static class RelationshipCardRenderer
{
    internal static CompanionCardVisual Create(RelationshipCardData data,PetSkin skin,SpriteAssets assets)=>Create(data,skin,assets,DesktopTheme.IsDark);
    internal static CompanionCardVisual Create(RelationshipCardData data,PetSkin skin,SpriteAssets assets,bool dark)
    {
        var portrait=CompanionPortraitSource.Load(skin,assets);var visual=Create(data,portrait,dark);visual.OwnedPortrait=portrait;return visual;
    }
    internal static CompanionCardVisual Create(RelationshipCardData data,CompanionPortraitSource portrait,bool dark)
    {
        string title=TextResources.Get("relationship.card.title"),name=data.Name,stage=data.StageName,memory=data.MemoryText,date=data.DateText,day=data.AcquaintanceText;
        var content=new CompanionCardVisual{Width=800,Height=480,Background=new SolidColorBrush(dark?Color.FromRgb(43,53,69):Color.FromRgb(244,247,252))};
        var picture=new CompanionPortraitVisual(portrait,dark){Width=296,Height=416};Canvas.SetLeft(picture,32);Canvas.SetTop(picture,32);content.Children.Add(picture);
        Add(content,title,360,30,408,24,16,16,dark,true,false,"RelationshipCardTitle");
        Add(content,name,360,62,408,40,27,24,dark,false,true,"RelationshipCardName");
        Add(content,stage,360,108,408,100,44,40,dark,false,false,"RelationshipCardStage");
        Add(content,memory,360,218,408,116,26,24,dark,false,false,"RelationshipCardMemory");
        Add(content,date,360,354,408,62,22,20,dark,true,false,"RelationshipCardDate");
        Add(content,day,360,424,408,26,19,18,dark,true,false,"RelationshipCardAcquaintance");
        Avalonia.Automation.AutomationProperties.SetName(content,name+" "+stage+" "+memory+" "+date+" "+day);return content;
    }
    static void Add(Canvas canvas,string value,double x,double y,double width,double height,double size,double minimum,bool dark,bool muted,bool oneLine,string name)
    {
        var text=new TextBlock{Name=name,Text=value,Width=width,Height=height,FontSize=size,FontFamily=new FontFamily("Microsoft YaHei UI, PingFang SC, Noto Sans CJK SC, Noto Sans, Segoe UI"),Foreground=new SolidColorBrush(dark?(muted?Color.FromRgb(181,196,219):Color.FromRgb(229,234,242)):(muted?Color.FromRgb(100,117,142):Color.FromRgb(32,55,84))),TextWrapping=oneLine?TextWrapping.NoWrap:TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,FontWeight=name=="RelationshipCardStage"?FontWeight.SemiBold:FontWeight.Normal,ClipToBounds=true};
        while(text.FontSize>minimum){var probe=new TextBlock{Text=value,FontSize=text.FontSize,FontFamily=text.FontFamily,FontWeight=text.FontWeight,TextWrapping=text.TextWrapping};probe.Measure(new Size(width,Double.PositiveInfinity));if(probe.DesiredSize.Height<=height)break;text.FontSize--;}
        ToolTip.SetTip(text,value);Avalonia.Automation.AutomationProperties.SetName(text,value);Canvas.SetLeft(text,x);Canvas.SetTop(text,y);canvas.Children.Add(text);
    }
    internal static byte[] Render(RelationshipCardData data,PetSkin skin,SpriteAssets assets)=>Render(data,skin,assets,DesktopTheme.IsDark);
    internal static byte[] Render(RelationshipCardData data,PetSkin skin,SpriteAssets assets,bool dark)
    {using var portrait=CompanionPortraitSource.Load(skin,assets);return Render(data,portrait,dark);}
    internal static byte[] Render(RelationshipCardData data,CompanionPortraitSource portrait,bool dark)
    {
        using var visual=Create(data,portrait,dark);visual.Measure(new Size(800,480));visual.Arrange(new Rect(0,0,800,480));
        using var bitmap=new RenderTargetBitmap(new PixelSize(800,480),new Vector(96,96));bitmap.Render(visual);
        using var memory=new MemoryStream();bitmap.Save(memory,new PngBitmapEncoderOptions());return memory.ToArray();
    }
}
