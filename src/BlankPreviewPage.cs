using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
namespace Dafeiyu
{
    internal sealed class BlankPreviewPage:DockPanel
    {
        internal BlankPreviewPage(Action<string> browse,Action file,Action terminal,Action chat,Action changes)
        {
            Name="BlankPreviewPage";Background=Ui.Brush("#F8F9FB");var searchBox=new Grid{Margin=new Thickness(16,14,16,16)};var search=Ui.Field();search.Margin=new Thickness(0);search.Name="NewTabAddress";search.ToolTip="输入网址或搜索内容";search.Height=search.MinHeight=40;search.FontSize=13;search.Padding=new Thickness(14,0,14,0);search.VerticalContentAlignment=VerticalAlignment.Center;search.Template=(ControlTemplate)ThemeManager.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TextBox'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='12'><ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' VerticalAlignment='Center'/></Border></ControlTemplate>");var hintAddress=Ui.InlineText("搜索或输入网址",13,AgentUi.Muted);hintAddress.Name="NewTabAddressPlaceholder";hintAddress.IsHitTestVisible=false;hintAddress.VerticalAlignment=VerticalAlignment.Center;hintAddress.Margin=new Thickness(15,0,0,0);searchBox.Children.Add(search);searchBox.Children.Add(hintAddress);search.TextChanged+=delegate{hintAddress.Visibility=String.IsNullOrEmpty(search.Text)?Visibility.Visible:Visibility.Collapsed;};search.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter&&!String.IsNullOrWhiteSpace(search.Text)){e.Handled=true;browse(search.Text);}};DockPanel.SetDock(searchBox,Dock.Top);Children.Add(searchBox);
            var content=new StackPanel{Margin=new Thickness(24,18,24,24)};var heading=Ui.Text("工具",15,AgentUi.Ink);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new Thickness(0,0,0,14);content.Children.Add(heading);var tools=new UniformGrid{Columns=2};tools.Children.Add(Tool("变更","查看当前任务文件总结","file","NewTabChanges",changes));tools.Children.Add(Tool("终端","独立 PowerShell 会话","terminal","NewTabTerminal",terminal));tools.Children.Add(Tool("文件","打开并预览本地文件","folder","NewTabFile",file));tools.Children.Add(Tool("侧边聊天","当前项目的临时会话","chat","NewTabSideChat",chat));tools.Children.Add(Tool("网页浏览器","输入网址或搜索内容","globe","NewTabBrowser",()=>browse(null)));content.Children.Add(tools);var hint=Ui.Text("选择工具，或在上方输入网址。",12,AgentUi.Muted);hint.Margin=new Thickness(4,20,4,0);content.Children.Add(hint);Children.Add(Ui.Scroll(content));SizeChanged+=delegate{tools.Columns=ActualWidth<560?1:2;};
        }
        static Button Tool(string title,string detail,string icon,string name,Action action){var row=new DockPanel();var image=UiIcons.Image(icon,AgentUi.Muted);image.Width=image.Height=19;image.VerticalAlignment=VerticalAlignment.Center;image.Margin=new Thickness(0,0,12,0);DockPanel.SetDock(image,Dock.Left);row.Children.Add(image);var text=new StackPanel();text.Children.Add(Ui.Text(title,13,AgentUi.Ink));text.Children.Add(Ui.Text(detail,11,AgentUi.Muted));row.Children.Add(text);var button=Ui.Button("");button.Name=name;button.Content=row;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Margin=new Thickness(0,0,10,10);button.Padding=new Thickness(14,12,14,12);button.Click+=delegate{action();};return button;}
    }
}
