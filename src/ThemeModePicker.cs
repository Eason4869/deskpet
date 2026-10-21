using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dafeiyu
{
    internal sealed class ThemeModePicker : UserControl
    {
        readonly Dictionary<string,Button> choices=new Dictionary<string,Button>();
        string value;
        internal event Action<string> Changed;
        internal string Value
        {
            get{return value;}
            set
            {
                this.value=ThemeColors.Normalize(value);
                foreach(var pair in choices)
                {
                    pair.Value.BorderBrush=pair.Key==this.value?Ui.Blue:Brushes.Transparent;
                    System.Windows.Automation.AutomationProperties.SetItemStatus(pair.Value,pair.Key==this.value?"已选中":"未选中");
                }
            }
        }
        internal ThemeModePicker(string initial)
        {
            Name="ThemeModePicker";
            var layout=new Grid();layout.ColumnDefinitions.Add(new ColumnDefinition());layout.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var label=Ui.InlineText("模式",13);label.FontWeight=FontWeights.SemiBold;layout.Children.Add(label);
            var row=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
            foreach(string mode in new[]{"system","light","dark"})
            {
                string selected=mode,title=mode=="system"?"跟随系统":mode=="light"?"浅色":"暗色";
                var button=new Button{Name="ThemeChoice"+mode,Content=Preview(mode),Width=84,Height=64,Margin=new Thickness(8,0,0,0),Padding=new Thickness(4),Background=Brushes.Transparent,BorderThickness=new Thickness(2),Cursor=System.Windows.Input.Cursors.Hand,ToolTip=title};
                var border=new FrameworkElementFactory(typeof(Border));border.Name="ThemeChoiceFrame";border.SetValue(Border.CornerRadiusProperty,new CornerRadius(10));
                border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));border.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty));border.SetValue(Border.BorderThicknessProperty,new TemplateBindingExtension(Control.BorderThicknessProperty));border.SetValue(Border.PaddingProperty,new TemplateBindingExtension(Control.PaddingProperty));
                border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));var template=new ControlTemplate(typeof(Button)){VisualTree=border};
                var focus=new Trigger{Property=UIElement.IsKeyboardFocusedProperty,Value=true};focus.Setters.Add(new Setter(Border.BackgroundProperty,Ui.Pale,"ThemeChoiceFrame"));template.Triggers.Add(focus);button.Template=template;
                System.Windows.Automation.AutomationProperties.SetName(button,title);
                button.Click+=delegate{Value=selected;if(Changed!=null)Changed(selected);};choices.Add(mode,button);row.Children.Add(button);
            }
            Grid.SetColumn(row,1);layout.Children.Add(row);
            Content=new Border{CornerRadius=new CornerRadius(16),Background=Ui.Brush("#FDFEFF"),BorderBrush=Ui.Line,BorderThickness=new Thickness(1),Padding=new Thickness(14,8,10,8),Child=layout};
            Value=initial;
        }
        static FrameworkElement Preview(string mode)
        {
            var grid=new Grid{Width=72,Height=52,Clip=new RectangleGeometry(new Rect(0,0,72,52),6,6)};
            if(mode!="dark")grid.Children.Add(Sketch(false));
            if(mode!="light")
            {
                var dark=Sketch(true);
                if(mode=="system")dark.Clip=new RectangleGeometry(new Rect(36,0,36,52));
                grid.Children.Add(dark);
            }
            return grid;
        }
        static Canvas Sketch(bool dark)
        {
            var canvas=new Canvas{Width=72,Height=52,Background=Ui.FixedBrush(dark?"#192332":"#FFFFFF")};
            Block(canvas,0,0,11,52,dark?"#111A27":"#F0F2F5",0);Block(canvas,11,0,12,52,dark?"#222E40":"#F7F8FA",0);
            Block(canvas,4,6,4,4,dark?"#75839A":"#BBC3CF",2);Block(canvas,4,15,4,2,dark?"#53627A":"#D6DBE3",1);
            Block(canvas,27,7,38,2,dark?"#7295D6":"#7198D8",1);Block(canvas,39,14,26,2,dark?"#53647D":"#D9DFE9",1);
            Block(canvas,29,20,30,2,dark?"#718098":"#C3CDDC",1);Block(canvas,40,27,25,2,"#4887EF",1);
            Block(canvas,27,34,38,2,dark?"#53647D":"#D9DFE9",1);Block(canvas,27,42,39,6,dark?"#2A384C":"#EDF1F7",2);Block(canvas,60,44,3,3,"#4887EF",1.5);
            return canvas;
        }
        static void Block(Canvas canvas,double x,double y,double width,double height,string color,double radius)
        {
            var block=new Border{Width=width,Height=height,Background=Ui.FixedBrush(color),CornerRadius=new CornerRadius(radius)};Canvas.SetLeft(block,x);Canvas.SetTop(block,y);canvas.Children.Add(block);
        }
    }
}
