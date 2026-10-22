using System.Windows;
using System.Windows.Controls;

namespace Dafeiyu
{
    internal sealed class CompanionConfigurationPanel:Grid
    {
        readonly SettingsPageHeader header;
        readonly UIElement[] pages;
        readonly ContentControl body=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,ClipToBounds=true};
        public CompanionConfigurationPanel(UIElement partner,UIElement behavior,UIElement greetings,TextBlock notice)
        {
            pages=new[]{partner,behavior,greetings};Margin=new Thickness(24,22,24,20);
            RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});RowDefinitions.Add(new RowDefinition());RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            header=new SettingsPageHeader("伙伴","选择伙伴，调整人格、陪伴方式与主动问候。",new[]{"伙伴与人格","陪伴方式","主动问候"},new[]{"CompanionPersonaTab","CompanionBehaviorTab","CompanionGreetingsTab"}){Margin=new Thickness(0,0,0,18)};
            Children.Add(header);Grid.SetRow(body,1);Children.Add(body);Grid.SetRow(notice,2);Children.Add(notice);header.SelectionChanged+=index=>body.Content=pages[index];SelectSection(0);
        }
        public void SelectSection(int index){header.Select(index);}
    }
}
