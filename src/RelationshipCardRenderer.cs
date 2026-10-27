using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Dafeiyu
{
    internal static class RelationshipCardRenderer
    {
        internal static FrameworkElement Create(RelationshipCardData data,ImageSource portrait){return Create(data,portrait,ThemeManager.IsDark);}
        internal static FrameworkElement Create(RelationshipCardData data,ImageSource portrait,bool dark){return Create(data,portrait,dark,null);}
        internal static FrameworkElement Create(RelationshipCardData data,ImageSource portrait,bool dark,BitmapSource scene)
        {
            if(scene!=null)return CreateScene(data,scene,dark);
            string title=TextResources.Get("relationship.card.title"),name=data.Name,stage=data.StageName,memory=data.MemoryText,date=data.DateText,day=data.AcquaintanceText;
            var content=new Canvas{Width=800,Height=480,Background=new SolidColorBrush(dark?Color.FromRgb(43,53,69):Color.FromRgb(244,247,252))};
            var source=new CompanionPortraitSource(portrait as BitmapSource);var picture=new CompanionPortraitVisual(source,dark){Width=296,Height=416};Canvas.SetLeft(picture,32);Canvas.SetTop(picture,32);content.Children.Add(picture);
            Add(content,title,360,30,408,24,16,16,dark,true,false,"RelationshipCardTitle");
            Add(content,name,360,62,408,40,27,24,dark,false,true,"RelationshipCardName");
            Add(content,stage,360,108,408,100,44,40,dark,false,false,"RelationshipCardStage");
            Add(content,memory,360,218,408,116,26,24,dark,false,false,"RelationshipCardMemory");
            Add(content,date,360,354,408,62,22,20,dark,true,false,"RelationshipCardDate");
            Add(content,day,360,424,408,26,19,18,dark,true,false,"RelationshipCardAcquaintance");
            System.Windows.Automation.AutomationProperties.SetName(content,name+" "+stage+" "+memory+" "+date+" "+day);return content;
        }
        static void Add(Canvas canvas,string value,double x,double y,double width,double height,double size,double minimum,bool dark,bool muted,bool oneLine,string name)
        {
            var text=new TextBlock{Name=name,Text=value,Width=width,Height=height,FontSize=size,FontFamily=new FontFamily("Global User Interface"),Foreground=new SolidColorBrush(dark?(muted?Color.FromRgb(181,196,219):Color.FromRgb(229,234,242)):(muted?Color.FromRgb(100,117,142):Color.FromRgb(32,55,84))),TextWrapping=oneLine?TextWrapping.NoWrap:TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,FontWeight=name=="RelationshipCardStage"?FontWeights.SemiBold:FontWeights.Normal,ClipToBounds=true};
            while(text.FontSize>minimum){var probe=new TextBlock{Text=value,FontSize=text.FontSize,FontFamily=text.FontFamily,FontWeight=text.FontWeight,TextWrapping=text.TextWrapping};probe.Measure(new Size(width,Double.PositiveInfinity));if(probe.DesiredSize.Height<=height)break;text.FontSize--;}
            text.ToolTip=value;System.Windows.Automation.AutomationProperties.SetName(text,value);Canvas.SetLeft(text,x);Canvas.SetTop(text,y);canvas.Children.Add(text);
        }

        // Scene is borrowed from the page. This branch never loads or disposes artwork.
        internal static Canvas CreateScene(RelationshipCardData data,BitmapSource scene,bool dark)
        {
            var content=SceneBackground(scene,dark);
SceneText(content,TextResources.Get("relationship.card.title"),32,24,15,15,dark,"RelationshipCardTitle",false);
            SceneText(content,data.Name,62,38,27,22,dark,"RelationshipCardName",false);
            double titleHeight=SceneText(content,data.StageName,108,104,44,32,dark,"RelationshipCardStage",true);
            var ornament=OrnamentFor(data.StageId,dark);
            Canvas.SetLeft(ornament,456);Canvas.SetTop(ornament,108+titleHeight+12);content.Children.Add(ornament);
            SceneText(content,data.MemoryText,270,92,24,18,dark,"RelationshipCardMemory",false);
            SceneText(content,data.DateText,378,52,20,16,dark,"RelationshipCardDate",false);
            SceneText(content,data.AcquaintanceText,438,24,17,15,dark,"RelationshipCardAcquaintance",false);
            System.Windows.Automation.AutomationProperties.SetName(content,data.Name+" "+data.StageName+" "+data.MemoryText+" "+data.DateText+" "+data.AcquaintanceText);
            return content;
        }

        static Canvas SceneBackground(BitmapSource scene,bool dark)
        {
            var content=new Canvas{Width=800,Height=480,ClipToBounds=true,Background=dark?Brushes.MidnightBlue:Brushes.White};
            content.Children.Add(new Image{Source=scene,Width=800,Height=480,Stretch=Stretch.Uniform});
            var fade=new LinearGradientBrush();fade.StartPoint=new Point(0,0);fade.EndPoint=new Point(1,0);
            var color=dark?Color.FromRgb(20,30,49):Color.FromRgb(248,249,251);
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0,color.R,color.G,color.B),0));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0,color.R,color.G,color.B),.36));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(dark?(byte)85:(byte)90,color.R,color.G,color.B),.45));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(dark?(byte)166:(byte)175,color.R,color.G,color.B),.55));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(dark?(byte)166:(byte)175,color.R,color.G,color.B),1));
            content.Children.Add(new Border{Name="RelationshipCardReadingGradient",Width=800,Height=480,Background=fade,IsHitTestVisible=false});
            return content;
        }
        internal static Canvas CreateHoliday(HolidayKeepsakeCardData data,BitmapSource scene,bool dark)
        {
            var content=SceneBackground(scene,dark);
            SceneText(content,TextResources.Get("holidayKeepsake.albumTitle"),32,24,15,15,dark,"HolidayCardHeading",false);
            SceneText(content,data.Name,62,38,27,22,dark,"HolidayCardName",false);
            double titleHeight=SceneText(content,data.Title,108,104,44,24,dark,"HolidayCardTitle",true);
            var ornament=OrnamentFor(data.Style,dark);Canvas.SetLeft(ornament,456);Canvas.SetTop(ornament,108+titleHeight+12);content.Children.Add(ornament);
            SceneText(content,data.Message,270,92,24,18,dark,"HolidayCardMessage",false);
            SceneText(content,data.DateText,378,52,20,16,dark,"HolidayCardDate",false);
            SceneText(content,data.YearText,438,24,17,15,dark,"HolidayCardYear",false);
            return content;
        }
        internal static byte[] RenderHoliday(HolidayKeepsakeCardData data,BitmapSource scene,bool dark)
        {
            var visual=CreateHoliday(data,scene,dark);visual.Measure(new Size(800,480));visual.Arrange(new Rect(0,0,800,480));visual.UpdateLayout();
            var bitmap=new RenderTargetBitmap(800,480,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var memory=new MemoryStream()){png.Save(memory);return memory.ToArray();}
        }
        internal static System.Windows.Shapes.Path OrnamentFor(string style,bool dark)
        {
            var decoration=Geometry.Parse(RelationshipCardDecoration.PathFor(style)).Clone();
            // Fixed design coordinates, with room for stroke/antialiasing. Avalonia's
            // geometry Bounds can omit a trailing line-only figure; never derive scale from it.
            decoration.Transform=new MatrixTransform(new Matrix(RelationshipCardDecoration.ScaleX,0,0,RelationshipCardDecoration.ScaleY,RelationshipCardDecoration.Inset,RelationshipCardDecoration.Inset));
            var ornament=new System.Windows.Shapes.Path{Data=decoration,Stroke=new SolidColorBrush(dark?Color.FromRgb(237,203,139):Color.FromRgb(122,86,35)),StrokeThickness=1.25,Stretch=Stretch.None,Width=RelationshipCardDecoration.Width,Height=RelationshipCardDecoration.Height,IsHitTestVisible=false};
            // WPF Path has no automation peer; decoration is not an accessible control.
            return ornament;
        }
        static double SceneText(Canvas canvas,string value,double y,double height,double size,double minimum,bool dark,string name,bool artistic)
        {
            var family=artistic?RelationshipTitleFont.FontForLocale(TextResources.CurrentLanguage,value):new FontFamily("Global User Interface");
            var text=new TextBlock{Name=name,Text=value,Width=312,Height=height,FontSize=size,FontFamily=family,TextWrapping=name=="HolidayCardName"?TextWrapping.NoWrap:TextWrapping.Wrap,TextTrimming=name=="HolidayCardName"?TextTrimming.CharacterEllipsis:TextTrimming.None,Foreground=new SolidColorBrush(artistic?(dark?Color.FromRgb(255,231,177):Color.FromRgb(73,52,22)):(dark?Color.FromRgb(255,249,236):Color.FromRgb(38,61,97)))};
            text.Language=System.Windows.Markup.XmlLanguage.GetLanguage(TextResources.CurrentLanguage.Replace('_','-'));
            double measured=height;
            // Measure the whole string with its actual local family; never truncate stage text.
            for(double candidate=size;candidate>=minimum;candidate--){
                var probe=new TextBlock{Text=value,Width=312,FontSize=candidate,FontFamily=family,TextWrapping=text.TextWrapping};
                probe.Language=System.Windows.Markup.XmlLanguage.GetLanguage(TextResources.CurrentLanguage.Replace('_','-'));
                probe.Measure(new Size(312,Double.PositiveInfinity));
                text.FontSize=candidate;measured=probe.DesiredSize.Height;
                if(probe.DesiredSize.Height<=height&&(!artistic||probe.DesiredSize.Height<=candidate*2.7))break;
            }
            text.ToolTip=value;
            System.Windows.Automation.AutomationProperties.SetName(text,value);Canvas.SetLeft(text,456);Canvas.SetTop(text,y);text.Tag=measured;canvas.Children.Add(text);return measured;
        }
        internal static byte[] Render(RelationshipCardData data,ImageSource portrait){return Render(data,portrait,ThemeManager.IsDark);}
        internal static byte[] Render(RelationshipCardData data,ImageSource portrait,bool dark){return Render(data,portrait,dark,null);}
        internal static byte[] Render(RelationshipCardData data,ImageSource portrait,bool dark,BitmapSource scene)
        {
            var visual=Create(data,portrait,dark,scene);visual.Measure(new Size(800,480));visual.Arrange(new Rect(0,0,800,480));visual.UpdateLayout();
            var bitmap=new RenderTargetBitmap(800,480,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var memory=new MemoryStream()){png.Save(memory);return memory.ToArray();}
        }
    }
}
