using System.Windows;
using System.Windows.Controls;

namespace Dafeiyu
{
    public sealed class AiConfigurationPanel:Grid
    {
        readonly SettingsPageHeader header;
        public int SelectedSection{get;private set;}
        public AiConfigurationPanel(ConnectionSettingsPanel editor)
        {
            Margin=new Thickness(24,22,24,20);RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});RowDefinitions.Add(new RowDefinition());
            header=new SettingsPageHeader("模型配置","把常用服务与模型收在一起，为伙伴连接对话与声音。",new[]{"文字模型","语音识别","声音与朗读","Live 实时语音"},new[]{"AiTextTab","AiAsrTab","AiSpeechTab","AiLiveTab"}){Margin=new Thickness(0,0,0,18)};
            Children.Add(header);editor.Margin=new Thickness(0);Grid.SetRow(editor,1);Children.Add(editor);header.SelectionChanged+=index=>{SelectedSection=index;editor.ShowSection(index);};SelectSection(0);
        }
        public void SelectSection(int index){header.Select(index);}
    }
}
