using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace Dafeiyu
{
    public sealed partial class ChatWindow
    {
        CompanionHomePage companionPage;
        string companionFeatureAnchor;
        MenuItem CompanionProfileEntry(ContextMenu menu)
        {
            var title=new StackPanel{Margin=new Thickness(0,4,12,4)};
            title.Children.Add(Ui.Text(()=>(pet.Skin.DisplayName),14,AgentUi.Ink));
            title.Children.Add(LocalizedUi.With(new TextBlock{Name="ProfileCompanionLabel",Text=TextResources.Get("relationship.home.open"),FontSize=11,Foreground=AgentUi.Muted},"Text",()=>(TextResources.Get("relationship.home.open"))));
            var heading=new DockPanel();var arrow=Ui.Text("›",18,AgentUi.Muted);DockPanel.SetDock(arrow,Dock.Right);heading.Children.Add(arrow);heading.Children.Add(title);
            var item=new MenuItem{Name="ProfileCompanionHome",Header=heading,Icon=new Image{Source=SkinCatalog.GetAvatar(pet.Skin),Width=32,Height=32},IsEnabled=true,Focusable=true,Background=Brushes.Transparent};
            item.Template=(ControlTemplate)ThemeManager.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='MenuItem'><Border x:Name='surface' Background='{TemplateBinding Background}' CornerRadius='7' Padding='10,8'><Grid><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><ContentPresenter Content='{TemplateBinding Icon}' Margin='0,0,12,0' VerticalAlignment='Center'/><ContentPresenter Grid.Column='1' ContentSource='Header' VerticalAlignment='Center'/></Grid></Border><ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='surface' Property='Background' Value='#EFEFEF'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='Background' Value='#EFEFEF'/></Trigger><Trigger Property='IsMouseCaptureWithin' Value='True'><Setter TargetName='surface' Property='Background' Value='#E1E8EE'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            LocalizedUi.Automation(item,()=>(pet.Skin.DisplayName+" · "+TextResources.Get("relationship.home.open")));
            item.Click+=delegate{menu.IsOpen=false;ShowCompanionPage();};
            return item;
        }
        internal void ShowCompanionPage(string anchor=null)
        {
            if(!shellReady||closed)return;
            if(companionPage==null){companionPage=new CompanionHomePage(pet);appPages.Children.Add(companionPage);}
            companionFeatureAnchor=anchor;companionPage.RequestNavigationAnchor(anchor);
            SetShellPage("companion");RecordPage("companion");pet.RecordVisit();
            RefreshCompanionPage();
        }
        async void RefreshCompanionPage(){await companionPage.RefreshAsync();}
    }
}
