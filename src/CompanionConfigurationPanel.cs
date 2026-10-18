using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Dafeiyu
{
    internal sealed class CompanionConfigurationPanel : Grid
    {
        readonly Button[] tabs=new Button[3];
        readonly UIElement[] pages;
        readonly ContentControl body=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,ClipToBounds=true};
        public CompanionConfigurationPanel(UIElement partner,UIElement behavior,UIElement greetings,TextBlock notice)
        {
            pages=new[]{partner,behavior,greetings};Margin=new Thickness(24,22,24,20);
            RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            RowDefinitions.Add(new RowDefinition());RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var heading=Ui.Text("伙伴",25);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new Thickness(0,0,0,16);Children.Add(heading);
            var strip=new UniformGrid{Rows=1,Columns=3};
            string[] labels={"伙伴与人格","陪伴方式","主动问候"},names={"CompanionPersonaTab","CompanionBehaviorTab","CompanionGreetingsTab"};
            for(int i=0;i<3;i++)
            {
                int section=i;var button=Ui.Button(labels[i]);button.Name=names[i];button.FontSize=12;button.Padding=new Thickness(4,8,4,8);button.Margin=new Thickness(2);button.MinHeight=38;
                button.Click+=delegate{SelectSection(section);};tabs[i]=button;strip.Children.Add(button);
            }
            var rail=new Border{Background=Ui.Pale,CornerRadius=new CornerRadius(12),Padding=new Thickness(3),Child=strip,Margin=new Thickness(0,0,0,18)};
            Grid.SetRow(rail,1);Children.Add(rail);Grid.SetRow(body,2);Children.Add(body);Grid.SetRow(notice,3);Children.Add(notice);SelectSection(0);
        }
        public void SelectSection(int index)
        {
            if(index<0 || index>=tabs.Length)throw new ArgumentOutOfRangeException("index");
            body.Content=pages[index];
            for(int i=0;i<tabs.Length;i++)
            {
                bool selected=i==index;tabs[i].Background=selected?Brushes.White:Brushes.Transparent;tabs[i].BorderBrush=selected?Ui.Line:Brushes.Transparent;tabs[i].Foreground=selected?Ui.Blue:Ui.Muted;tabs[i].FontWeight=selected?FontWeights.SemiBold:FontWeights.Normal;
                System.Windows.Automation.AutomationProperties.SetHelpText(tabs[i],selected?"当前配置页":"切换配置页");
            }
        }
    }
}
