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
    internal static CompanionCardVisual Create(RelationshipCardData data,CompanionPortraitSource portrait,bool dark)=>Create(data,portrait,dark,null);
    internal static CompanionCardVisual Create(RelationshipCardData data,CompanionPortraitSource portrait,bool dark,Bitmap? scene)
    {
        if(scene!=null)return CreateScene(data,scene,dark);
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

    // Scene is borrowed from the page. This branch never loads or disposes artwork.
    internal static CompanionCardVisual CreateScene(RelationshipCardData data,Bitmap scene,bool dark)
    {
        var content=new CompanionCardVisual{Width=800,Height=480,ClipToBounds=true,Background=dark?Brushes.MidnightBlue:Brushes.White};
        content.Children.Add(new Image{Source=scene,Width=800,Height=480,Stretch=Stretch.Uniform});
        var fade=new LinearGradientBrush();fade.StartPoint=new RelativePoint(0,0,RelativeUnit.Relative);fade.EndPoint=new RelativePoint(1,0,RelativeUnit.Relative);
        var color=dark?Color.FromRgb(20,30,49):Color.FromRgb(248,249,251);
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0,color.R,color.G,color.B),0));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(0,color.R,color.G,color.B),.36));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(dark?(byte)85:(byte)90,color.R,color.G,color.B),.45));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(dark?(byte)166:(byte)175,color.R,color.G,color.B),.55));
        fade.GradientStops.Add(new GradientStop(Color.FromArgb(dark?(byte)166:(byte)175,color.R,color.G,color.B),1));
        content.Children.Add(new Border{Name="RelationshipCardReadingGradient",Width=800,Height=480,Background=fade,IsHitTestVisible=false});
        SceneText(content,TextResources.Get("relationship.card.title"),32,24,15,15,dark,"RelationshipCardTitle",false);
        SceneText(content,data.Name,62,38,27,22,dark,"RelationshipCardName",false);
        double titleHeight=SceneText(content,data.StageName,108,104,44,32,dark,"RelationshipCardStage",true);
        var ornament=OrnamentFor(data.StageId,dark);
        Canvas.SetLeft(ornament,456);Canvas.SetTop(ornament,108+titleHeight+12);content.Children.Add(ornament);
        SceneText(content,data.MemoryText,270,92,24,18,dark,"RelationshipCardMemory",false);
        SceneText(content,data.DateText,378,52,20,16,dark,"RelationshipCardDate",false);
        SceneText(content,data.AcquaintanceText,438,24,17,15,dark,"RelationshipCardAcquaintance",false);
        Avalonia.Automation.AutomationProperties.SetName(content,data.Name+" "+data.StageName+" "+data.MemoryText+" "+data.DateText+" "+data.AcquaintanceText);
        return content;
    }
    internal static Avalonia.Controls.Shapes.Path OrnamentFor(string style,bool dark)
    {
        var decoration=Geometry.Parse(RelationshipCardDecoration.PathFor(style));
        // Fixed design coordinates, with room for stroke/antialiasing. Avalonia's
        // geometry Bounds can omit a trailing line-only figure; never derive scale from it.
        decoration.Transform=new MatrixTransform(new Matrix(RelationshipCardDecoration.ScaleX,0,0,RelationshipCardDecoration.ScaleY,RelationshipCardDecoration.Inset,RelationshipCardDecoration.Inset));
        var ornament=new Avalonia.Controls.Shapes.Path{Data=decoration,Stroke=new SolidColorBrush(dark?Color.FromRgb(237,203,139):Color.FromRgb(122,86,35)),StrokeThickness=1.25,Stretch=Stretch.None,Width=RelationshipCardDecoration.Width,Height=RelationshipCardDecoration.Height,IsHitTestVisible=false};
        Avalonia.Automation.AutomationProperties.SetAccessibilityView(ornament,Avalonia.Automation.AccessibilityView.Raw);
        return ornament;
    }
    static double SceneText(CompanionCardVisual canvas,string value,double y,double height,double size,double minimum,bool dark,string name,bool artistic)
    {
        var family=artistic?RelationshipTitleFont.FontForLocale(TextResources.CurrentLanguage,value):new FontFamily("Microsoft YaHei UI, PingFang SC, Noto Sans CJK SC, Noto Sans, Segoe UI");
        var text=new TextBlock{Name=name,Text=value,Width=312,Height=height,FontSize=size,FontFamily=family,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.None,Foreground=new SolidColorBrush(artistic?(dark?Color.FromRgb(255,231,177):Color.FromRgb(73,52,22)):(dark?Color.FromRgb(255,249,236):Color.FromRgb(38,61,97)))};

        double measured=height;
        // Measure the whole string with its actual local family; never truncate stage text.
        for(double candidate=size;candidate>=minimum;candidate--){
            var probe=new TextBlock{Text=value,Width=312,FontSize=candidate,FontFamily=family,TextWrapping=TextWrapping.Wrap};

            probe.Measure(new Size(312,Double.PositiveInfinity));
            text.FontSize=candidate;measured=probe.DesiredSize.Height;
            if(probe.DesiredSize.Height<=height&&(!artistic||probe.DesiredSize.Height<=candidate*2.7))break;
        }
        ToolTip.SetTip(text,value);
        Avalonia.Automation.AutomationProperties.SetName(text,value);Canvas.SetLeft(text,456);Canvas.SetTop(text,y);text.Tag=measured;canvas.Children.Add(text);return measured;
    }
    internal static byte[] Render(RelationshipCardData data,PetSkin skin,SpriteAssets assets)=>Render(data,skin,assets,DesktopTheme.IsDark);
    internal static byte[] Render(RelationshipCardData data,PetSkin skin,SpriteAssets assets,bool dark)
    {using var portrait=CompanionPortraitSource.Load(skin,assets);return Render(data,portrait,dark);}
    internal static byte[] Render(RelationshipCardData data,CompanionPortraitSource portrait,bool dark)=>Render(data,portrait,dark,null);
    internal static byte[] Render(RelationshipCardData data,CompanionPortraitSource portrait,bool dark,Bitmap? scene)
    {
        using var visual=Create(data,portrait,dark,scene);visual.Measure(new Size(800,480));visual.Arrange(new Rect(0,0,800,480));
        using var bitmap=new RenderTargetBitmap(new PixelSize(800,480),new Vector(96,96));bitmap.Render(visual);
        using var memory=new MemoryStream();bitmap.Save(memory,new PngBitmapEncoderOptions());return memory.ToArray();
    }
}
