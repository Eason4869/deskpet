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
        var clear=Ui.Button(TextResources.Get("dataManagement.clearHistory"),()=>{
            var dialog=new DataActionConfirmationWindow(TextResources.Get("dataManagement.clearHistory"),TextResources.Get("dataManagement.historyScope"),async()=>DataManagementPresentation.Describe(await service.ClearAllAsync()),owner.FlushDeferredSettingsAsync);_=dialog.ShowDialog(host);
        });clear.Name="ClearAllHistory";
        var reset=Ui.Button(TextResources.Get("dataManagement.restorePreferences"),()=>{
            var dialog=new DataActionConfirmationWindow(TextResources.Get("dataManagement.restorePreferences"),TextResources.Get("dataManagement.preferenceScope"),async()=>{
                try{await owner.RestoreDefaultPreferencesAsync();}catch(InvalidOperationException){return TextResources.Get("dataMaintenance.busy");}catch(Exception){return TextResources.Get("dataManagement.resetFailed");}
                try{owner.ApplyRestoredPreferences();refresh();}catch(Exception){return TextResources.Get("dataManagement.applyFailed");}
                return TextResources.Get("dataManagement.resetComplete");
            },owner.FlushDeferredSettingsAsync);_=dialog.ShowDialog(host);
        });reset.Name="RestoreDefaultPreferences";
        var attachments=Ui.Button(TextResources.Get("dataManagement.attachments"),()=>{});attachments.Name="OpenPreservedAttachments";attachments.Click+=async(_,_)=>{
            attachments.IsEnabled=false;try{if(!await DataManagementPresentation.HasAttachmentsAsync(service.AttachmentDirectory))status.Text=TextResources.Get("dataManagement.attachmentsEmpty");else if(!await host.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(service.AttachmentDirectory)))status.Text=TextResources.Get("dataManagement.attachmentsFailed");else status.Text="";}catch(Exception){status.Text=TextResources.Get("dataManagement.attachmentsFailed");}finally{attachments.IsEnabled=true;}
        };
        Children.Add(Ui.Text(TextResources.Get("dataManagement.title"),20));var save=Ui.Check(TextResources.Get("settingsWindow.storage.check"),owner.Settings.SaveChatHistory,v=>{owner.Settings.SaveChatHistory=v;owner.Save();});save.Name="SaveChatHistory";Children.Add(save);
        foreach(var button in new[]{clear,reset,attachments})Children.Add(button);Children.Add(Ui.Text(TextResources.Get("dataManagement.retained")));Children.Add(status);
    }
}
