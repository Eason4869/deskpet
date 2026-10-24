using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Dafeiyu.Maintenance;

namespace Dafeiyu
{
    public sealed class DataManagementPanel:StackPanel
    {
        readonly TextBlock status;readonly HistoryMaintenanceService service;
        public DataManagementPanel(PetWindow pet,Action refresh)
        {
            service=new HistoryMaintenanceService(pet.Store.DirectoryPath,pet.DataMaintenance,pet.ImageTasks);
            Margin=new Thickness(0,16,0,0);
            var clear=Ui.Button(TextResources.Get("dataManagement.clearHistory"));clear.Name="ClearAllHistory";
            clear.Click+=delegate{new DataActionConfirmationWindow(TextResources.Get("dataManagement.clearHistory"),TextResources.Get("dataManagement.historyScope"),async delegate{return DataManagementPresentation.Describe(await service.ClearAllAsync());},pet.FlushDeferredSettingsAsync){Owner=Window.GetWindow(this)}.ShowDialog();};
            var reset=Ui.Button(TextResources.Get("dataManagement.restorePreferences"));reset.Name="RestoreDefaultPreferences";
            reset.Click+=delegate{new DataActionConfirmationWindow(TextResources.Get("dataManagement.restorePreferences"),TextResources.Get("dataManagement.preferenceScope"),async delegate{
                try{await pet.Store.RestoreDefaultPreferencesAsync(pet.Settings,pet.DataMaintenance);}catch(InvalidOperationException){return TextResources.Get("dataMaintenance.busy");}catch(Exception){return TextResources.Get("dataManagement.resetFailed");}
                try{string warning=pet.ApplyRestoredPreferences();refresh();return String.IsNullOrEmpty(warning)?TextResources.Get("dataManagement.resetComplete"):TextResources.Get("dataManagement.applyFailed")+"\n"+warning;}catch(Exception){return TextResources.Get("dataManagement.applyFailed");}
            },pet.FlushDeferredSettingsAsync){Owner=Window.GetWindow(this)}.ShowDialog();};
            var attachments=Ui.Button(TextResources.Get("dataManagement.attachments"));attachments.Name="OpenPreservedAttachments";attachments.Click+=async delegate{
                attachments.IsEnabled=false;try{if(!await DataManagementPresentation.HasAttachmentsAsync(service.AttachmentDirectory))status.Text=TextResources.Get("dataManagement.attachmentsEmpty");else{Process.Start(new ProcessStartInfo(service.AttachmentDirectory){UseShellExecute=true});status.Text="";}}catch(Exception){status.Text=TextResources.Get("dataManagement.attachmentsFailed");}finally{attachments.IsEnabled=true;}
            };
            foreach(var button in new[]{clear,reset,attachments}){button.HorizontalAlignment=HorizontalAlignment.Left;button.Margin=new Thickness(0,4,0,8);Children.Add(button);}
            Children.Add(Ui.Text(TextResources.Get("dataManagement.retained"),12,Ui.Muted));status=Ui.Text("",12,Ui.Muted);status.Name="DataManagementStatus";status.Margin=new Thickness(0,12,0,0);Children.Add(status);
        }
    }
}
