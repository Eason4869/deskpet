using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.LogicalTree;
using Avalonia.Automation;
using Avalonia.Platform.Storage;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using Avalonia.Styling;
using Dafeiyu;
using Path=Avalonia.Controls.Shapes.Path;
namespace DeskPet.Desktop;
    internal static class HomeVisual
    {
        static TextBlock HomeText(string value,double size,IBrush color)=>new(){Text=value,FontSize=size,Foreground=color,TextWrapping=TextWrapping.Wrap};
        internal static void Disclosure(Expander expander)
        {
            expander.HorizontalAlignment=HorizontalAlignment.Stretch;
            expander.Template=new FuncControlTemplate<Expander>((control,scope)=>{
                var header=new Grid{ColumnDefinitions=new ColumnDefinitions("*,20")};
                var title=new ContentPresenter{Content=control.Header,VerticalAlignment=VerticalAlignment.Center};header.Children.Add(title);
                var arrow=new Path{Data=Geometry.Parse("M2,6 L8,12 L14,6"),Stroke=DesktopTheme.Brush("#7A7E86"),StrokeThickness=1.5,Width=16,Height=16,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(arrow,1);header.Children.Add(arrow);
                var button=Ui.Button("",()=>control.IsExpanded=!control.IsExpanded);button.Content=header;button.Background=Brushes.Transparent;button.Padding=new Thickness(0,8);button.HorizontalAlignment=HorizontalAlignment.Stretch;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
                var content=new ContentPresenter{Content=control.Content,Margin=new Thickness(0,8,0,0),HorizontalAlignment=HorizontalAlignment.Stretch};content.Bind(Visual.IsVisibleProperty,new Binding("IsExpanded"){Source=control});
                void UpdateArrow(){arrow.RenderTransform=new RotateTransform(control.IsExpanded?180:0);}
                control.PropertyChanged+=(_,e)=>{if(e.Property==Expander.IsExpandedProperty)UpdateArrow();};UpdateArrow();
                return new StackPanel{Children={button,content}};
            });
        }
        internal static void SecondaryButton(Button button)
        {
            button.Background=DesktopTheme.Surface;button.Foreground=DesktopTheme.Brush("#203754");button.BorderBrush=DesktopTheme.Brush("#D5E1ED");button.BorderThickness=new Thickness(1);button.CornerRadius=new CornerRadius(9);button.Padding=new Thickness(12,7);
            foreach(string state in new[]{":pointerover",":pressed",":focus-visible"}){
                var style=new Style(x=>x.OfType<Button>().Class(state).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"));style.Setters.Add(new Setter(ContentPresenter.BackgroundProperty,DesktopTheme.Brush("#EDF3FF")));style.Setters.Add(new Setter(ContentPresenter.BorderBrushProperty,Accent));style.Setters.Add(new Setter(ContentPresenter.BorderThicknessProperty,new Thickness(1)));style.Setters.Add(new Setter(ContentPresenter.CornerRadiusProperty,new CornerRadius(9)));button.Styles.Add(style);
            }
        }
        internal static readonly IBrush Accent=DesktopTheme.Brush("#357FF5");
        internal static Border Box(Control child,bool tinted){return new Border{Child=child,Padding=new Thickness(22),CornerRadius=new CornerRadius(18),Background=tinted?DesktopTheme.Brush("#EDF3FF"):DesktopTheme.Surface,BorderBrush=tinted?Brushes.Transparent:DesktopTheme.Brush("#D5E1ED"),BorderThickness=new Thickness(1)};}
        internal static Control Icon(string kind,double size=22)
        {
            string path=kind=="heart"?"M12,21 C9,18 2,13 2,7 C2,1 9,1 12,6 C15,1 22,1 22,7 C22,13 15,18 12,21 Z":kind=="calendar"?"M3,5 L21,5 L21,22 L3,22 Z M3,10 L21,10 M7,2 L7,7 M17,2 L17,7 M7,14 L9,14 M14,14 L16,14 M7,18 L9,18 M14,18 L16,18":kind=="sun"?"M12,5 A7,7 0 1 1 11.99,5 M12,0 L12,3 M12,21 L12,24 M0,12 L3,12 M21,12 L24,12 M3,3 L5,5 M19,19 L21,21 M3,21 L5,19 M19,5 L21,3":kind=="diamond"?"M12,2 L22,12 L12,22 L2,12 Z M7,12 L17,12 M12,7 L12,17":kind=="settings"?"M12,2 L14,6 L18,5 L19,9 L23,12 L19,15 L18,19 L14,18 L12,22 L10,18 L6,19 L5,15 L1,12 L5,9 L6,5 L10,6 Z M12,8 A4,4 0 1 1 11.99,8":kind=="chat"?"M2,3 L22,3 L22,17 L10,17 L4,22 L4,17 L2,17 Z M6,8 L18,8 M6,12 L16,12":kind=="work"?"M2,7 L22,7 L22,21 L2,21 Z M8,7 L8,3 L16,3 L16,7 M2,12 L22,12 M10,12 L10,15 L14,15 L14,12":"M3,12 L12,3 L21,12 M6,10 L6,22 L18,22 L18,10";
            return new Viewbox{Width=size,Height=size,Child=new Path{Data=Geometry.Parse(path),Stroke=Accent,StrokeThickness=1.7,StrokeJoin=PenLineJoin.Round,Width=24,Height=24,Stretch=Stretch.Uniform},Focusable=false};
        }
        internal static Control Ring(double fraction)
        {
            var grid=new Grid{Width=108,Height=108,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
            grid.Children.Add(new Ellipse{Width=98,Height=98,Stroke=DesktopTheme.Brush("#D5E1ED"),StrokeThickness=8});
            if(fraction>=1)grid.Children.Add(new Ellipse{Width=98,Height=98,Stroke=Accent,StrokeThickness=8});
            else if(fraction>0){double angle=fraction*Math.PI*2,x=54+49*Math.Sin(angle),y=54-49*Math.Cos(angle);string path=String.Format(System.Globalization.CultureInfo.InvariantCulture,"M54,5 A49,49 0 {0} 1 {1},{2}",fraction>.5?1:0,x,y);grid.Children.Add(new Path{Data=Geometry.Parse(path),Stroke=Accent,StrokeThickness=8,StrokeLineCap=PenLineCap.Round});}
            grid.Children.Add(Icon("heart",32));AutomationProperties.SetName(grid,TextResources.Get("relationship.home.relation"));return grid;
        }
        internal static UniformGrid Nodes(RelationshipProgress progress)
        {
            var panel=new UniformGrid{Columns=6,Rows=1,Margin=new Thickness(0,8,0,0),Name="RelationshipStageNodes"};
            foreach(var node in progress.Nodes){var item=new StackPanel{Margin=new Thickness(0,0,0,16)};var line=new Grid{Height=18};line.Children.Add(new Border{Height=1,Background=DesktopTheme.Brush("#D5E1ED"),VerticalAlignment=VerticalAlignment.Center});line.Children.Add(new Ellipse{Width=node.Current?16:12,Height=node.Current?16:12,Fill=node.Completed?Accent:DesktopTheme.Surface,Stroke=node.Completed||node.Current?Accent:DesktopTheme.Brush("#C9D2DE"),StrokeThickness=node.Current?3:1.5});item.Children.Add(line);var text=HomeText(RelationshipContent.StageName(node.StageId),12,node.Current?Accent:DesktopTheme.Brush("#7A7E86"));text.TextAlignment=TextAlignment.Center;text.Margin=new Thickness(3,7,3,0);item.Children.Add(text);AutomationProperties.SetName(item,RelationshipContent.StageName(node.StageId)+": "+(node.Current?TextResources.Get("relationship.home.stageCurrent"):node.Completed?TextResources.Get("relationship.home.stageCompleted"):TextResources.Get("relationship.home.stageLocked")));panel.Children.Add(item);}return panel;
        }
        internal static Control Stat(string icon,string label,string value,string name){var grid=new Grid{Margin=new Thickness(0,6,0,6)};grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(48)});grid.ColumnDefinitions.Add(new ColumnDefinition());grid.Children.Add(new Border{Width=36,Height=36,CornerRadius=new CornerRadius(11),Background=DesktopTheme.Brush("#EDF3FF"),Child=Icon(icon),HorizontalAlignment=HorizontalAlignment.Left});var words=new StackPanel();words.Children.Add(HomeText(label,12,DesktopTheme.Brush("#7A7E86")));var text=HomeText(value,22,DesktopTheme.Brush("#282A2F"));text.FontWeight=FontWeight.SemiBold;text.Name=name;words.Children.Add(text);Grid.SetColumn(words,1);grid.Children.Add(words);return grid;}
        internal static Control Milestone(string title,string date){var row=new Grid{MinHeight=78};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(26)});row.ColumnDefinitions.Add(new ColumnDefinition());row.Children.Add(new Border{Width=1,Background=DesktopTheme.Brush("#D5E1ED"),HorizontalAlignment=HorizontalAlignment.Center});row.Children.Add(new Ellipse{Width=10,Height=10,Fill=Accent,Stroke=DesktopTheme.Surface,StrokeThickness=2,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,7,0,0)});var words=new StackPanel{Margin=new Thickness(6,0,8,16)};var text=HomeText(title,14,DesktopTheme.Brush("#282A2F"));text.FontWeight=FontWeight.SemiBold;words.Children.Add(text);words.Children.Add(HomeText(date,12,DesktopTheme.Brush("#7A7E86")));Grid.SetColumn(words,1);row.Children.Add(words);return row;}
        internal static Control Header(string icon,string value){var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(2,5,2,5)};row.Children.Add(Icon(icon,18));var text=HomeText(value,14,DesktopTheme.Brush("#282A2F"));text.Margin=new Thickness(12,0,0,0);row.Children.Add(text);return row;}
        internal static Control Rules(){var panel=new StackPanel{Margin=new Thickness(0,12,0,0)};panel.Children.Add(Header("home",TextResources.Get("relationship.home.ruleVisit")));panel.Children.Add(Header("heart",TextResources.Get("relationship.home.ruleInteraction")));panel.Children.Add(Header("chat",TextResources.Get("relationship.home.ruleConversation")));panel.Children.Add(Header("work",TextResources.Get("relationship.home.ruleWork")));var total=HomeText(TextResources.Get("relationship.home.ruleLimit"),13,DesktopTheme.Brush("#7A7E86"));total.Margin=new Thickness(0,10,0,0);panel.Children.Add(total);return panel;}
        internal static Control? FindNamed(Control root,string name)=>root.GetLogicalDescendants().OfType<Control>().FirstOrDefault(x=>x.Name==name);
    }

internal sealed class HomeCardCapture:IDisposable
{
    internal readonly RelationshipCardData Data;internal readonly byte[] Png;internal readonly Bitmap Image;internal bool Expanded;
    internal HomeCardCapture(RelationshipCardData data,byte[] png){Data=data;Png=png;using var stream=new MemoryStream(png);Image=new Bitmap(stream);}
    public void Dispose()=>Image.Dispose();
}
internal sealed class RelationshipCardPreview:StackPanel
{
    internal RelationshipCardPreview(HomeCardCapture capture,string welcome,Func<bool> current,Func<bool> sameIdentity,Action resized)
    {
        Tag=capture;Margin=new Thickness(0,0,14,18);var image=new Image{Name="RelationshipCardImage",Source=capture.Image,Stretch=Stretch.Uniform,MaxWidth=340,HorizontalAlignment=HorizontalAlignment.Left};
        var expanded=new Image{Name="RelationshipCardExpandedImage",Source=capture.Image,Stretch=Stretch.Uniform};
        var preview=new Expander{Name="RelationshipCardPreview",Header=TextResources.Get("relationship.home.preview"),Content=expanded,IsExpanded=capture.Expanded,HorizontalContentAlignment=HorizontalAlignment.Stretch,HorizontalAlignment=HorizontalAlignment.Stretch};
        var thumbnail=Ui.Button("",()=>preview.IsExpanded=!preview.IsExpanded);thumbnail.Name="RelationshipCardThumbnail";thumbnail.Content=image;thumbnail.BorderBrush=DesktopTheme.Brush("#D5E1ED");thumbnail.BorderThickness=new Thickness(1);thumbnail.CornerRadius=new CornerRadius(12);thumbnail.Padding=new Thickness(0);thumbnail.Margin=new Thickness(0);thumbnail.HorizontalContentAlignment=HorizontalAlignment.Stretch;AutomationProperties.SetName(thumbnail,TextResources.Get("relationship.home.preview")+" "+capture.Data.Name+" "+capture.Data.StageName+" "+capture.Data.DateText);thumbnail.HorizontalAlignment=HorizontalAlignment.Left;ToolTip.SetTip(thumbnail,TextResources.Get("relationship.home.preview"));Children.Add(thumbnail);
        HomeVisual.Disclosure(preview);AutomationProperties.SetName(preview,TextResources.Get("relationship.home.preview")+" "+capture.Data.Name+" "+capture.Data.StageName+" "+capture.Data.DateText);
        preview.PropertyChanged+=(_,e)=>{if(e.Property==Expander.IsExpandedProperty){capture.Expanded=preview.IsExpanded;resized();}};Children.Add(preview);
        var greeting=Ui.Text(welcome,13);greeting.Foreground=DesktopTheme.Brush("#7A7E86");greeting.Margin=new Thickness(0,10,0,10);Children.Add(greeting);
        var status=Ui.Text("",12);status.Name="RelationshipCardStatus";Button? save=null;
        save=Ui.Button(TextResources.Get("relationship.card.save"),async()=>{
            if(!current()||save==null||!save.IsEnabled)return;save.IsEnabled=false;status.Text="";
            try{var top=TopLevel.GetTopLevel(this);if(top==null)throw new InvalidOperationException();
                var file=await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions{Title=TextResources.Get("relationship.card.save"),SuggestedFileName=capture.Data.FileName,DefaultExtension="png",ShowOverwritePrompt=true,FileTypeChoices=new[]{new FilePickerFileType(TextResources.Get("relationship.card.pngFilter")){Patterns=new[]{"*.png"},MimeTypes=new[]{"image/png"}}}});
                if(file==null){if(current())status.Text=TextResources.Get("relationship.home.saveCancelled");return;}using(file){if(!sameIdentity())return;var path=file.TryGetLocalPath();var result=path==null?RelationshipCardSaveResult.Failed:await RelationshipCardFile.SaveAsync(path,capture.Png);if(current())status.Text=TextResources.Get(result==RelationshipCardSaveResult.Saved?"relationship.card.saved":"relationship.card.failed");}
            }catch(Exception error){if(error is OperationCanceledException){if(current())status.Text=TextResources.Get("relationship.home.saveCancelled");return;}if(!(error is IOException||error is UnauthorizedAccessException||error is System.Security.SecurityException||error is InvalidOperationException||error is ArgumentException||error is NotSupportedException))throw;if(current())status.Text=TextResources.Get("relationship.card.failed");}finally{save.IsEnabled=current();}
        });HomeVisual.SecondaryButton(save);save.Name="SaveRelationshipCard";save.HorizontalAlignment=HorizontalAlignment.Left;Children.Add(save);Children.Add(status);
    }
}
