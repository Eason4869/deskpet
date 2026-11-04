using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class ConversationSettingsView : Grid
{
    private readonly DesktopController owner;
    private readonly ComboBox mode;
    private readonly CheckBox readReplies, detailed;
    private readonly TextBlock notice = SettingsVisual.Text("",12,true);
    private bool baselineMode, baselineRead, baselineDetailed;
    private bool refreshing;
    internal bool HasUnsavedChanges => mode.SelectedIndex!=(baselineMode?1:0)||readReplies.IsChecked!=baselineRead||detailed.IsChecked!=baselineDetailed;

    internal ConversationSettingsView(DesktopController controller)
    {
        owner=controller;Name="ConversationSettings";MaxWidth=900;RowDefinitions=new RowDefinitions("*,Auto");
        mode=LocalizedUi.SetOptions(new ComboBox{Name="DefaultConversationMode",IsEnabled=OperatingSystem.IsWindows(),HorizontalAlignment=HorizontalAlignment.Stretch},()=>new[]{TextResources.Get("settingsLayout.chatMode"),TextResources.Get("settingsLayout.workMode")});
        readReplies=new CheckBox{Name="ReadReplies"};detailed=new CheckBox{Name="AgentDetailedProcess"};
        LocalizedUi.Automation(mode,()=>TextResources.Get("settingsLayout.defaultMode"));LocalizedUi.Automation(readReplies,()=>TextResources.Get("settingsLayout.readReplies"));LocalizedUi.Automation(detailed,()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAF"));
        var defaults=SettingsVisual.Group(()=>TextResources.Get("settingsLayout.modeGroup"),
            SettingsVisual.ActionRow("",()=>TextResources.Get("settingsLayout.defaultMode"),()=>TextResources.Get("settingsLayout.nativeDefaultModeHelp"),mode));
        if(!OperatingSystem.IsWindows()&&defaults is StackPanel group)group.Children.Add(SettingsVisual.Text(()=>TextResources.Get("workSettingsView.workSettingsView.text.alternativeB"),12,true));
        var defaultsPage=SettingsVisual.Page(SettingsVisual.Text(()=>TextResources.Get("settingsLayout.conversationDescription"),13,true),defaults,
            SettingsVisual.Group(()=>TextResources.Get("settingsLayout.inputGroup"),SettingsVisual.Text(()=>TextResources.Get("settingsLayout.nativeInputHint"),13,true)));
        var displayPage=SettingsVisual.Page(SettingsVisual.Group(()=>TextResources.Get("settingsLayout.displayGroup"),
            SettingsVisual.ActionRow("",()=>TextResources.Get("settingsLayout.readReplies"),()=>TextResources.Get("settingsLayout.readRepliesHelp"),readReplies),
            SettingsVisual.ActionRow("",()=>TextResources.Get("workSettingsPanel.workSettingsPanel.settingRow.alternativeAF"),()=>TextResources.Get("settingsLayout.processDescription"),detailed)));
        Control Scroll(Control content)=>new ScrollViewer{HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,Content=new Border{Padding=new Thickness(24),Child=content}};
        var tabs=new TabControl{Name="ConversationSettingsTabs",HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,
            ItemsPanel=new Avalonia.Controls.Templates.FuncTemplate<Panel?>(()=>new WrapPanel{Orientation=Orientation.Horizontal}),
            ItemsSource=new[]{LocalizedUi.With(new TabItem{Content=Scroll(defaultsPage),FontSize=12,Padding=new Thickness(8,10)},"Header",()=>TextResources.Get("settingsLayout.modeGroup")),LocalizedUi.With(new TabItem{Content=Scroll(displayPage),FontSize=12,Padding=new Thickness(8,10)},"Header",()=>TextResources.Get("settingsLayout.displayGroup"))},SelectedIndex=0};
        Children.Add(tabs);
        var save=LocalizedUi.Button(()=>TextResources.Get("settingsLayout.save"),Save);save.Name="SaveConversationSettings";
        var restore=LocalizedUi.Button(()=>TextResources.Get("settingsLayout.restore"),RefreshRestoredPreferences);restore.Name="RestoreConversationSettings";
        var footer=new Border{Padding=new Thickness(24,14),BorderBrush=DesktopTheme.CardBorder,BorderThickness=new Thickness(0,1,0,0),Child=Ui.Column(notice,SettingsVisual.Wrap(save,restore))};Grid.SetRow(footer,1);Children.Add(footer);
        mode.SelectionChanged+=(_,_)=>RefreshNotice();readReplies.IsCheckedChanged+=(_,_)=>RefreshNotice();detailed.IsCheckedChanged+=(_,_)=>RefreshNotice();
        RefreshRestoredPreferences();
    }
    internal void RefreshRestoredPreferences()
    {
        refreshing=true;
        try{baselineMode=owner.Settings.AgentEnabled;baselineRead=owner.Settings.ReadReplies;baselineDetailed=owner.Settings.AgentDetailedProcess;mode.SelectedIndex=baselineMode?1:0;readReplies.IsChecked=baselineRead;detailed.IsChecked=baselineDetailed;}
        finally{refreshing=false;}
        RefreshNotice();
    }
    private void RefreshNotice()
    {
        if(refreshing)return;LocalizedUi.Set(notice,"Text",()=>TextResources.Get(HasUnsavedChanges?"settingsLayout.unsaved":"settingsLayout.saved"));
    }
    private void Save()
    {
        var settings=owner.Settings;bool previousMode=settings.AgentEnabled,previousRead=settings.ReadReplies,previousDetailed=settings.AgentDetailedProcess;
        try{
            if(OperatingSystem.IsWindows()&&mode.SelectedIndex!=(baselineMode?1:0))settings.AgentEnabled=mode.SelectedIndex==1;
            if(readReplies.IsChecked!=baselineRead)settings.ReadReplies=readReplies.IsChecked==true;
            if(detailed.IsChecked!=baselineDetailed)settings.AgentDetailedProcess=detailed.IsChecked==true;
            owner.Save();RefreshRestoredPreferences();
        }catch(Exception error){settings.AgentEnabled=previousMode;settings.ReadReplies=previousRead;settings.AgentDetailedProcess=previousDetailed;owner.Error(error);}
    }
}
