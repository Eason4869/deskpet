using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Dafeiyu;
namespace DeskPet.Desktop;

internal sealed class WebSearchSettingsView : Grid
{
    private readonly DesktopController owner;
    private WebSearchSettings baseline;
    private WebSearchSettings connections;
    private string selectedProvider;
    private readonly CheckBox enabled,sources;
    private readonly ComboBox provider,protocol,customProtocol;
    private readonly TextBox address,key;
    private readonly TextBlock notice=SettingsVisual.Text("",12,true);
    private static readonly string[] protocols={"auto","openai-responses","anthropic","qwen"};
    private bool refreshing;
    internal bool HasUnsavedChanges=>enabled.IsChecked!=baseline.Enabled||sources.IsChecked!=baseline.ShowSources||ProviderId!=baseline.Provider||protocol.SelectedIndex!=Math.Max(0,Array.IndexOf(protocols,baseline.NativeProtocol))||customProtocol.SelectedIndex!=Math.Max(0,Array.IndexOf(WebSearchProviders.SchemaIds,baseline.CustomProtocol))||(address.Text??"").Trim()!=baseline.ReadConnection(selectedProvider).BaseUrl||(key.Text??"")!=baseline.ReadConnection(selectedProvider).ApiKey||OtherConnectionsChanged();
    public WebSearchSettingsView(DesktopController controller)
    {
        owner=controller;Name="WebSearchSettings";MaxWidth=900;RowDefinitions=new RowDefinitions("*,Auto");
        baseline=(owner.Settings.WebSearch??new WebSearchSettings()).Copy();baseline.Normalize();connections=baseline.Copy();selectedProvider=baseline.Provider;
        enabled=new CheckBox{Name="WebSearchEnabled"};LocalizedUi.Automation(enabled,()=>TextResources.Get("webSearch.enabled"));
        provider=LocalizedUi.SetOptions(new ComboBox{Name="WebSearchProvider"},()=>Array.ConvertAll(WebSearchProviders.Ids,id=>id=="native"?TextResources.Get("webSearch.native"):id=="custom"?TextResources.Get("webSearch.custom"):id=="doubao"?TextResources.Get("webSearch.doubao"):WebSearchProviders.DisplayName(id)));
        protocol=LocalizedUi.SetOptions(new ComboBox{Name="WebSearchProtocol"},()=>new[]{TextResources.Get("webSearch.auto"),"OpenAI Responses","Anthropic","Qwen / DashScope"});
        address=Ui.Input();address.Name="WebSearchAddress";key=Ui.Input();key.Name="WebSearchKey";key.PasswordChar='●';
        sources=new CheckBox{Name="WebSearchSources"};LocalizedUi.Automation(sources,()=>TextResources.Get("webSearch.showSources"));
        var native=Ui.Column(SettingsVisual.ActionRow("",()=>TextResources.Get("webSearch.protocol"),()=>"",protocol));
        customProtocol=LocalizedUi.SetOptions(new ComboBox{Name="WebSearchCustomProtocol"},()=>Array.ConvertAll(WebSearchProviders.SchemaIds,WebSearchProviders.DisplayName));
        var customRow=SettingsVisual.ActionRow("",()=>TextResources.Get("webSearch.customProtocol"),()=>TextResources.Get("webSearch.customHelp"),customProtocol);
        var externalFields=Ui.Column(customRow,SettingsVisual.ActionRow("",()=>TextResources.Get("webSearch.address"),()=>"",address),SettingsVisual.ActionRow("",()=>TextResources.Get("webSearch.key"),()=>"",key));
        var body=SettingsVisual.Page(SettingsVisual.Group(()=>TextResources.Get("webSearch.title"),
            SettingsVisual.ActionRow("",()=>TextResources.Get("webSearch.enabled"),()=>"",enabled),
            SettingsVisual.ActionRow("",()=>TextResources.Get("webSearch.provider"),()=>"",provider),native,externalFields,
            SettingsVisual.ActionRow("",()=>TextResources.Get("webSearch.showSources"),()=>"",sources),SettingsVisual.Text(()=>TextResources.Get("webSearch.help"),12,true)));
        Children.Add(new ScrollViewer{HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,Content=new Border{Padding=new Thickness(24),Child=body}});
        var save=LocalizedUi.Button(()=>TextResources.Get("webSearch.save"),Save);save.Name="SaveWebSearch";
        var restore=LocalizedUi.Button(()=>TextResources.Get("settingsLayout.restore"),Restore);restore.Name="RestoreWebSearch";
        var footer=new Border{Padding=new Thickness(24,14),BorderBrush=DesktopTheme.CardBorder,BorderThickness=new Thickness(0,1,0,0),Child=Ui.Column(notice,SettingsVisual.Wrap(save,restore))};Grid.SetRow(footer,1);Children.Add(footer);
        void RefreshVisibility(){bool external=provider.SelectedIndex>0;native.IsVisible=!external;externalFields.IsVisible=external;native.IsEnabled=externalFields.IsEnabled=enabled.IsChecked==true;customRow.IsVisible=ProviderId=="custom";RefreshNotice();}
        provider.SelectionChanged+=(_,_)=>{SwitchProvider();RefreshVisibility();};enabled.IsCheckedChanged+=(_,_)=>RefreshVisibility();
        protocol.SelectionChanged+=(_,_)=>RefreshNotice();customProtocol.SelectionChanged+=(_,_)=>RefreshNotice();sources.IsCheckedChanged+=(_,_)=>RefreshNotice();address.TextChanged+=(_,_)=>RefreshNotice();key.TextChanged+=(_,_)=>RefreshNotice();
        Restore();RefreshVisibility();
    }
    private string ProviderId=>WebSearchProviders.Ids[Math.Max(0,provider.SelectedIndex)];
    private bool OtherConnectionsChanged()
    {
        foreach(var id in WebSearchProviders.Ids){if(id==selectedProvider)continue;var a=connections.ReadConnection(id);var b=baseline.ReadConnection(id);if(a.BaseUrl!=b.BaseUrl||a.ApiKey!=b.ApiKey)return true;}return false;
    }
    private void SwitchProvider()
    {
        if(refreshing)return;
        var current=connections.ReadConnection(selectedProvider);current.BaseUrl=(address.Text??"").Trim();current.ApiKey=key.Text??"";connections.SetConnection(selectedProvider,current);
        selectedProvider=ProviderId;connections.Provider=selectedProvider;var next=connections.ReadConnection(selectedProvider);connections.Connection=next.Copy();address.Text=next.BaseUrl;key.Text=next.ApiKey;
    }
    private void RefreshNotice()
    {
        if(refreshing)return;LocalizedUi.Set(notice,"Text",()=>(HasUnsavedChanges?TextResources.Get("settingsLayout.unsaved"):TextResources.Get("settingsLayout.saved")));
    }
    private void Restore()
    {
        refreshing=true;
        try{baseline=(owner.Settings.WebSearch??new WebSearchSettings()).Copy();baseline.Normalize();enabled.IsChecked=baseline.Enabled;sources.IsChecked=baseline.ShowSources;connections=baseline.Copy();selectedProvider=baseline.Provider;provider.SelectedIndex=Array.IndexOf(WebSearchProviders.Ids,baseline.Provider);protocol.SelectedIndex=Math.Max(0,Array.IndexOf(protocols,baseline.NativeProtocol));customProtocol.SelectedIndex=Math.Max(0,Array.IndexOf(WebSearchProviders.SchemaIds,baseline.CustomProtocol));address.Text=baseline.ReadConnection(selectedProvider).BaseUrl;key.Text=baseline.ReadConnection(selectedProvider).ApiKey;}
        finally{refreshing=false;}
        RefreshNotice();
    }
    private void Save()
    {
        try{
            var draft=connections.Copy();draft.Enabled=enabled.IsChecked==true;draft.Provider=ProviderId;draft.Connection=draft.ReadConnection(ProviderId);draft.NativeProtocol=protocols[Math.Max(0,protocol.SelectedIndex)];draft.CustomProtocol=WebSearchProviders.SchemaIds[Math.Max(0,customProtocol.SelectedIndex)];draft.ShowSources=sources.IsChecked==true;draft.Connection.BaseUrl=(address.Text??"").Trim();draft.Connection.ApiKey=key.Text??"";
            draft.SetConnection(ProviderId,draft.Connection);draft.Normalize();if(draft.Enabled)draft.Validate(owner.Settings.Text);owner.SaveWebSearchDraft(draft);Restore();
        }catch(Exception error){owner.Error(error);}
    }
}
