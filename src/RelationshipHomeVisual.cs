using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path=System.Windows.Shapes.Path;

namespace Dafeiyu
{
    internal static class HomeVisual
    {
        internal static readonly Brush Accent=ThemeManager.Brush("#357FF5");
        internal static Border Box(UIElement child,bool tinted){return new Border{Child=child,Padding=new Thickness(22),CornerRadius=new CornerRadius(18),Background=tinted?ThemeManager.Brush("#EDF3FF"):ThemeManager.Surface,BorderBrush=tinted?Brushes.Transparent:ThemeManager.Brush("#D5E1ED"),BorderThickness=new Thickness(1)};}
        internal static FrameworkElement Icon(string kind,double size=22)
        {
            string path=kind=="heart"?"M12,21 C9,18 2,13 2,7 C2,1 9,1 12,6 C15,1 22,1 22,7 C22,13 15,18 12,21 Z":kind=="calendar"?"M3,5 L21,5 L21,22 L3,22 Z M3,10 L21,10 M7,2 L7,7 M17,2 L17,7 M7,14 L9,14 M14,14 L16,14 M7,18 L9,18 M14,18 L16,18":kind=="sun"?"M12,5 A7,7 0 1 1 11.99,5 M12,0 L12,3 M12,21 L12,24 M0,12 L3,12 M21,12 L24,12 M3,3 L5,5 M19,19 L21,21 M3,21 L5,19 M19,5 L21,3":kind=="diamond"?"M12,2 L22,12 L12,22 L2,12 Z M7,12 L17,12 M12,7 L12,17":kind=="settings"?"M12,2 L14,6 L18,5 L19,9 L23,12 L19,15 L18,19 L14,18 L12,22 L10,18 L6,19 L5,15 L1,12 L5,9 L6,5 L10,6 Z M12,8 A4,4 0 1 1 11.99,8":kind=="chat"?"M2,3 L22,3 L22,17 L10,17 L4,22 L4,17 L2,17 Z M6,8 L18,8 M6,12 L16,12":kind=="work"?"M2,7 L22,7 L22,21 L2,21 Z M8,7 L8,3 L16,3 L16,7 M2,12 L22,12 M10,12 L10,15 L14,15 L14,12":"M3,12 L12,3 L21,12 M6,10 L6,22 L18,22 L18,10";
            return new Viewbox{Width=size,Height=size,Child=new Path{Data=Geometry.Parse(path),Stroke=Accent,StrokeThickness=1.7,StrokeLineJoin=PenLineJoin.Round,Width=24,Height=24,Stretch=Stretch.Uniform},Focusable=false};
        }
        internal static FrameworkElement Ring(double fraction)
        {
            var grid=new Grid{Width=108,Height=108,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
            grid.Children.Add(new Ellipse{Width=98,Height=98,Stroke=ThemeManager.Brush("#D5E1ED"),StrokeThickness=8});
            if(fraction>=1)grid.Children.Add(new Ellipse{Width=98,Height=98,Stroke=Accent,StrokeThickness=8});
            else if(fraction>0){double angle=fraction*Math.PI*2,x=54+49*Math.Sin(angle),y=54-49*Math.Cos(angle);string path=String.Format(System.Globalization.CultureInfo.InvariantCulture,"M54,5 A49,49 0 {0} 1 {1},{2}",fraction>.5?1:0,x,y);grid.Children.Add(new Path{Data=Geometry.Parse(path),Stroke=Accent,StrokeThickness=8,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round});}
            grid.Children.Add(Icon("heart",32));System.Windows.Automation.AutomationProperties.SetName(grid,TextResources.Get("relationship.home.relation"));return grid;
        }
        internal static UniformGrid Nodes(RelationshipProgress progress)
        {
            var panel=new UniformGrid{Columns=6,Rows=1,Margin=new Thickness(0,8,0,0),Name="RelationshipStageNodes"};
            foreach(var node in progress.Nodes){var item=new StackPanel{Margin=new Thickness(0,0,0,6)};var line=new Grid{Height=18};line.Children.Add(new Border{Height=node.Current?5:node.Completed?3:1,Margin=new Thickness(3,0,3,0),Background=node.Completed||node.Current?Accent:ThemeManager.Brush("#C9D2DE"),VerticalAlignment=VerticalAlignment.Center});item.Children.Add(line);var text=Ui.Text(RelationshipContent.StageName(node.StageId),12,node.Current?Accent:AgentUi.Muted);text.TextAlignment=TextAlignment.Center;text.Margin=new Thickness(3,7,3,0);item.Children.Add(text);System.Windows.Automation.AutomationProperties.SetName(item,RelationshipContent.StageName(node.StageId)+": "+(node.Current?TextResources.Get("relationship.home.stageCurrent"):node.Completed?TextResources.Get("relationship.home.stageCompleted"):TextResources.Get("relationship.home.stageLocked")));panel.Children.Add(item);}return panel;
        }
        internal static FrameworkElement Stat(string icon,string label,string value,string name){var grid=new Grid{Margin=new Thickness(0,6,0,6)};grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(48)});grid.ColumnDefinitions.Add(new ColumnDefinition());grid.Children.Add(new Border{Width=36,Height=36,CornerRadius=new CornerRadius(11),Background=ThemeManager.Brush("#EDF3FF"),Child=Icon(icon),HorizontalAlignment=HorizontalAlignment.Left});var words=new StackPanel();words.Children.Add(Ui.Text(label,12,AgentUi.Muted));var text=Ui.Text(value,22,AgentUi.Ink);text.FontWeight=FontWeights.SemiBold;text.Name=name;words.Children.Add(text);Grid.SetColumn(words,1);grid.Children.Add(words);return grid;}
        internal static FrameworkElement Milestone(string title,string date){var row=new Grid{MinHeight=78};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(26)});row.ColumnDefinitions.Add(new ColumnDefinition());row.Children.Add(new Border{Width=1,Background=ThemeManager.Brush("#D5E1ED"),HorizontalAlignment=HorizontalAlignment.Center});row.Children.Add(new Ellipse{Width=10,Height=10,Fill=Accent,Stroke=ThemeManager.Surface,StrokeThickness=2,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,7,0,0)});var words=new StackPanel{Margin=new Thickness(6,0,8,16)};var text=Ui.Text(title,14,AgentUi.Ink);text.FontWeight=FontWeights.SemiBold;words.Children.Add(text);words.Children.Add(Ui.Text(date,12,AgentUi.Muted));Grid.SetColumn(words,1);row.Children.Add(words);return row;}
        internal static FrameworkElement Header(string icon,string value){var row=new Grid{Margin=new Thickness(2,5,2,5)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});row.ColumnDefinitions.Add(new ColumnDefinition());var graphic=Icon(icon,18);graphic.Margin=new Thickness(0,0,12,0);graphic.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(graphic);var text=Ui.Text(value,14,AgentUi.Ink);Grid.SetColumn(text,1);row.Children.Add(text);return row;}
        internal static FrameworkElement Rules(){var panel=new StackPanel{Margin=new Thickness(0,12,0,0)};panel.Children.Add(Header("home",TextResources.Get("relationship.home.ruleVisit")));panel.Children.Add(Header("heart",TextResources.Get("relationship.home.ruleInteraction")));panel.Children.Add(Header("chat",TextResources.Get("relationship.home.ruleConversation")));panel.Children.Add(Header("work",TextResources.Get("relationship.home.ruleWork")));var total=Ui.Text(TextResources.Get("relationship.home.ruleLimit"),13,AgentUi.Muted);total.Margin=new Thickness(0,10,0,0);panel.Children.Add(total);return panel;}
        internal sealed class FocusToken
        {
            internal string CompanionId,StageId,ActionName,DisclosureName;
        }
        static System.Collections.Generic.IEnumerable<FrameworkElement> Visuals(DependencyObject root)
        {
            var element=root as FrameworkElement;if(element!=null)yield return element;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Visuals(VisualTreeHelper.GetChild(root,i)))yield return child;
        }
        internal static FocusToken CaptureFocus(FrameworkElement root,FrameworkElement focused)
        {
            if(focused==null||(!Object.ReferenceEquals(root,focused)&&!root.IsAncestorOf(focused)))return null;
            var token=new FocusToken{ActionName=focused.Name};
            var disclosure=focused is ToggleButton?focused.TemplatedParent as Expander:null;
            if(disclosure!=null)token.DisclosureName=disclosure.Name;
            if(String.IsNullOrEmpty(token.ActionName)&&String.IsNullOrEmpty(token.DisclosureName))return null;
            for(DependencyObject ancestor=focused;ancestor!=null;ancestor=VisualTreeHelper.GetParent(ancestor)){
                var card=ancestor as RelationshipCardPreview;if(card==null)continue;var capture=(HomeCardCapture)card.Tag;token.CompanionId=capture.Data.CompanionId;token.StageId=capture.Data.StageId;break;
            }
            return token;
        }
        internal static FrameworkElement DisclosureHeader(Expander expander)
        {
            expander.ApplyTemplate();return Visuals(expander).OfType<ToggleButton>().FirstOrDefault(x=>Object.ReferenceEquals(x.TemplatedParent,expander));
        }
        internal static FrameworkElement ResolveFocus(FrameworkElement root,FocusToken token)
        {
            if(token==null)return null;FrameworkElement scope=root;
            if(token.StageId!=null){scope=Visuals(root).OfType<RelationshipCardPreview>().FirstOrDefault(x=>{var capture=(HomeCardCapture)x.Tag;return capture.Data.StageId==token.StageId&&capture.Data.CompanionId==token.CompanionId;});if(scope==null)return null;}
            if(!String.IsNullOrEmpty(token.DisclosureName)){var disclosure=Visuals(scope).OfType<Expander>().FirstOrDefault(x=>x.Name==token.DisclosureName);return disclosure==null?null:DisclosureHeader(disclosure);}
            return Visuals(scope).FirstOrDefault(x=>x.Name==token.ActionName);
        }

    }
    internal sealed class HomeCardCapture
    {
        internal readonly RelationshipCardData Data;internal readonly string StageText,DateText,Caption;internal readonly byte[] Png;internal readonly BitmapImage Image;internal bool Expanded;
        internal HomeCardCapture(RelationshipCardData data,byte[] png){Data=data;StageText=data.StageName;DateText=data.DateText;Caption=data.Name+" "+StageText+" "+data.MemoryText+" "+DateText+" "+data.AcquaintanceText;Png=png;Image=new BitmapImage();using(var stream=new MemoryStream(png)){Image.BeginInit();Image.CacheOption=BitmapCacheOption.OnLoad;Image.StreamSource=stream;Image.EndInit();Image.Freeze();}}
    }
    internal sealed class RelationshipCardPreview:StackPanel
    {
        internal RelationshipCardPreview(HomeCardCapture capture,string welcome,Func<bool> current,Func<bool> sameIdentity,Action resized)
        {
            Tag=capture;Margin=new Thickness(0,0,14,18);var image=new Image{Name="RelationshipCardImage",Source=capture.Image,Stretch=Stretch.Uniform,MaxWidth=520,HorizontalAlignment=HorizontalAlignment.Left};
            image.SizeChanged+=delegate{image.Clip=new RectangleGeometry(new Rect(0,0,image.ActualWidth,image.ActualHeight),12,12);};
            var expanded=new Image{Name="RelationshipCardExpandedImage",Source=capture.Image,Stretch=Stretch.Uniform};
            var preview=new Expander{Name="RelationshipCardPreview",Header=TextResources.Get("relationship.home.preview"),Content=expanded,IsExpanded=capture.Expanded,HorizontalContentAlignment=HorizontalAlignment.Stretch};
            preview.Template=(ControlTemplate)ThemeManager.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Expander'><StackPanel><ToggleButton x:Name='CompanionDisclosureHeader' IsChecked='{Binding IsExpanded,RelativeSource={RelativeSource TemplatedParent}}' Content='{TemplateBinding Header}' HorizontalContentAlignment='Stretch' Background='Transparent' BorderThickness='0'><ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border x:Name='frame' Background='Transparent' BorderBrush='{TemplateBinding Foreground}' BorderThickness='0' CornerRadius='12'><DockPanel><Path DockPanel.Dock='Right' Data='M0,0 L5,5 L10,0' Stroke='{TemplateBinding Foreground}' StrokeThickness='1.5' Width='10' Height='5' Margin='12,0,4,0'/><ContentPresenter Content='{TemplateBinding Content}' HorizontalAlignment='Left'/></DockPanel></Border><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='frame' Property='BorderThickness' Value='2'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='frame' Property='Opacity' Value='0.9'/></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template></ToggleButton><ContentPresenter x:Name='expanded' ContentSource='Content' Visibility='Collapsed' Margin='0,12,0,0'/></StackPanel><ControlTemplate.Triggers><Trigger Property='IsExpanded' Value='True'><Setter TargetName='expanded' Property='Visibility' Value='Visible'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            var thumbnail=Ui.Button("");thumbnail.Name="RelationshipCardThumbnail";thumbnail.Content=image;thumbnail.Background=Brushes.Transparent;thumbnail.Padding=new Thickness(0);thumbnail.Margin=new Thickness(0);thumbnail.HorizontalContentAlignment=HorizontalAlignment.Stretch;System.Windows.Automation.AutomationProperties.SetName(thumbnail,TextResources.Get("relationship.home.preview")+" "+capture.Caption);thumbnail.HorizontalAlignment=HorizontalAlignment.Left;thumbnail.Click+=delegate{preview.IsExpanded=!preview.IsExpanded;};thumbnail.ToolTip=TextResources.Get("relationship.home.preview");Children.Add(thumbnail);
            System.Windows.Automation.AutomationProperties.SetName(preview,TextResources.Get("relationship.home.preview")+" "+capture.Caption);
            preview.Expanded+=delegate{capture.Expanded=true;resized();};preview.Collapsed+=delegate{capture.Expanded=false;resized();};Children.Add(preview);
            var stageCaption=Ui.Text(capture.StageText,16,AgentUi.Ink);stageCaption.FontWeight=FontWeights.SemiBold;stageCaption.Margin=new Thickness(0,10,0,2);var captions=new StackPanel();captions.Children.Add(stageCaption);var dateCaption=Ui.Text(capture.DateText,13,AgentUi.Ink);captions.Children.Add(dateCaption);preview.Header=captions;
            var greeting=Ui.Text(welcome,13,AgentUi.Muted);greeting.Margin=new Thickness(0,10,0,10);Children.Add(greeting);
            var status=Ui.Text("",12,AgentUi.Muted);status.Name="RelationshipCardStatus";var save=Ui.Button(TextResources.Get("relationship.card.save"));save.Name="SaveRelationshipCard";save.HorizontalAlignment=HorizontalAlignment.Left;
            save.Click+=async delegate{
                if(!current()||!save.IsEnabled)return;save.IsEnabled=false;status.Text="";
                try{var picker=new Microsoft.Win32.SaveFileDialog{Title=TextResources.Get("relationship.card.save"),Filter=TextResources.Get("relationship.card.pngFilter")+"|*.png",DefaultExt=".png",AddExtension=true,OverwritePrompt=true,FileName=capture.Data.FileName};
                    if(picker.ShowDialog(Window.GetWindow(this))!=true){if(current())status.Text=TextResources.Get("relationship.home.saveCancelled");return;}if(!sameIdentity())return;
                    var result=await RelationshipCardFile.SaveAsync(picker.FileName,capture.Png);if(current())status.Text=TextResources.Get(result==RelationshipCardSaveResult.Saved?"relationship.card.saved":"relationship.card.failed");
                }catch(Exception error){if(!(error is IOException||error is UnauthorizedAccessException||error is System.Security.SecurityException||error is InvalidOperationException||error is ArgumentException))throw;if(current())status.Text=TextResources.Get("relationship.card.failed");}finally{save.IsEnabled=current();}
            };Children.Add(save);Children.Add(status);
        }
    }
}
