using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
namespace Dafeiyu
{
    public static class UiIcons
    {
        public static FrameworkElement Image(string name,Brush ink)
        {
            string shape=name=="pin"||name=="pin-filled"?"M15,3 L21,9 18,10 14,15 14,18 11,18 6,13 6,10 9,10 14,6 Z M9,15 L3,21":name=="archive"?"M3,4 L21,4 21,8 3,8 Z M5,8 L5,19 Q5,21 7,21 L17,21 Q19,21 19,19 L19,8 M9,12 L15,12":name=="speaker"?"M3,9 L7,9 12,5 12,19 7,15 3,15 Z M16,9 C19,11 19,13 16,15 M18,6 C23,9 23,15 18,18":
                name=="copy"?"M9,8 L19,8 19,21 9,21 Z M5,16 L4,16 4,3 14,3 14,5":
                name=="warning"?"M10,4 Q12,1 14,4 L22,18 Q23,21 20,21 L4,21 Q1,21 2,18 Z M12,8 L12,13 M12,16 L12,17":
                name=="pet-visible"?"M2,12 C6,4 18,4 22,12 C18,20 6,20 2,12 M15,12 A3,3 0 1 1 9,12 A3,3 0 1 1 15,12":
                name=="quiet"?"M18,4 C8,1 3,8 6,15 C9,23 20,22 22,14 C15,17 10,10 18,4 Z":
                name=="pointer"?"M5,3 L5,19 10,15 14,22 17,20 13,13 20,12 Z":
                name=="summary"?"M7,5 A2,2 0 1 1 3,5 A2,2 0 1 1 7,5 M7,12 A2,2 0 1 1 3,12 A2,2 0 1 1 7,12 M7,19 A2,2 0 1 1 3,19 A2,2 0 1 1 7,19 M12,5 L21,5 M12,12 L21,12 M12,19 L21,19":
                name=="new-tab"?"M5,3 L19,3 Q21,3 21,5 L21,19 Q21,21 19,21 L5,21 Q3,21 3,19 L3,5 Q3,3 5,3 M12,7 L12,17 M7,12 L17,12":
                name=="add"?"M12,5 L12,19 M5,12 L19,12":                name=="file"?"M6,3 L14,3 19,8 19,21 6,21 Z M14,3 L14,8 19,8":
                name=="folder"?"M3,8 L3,6 Q3,4 5,4 L9,4 12,7 19,7 Q21,7 21,9 L21,18 Q21,20 19,20 L5,20 Q3,20 3,18 Z M3,10 L21,10":                name=="folder-open"?"M3,12 L3,6 Q3,4 5,4 L9,4 12,7 19,7 Q21,7 21,9 L21,10 M3,11 L21,11 Q23,11 22,13 L19,19 Q18.5,20 17,20 L5,20 Q3,20 3,18 Z":
                name=="chat"?"M5,4 L19,4 Q21,4 21,6 L21,16 Q21,18 19,18 L10,18 5,21 5,18 Q3,18 3,16 L3,6 Q3,4 5,4 Z M8,9 L8,10 M12,9 L12,10 M16,9 L16,10":
                name=="globe"?"M22,12 A10,10 0 1 1 2,12 A10,10 0 1 1 22,12 M2,12 L22,12 M12,2 C6,7 6,17 12,22 C18,17 18,7 12,2":name=="link"?"M9,15 L15,9 M8,17 L6,19 C4,21 1,18 3,16 L6,13 M16,7 L18,5 C20,3 23,6 21,8 L18,11":                name=="attachment"?"M8,14 L14,6 C18,2 23,7 19,11 L10,20 C6,24 0,18 4,14 L14,4 M8,15 L16,7":
                name=="snapshot"?"M3,8 L3,4 7,4 M17,4 L21,4 21,8 M21,16 L21,20 17,20 M7,20 L3,20 3,16 M7,9 L17,9 17,15 7,15 Z":
                name=="target"?"M21,12 A9,9 0 1 1 12,3 M17,12 A5,5 0 1 1 12,7 M12,12 L22,2 M17,2 L22,2 22,7":
                name=="more"?"M5,11 L5,13 M12,11 L12,13 M19,11 L19,13":
                name=="muted"?"M3,9 L7,9 12,5 12,19 7,15 3,15 Z M16,9 L22,15 M22,9 L16,15":
                name=="live"?"M4,9 L4,15 M8,5 L8,19 M12,2 L12,22 M16,6 L16,18 M20,9 L20,15":
                name=="stop"?"M6,6 L18,6 18,18 6,18 Z":
                name=="split"?"M4,3 L20,3 Q21,3 21,4 L21,20 Q21,21 20,21 L4,21 Q3,21 3,20 L3,4 Q3,3 4,3 M12,3 L12,21":
                name=="full-view"?"M8,3 L3,3 3,8 M16,3 L21,3 21,8 M3,16 L3,21 8,21 M21,16 L21,21 16,21":
                name=="expand"?"M14,4 L20,4 20,10 M20,4 L11,13 M10,5 L5,5 5,19 19,19 19,14":
                name=="send"?"M12,19 L12,5 M6,11 L12,5 18,11":
                name=="back"?"M19,12 L5,12 M11,6 L5,12 11,18":
                name=="edit"?"M4,20 L5,15 16,4 C18,2 22,6 20,8 L9,19 Z M14,6 L18,10":
                name=="arrow-down"?"M12,4 L12,20 M5,13 L12,20 19,13":
                name=="down"?"M6,9 L12,15 18,9":
                name=="queue"?"M4,4 L4,16 Q4,19 7,19 L20,19 M17,16 L20,19 17,22 M9,5 L20,5 M9,10 L17,10 M9,14 L14,14":
                name=="steer"?"M4,5 L4,10 Q4,13 8,13 L20,13 M15,8 L20,13 15,18":
                name=="mic"?"M9,6 C9,2 15,2 15,6 L15,12 C15,16 9,16 9,12 Z M5,11 L5,12 C5,21 19,21 19,12 L19,11 M12,19 L12,22":
                name=="trash"?"M4,6 L20,6 M9,6 L9,3 15,3 15,6 M6,6 L7,21 17,21 18,6 M10,10 L10,17 M14,10 L14,17":
                name=="settings"?"M4,7 L20,7 M4,17 L20,17 M9,4 L9,10 M15,14 L15,20":
                name=="terminal"?"M5,3 L19,3 Q21,3 21,5 L21,19 Q21,21 19,21 L5,21 Q3,21 3,19 L3,5 Q3,3 5,3 M7,8 L11,12 7,16 M14,16 L18,16":
                name=="search"?"M10,3 A7,7 0 1 1 9.99,3 M15,15 L21,21":
                name=="list"?"M4,5 L4,6 M4,11 L4,12 M4,17 L4,18 M9,5.5 L21,5.5 M9,11.5 L21,11.5 M9,17.5 L21,17.5":
                name=="globe"?"M12,3 A9,9 0 1 1 11.99,3 M3,12 L21,12 M12,3 C6,8 6,16 12,21 C18,16 18,8 12,3":
                name=="phone"?"M3,10 C8,5 16,5 21,10 L20,15 C19,16 16,14 15,13 L15,10 C13,9 11,9 9,10 L9,13 C8,14 5,16 4,15 Z":"M6,6 L18,18 M18,6 L6,18";
            // Keep every glyph on the same 24-unit canvas. Stretching the path
            // itself made small shapes (especially stop/expand) look oversized.
            var canvas=new Canvas {Width=24,Height=24};
            // The arrowhead carries more visual weight than the stem. A small
            // optical correction centers the send glyph in both circular buttons.
            if(name=="send")canvas.RenderTransform=new TranslateTransform(0,1);
            Geometry geometry=name=="stop" || name=="stop-solid"?new RectangleGeometry(new Rect(6,6,12,12),1.5,1.5):Geometry.Parse(shape);
            if(name=="github")geometry=Geometry.Parse("M12,2 C6.477,2 2,6.477 2,12 C2,16.419 4.865,20.166 8.839,21.489 C9.339,21.581 9.521,21.272 9.521,21.007 L9.507,19.304 C6.725,19.908 6.138,17.962 6.138,17.962 C5.684,16.807 5.028,16.499 5.028,16.499 C4.12,15.878 5.097,15.891 5.097,15.891 C6.101,15.962 6.629,16.922 6.629,16.922 C7.521,18.451 8.97,18.009 9.539,17.753 C9.63,17.107 9.888,16.666 10.174,16.416 C7.953,16.163 5.619,15.305 5.619,11.473 C5.619,10.382 6.009,9.489 6.648,8.79 C6.545,8.537 6.202,7.52 6.746,6.143 C6.746,6.143 7.586,5.874 9.496,7.168 C11.134,6.718 12.866,6.718 14.504,7.168 C16.413,5.874 17.252,6.143 17.252,6.143 C17.798,7.52 17.454,8.537 17.352,8.79 C17.992,9.489 18.38,10.382 18.38,11.473 C18.38,15.315 16.043,16.16 13.814,16.408 C14.173,16.717 14.492,17.327 14.492,18.26 L14.48,21.004 C14.48,21.272 14.66,21.583 15.168,21.485 C19.138,20.162 22,16.417 22,12 C22,6.477 17.523,2 12,2 Z");
            if(name=="stop")ink=Ui.Brush("#CC4055");
            if(name=="settings")
            {
                var gear=new StreamGeometry();
                using(var context=gear.Open())
                {
                    for(int i=0;i<32;i++)
                    {
                        double angle=(i*11.25-5.625)*Math.PI/180,radius=i%4<2?9.5:7.5;
                        var point=new Point(12+Math.Cos(angle)*radius,12+Math.Sin(angle)*radius);
                        if(i==0)context.BeginFigure(point,false,true);else context.LineTo(point,true,false);
                    }
                }
                var group=new GeometryGroup();group.Children.Add(gear);group.Children.Add(new EllipseGeometry(new Point(12,12),3,3));geometry=group;
            }
            geometry.Freeze();
            canvas.Children.Add(new Path {Data=geometry,Stroke=ink,Fill=name=="github" || name=="phone" || name=="stop-solid" || name=="pin-filled"?ink:null,StrokeThickness=name=="github"?0:name=="queue"?1.5:1.7,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round});
            return new Viewbox {Width=18,Height=18,Child=canvas};
        }
        public static Button Button(string icon,string label,bool primary=false)
        {
            var button=Ui.Button(label,primary);button.Content=Image(icon,primary?Brushes.White:Ui.Ink);button.ToolTip=null;
            button.Width=36;button.Height=36;button.MinHeight=36;button.Padding=new Thickness(8);button.Margin=new Thickness(0,0,6,0);
            if(!primary){button.Background=Brushes.Transparent;button.BorderBrush=Brushes.Transparent;}
            System.Windows.Automation.AutomationProperties.SetName(button,label);return button;
        }
        public static CheckBox Speech(PetWindow pet)
        {
            var toggle=new CheckBox {Name="TtsOutput",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,0,0),Cursor=System.Windows.Input.Cursors.Hand,Focusable=true,FocusVisualStyle=null};
            toggle.Template=(ControlTemplate)ThemeManager.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='CheckBox'><Border x:Name='b' Background='Transparent' CornerRadius='9' Padding='7' BorderBrush='Transparent' BorderThickness='1'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='b' Property='Background' Value='#EFEFEF'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='#EFEFEF'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='b' Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            Action sync=delegate {toggle.IsChecked=pet.Settings.ReadReplies;toggle.Content=Image(pet.Settings.ReadReplies?"speaker":"muted",pet.Settings.ReadReplies?Ui.Blue:Ui.Muted);toggle.ToolTip=pet.Settings.ReadReplies?TextResources.Get("uiIcons.speech.petSettingsReadReplies"):TextResources.Get("uiIcons.speech.petSettingsReadReplies.alternativeA");System.Windows.Automation.AutomationProperties.SetName(toggle,(string)toggle.ToolTip);};
            toggle.Checked+=delegate {pet.SetReplySpeech(true);sync();};toggle.Unchecked+=delegate {pet.SetReplySpeech(false);sync();};
            toggle.Loaded+=delegate {sync();pet.ReplySpeechChanged+=sync;};toggle.Unloaded+=delegate {pet.ReplySpeechChanged-=sync;};sync();return toggle;
        }
    }
}
