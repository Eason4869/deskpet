using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Dafeiyu;
using Dafeiyu.Maintenance;

namespace DeskPet.Desktop;

internal sealed class DataManagementView:StackPanel
{
    internal DataManagementView(DesktopController owner,Window host,Action refresh)
    {
        Spacing=12;var service=new HistoryMaintenanceService(owner.DataDirectory,owner.DataMaintenance,owner.ImageTasks);var status=Ui.Text("");status.Name="DataManagementStatus";
        var clear=SettingsVisual.Button(()=> (TextResources.Get("dataManagement.clearHistory")),()=>{
            var dialog=new DataActionConfirmationWindow(TextResources.Get("dataManagement.clearHistory"),TextResources.Get("dataManagement.historyScope"),async()=>DataManagementPresentation.Describe(await service.ClearAllAsync()),owner.FlushDeferredSettingsAsync);_=dialog.ShowDialog(host);
        },true);clear.Name="ClearAllHistory";
        var reset=SettingsVisual.Button(()=> (TextResources.Get("dataManagement.restorePreferences")),()=>{
            var dialog=new DataActionConfirmationWindow(TextResources.Get("dataManagement.restorePreferences"),TextResources.Get("dataManagement.preferenceScope"),async()=>{
                try{await owner.RestoreDefaultPreferencesAsync();}catch(InvalidOperationException){return TextResources.Get("dataMaintenance.busy");}catch(Exception){return TextResources.Get("dataManagement.resetFailed");}
                try{owner.ApplyRestoredPreferences();refresh();}catch(Exception){return TextResources.Get("dataManagement.applyFailed");}
                return TextResources.Get("dataManagement.resetComplete");
            },owner.FlushDeferredSettingsAsync);_=dialog.ShowDialog(host);
        });reset.Name="RestoreDefaultPreferences";
        var attachments=SettingsVisual.Button(()=> (TextResources.Get("dataManagement.attachments")),()=>{});attachments.Name="OpenPreservedAttachments";attachments.Click+=async(_,_)=>{
            attachments.IsEnabled=false;try{if(!await DataManagementPresentation.HasAttachmentsAsync(service.AttachmentDirectory))LocalizedUi.Set(status,"Text",()=>(TextResources.Get("dataManagement.attachmentsEmpty")));else if(!await host.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(service.AttachmentDirectory)))LocalizedUi.Set(status,"Text",()=>(TextResources.Get("dataManagement.attachmentsFailed")));else status.Text="";}catch(Exception){LocalizedUi.Set(status,"Text",()=>(TextResources.Get("dataManagement.attachmentsFailed")));}finally{attachments.IsEnabled=true;}
        };
        var title=SettingsVisual.Text(()=> (TextResources.Get("dataManagement.title")),25);title.FontWeight=Avalonia.Media.FontWeight.SemiBold;Children.Add(title);Children.Add(SettingsVisual.Text(()=> (TextResources.Get("dataManagement.intro")),13,true));
        var save=LocalizedUi.Check(()=>(TextResources.Get("settingsWindow.storage.check")),owner.Settings.SaveChatHistory,v=>{owner.Settings.SaveChatHistory=v;owner.Save();});save.Name="SaveChatHistory";save.Content=SettingsVisual.Text(()=> (TextResources.Get("settingsWindow.storage.check")),14);
        var history=Ui.Column(SettingsVisual.Header("chat",()=> (TextResources.Get("dataManagement.historyTitle"))),save,SettingsVisual.Text(()=> (TextResources.Get("dataManagement.historyNote")),12,true),
            new Avalonia.Controls.Border{Height=1,Background=DesktopTheme.Brush("#D5E1ED"),Margin=new Avalonia.Thickness(0,6)},SettingsVisual.ActionRow("",()=> (TextResources.Get("dataManagement.clearHistory")),()=> (TextResources.Get("dataManagement.clearNote")),clear));
        Children.Add(SettingsVisual.Card(history));
        Children.Add(SettingsVisual.Card(SettingsVisual.ActionRow("settings",()=> (TextResources.Get("dataManagement.preferenceTitle")),()=> (TextResources.Get("dataManagement.preferenceNote")),reset)));
        Children.Add(SettingsVisual.Card(Ui.Column(SettingsVisual.ActionRow("folder",()=> (TextResources.Get("dataManagement.attachmentsTitle")),()=> (TextResources.Get("dataManagement.attachmentsNote")),attachments),status)));
        status.IsVisible=false;status.PropertyChanged+=(_,e)=>{if(e.Property==TextBlock.TextProperty)status.IsVisible=!string.IsNullOrEmpty(status.Text);};
        Children.Add(new Border{CornerRadius=new Avalonia.CornerRadius(10),Padding=new Avalonia.Thickness(14),Background=DesktopTheme.Brush("#F7F8FA"),Child=SettingsVisual.Note("shield",()=> (TextResources.Get("dataManagement.retained")))});
    }
}
