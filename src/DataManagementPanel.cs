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
        public DataManagementPanel(PetWindow pet,Action refresh,FrameworkElement historyToggle=null)
        {
            service=new HistoryMaintenanceService(pet.Store.DirectoryPath,pet.DataMaintenance,pet.ImageTasks);
            Margin=new Thickness(0,16,0,0);
            var clear=LocalizedUi.Button(()=>(TextResources.Get("dataManagement.clearHistory")));clear.Name="ClearAllHistory";
            clear.Click+=delegate{new DataActionConfirmationWindow(TextResources.Get("dataManagement.clearHistory"),TextResources.Get("dataManagement.historyScope"),async delegate{return DataManagementPresentation.Describe(await service.ClearAllAsync());},pet.FlushDeferredSettingsAsync){Owner=Window.GetWindow(this)}.ShowDialog();};
            var reset=LocalizedUi.Button(()=>(TextResources.Get("dataManagement.restorePreferences")));reset.Name="RestoreDefaultPreferences";
            reset.Click+=delegate{new DataActionConfirmationWindow(TextResources.Get("dataManagement.restorePreferences"),TextResources.Get("dataManagement.preferenceScope"),async delegate{
                try{await pet.Store.RestoreDefaultPreferencesAsync(pet.Settings,pet.DataMaintenance);}catch(InvalidOperationException){return TextResources.Get("dataMaintenance.busy");}catch(Exception){return TextResources.Get("dataManagement.resetFailed");}
                try{string warning=pet.ApplyRestoredPreferences();refresh();return String.IsNullOrEmpty(warning)?TextResources.Get("dataManagement.resetComplete"):TextResources.Get("dataManagement.applyFailed")+"\n"+warning;}catch(Exception){return TextResources.Get("dataManagement.applyFailed");}
            },pet.FlushDeferredSettingsAsync){Owner=Window.GetWindow(this)}.ShowDialog();};
            var attachments=LocalizedUi.Button(()=>(TextResources.Get("dataManagement.attachments")));attachments.Name="OpenPreservedAttachments";attachments.Click+=async delegate{
                attachments.IsEnabled=false;try{if(!await DataManagementPresentation.HasAttachmentsAsync(service.AttachmentDirectory))LocalizedUi.Set(status,"Text",()=>(TextResources.Get("dataManagement.attachmentsEmpty")));else{Process.Start(new ProcessStartInfo(service.AttachmentDirectory){UseShellExecute=true});status.Text="";}}catch(Exception){LocalizedUi.Set(status,"Text",()=>(TextResources.Get("dataManagement.attachmentsFailed")));}finally{attachments.IsEnabled=true;}
            };
            foreach(var button in new[]{clear,reset,attachments})DataManagementVisual.Button(button,button==clear);
            var intro=LocalizedUi.Text(()=>(TextResources.Get("dataManagement.intro")),13,Ui.Muted);intro.Margin=new Thickness(0,0,0,16);Children.Add(intro);
            var history=new StackPanel();history.Children.Add(DataManagementVisual.Header("chat",()=> (TextResources.Get("dataManagement.historyTitle"))));
            if(historyToggle!=null){historyToggle.Margin=new Thickness(0,12,0,6);history.Children.Add(historyToggle);}
            history.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("dataManagement.historyNote")),12,Ui.Muted));history.Children.Add(new Border{Height=1,Background=ThemeManager.CardBorder,Margin=new Thickness(0,16,0,16)});
            history.Children.Add(DataManagementVisual.ActionRow("",()=> (TextResources.Get("dataManagement.clearHistory")),()=> (TextResources.Get("dataManagement.clearNote")),clear));Children.Add(DataManagementVisual.Card(history));
            Children.Add(DataManagementVisual.Card(DataManagementVisual.ActionRow("settings",()=> (TextResources.Get("dataManagement.preferenceTitle")),()=> (TextResources.Get("dataManagement.preferenceNote")),reset)));
            var attachmentGroup=new StackPanel();attachmentGroup.Children.Add(DataManagementVisual.ActionRow("folder",()=> (TextResources.Get("dataManagement.attachmentsTitle")),()=> (TextResources.Get("dataManagement.attachmentsNote")),attachments));
            status=Ui.Text("",12,Ui.Muted);status.Name="DataManagementStatus";status.Margin=new Thickness(0,12,0,0);var statusStyle=new Style(typeof(TextBlock));var empty=new Trigger{Property=TextBlock.TextProperty,Value=""};empty.Setters.Add(new Setter(UIElement.VisibilityProperty,Visibility.Collapsed));statusStyle.Triggers.Add(empty);status.Style=statusStyle;attachmentGroup.Children.Add(status);Children.Add(DataManagementVisual.Card(attachmentGroup));
            Children.Add(new Border{CornerRadius=new CornerRadius(10),Padding=new Thickness(14),Background=ThemeManager.Brush("#F7F8FA"),Child=DataManagementVisual.Note("shield",()=> (TextResources.Get("dataManagement.retained")))});
        }
    }
}
