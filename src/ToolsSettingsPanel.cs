using System;
using System.Windows;
using System.Windows.Controls;
namespace Dafeiyu
{
    internal sealed class ToolsSettingsPanel : Grid,IDisposable
    {
        readonly PetWindow pet;readonly SettingsPageHeader header;readonly ContentControl body=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        WebSearchSettingsPanel web;ImChannelSettingsPanel channels;
        UIElement webPage,channelPage;
        internal event Action SelectionChanged;
        internal int SelectedSection{get{return header.SelectedIndex==1?2:0;}}
        internal bool HasUnsavedChanges{get{return web!=null&&web.HasUnsavedChanges||channels!=null&&channels.HasUnsavedChanges;}}
        internal ToolsSettingsPanel(PetWindow pet)
        {
            this.pet=pet;Name="ToolsSettingsPanel";Margin=new Thickness(24,22,24,20);RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});RowDefinitions.Add(new RowDefinition());
            header=new SettingsPageHeader(()=>TextResources.Get("settingsLayout.toolsTitle"),()=>TextResources.Get("settingsLayout.toolsDescription"),()=>new[]{TextResources.Get("webSearch.title"),TextResources.Get("settingsLayout.channelsTab")},new[]{"ToolsWebSearchTab","ToolsChannelsTab"}){Margin=new Thickness(0,0,0,28)};
            Children.Add(header);Grid.SetRow(body,1);Children.Add(body);header.SelectionChanged+=index=>{Show(index);if(SelectionChanged!=null)SelectionChanged();};SelectSection(0);
        }
        void Show(int index)
        {
            if(index==0){if(web==null){web=new WebSearchSettingsPanel(pet,false);var surface=new DockPanel();var footer=web.DetachSaveFooter();DockPanel.SetDock(footer,Dock.Bottom);surface.Children.Add(footer);surface.Children.Add(Ui.Scroll(web));webPage=surface;}body.Content=webPage;}
            else {if(channels==null){channels=new ImChannelSettingsPanel(pet);channelPage=Ui.Scroll(channels);}body.Content=channelPage;}
        }
        internal void SelectSection(int index){header.Select(index==2?1:0);}
        public void Dispose(){if(channels!=null)channels.Dispose();}
    }
}
