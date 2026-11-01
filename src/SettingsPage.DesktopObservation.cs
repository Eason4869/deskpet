using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;

namespace Dafeiyu
{
    public sealed partial class SettingsPage
    {
        CheckBox observationToggle;
        StackPanel observationGroup;
        TextBlock observationStatus, observationDestination, observationTextDestination, observationRoute, observationModel;Button observationConfirm,observationPause,observationTest;
        bool observationDraftEnabled;
        List<string> observationExcludedDraft;ListBox observationExcludedList;bool resumeAfterExclusionSave;
        FrameworkElement CreateObservationSettingsGroup()
        {
            observationDraftEnabled=pet.Settings.DesktopObservationEnabled;observationExcludedDraft=new List<string>(pet.Settings.DesktopObservationExcludedApps);
            observationGroup=new StackPanel { Name="DesktopObservationGroup",Margin=new Thickness(0,18,0,0) };
            observationGroup.Children.Add(Heading(()=>TextResources.Get("desktopObservation.title")));
            observationToggle=LocalizedUi.Toggle(()=>TextResources.Get("desktopObservation.enabled"),observationDraftEnabled,async value=>{observationDraftEnabled=value;await SaveObservationEnabledAsync(value);});
            observationToggle.Name="DesktopObservationEnabled";observationGroup.Children.Add(observationToggle);
            observationGroup.Children.Add(LocalizedUi.Text(()=>TextResources.Get("desktopObservation.help"),12,Ui.Muted));
            observationStatus=LocalizedUi.Text(()=>ObservationStatus(),12,Ui.Muted);observationStatus.Name="DesktopObservationStatus";observationStatus.Margin=new Thickness(0,10,0,0);observationGroup.Children.Add(observationStatus);
            observationModel=LocalizedUi.With(Ui.InlineText(ObservationModel(),12,Ui.Muted),"Text",()=>ObservationModel());observationModel.Name="DesktopObservationModel";observationModel.Margin=new Thickness(0,4,0,0);LocalizedUi.Set(observationModel,"ToolTip",()=>ObservationModel());observationGroup.Children.Add(observationModel);
            observationRoute=LocalizedUi.Text(()=>ObservationRoute(),12,Ui.Muted);observationRoute.Name="DesktopObservationRoute";observationRoute.Margin=new Thickness(0,4,0,0);observationGroup.Children.Add(observationRoute);
            observationDestination=LocalizedUi.With(Ui.InlineText(ObservationDestinationLine(0),12,Ui.Muted),"Text",()=>ObservationDestinationLine(0));observationDestination.Name="DesktopObservationDestinations";observationDestination.Margin=new Thickness(0,4,0,0);LocalizedUi.Set(observationDestination,"ToolTip",()=>ObservationDestinationLine(0));observationGroup.Children.Add(observationDestination);
            observationTextDestination=LocalizedUi.With(Ui.InlineText(ObservationDestinationLine(1),12,Ui.Muted),"Text",()=>ObservationDestinationLine(1));observationTextDestination.Name="DesktopObservationTextDestination";LocalizedUi.Set(observationTextDestination,"ToolTip",()=>ObservationDestinationLine(1));observationGroup.Children.Add(observationTextDestination);
            var actions=new WrapPanel { Margin=new Thickness(0,8,0,0) };
            var preview=LocalizedUi.Button(()=>TextResources.Get("desktopObservation.preview"));preview.Name="DesktopObservationPreview";preview.Click+=async delegate{await pet.PreviewObservationAsync();};actions.Children.Add(preview);
            var test=LocalizedUi.Button(()=>TextResources.Get("desktopObservation.uploadTest"));observationTest=test;test.Name="DesktopObservationUploadTest";test.Click+=async delegate{await pet.TestObservationAsync();};actions.Children.Add(test);
            var pause=LocalizedUi.Button(()=>pet.ObservationPaused?TextResources.Get("desktopObservation.resume"):TextResources.Get("desktopObservation.pause"));observationPause=pause;pause.Name="DesktopObservationPause";pause.Click+=delegate{if(!pet.ObservationPaused)pet.SetObservationPaused(true);else if(observationDraftEnabled && pet.Settings.DesktopObservationEnabled && !ExclusionsDirty())pet.SetObservationPaused(false);RefreshObservation();};actions.Children.Add(pause);
            var confirm=LocalizedUi.Button(()=>TextResources.Get("desktopObservation.confirm"));observationConfirm=confirm;confirm.Name="DesktopObservationConfirm";confirm.IsEnabled=pet.Settings.DesktopObservationEnabled;confirm.Click+=async delegate{if(pet.Settings.DesktopObservationEnabled)await SaveObservationEnabledAsync(true);};actions.Children.Add(confirm);
            observationGroup.Children.Add(actions);
            var configureText=new TextBlock{Margin=new Thickness(0,4,0,0)};var configureRun=new Run();LocalizedUi.Set(configureRun,"Text",()=>TextResources.Get("desktopObservation.configure"));var configure=new Hyperlink(configureRun){Foreground=ThemeManager.Brush("#456DE6","#9DBBFF")};configure.Click+=delegate{pet.ShowSettings(true);};configureText.Inlines.Add(configure);observationGroup.Children.Add(configureText);
            var exclusionBody=new StackPanel{Margin=new Thickness(0,8,0,0)};
            observationExcludedList=new ListBox{Name="DesktopObservationExcludedApps",MinHeight=42,MaxHeight=110,ItemsSource=observationExcludedDraft,Background=ThemeManager.FieldSurface,Foreground=Ui.Ink};exclusionBody.Children.Add(observationExcludedList);
            var appActions=new WrapPanel();
            var apps=LocalizedUi.Button(()=>TextResources.Get("desktopObservation.addApp"));apps.Name="DesktopObservationAddApp";apps.Click+=delegate{var picker=new OpenFileDialog{Filter=TextResources.Get("desktopObservation.excludedApps")+" (*.exe)|*.exe",CheckFileExists=true};if(picker.ShowDialog()!=true)return;string path=Path.GetFullPath(picker.FileName);if(!observationExcludedDraft.Contains(path,StringComparer.OrdinalIgnoreCase)){PauseForExclusionEdit();observationExcludedDraft.Add(path);observationExcludedList.Items.Refresh();RefreshObservation();}};appActions.Children.Add(apps);
            var remove=LocalizedUi.Button(()=>TextResources.Get("desktopObservation.removeApp"));remove.Name="DesktopObservationRemoveApp";remove.Click+=delegate{var selected=observationExcludedList.SelectedItem as string;if(selected!=null){PauseForExclusionEdit();observationExcludedDraft.Remove(selected);observationExcludedList.Items.Refresh();RefreshObservation();}};appActions.Children.Add(remove);
            exclusionBody.Children.Add(appActions);var exclusions=new Expander{Name="DesktopObservationExclusions",Content=exclusionBody,Margin=new Thickness(0,10,0,0)};LocalizedUi.Set(exclusions,"Header",()=>TextResources.Get("desktopObservation.excludedApps"));observationGroup.Children.Add(exclusions);
            var footer=new StackPanel{Margin=new Thickness(0,12,0,0)};var save=LocalizedUi.Button(()=>TextResources.Get("common.save"),true);save.Name="DesktopObservationSave";save.HorizontalAlignment=HorizontalAlignment.Left;save.Click+=async delegate{await SaveObservationDraftAsync();};footer.Children.Add(save);observationGroup.Children.Add(footer);
            RefreshObservation();return observationGroup;
        }
        bool ExclusionsDirty(){return observationExcludedDraft!=null && !observationExcludedDraft.SequenceEqual(pet.Settings.DesktopObservationExcludedApps,StringComparer.OrdinalIgnoreCase);}
        void PauseForExclusionEdit(){if(!ExclusionsDirty())resumeAfterExclusionSave=!pet.ObservationPaused;pet.SetObservationPaused(true);RefreshObservation();}
        string ObservationStatus()
        {
            var settings=pet.ObservationSettingsSnapshot();if(settings.GreetingEnabled && settings.AiGreetingEnabled && !settings.DesktopObservationEnabled)return TextResources.Get("desktopObservation.disabled");
            if(pet.ObservationPaused)return TextResources.Get("desktopObservation.paused");var context=new DesktopObservationContext(settings,pet.Skin.Id,pet.Skin.Name,null,null,DateTime.Now,true,true,()=>true);
            var reason=pet.ObservationAdmission(context).Reason;if(reason==ObservationReason.Allowed && pet.ObservationBusy)reason=ObservationReason.Busy;if(reason==ObservationReason.Allowed){var last=pet.LastObservationReason;if(last.HasValue)reason=last.Value;}
            string text=DesktopObservationUiText.ReasonText(reason);var lastTime=pet.LastAutomaticObservationUtc;if(lastTime.HasValue)text+=" · "+TextResources.Format("desktopObservation.lastObserved",lastTime.Value.ToLocalTime().ToString("g"));return text;
        }
        string ObservationModel(){var model=pet.Settings.Text.Model;return TextResources.Get("desktopObservation.model")+": "+(String.IsNullOrWhiteSpace(model)?TextResources.Get("desktopObservation.noDestination"):model);}
        string ObservationRoute()
        {
            var settings=pet.ObservationSettingsSnapshot();var route=ImageModelResolver.Resolve(settings,null).Route;
            return route==ImageRouteKind.Direct?TextResources.Get("desktopObservation.routeDirect"):route==ImageRouteKind.Described?TextResources.Get("desktopObservation.routeDescribed"):TextResources.Get("desktopObservation.routeConfigure");
        }
        string ObservationDestinationLine(int index)
        {
            string summary=DesktopObservationUiText.ReceivingSummary(pet.ObservationSettingsSnapshot());
            if(summary.Length==0)return index==0?TextResources.Get("desktopObservation.noDestination"):"";
            var lines=summary.Split('\n');return index<lines.Length?lines[index]:"";
        }
        internal void RefreshObservation(){if(observationTest!=null){var testSettings=pet.ObservationSettingsSnapshot();observationTest.IsEnabled=testSettings.DesktopObservationEnabled && testSettings.GreetingEnabled && testSettings.AiGreetingEnabled && !pet.ObservationPaused && !pet.ObservationBusy && DesktopObservationConsent.IsConfirmed(testSettings) && ImageModelResolver.Resolve(testSettings,null).Route!=ImageRouteKind.Unavailable;}if(observationPause!=null){observationPause.IsEnabled=pet.Settings.DesktopObservationEnabled && observationDraftEnabled && !ExclusionsDirty();observationPause.Content=pet.ObservationPaused?TextResources.Get("desktopObservation.resume"):TextResources.Get("desktopObservation.pause");}if(observationConfirm!=null){var global=pet.ObservationSettingsSnapshot();observationConfirm.Visibility=global.DesktopObservationEnabled && !DesktopObservationConsent.IsConfirmed(global)?Visibility.Visible:Visibility.Collapsed;observationConfirm.IsEnabled=global.DesktopObservationEnabled;}if(observationModel!=null)observationModel.Text=ObservationModel();if(observationRoute!=null)observationRoute.Text=ObservationRoute();if(observationStatus!=null)observationStatus.Text=ObservationStatus();if(observationDestination!=null)observationDestination.Text=ObservationDestinationLine(0);if(observationTextDestination!=null){observationTextDestination.Text=ObservationDestinationLine(1);observationTextDestination.Visibility=observationTextDestination.Text.Length==0?Visibility.Collapsed:Visibility.Visible;}}
        async Task SaveObservationDraftAsync()
        {
            if(observationDraftEnabled!=pet.Settings.DesktopObservationEnabled){await SaveObservationEnabledAsync(observationDraftEnabled);if(observationDraftEnabled!=pet.Settings.DesktopObservationEnabled)return;}
            if(!ExclusionsDirty()){RefreshObservation();return;}
            var pending=observationExcludedDraft.ToArray();bool saved=await pet.RunSettingsEditAsync(observationGroup,()=>pet.PersistSettingsAsync(s=>s.DesktopObservationExcludedApps=new List<string>(pending)),"observation-exclusions");
            if(!saved)ShowNotice(()=>TextResources.Get("desktopObservation.saveFailed"));else{if(resumeAfterExclusionSave && observationDraftEnabled && pet.Settings.DesktopObservationEnabled)pet.SetObservationPaused(false);resumeAfterExclusionSave=false;}RefreshObservation();
        }
        async Task SaveObservationEnabledAsync(bool enabled)
        {
            if(!enabled)pet.SetObservationPaused(true);
            var snapshot=pet.ObservationSettingsSnapshot();string digest="";
            if(enabled)
            {
                string summary=DesktopObservationUiText.ReceivingSummary(snapshot);
                try{digest=DesktopObservationConsent.Destination(snapshot);}catch(ApiException){}
                if(summary.Length==0 || digest.Length==0){RefreshObservation();return;}
                if(!DesktopObservationConsent.IsConfirmed(snapshot))
                {
                    if(new DesktopObservationConsentWindow(snapshot,false){Owner=HostWindow}.ShowDialog()!=true){RefreshObservation();return;}
                }
            }
            string confirmed=digest;
            bool saved=await pet.RunSettingsEditAsync(observationGroup,()=>pet.PersistSettingsAsync(s=>{
                var global=s.Copy();global.ImageDescriptionOverride=null;
                if(enabled && DesktopObservationConsent.Destination(global)!=confirmed)throw new InvalidOperationException();
                s.DesktopObservationEnabled=enabled;
                if(enabled){s.DesktopObservationConsentVersion=DesktopObservationConsent.Version;s.DesktopObservationConsentDestination=confirmed;}
            }),"observation");
            if(saved){observationDraftEnabled=enabled;if(enabled)pet.SetObservationPaused(false);}
            if(!saved)ShowNotice(()=>TextResources.Get("desktopObservation.saveFailed"));
            RefreshObservation();
        }
    }
}
