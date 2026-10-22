using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dafeiyu
{
    // Offline vector paths; attribution is in docs/sources.md.
    public static class ProviderLogo
    {
        static readonly Dictionary<string,ImageSource> images=new Dictionary<string,ImageSource>();
        static Dictionary<string,object> artwork;
        public static FrameworkElement Create(string address,string name,double size=24,string brand=null)
        {
            brand=brand ?? ProviderIdentity.Brand(address);if(brand=="custom")brand="";
            if(brand.Length>0)
            {
                ImageSource source;
                if(!images.TryGetValue(brand,out source))
                {
                    if(artwork==null)using(var reader=new StreamReader(typeof(ProviderLogo).Assembly.GetManifestResourceStream("Dafeiyu.ProviderLogos")))artwork=(Dictionary<string,object>)((Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(reader.ReadToEnd()))["icons"];
                    var group=new DrawingGroup();var icon=(Dictionary<string,object>)artwork[brand];
                    group.Children.Add(new GeometryDrawing(Brushes.Transparent,null,new RectangleGeometry(new Rect(0,0,24,24))));
                    foreach(var raw in (object[])icon["paths"])
                    {
                        var path=(Dictionary<string,object>)raw;var geometry=Geometry.Parse(((bool)path["evenOdd"]?"F0 ":"F1 ")+(string)path["data"]);geometry.Freeze();
                        var fill=path.ContainsKey("fill")?Ui.FixedBrush((string)path["fill"]):Color(brand);
                        group.Children.Add(new GeometryDrawing(fill,null,geometry));
                    }
                    group.Freeze();source=new DrawingImage(group);source.Freeze();images[brand]=source;
                }
                return new Image{Name="ProviderLogo_"+brand,Source=source,Width=size,Height=size,Stretch=Stretch.Uniform,ToolTip=name,VerticalAlignment=VerticalAlignment.Center};
            }
            string label=String.IsNullOrWhiteSpace(name)?"＋":System.Globalization.StringInfo.GetNextTextElement(name.Trim()).ToUpperInvariant();
            return new Border{Name="ProviderLogo_custom",Width=size,Height=size,CornerRadius=new CornerRadius(6),Background=Ui.Pale,Child=new TextBlock{Text=label,Foreground=Ui.Blue,FontSize=size*.48,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
        }
        static Brush Color(string brand)
        {
            switch(brand){case "openai":return Ui.FixedBrush("#16836B");case "anthropic":return Ui.FixedBrush("#A86C53");case "deepseek":return Ui.FixedBrush("#4D6BFE");case "gemini":return Ui.FixedBrush("#4285F4");case "xiaomimimo":return Brushes.Black;case "minimax":return Ui.FixedBrush("#EF4768");case "moonshot":return Ui.FixedBrush("#111827");case "qwen":return Ui.FixedBrush("#615CED");case "zhipu":return Ui.FixedBrush("#3B62F6");case "doubao":return Ui.FixedBrush("#4A72F5");default:return Ui.FixedBrush("#203754");}
        }
    }
}
