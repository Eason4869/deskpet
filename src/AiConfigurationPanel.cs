using System.Windows;
using System.Windows.Controls;

namespace Dafeiyu
{
    public sealed class AiConfigurationPanel:Grid
    {
        readonly SettingsPageHeader header;
        public int SelectedSection{get;private set;}
        internal event System.Action SelectionChanged;
        public AiConfigurationPanel(ConnectionSettingsPanel editor)
        {
            Margin=new Thickness(24,22,24,20);RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});RowDefinitions.Add(new RowDefinition());
            header=new SettingsPageHeader(TextResources.Get("aiConfigurationPanel.aiConfigurationPanel.headerSettingsPageHeader"),TextResources.Get("aiConfigurationPanel.aiConfigurationPanel.headerSettingsPageHeader.alternativeA"),new[]{TextResources.Get("aiConfigurationPanel.aiConfigurationPanel.headerSettingsPageHeader.alternativeB"),TextResources.Get("aiConfigurationPanel.aiConfigurationPanel.headerSettingsPageHeader.alternativeC"),TextResources.Get("aiConfigurationPanel.aiConfigurationPanel.headerSettingsPageHeader.alternativeD"),TextResources.Get("aiConfigurationPanel.aiConfigurationPanel.headerSettingsPageHeader.alternativeE"),TextResources.Get("aiConfigurationPanel.aiConfigurationPanel.headerSettingsPageHeader.alternativeF")},new[]{"AiTextTab","AiAsrTab","AiSpeechTab","AiLiveTab","AiImageTab"}){Margin=new Thickness(0,0,0,18)};
            Children.Add(header);editor.Margin=new Thickness(0);Grid.SetRow(editor,1);Children.Add(editor);header.SelectionChanged+=index=>{SelectedSection=index;editor.ShowSection(index);if(SelectionChanged!=null)SelectionChanged();};SelectSection(0);
        }
        public void SelectSection(int index){header.Select(index);}
    }
}
