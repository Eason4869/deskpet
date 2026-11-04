using System;
using System.Windows;
using System.Windows.Controls;
namespace Dafeiyu
{
    internal sealed class WebSearchSettingsPanel : StackPanel
    {
        readonly Button save;Button restore;bool saving;readonly PetWindow pet;readonly CheckBox enabled,sources;readonly ComboBox provider,protocol,customProtocol;readonly FrameworkElement customProtocolRow;
        readonly TextBox address;readonly PasswordBox key;WebSearchSettings connections;string selectedProvider;bool changingProvider;readonly StackPanel nativeFields=new StackPanel(),externalFields=new StackPanel();
        readonly TextBlock notice=Ui.Text("",12,Ui.Muted);WebSearchSettings baseline;
        static readonly string[] protocols={"auto","openai-responses","anthropic","qwen"};
        internal WebSearchSettingsPanel(PetWindow pet,bool showHeading=true)
        {
            this.pet=pet;baseline=(pet.Settings.WebSearch??new WebSearchSettings()).Copy();baseline.Normalize();connections=baseline.Copy();selectedProvider=baseline.Provider;Margin=new Thickness(0,showHeading?24:0,0,8);
            var title=LocalizedUi.Text(()=>TextResources.Get("webSearch.title"),17,Ui.Ink);title.FontWeight=FontWeights.SemiBold;if(showHeading)Children.Add(title);
            var service=new StackPanel();
            enabled=Ui.Toggle("",baseline.Enabled,v=>RefreshFields());enabled.Name="WebSearchEnabled";service.Children.Add(Ui.SettingRow(()=>TextResources.Get("webSearch.enabled"),null,enabled));
            provider=LocalizedUi.Options(new ComboBox{Name="WebSearchProvider",MinHeight=36,ItemsSource=ProviderNames(),SelectedIndex=Array.IndexOf(WebSearchProviders.Ids,baseline.Provider)},ProviderNames);
            service.Children.Add(Ui.SettingRow(()=>TextResources.Get("webSearch.provider"),null,provider));
            protocol=LocalizedUi.Options(new ComboBox{Name="WebSearchProtocol",MinHeight=36,ItemsSource=new[]{TextResources.Get("webSearch.auto"),"OpenAI Responses","Anthropic","Qwen / DashScope"},SelectedIndex=Math.Max(0,Array.IndexOf(protocols,baseline.NativeProtocol))},()=>new[]{TextResources.Get("webSearch.auto"),"OpenAI Responses","Anthropic","Qwen / DashScope"});
            nativeFields.Children.Add(Ui.SettingRow(()=>TextResources.Get("webSearch.protocol"),null,protocol));service.Children.Add(nativeFields);
            customProtocol=LocalizedUi.Options(new ComboBox{Name="WebSearchCustomProtocol",MinHeight=36,ItemsSource=Array.ConvertAll(WebSearchProviders.SchemaIds,WebSearchProviders.DisplayName),SelectedIndex=Math.Max(0,Array.IndexOf(WebSearchProviders.SchemaIds,baseline.CustomProtocol))},()=>Array.ConvertAll(WebSearchProviders.SchemaIds,WebSearchProviders.DisplayName));
            customProtocolRow=Ui.SettingRow(()=>TextResources.Get("webSearch.customProtocol"),()=>TextResources.Get("webSearch.customHelp"),customProtocol);externalFields.Children.Add(customProtocolRow);
            address=Ui.Field(baseline.ReadConnection(selectedProvider).BaseUrl);address.Name="WebSearchAddress";externalFields.Children.Add(Ui.SettingRow(()=>TextResources.Get("webSearch.address"),null,address));
            key=new PasswordBox{Name="WebSearchKey",Password=baseline.ReadConnection(selectedProvider).ApiKey,MinHeight=36,Padding=new Thickness(8)};externalFields.Children.Add(Ui.SettingRow(()=>TextResources.Get("webSearch.key"),null,key));service.Children.Add(externalFields);
            SettingsSurface.Section(this,()=>TextResources.Get("settingsLayout.searchService"),service);
            sources=Ui.Toggle("",baseline.ShowSources,v=>{});sources.Name="WebSearchSources";var display=new StackPanel();display.Children.Add(Ui.SettingRow(()=>TextResources.Get("webSearch.showSources"),null,sources));SettingsSurface.Section(this,()=>TextResources.Get("settingsLayout.sourcesGroup"),display);
            var help=LocalizedUi.Text(()=>TextResources.Get("webSearch.help"),12,Ui.Muted);help.Margin=new Thickness(0,24,0,12);Children.Add(help);Children.Add(notice);
            save=LocalizedUi.Button(()=>TextResources.Get("webSearch.save"),true);save.Name="SaveWebSearch";save.HorizontalAlignment=HorizontalAlignment.Left;save.Margin=new Thickness(0,10,0,0);Children.Add(save);
            save.Click+=async delegate{
                if(saving)return;saving=true;save.IsEnabled=false;if(restore!=null)restore.IsEnabled=false;
                try{var draft=Read();if(await pet.RunSettingsEditAsync(this,()=>pet.PersistSettingsAsync(target=>target.WebSearch=draft.Copy()),"web-search")){RefreshSaved();notice.Text=TextResources.Get("webSearch.saved");}else notice.Text=pet.Notice;}
                catch(Exception error){if(!(error is ApiException||error is InvalidOperationException||error is ArgumentException))throw;notice.Text=error.Message;}
                finally{saving=false;save.IsEnabled=true;if(restore!=null)restore.IsEnabled=true;}
            };
            provider.SelectionChanged+=delegate{SwitchProvider();RefreshFields();DraftChanged();};address.TextChanged+=delegate{DraftChanged();};key.PasswordChanged+=delegate{DraftChanged();};enabled.Checked+=delegate{DraftChanged();};enabled.Unchecked+=delegate{DraftChanged();};sources.Checked+=delegate{DraftChanged();};sources.Unchecked+=delegate{DraftChanged();};protocol.SelectionChanged+=delegate{DraftChanged();};customProtocol.SelectionChanged+=delegate{DraftChanged();};RefreshFields();DraftChanged();
        }
        static string[] ProviderNames(){return Array.ConvertAll(WebSearchProviders.Ids,id=>id=="native"?TextResources.Get("webSearch.native"):id=="custom"?TextResources.Get("webSearch.custom"):id=="doubao"?TextResources.Get("webSearch.doubao"):WebSearchProviders.DisplayName(id));}
        void SwitchProvider()
        {
            if(changingProvider||address==null)return;
            var current=connections.ReadConnection(selectedProvider);current.BaseUrl=address.Text.Trim();current.ApiKey=key.Password;connections.SetConnection(selectedProvider,current);
            selectedProvider=WebSearchProviders.Ids[Math.Max(0,provider.SelectedIndex)];connections.Provider=selectedProvider;var next=connections.ReadConnection(selectedProvider);connections.Connection=next.Copy();address.Text=next.BaseUrl;key.Password=next.ApiKey;
        }
        void DraftChanged(){notice.Text=TextResources.Get(HasUnsavedChanges?"settingsLayout.unsaved":"settingsLayout.saved");}
        internal FrameworkElement DetachSaveFooter(){Children.Remove(save);Children.Remove(notice);var footer=new DockPanel{Margin=new Thickness(0,14,0,0)};var actions=new StackPanel{Orientation=Orientation.Horizontal};restore=LocalizedUi.Button(()=>TextResources.Get("settingsLayout.restore"));restore.Name="RestoreWebSearch";restore.Margin=new Thickness(0,0,10,0);restore.Click+=delegate{RefreshSaved();enabled.IsChecked=baseline.Enabled;changingProvider=true;connections=baseline.Copy();selectedProvider=baseline.Provider;provider.SelectedIndex=Array.IndexOf(WebSearchProviders.Ids,baseline.Provider);changingProvider=false;protocol.SelectedIndex=Math.Max(0,Array.IndexOf(protocols,baseline.NativeProtocol));customProtocol.SelectedIndex=Math.Max(0,Array.IndexOf(WebSearchProviders.SchemaIds,baseline.CustomProtocol));sources.IsChecked=baseline.ShowSources;address.Text=baseline.ReadConnection(selectedProvider).BaseUrl;key.Password=baseline.ReadConnection(selectedProvider).ApiKey;RefreshFields();DraftChanged();};actions.Children.Add(restore);save.Margin=new Thickness(0);actions.Children.Add(save);DockPanel.SetDock(actions,Dock.Right);footer.Children.Add(actions);footer.Children.Add(notice);return new Border{Child=footer,BorderBrush=Ui.Line,BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(0,12,0,0)};}
        void RefreshFields(){if(provider==null)return;bool external=provider.SelectedIndex>0;nativeFields.Visibility=external?Visibility.Collapsed:Visibility.Visible;externalFields.Visibility=external?Visibility.Visible:Visibility.Collapsed;nativeFields.IsEnabled=externalFields.IsEnabled=enabled.IsChecked==true;customProtocolRow.Visibility=selectedProvider=="custom"?Visibility.Visible:Visibility.Collapsed;}
        internal WebSearchSettings Read()
        {
            var value=connections.Copy();value.Enabled=enabled.IsChecked==true;value.Provider=WebSearchProviders.Ids[Math.Max(0,provider.SelectedIndex)];value.Connection=value.ReadConnection(value.Provider);value.NativeProtocol=protocols[Math.Max(0,protocol.SelectedIndex)];value.CustomProtocol=WebSearchProviders.SchemaIds[Math.Max(0,customProtocol.SelectedIndex)];value.ShowSources=sources.IsChecked==true;value.Connection.BaseUrl=address.Text.Trim();
            value.Connection.ApiKey=key.Password;value.SetConnection(value.Provider,value.Connection);value.Normalize();if(value.Enabled)value.Validate(pet.Settings.Text);return value;
        }
        bool OtherConnectionsChanged(){foreach(var id in WebSearchProviders.Ids){if(id==selectedProvider)continue;var a=connections.ReadConnection(id);var b=baseline.ReadConnection(id);if(a.BaseUrl!=b.BaseUrl||a.ApiKey!=b.ApiKey)return true;}return false;}
        internal bool HasUnsavedChanges {get{return OtherConnectionsChanged()||enabled.IsChecked!=baseline.Enabled||WebSearchProviders.Ids[Math.Max(0,provider.SelectedIndex)]!=baseline.Provider||protocols[Math.Max(0,protocol.SelectedIndex)]!=baseline.NativeProtocol||WebSearchProviders.SchemaIds[Math.Max(0,customProtocol.SelectedIndex)]!=baseline.CustomProtocol||sources.IsChecked!=baseline.ShowSources||address.Text.Trim()!=baseline.ReadConnection(selectedProvider).BaseUrl||key.Password!=baseline.ReadConnection(selectedProvider).ApiKey;}}
        internal void RefreshSaved(){baseline=(pet.Settings.WebSearch??new WebSearchSettings()).Copy();}
    }
}
