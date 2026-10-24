using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Dafeiyu
{
    // Scoped presentation for the existing data-maintenance surfaces.
    internal static class DataManagementVisual
    {
        internal static FrameworkElement Icon(string kind,double size=22)
        {
            string data=kind=="chat"?"M3,4 L21,4 L21,17 L9,17 L3,22 Z M7,9 L17,9 M7,13 L14,13":kind=="settings"?"M5,3 L5,21 M12,3 L12,21 M19,3 L19,21 M2,8 L8,8 M9,16 L15,16 M16,10 L22,10":kind=="folder"?"M3,6 L10,6 L13,9 L21,9 L21,20 L3,20 Z":kind=="warning"?"M12,2 L23,22 L1,22 Z M12,8 L12,14 M12,17 L12,19":kind=="shield"?"M12,2 L21,6 L20,15 Q18,20 12,23 Q6,20 4,15 L3,6 Z M8,12 L11,15 L17,9":kind=="download"?"M12,2 L12,16 M7,11 L12,16 L17,11 M3,17 L3,22 L21,22 L21,17":"M4,3 L16,3 L21,8 L21,22 L4,22 Z M16,3 L16,9 L21,9 M8,13 L17,13 M8,17 L17,17";
            return new Viewbox{Width=size,Height=size,VerticalAlignment=VerticalAlignment.Center,Child=new Path{Data=Geometry.Parse(data),Stroke=ThemeManager.Brush("#456DE6"),StrokeThickness=1.6,StrokeLineJoin=PenLineJoin.Round,Width=24,Height=24,Stretch=Stretch.Uniform},Focusable=false};
        }
        internal static FrameworkElement Header(string icon,string title,double size=15)
        {
            var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(30)});row.ColumnDefinitions.Add(new ColumnDefinition());row.Children.Add(Icon(icon,20));var text=Ui.Text(title,size,Ui.Ink);text.FontWeight=FontWeights.SemiBold;Grid.SetColumn(text,1);row.Children.Add(text);return row;
        }
        internal static FrameworkElement Note(string icon,string copy)
        {
            var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(28)});row.ColumnDefinitions.Add(new ColumnDefinition());row.Children.Add(Icon(icon,18));var text=Ui.Text(copy,12,Ui.Muted);Grid.SetColumn(text,1);row.Children.Add(text);return row;
        }
        internal static Border Card(UIElement child){return new Border{Child=child,Padding=new Thickness(18),Margin=new Thickness(0,0,0,12),CornerRadius=new CornerRadius(14),Background=ThemeManager.CardSurface,BorderBrush=ThemeManager.CardBorder,BorderThickness=new Thickness(1)};}
        internal static void Button(Button button,bool danger=false)
        {
            button.HorizontalAlignment=HorizontalAlignment.Left;button.MinHeight=36;button.Margin=new Thickness(0);button.Padding=new Thickness(12,8,12,8);
            button.Content=Ui.Text(button.Content==null?"":button.Content.ToString(),12,danger?ThemeManager.Brush("#AC4A57"):Ui.Ink);
            button.Background=danger?ThemeManager.Brush("#FFF3F5"):ThemeManager.CardSurface;button.BorderBrush=danger?ThemeManager.Brush("#F5DCE1"):ThemeManager.CardBorder;button.BorderThickness=new Thickness(1);
            button.Template=(ControlTemplate)ThemeManager.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='frame' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='9' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='True'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='frame' Property='Background' Value='#EDF3FF'/><Setter TargetName='frame' Property='BorderBrush' Value='#456DE6'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='frame' Property='BorderBrush' Value='#456DE6'/><Setter TargetName='frame' Property='BorderThickness' Value='2'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='frame' Property='Background' Value='#E8EBF0'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
        }
        internal static FrameworkElement ActionRow(string icon,string title,string description,FrameworkElement action)
        {
            var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(icon.Length>0?38:0)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});row.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            if(icon.Length>0)row.Children.Add(new Border{Width=32,Height=32,CornerRadius=new CornerRadius(10),Background=ThemeManager.Brush("#EDF3FF"),Child=Icon(icon,20),VerticalAlignment=VerticalAlignment.Top});
            var words=new StackPanel{Margin=new Thickness(icon.Length>0?10:0,0,12,0)};var heading=Ui.Text(title,15);heading.FontWeight=FontWeights.SemiBold;words.Children.Add(heading);var note=Ui.Text(description,12,Ui.Muted);note.Margin=new Thickness(0,5,0,0);words.Children.Add(note);Grid.SetColumn(words,1);row.Children.Add(words);row.Children.Add(action);
            Action reflow=delegate{bool narrow=row.ActualWidth<490;Grid.SetRow(action,narrow?1:0);Grid.SetColumn(action,narrow?1:2);action.Margin=new Thickness(narrow&&icon.Length>0?10:0,narrow?14:0,0,0);Grid.SetColumnSpan(words,narrow?2:1);action.VerticalAlignment=narrow?VerticalAlignment.Top:VerticalAlignment.Center;};row.SizeChanged+=delegate{reflow();};reflow();return row;
        }
        internal static FrameworkElement Category(string icon,string title,FrameworkElement value)
        {
            var row=new Grid{Margin=new Thickness(0,8,0,8)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(28)});row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.Children.Add(Icon(icon,18));var label=Ui.Text(title,13);label.Margin=new Thickness(6,0,12,0);Grid.SetColumn(label,1);row.Children.Add(label);Grid.SetColumn(value,2);row.Children.Add(value);return row;
        }
    }
}
