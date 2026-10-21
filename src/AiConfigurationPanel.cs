using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Dafeiyu
{
    public sealed class AiConfigurationPanel : Grid
    {
        readonly ConnectionSettingsPanel connections;
        readonly Button[] tabs=new Button[4];
        public int SelectedSection { get; private set; }

        public AiConfigurationPanel(ConnectionSettingsPanel editor)
        {
            connections=editor;
            Margin=new Thickness(24,22,24,20);
            RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            RowDefinitions.Add(new RowDefinition());
            var heading=new StackPanel { Margin=new Thickness(0,0,0,18) };
            var title=Ui.Text("模型配置",25);title.FontWeight=FontWeights.SemiBold;heading.Children.Add(title);
            heading.Children.Add(Ui.Text("把常用服务与模型收在一起，为伙伴连接对话与声音。",12,Ui.Muted));
            Children.Add(heading);
            var strip=new UniformGrid { Rows=1,Columns=4 };
            string[] labels={"文字模型","语音识别","声音与朗读","Live 实时语音"};
            string[] names={"AiTextTab","AiAsrTab","AiSpeechTab","AiLiveTab"};
            for(int index=0;index<tabs.Length;index++)
            {
                int section=index;
                var button=Ui.Button(labels[index]);button.Name=names[index];button.FontSize=12;
                button.Padding=new Thickness(4,8,4,8);button.Margin=new Thickness(2);button.MinHeight=38;
                button.Click+=delegate {SelectSection(section);};tabs[index]=button;strip.Children.Add(button);
            }
            var rail=new Border { Background=new LinearGradientBrush(Ui.Brush("#F2F7FF").Color,Ui.Brush("#FDFEFF").Color,90),BorderBrush=Ui.Brush("#E0E9FA"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(4),Child=strip,Margin=new Thickness(0,0,0,20) };
            Grid.SetRow(rail,1);Children.Add(rail);
            connections.Margin=new Thickness(0);Grid.SetRow(connections,2);Children.Add(connections);
            SelectSection(0);
        }

        public void SelectSection(int index)
        {
            if(index<0 || index>=tabs.Length)throw new ArgumentOutOfRangeException("index");
            SelectedSection=index;
            for(int i=0;i<tabs.Length;i++)
            {
                bool selected=i==index;
                tabs[i].Background=selected?ThemeManager.Surface:Brushes.Transparent;
                tabs[i].BorderBrush=selected?Ui.Line:Brushes.Transparent;
                tabs[i].Foreground=selected?Ui.Blue:Ui.Muted;
                tabs[i].FontWeight=selected?FontWeights.SemiBold:FontWeights.Normal;
                System.Windows.Automation.AutomationProperties.SetHelpText(tabs[i],selected?"当前配置页":"切换配置页");
            }
            connections.ShowSection(index);
        }
    }
}
