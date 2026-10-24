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
        internal static FrameworkElement Create(RelationshipCardData data,ImageSource avatar)
        {
            var content=new Grid{Width=800,Height=480,Background=new SolidColorBrush(Color.FromRgb(244,247,252))};
            var panel=new StackPanel{Margin=new Thickness(42,32,42,30)};
            var title=Text(TextResources.Get("relationship.card.title"),20);title.Foreground=new SolidColorBrush(Color.FromRgb(53,99,160));panel.Children.Add(title);
            var row=new Grid{Margin=new Thickness(0,28,0,24)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(200)});row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(new Image{Source=avatar,Width=168,Height=168,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left});
            var words=new StackPanel{VerticalAlignment=VerticalAlignment.Center};words.Children.Add(Text(data.Name,34));words.Children.Add(Text(data.StageName,27));Grid.SetColumn(words,1);row.Children.Add(words);panel.Children.Add(row);
            panel.Children.Add(Text(data.DateText,20));panel.Children.Add(Text(TextResources.Get("relationship.card.footer"),16));content.Children.Add(panel);return content;
        }
        static TextBlock Text(string value,double size){return new TextBlock{Text=value,FontSize=size,FontFamily=new FontFamily("Global User Interface"),Foreground=new SolidColorBrush(Color.FromRgb(29,44,68)),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12)};}
        internal static byte[] Render(RelationshipCardData data,ImageSource avatar)
        {
            var visual=Create(data,avatar);visual.Measure(new Size(800,480));visual.Arrange(new Rect(0,0,800,480));visual.UpdateLayout();
            var bitmap=new RenderTargetBitmap(800,480,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var memory=new MemoryStream()){png.Save(memory);return memory.ToArray();}
        }
    }
}
