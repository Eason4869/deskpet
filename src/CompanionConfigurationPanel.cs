using System.Windows;
using System.Windows.Controls;

namespace Dafeiyu
{
    internal sealed class CompanionConfigurationPanel:Grid
    {
        readonly SettingsPageHeader header;
        internal int SelectedSection{get{return header.SelectedIndex;}}
        internal event System.Action SelectionChanged;
        readonly UIElement[] pages;
        readonly ContentControl body=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,ClipToBounds=true};
        public CompanionConfigurationPanel(UIElement partner,UIElement behavior,UIElement greetings,TextBlock notice)
        {
            pages=new[]{partner,behavior,greetings};Margin=new Thickness(24,22,24,20);
            RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});RowDefinitions.Add(new RowDefinition());RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            header=new SettingsPageHeader(()=>(TextResources.Get("companionConfigurationPanel.companionConfigurationPanel.headerSettingsPageHeader")),()=>(TextResources.Get("companionConfigurationPanel.companionConfigurationPanel.headerSettingsPageHeader.alternativeA")),()=>(new[]{TextResources.Get("companionConfigurationPanel.companionConfigurationPanel.headerSettingsPageHeader.alternativeB"),TextResources.Get("companionConfigurationPanel.companionConfigurationPanel.headerSettingsPageHeader.alternativeC"),TextResources.Get("companionConfigurationPanel.companionConfigurationPanel.headerSettingsPageHeader.alternativeD")}),new[]{"CompanionPersonaTab","CompanionBehaviorTab","CompanionGreetingsTab"}){Margin=new Thickness(0,0,0,18)};
            Children.Add(header);Grid.SetRow(body,1);Children.Add(body);Grid.SetRow(notice,2);Children.Add(notice);header.SelectionChanged+=index=>{body.Content=pages[index];if(SelectionChanged!=null)SelectionChanged();};SelectSection(0);
        }
        public void SelectSection(int index){header.Select(index);}
    }
}
