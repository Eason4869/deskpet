using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Data;
using Avalonia.Input;
using Path = Avalonia.Controls.Shapes.Path;

namespace DeskPet.Desktop;

// Local settings/maintenance presentation. Brushes remain live on retained pages.
internal static class SettingsVisual
{
    internal static IBrush Ink => DesktopTheme.Brush("#203754");
    internal static IBrush Muted => DesktopTheme.Brush("#61738B");
    internal static IBrush Accent => DesktopTheme.Brush("#357FF5");
    internal static TextBlock Text(string text,double size=14,bool muted=false) => new() {Text=text,FontSize=size,Foreground=muted?Muted:Ink,TextWrapping=TextWrapping.Wrap};
    internal static Control Icon(string kind,double size=22)
    {
        string data=kind switch {
            "chat" => "M3,4 L21,4 L21,17 L9,17 L3,22 Z M7,9 L17,9 M7,13 L14,13",
            "folder" or "storage" => "M3,6 L10,6 L13,9 L21,9 L21,20 L3,20 Z",
            "settings" => "M5,3 L5,21 M12,3 L12,21 M19,3 L19,21 M2,8 L8,8 M9,16 L15,16 M16,10 L22,10",
            "work" => "M3,7 L21,7 L21,21 L3,21 Z M8,7 L8,3 L16,3 L16,7 M3,12 L21,12",
            "model" => "M3,3 L21,3 L21,21 L3,21 Z M7,8 L17,8 M7,12 L17,12 M7,16 L13,16",
            "partner" => "M12,12 A4,4 0 1 1 11.99,4 M4,22 C4,12 20,12 20,22",
            "asr" => "M9,4 A3,3 0 0 1 15,4 L15,12 A3,3 0 0 1 9,12 Z M5,11 C5,21 19,21 19,11 M12,19 L12,23",
            "speech" or "voice" or "live" => "M3,9 L7,9 L12,4 L12,20 L7,15 L3,15 Z M16,7 C21,9 21,15 16,17 M16,11 C18,11 18,13 16,13",
            "warning" => "M12,2 L23,22 L1,22 Z M12,8 L12,14 M12,17 L12,19",
            "shield" => "M12,2 L21,6 L20,15 Q18,20 12,23 Q6,20 4,15 L3,6 Z M8,12 L11,15 L17,9",
            "download" => "M12,2 L12,16 M7,11 L12,16 L17,11 M3,17 L3,22 L21,22 L21,17",
            _ => "M4,3 L16,3 L21,8 L21,22 L4,22 Z M16,3 L16,9 L21,9 M8,13 L17,13 M8,17 L17,17"};
        return new Viewbox{Width=size,Height=size,VerticalAlignment=VerticalAlignment.Center,Child=new Path{Data=Geometry.Parse(data),Stroke=Accent,StrokeThickness=1.6,StrokeJoin=PenLineJoin.Round,Width=24,Height=24,Stretch=Stretch.Uniform},Focusable=false};
    }
    internal static Control Header(string icon,System.Func<string> title)
    {
        var grid=new Grid{ColumnDefinitions=new ColumnDefinitions("24,*")};grid.Children.Add(Icon(icon,19));var text=Text(()=> (title()),14);text.FontWeight=FontWeight.SemiBold;text.Margin=new Thickness(10,0,0,0);Grid.SetColumn(text,1);grid.Children.Add(text);return grid;
    }
    internal static Control Note(string icon,System.Func<string> text)
    {
        var row=new Grid{ColumnDefinitions=new ColumnDefinitions("22,*")};row.Children.Add(Icon(icon,18));var copy=Text(()=> (text()),12,true);copy.Margin=new Thickness(10,0,0,0);Grid.SetColumn(copy,1);row.Children.Add(copy);return row;
    }
    internal static Button Button(string label,Action action,bool danger=false)
    {
        var button=Ui.Button(label,action);StyleButton(button,danger);return button;
    }
    internal static void StyleButton(Button button,bool danger=false)
    {
        var previous=button.Content;var caption=previous as TextBlock??Text(previous?.ToString()??"",13);caption.FontSize=13;button.Content=caption;LocalizedUi.TransferContent(button,previous);button.Foreground=danger?DesktopTheme.Brush("#AC4A57"):Ink;
        ((TextBlock)button.Content).Foreground=button.Foreground;
        button.MinHeight=36;button.Padding=new Thickness(12,8);button.CornerRadius=new CornerRadius(9);button.BorderThickness=new Thickness(1);button.BorderBrush=DesktopTheme.Brush(danger?"#F5DCE1":"#D5E1ED");button.Background=DesktopTheme.Brush(danger?"#FFF3F5":"#FFFFFF");button.HorizontalAlignment=HorizontalAlignment.Left;
        foreach(string state in new[]{":pointerover",":pressed",":focus-visible"}) {
            var style=new Style(s=>s.OfType<Button>().Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));style.Setters.Add(new Setter(ContentPresenter.BackgroundProperty,DesktopTheme.Brush(danger?"#F9E5E5":"#EDF3FF")));style.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty,Accent));style.Setters.Add(new Setter(ContentPresenter.BorderThicknessProperty,new Thickness(1)));style.Setters.Add(new Setter(ContentPresenter.CornerRadiusProperty,new CornerRadius(9)));button.Styles.Add(style);
        }
    }
    internal static WrapPanel Wrap(params Control[] controls)
    {
        var row=new WrapPanel{Orientation=Orientation.Horizontal};foreach(var control in controls){control.Margin=new Thickness(0,0,8,8);row.Children.Add(control);}return row;
    }
    internal static Border Card(Control child) => new(){Child=child,Padding=new Thickness(18),CornerRadius=new CornerRadius(14),Background=DesktopTheme.CardSurface,BorderBrush=DesktopTheme.CardBorder,BorderThickness=new Thickness(1)};
    internal static Control ActionRow(string icon,System.Func<string> title,System.Func<string> description,Control action)
    {
        var grid=new Grid{ColumnDefinitions=new ColumnDefinitions("38,*,Auto"),RowDefinitions=new RowDefinitions("Auto,Auto")};
        if(icon.Length>0)grid.Children.Add(new Border{Width=32,Height=32,CornerRadius=new CornerRadius(10),Background=DesktopTheme.Brush("#EDF3FF"),Child=Icon(icon,20),VerticalAlignment=VerticalAlignment.Top});
        else grid.ColumnDefinitions[0].Width=new GridLength(0);
        var words=new StackPanel{Spacing=5,Margin=new Thickness(icon.Length>0?10:0,0,12,0)};var heading=Text(()=> (title()),15);heading.FontWeight=FontWeight.SemiBold;words.Children.Add(heading);words.Children.Add(Text(()=> (description()),12,true));Grid.SetColumn(words,1);grid.Children.Add(words);grid.Children.Add(action);
        void Reflow(){bool narrow=grid.Bounds.Width<490;Grid.SetRow(action,narrow?1:0);Grid.SetColumn(action,narrow?1:2);action.Margin=new Thickness(narrow&&icon.Length>0?10:0,narrow?14:0,0,0);Grid.SetColumnSpan(words,narrow?2:1);action.VerticalAlignment=narrow?VerticalAlignment.Top:VerticalAlignment.Center;}
        grid.SizeChanged+=(_,_)=>Reflow();Reflow();return grid;
    }
    internal static void Disclosure(Expander expander,Control? action=null)
    {
        expander.HorizontalAlignment=HorizontalAlignment.Stretch;
        expander.Template=new FuncControlTemplate<Expander>((control,scope)=>{
            var header=new Grid{ColumnDefinitions=new ColumnDefinitions("*,20")};header.Children.Add(new ContentPresenter{Content=control.Header,VerticalAlignment=VerticalAlignment.Center});
            var arrow=new Path{Data=Geometry.Parse("M2,6 L8,12 L14,6"),Stroke=Muted,StrokeThickness=1.5,Width=16,Height=16,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(arrow,1);header.Children.Add(arrow);
            var button=Button("",()=>control.IsExpanded=!control.IsExpanded);button.Name="SettingsDisclosureHeader";button.Content=header;button.Background=Brushes.Transparent;button.Padding=new Thickness(4,12);button.HorizontalAlignment=HorizontalAlignment.Stretch;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.BorderThickness=new Thickness(0);
            var content=new ContentPresenter{Content=control.Content,Margin=new Thickness(4,8,4,12),HorizontalAlignment=HorizontalAlignment.Stretch};content.Bind(Visual.IsVisibleProperty,new Binding("IsExpanded"){Source=control});
            void Rotate()=>arrow.RenderTransform=new RotateTransform(control.IsExpanded?180:0);control.PropertyChanged+=(_,e)=>{if(e.Property==Expander.IsExpandedProperty)Rotate();};Rotate();
            Control headerRow=button;
            if(action!=null){var row=new Grid{ColumnDefinitions=new ColumnDefinitions("*,Auto")};row.Children.Add(button);action.Margin=new Thickness(12,0,0,0);action.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(action,1);row.Children.Add(action);headerRow=row;}
            return new StackPanel{Children={headerRow,content}};
        });
    }
    internal static void NavigationItem(ListBoxItem item)
    {
        item.Template=new FuncControlTemplate<ListBoxItem>((control,scope)=>{
            var border=new Border{Padding=control.Padding,CornerRadius=new CornerRadius(9),BorderThickness=new Thickness(1),Child=new ContentPresenter{Content=control.Content,HorizontalContentAlignment=HorizontalAlignment.Stretch}};
            void Refresh(){border.Background=control.IsSelected?DesktopTheme.Brush("#EDF3FF"):control.IsPointerOver?DesktopTheme.Brush("#E8EBF0"):Brushes.Transparent;border.BorderBrush=control.IsKeyboardFocusWithin?Accent:Brushes.Transparent;}
            control.PropertyChanged+=(_,e)=>{if(e.Property==ListBoxItem.IsSelectedProperty||e.Property==InputElement.IsPointerOverProperty||e.Property==InputElement.IsKeyboardFocusWithinProperty)Refresh();};Refresh();return border;
        });
    }
internal static TextBlock Text(System.Func<string> text,double size=14,bool muted=false){return LocalizedUi.With(Text(text(),size,muted),"Text",text);}
        internal static Button Button(System.Func<string> label,Action action,bool danger=false){return LocalizedUi.With(Button(label(),action,danger),"Content",label);}}
