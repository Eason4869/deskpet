using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;

namespace Dafeiyu
{
    public sealed partial class SettingsPage
    {
        CheckBox observationToggle;
        StackPanel observationGroup;
        TextBlock observationStatus, observationDestination, observationTextDestination, observationRoute, observationModel;Button observationConfirm,observationPause,observationTest;
        bool observationDraftEnabled,refreshingObservationEnabled;
        List<string> observationExcludedDraft;ListBox observationExcludedList;bool resumeAfterExclusionSave;
        ComboBox observationScope;StackPanel observationExclusions;TextBlock observationExcludedEmpty;bool refreshingObservationScope;
        FrameworkElement CreateObservationSettingsGroup()
        {
            observationDraftEnabled=pet.Settings.DesktopObservationEnabled;observationExcludedDraft=new List<string>(pet.Settings.DesktopObservationExcludedApps);
            observationGroup=new StackPanel { Name="DesktopObservationGroup" };
            observationGroup.Children.Add(Heading(()=>TextResources.Get("desktopObservation.settings.title")));
            observationToggle=LocalizedUi.Toggle(()=>TextResources.Get("desktopObservation.settings.enabled"),observationDraftEnabled,async value=>{if(refreshingObservationEnabled)return;observationDraftEnabled=value;await SaveObservationEnabledAsync(value);});
            observationToggle.Name="DesktopObservationEnabled";observationGroup.Children.Add(observationToggle);
            observationGroup.Children.Add(LocalizedUi.Text(()=>TextResources.Get("desktopObservation.settings.help"),12,Ui.Muted));
            observationScope=new ComboBox{Name="DesktopObservationScope",MinHeight=36,HorizontalContentAlignment=HorizontalAlignment.Stretch};
            observationScope.Items.Add(LocalizedUi.With(new ComboBoxItem(),"Content",()=>TextResources.Get("desktopObservation.application")));
            observationScope.Items.Add(LocalizedUi.With(new ComboBoxItem(),"Content",()=>TextResources.Get("desktopObservation.fullScreen")));
            observationScope.SelectionChanged+=async delegate{if(!refreshingObservationScope && observationScope.SelectedIndex>=0)await SaveObservationScopeAsync(observationScope.SelectedIndex==1?DesktopObservationCaptureScope.FullScreen:DesktopObservationCaptureScope.Application);};
            var scopeRow=Ui.SettingRow(()=>TextResources.Get("desktopObservation.captureMode"),null,observationScope,false);scopeRow.Margin=new Thickness(0,14,0,14);observationGroup.Children.Add(scopeRow);
            observationExclusions=new StackPanel{Name="DesktopObservationExclusions",Margin=new Thickness(0,16,0,0)};var exclusionBody=observationExclusions;var exclusionLabel=LocalizedUi.Text(()=>TextResources.Get("desktopObservation.exclusions.title"),13);exclusionLabel.Margin=new Thickness(0,0,0,6);exclusionBody.Children.Add(exclusionLabel);
            observationExcludedList=new ListBox{Name="DesktopObservationExcludedApps",MinHeight=46,MaxHeight=150,ItemsSource=observationExcludedDraft,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(4),Foreground=Ui.Ink,HorizontalContentAlignment=HorizontalAlignment.Stretch,ItemContainerStyle=Ui.NavigationItemStyle};
            ScrollViewer.SetHorizontalScrollBarVisibility(observationExcludedList,ScrollBarVisibility.Disabled);
            var appRow=new FrameworkElementFactory(typeof(StackPanel));appRow.SetValue(MarginProperty,new Thickness(8,5,8,5));
            var appName=new FrameworkElementFactory(typeof(TextBlock));appName.SetBinding(TextBlock.TextProperty,new Binding{Converter=new ExcludedApplicationName()});appName.SetValue(TextBlock.FontSizeProperty,12.0);appName.SetValue(TextBlock.ForegroundProperty,Ui.Ink);appRow.AppendChild(appName);
            var appPath=new FrameworkElementFactory(typeof(TextBlock));appPath.SetBinding(TextBlock.TextProperty,new Binding());appPath.SetBinding(ToolTipProperty,new Binding());appPath.SetValue(TextBlock.FontSizeProperty,10.0);appPath.SetValue(TextBlock.ForegroundProperty,Ui.Muted);appPath.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);appRow.AppendChild(appPath);observationExcludedList.ItemTemplate=new DataTemplate{VisualTree=appRow};
            var listContent=new Grid();listContent.Children.Add(observationExcludedList);observationExcludedEmpty=LocalizedUi.Text(()=>TextResources.Get("desktopObservation.emptyExclusions"),12,Ui.Muted);observationExcludedEmpty.Margin=new Thickness(12,0,12,0);observationExcludedEmpty.VerticalAlignment=VerticalAlignment.Center;observationExcludedEmpty.IsHitTestVisible=false;listContent.Children.Add(observationExcludedEmpty);
            exclusionBody.Children.Add(new Border{Background=ThemeManager.FieldSurface,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(9),Child=listContent});
            var appActions=new WrapPanel{Margin=new Thickness(0,8,0,0)};
            var apps=LocalizedUi.Button(()=>TextResources.Get("desktopObservation.addApp"));apps.Name="DesktopObservationAddApp";apps.Click+=delegate{var picker=new OpenFileDialog{Filter=TextResources.Get("desktopObservation.exclusions.title")+" (*.exe)|*.exe",CheckFileExists=true};if(picker.ShowDialog()!=true)return;string path=Path.GetFullPath(picker.FileName);if(!observationExcludedDraft.Contains(path,StringComparer.OrdinalIgnoreCase)){PauseForExclusionEdit();observationExcludedDraft.Add(path);observationExcludedList.Items.Refresh();RefreshObservation();}};appActions.Children.Add(apps);
            var remove=LocalizedUi.Button(()=>TextResources.Get("desktopObservation.removeApp"));remove.Name="DesktopObservationRemoveApp";remove.Click+=delegate{var selected=observationExcludedList.SelectedItem as string;if(selected!=null){PauseForExclusionEdit();observationExcludedDraft.Remove(selected);observationExcludedList.Items.Refresh();RefreshObservation();}};appActions.Children.Add(remove);
            remove.IsEnabled=false;observationExcludedList.SelectionChanged+=delegate{remove.IsEnabled=observationExcludedList.SelectedItem!=null;};
            exclusionBody.Children.Add(appActions);observationGroup.Children.Add(exclusionBody);
            var save=LocalizedUi.Button(()=>TextResources.Get("desktopObservation.saveExclusions"),true);save.Name="DesktopObservationSave";save.FontWeight=FontWeights.Normal;save.Click+=async delegate{await SaveObservationDraftAsync();};appActions.Children.Add(save);observationGroup.Children.Add(LocalizedUi.Text(()=>TextResources.Get("desktopObservation.exclusionsHelp"),11,Ui.Muted));
            observationGroup.Children.Add(new ImageDescriptionSettings(pet,"ObservationImageDescriptionModel",false){Margin=new Thickness(0,18,0,0)});
            var details=new StackPanel();
            observationStatus=LocalizedUi.Text(()=>ObservationStatus(),12,Ui.Muted);observationStatus.Name="DesktopObservationStatus";observationStatus.Margin=new Thickness(0,10,0,0);details.Children.Add(observationStatus);
            observationModel=LocalizedUi.With(Ui.InlineText(ObservationModel(),12,Ui.Muted),"Text",()=>ObservationModel());observationModel.Name="DesktopObservationModel";observationModel.Margin=new Thickness(0,4,0,0);LocalizedUi.Set(observationModel,"ToolTip",()=>ObservationModel());details.Children.Add(observationModel);
            observationRoute=LocalizedUi.Text(()=>ObservationRoute(),12,Ui.Muted);observationRoute.Name="DesktopObservationRoute";observationRoute.Margin=new Thickness(0,4,0,0);details.Children.Add(observationRoute);
            observationDestination=LocalizedUi.With(Ui.InlineText(ObservationDestinationLine(0),12,Ui.Muted),"Text",()=>ObservationDestinationLine(0));observationDestination.Name="DesktopObservationDestinations";observationDestination.Margin=new Thickness(0,4,0,0);LocalizedUi.Set(observationDestination,"ToolTip",()=>ObservationDestinationLine(0));details.Children.Add(observationDestination);
            observationTextDestination=LocalizedUi.With(Ui.InlineText(ObservationDestinationLine(1),12,Ui.Muted),"Text",()=>ObservationDestinationLine(1));observationTextDestination.Name="DesktopObservationTextDestination";LocalizedUi.Set(observationTextDestination,"ToolTip",()=>ObservationDestinationLine(1));details.Children.Add(observationTextDestination);
            var actions=new WrapPanel { Margin=new Thickness(0,8,0,0) };
            var preview=LocalizedUi.Button(()=>TextResources.Get("desktopObservation.preview.action"));preview.Name="DesktopObservationPreview";preview.Click+=async delegate{await pet.PreviewObservationAsync();};actions.Children.Add(preview);
            var test=LocalizedUi.Button(()=>TextResources.Get("desktopObservation.test.action"));observationTest=test;test.Name="DesktopObservationUploadTest";test.Click+=async delegate{await pet.TestObservationAsync();};actions.Children.Add(test);
            var pause=LocalizedUi.Button(()=>pet.ObservationPaused?TextResources.Get("desktopObservation.resume"):TextResources.Get("desktopObservation.pause"));observationPause=pause;pause.Name="DesktopObservationPause";pause.Click+=delegate{if(!pet.ObservationPaused)pet.SetObservationPaused(true);else if(observationDraftEnabled && pet.Settings.DesktopObservationEnabled && !ActiveExclusionsDirty())pet.SetObservationPaused(false);RefreshObservation();};actions.Children.Add(pause);
            var confirm=LocalizedUi.Button(()=>TextResources.Get("desktopObservation.confirm"));observationConfirm=confirm;confirm.Name="DesktopObservationConfirm";confirm.IsEnabled=pet.Settings.DesktopObservationEnabled;confirm.Click+=async delegate{if(pet.Settings.DesktopObservationEnabled)await SaveObservationEnabledAsync(true);};actions.Children.Add(confirm);
            details.Children.Add(actions);
            var configureText=new TextBlock{Margin=new Thickness(0,4,0,0)};var configureRun=new Run();LocalizedUi.Set(configureRun,"Text",()=>TextResources.Get("desktopObservation.configure"));var configure=new Hyperlink(configureRun){Foreground=ThemeManager.Brush("#456DE6","#9DBBFF")};configure.Click+=delegate{pet.ShowSettings(true);};configureText.Inlines.Add(configure);details.Children.Add(configureText);
            var advanced=new Expander{Name="DesktopObservationDetails",Content=details,Margin=new Thickness(0,16,0,0)};SettingsSurface.StyleExpander(advanced);LocalizedUi.Set(advanced,"Header",()=>TextResources.Get("desktopObservation.details"));observationGroup.Children.Add(advanced);
            RefreshObservation();return observationGroup;
        }
        async Task SaveObservationScopeAsync(DesktopObservationCaptureScope scope)
        {
            if(scope==pet.Settings.DesktopObservationCaptureScope)return;
            bool paused=pet.ObservationPaused;pet.SetObservationPaused(true);
            var snapshot=pet.ObservationSettingsSnapshot();snapshot.DesktopObservationCaptureScope=scope;
            string confirmed="";
            if(snapshot.DesktopObservationEnabled && DesktopObservationUiText.ReceivingSummary(snapshot).Length>0)try{confirmed=DesktopObservationConsent.Destination(snapshot);}catch(ApiException){}
            if(snapshot.DesktopObservationEnabled && confirmed.Length>0 && !DesktopObservationConsent.IsConfirmed(snapshot)){
                if(new DesktopObservationConsentWindow(snapshot,false){Owner=HostWindow}.ShowDialog()!=true){pet.SetObservationPaused(paused);RefreshObservation();return;}
            }
            bool saved=await pet.RunSettingsEditAsync(observationGroup,()=>pet.PersistSettingsAsync(s=>{
                var candidate=s.Copy();candidate.ImageDescriptionOverride=null;candidate.DesktopObservationCaptureScope=scope;
                if(s.DesktopObservationEnabled && confirmed.Length>0 && DesktopObservationConsent.Destination(candidate)!=confirmed)throw new InvalidOperationException();
                s.DesktopObservationCaptureScope=scope;
                if(s.DesktopObservationEnabled){s.DesktopObservationConsentVersion=confirmed.Length>0?DesktopObservationConsent.Version:0;s.DesktopObservationConsentDestination=confirmed;}
            }),"observation-scope");
            if(saved){
                if(ActiveExclusionsDirty()){resumeAfterExclusionSave=!paused;pet.SetObservationPaused(true);}
                else{pet.SetObservationPaused(paused && !(scope==DesktopObservationCaptureScope.FullScreen && resumeAfterExclusionSave));if(scope==DesktopObservationCaptureScope.FullScreen)resumeAfterExclusionSave=false;}
            }
            if(!saved)ShowNotice(()=>TextResources.Get("desktopObservation.saveFailed"));RefreshObservation();
        }
        void RefreshObservationScope()
        {
            if(observationScope==null)return;
            refreshingObservationScope=true;
            bool fullScreen=pet.Settings.DesktopObservationCaptureScope==DesktopObservationCaptureScope.FullScreen;
            observationScope.SelectedIndex=fullScreen?1:0;
            if(observationExclusions!=null){observationExclusions.IsEnabled=!fullScreen;observationExclusions.Opacity=fullScreen?.45:1;}
            if(observationExcludedEmpty!=null)observationExcludedEmpty.Visibility=observationExcludedDraft.Count==0?Visibility.Visible:Visibility.Collapsed;
            refreshingObservationScope=false;
        }
        bool ExclusionsDirty(){return observationExcludedDraft!=null && !observationExcludedDraft.SequenceEqual(pet.Settings.DesktopObservationExcludedApps,StringComparer.OrdinalIgnoreCase);}
        bool ActiveExclusionsDirty(){return pet.Settings.DesktopObservationCaptureScope==DesktopObservationCaptureScope.Application && ExclusionsDirty();}
        sealed class ExcludedApplicationName:IValueConverter
        {
            public object Convert(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture){return Path.GetFileNameWithoutExtension(value as string??"");}
            public object ConvertBack(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture){return Binding.DoNothing;}
        }
        void PauseForExclusionEdit(){if(!ExclusionsDirty())resumeAfterExclusionSave=!pet.ObservationPaused;pet.SetObservationPaused(true);RefreshObservation();}
        string ObservationStatus()
        {
            if(observationStatus!=null)observationStatus.Foreground=Ui.Muted;
            var settings=pet.ObservationSettingsSnapshot();if(settings.GreetingEnabled && settings.AiGreetingEnabled && !settings.DesktopObservationEnabled)return TextResources.Get("desktopObservation.disabled");
            if(pet.ObservationPaused)return TextResources.Get("desktopObservation.paused");var context=new DesktopObservationContext(settings,pet.Skin.Id,pet.Skin.Name,null,null,DateTime.Now,true,true,()=>true);
            var reason=pet.ObservationAdmission(context).Reason;if(reason==ObservationReason.Allowed && pet.ObservationBusy)reason=ObservationReason.Busy;if(reason==ObservationReason.Allowed){var last=pet.LastObservationReason;if(last.HasValue)reason=last.Value;}
            string marker="";Brush color=Ui.Muted;
            switch(reason){
                case ObservationReason.Allowed:marker="\u2705 ";color=ThemeManager.Brush("#218344","#76D99B");break;
                case ObservationReason.ModelFailed:case ObservationReason.InvalidResponse:case ObservationReason.ModelTimeout:case ObservationReason.TotalTimeout:case ObservationReason.CaptureTimeout:case ObservationReason.NoFrame:
                    marker="\u274C ";color=ThemeManager.Brush("#C73535","#FF9797");break;
                case ObservationReason.NoRoute:case ObservationReason.InvalidConfiguration:case ObservationReason.ConsentRequired:case ObservationReason.CaptureUnavailable:
                    marker="\u26A0\uFE0F ";color=ThemeManager.Brush("#946200","#F0C46A");break;
            }
            if(observationStatus!=null)observationStatus.Foreground=color;
            string text=marker+DesktopObservationUiText.ReasonText(reason);var lastTime=pet.LastAutomaticObservationUtc;if(lastTime.HasValue)text+=" · "+TextResources.Format("desktopObservation.lastObserved",lastTime.Value.ToLocalTime().ToString("g"));return text;
        }
        string ObservationModel(){var model=pet.Settings.Text.Model;return TextResources.Get("desktopObservation.model")+": "+(String.IsNullOrWhiteSpace(model)?TextResources.Get("desktopObservation.settings.noDestination"):model);}
        string ObservationRoute()
        {
            var settings=pet.ObservationSettingsSnapshot();var route=ImageModelResolver.Resolve(settings,null).Route;
            return route==ImageRouteKind.Direct?TextResources.Get("desktopObservation.route.direct"):route==ImageRouteKind.Described?TextResources.Get("desktopObservation.route.described"):TextResources.Get("desktopObservation.route.configure");
        }
        string ObservationDestinationLine(int index)
        {
            string summary=DesktopObservationUiText.ReceivingSummary(pet.ObservationSettingsSnapshot());
            if(summary.Length==0)return index==0?TextResources.Get("desktopObservation.settings.noDestination"):"";
            var lines=summary.Split('\n');return index<lines.Length?lines[index]:"";
        }
        internal void RefreshObservation(){if(observationToggle!=null){refreshingObservationEnabled=true;observationToggle.IsChecked=observationDraftEnabled;refreshingObservationEnabled=false;}RefreshObservationScope();if(observationTest!=null){var testSettings=pet.ObservationSettingsSnapshot();observationTest.IsEnabled=testSettings.DesktopObservationEnabled && testSettings.GreetingEnabled && testSettings.AiGreetingEnabled && !pet.ObservationPaused && !pet.ObservationBusy && DesktopObservationConsent.IsConfirmed(testSettings) && ImageModelResolver.Resolve(testSettings,null).Route!=ImageRouteKind.Unavailable;}if(observationPause!=null){observationPause.IsEnabled=pet.Settings.DesktopObservationEnabled && observationDraftEnabled && !ActiveExclusionsDirty();observationPause.Content=pet.ObservationPaused?TextResources.Get("desktopObservation.resume"):TextResources.Get("desktopObservation.pause");}if(observationConfirm!=null){var global=pet.ObservationSettingsSnapshot();observationConfirm.Visibility=global.DesktopObservationEnabled && !DesktopObservationConsent.IsConfirmed(global)?Visibility.Visible:Visibility.Collapsed;observationConfirm.IsEnabled=global.DesktopObservationEnabled && DesktopObservationUiText.ReceivingSummary(global).Length>0;}if(observationModel!=null)observationModel.Text=ObservationModel();if(observationRoute!=null)observationRoute.Text=ObservationRoute();if(observationStatus!=null)observationStatus.Text=ObservationStatus();if(observationDestination!=null)observationDestination.Text=ObservationDestinationLine(0);if(observationTextDestination!=null){observationTextDestination.Text=ObservationDestinationLine(1);observationTextDestination.Visibility=observationTextDestination.Text.Length==0?Visibility.Collapsed:Visibility.Visible;}}
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
                if(summary.Length==0)digest="";
                if(summary.Length>0 && digest.Length>0 && !DesktopObservationConsent.IsConfirmed(snapshot))
                {
                    if(new DesktopObservationConsentWindow(snapshot,false){Owner=HostWindow}.ShowDialog()!=true){observationDraftEnabled=pet.Settings.DesktopObservationEnabled;RefreshObservation();return;}
                }
            }
            string confirmed=digest;
            bool saved=await pet.RunSettingsEditAsync(observationGroup,()=>pet.PersistSettingsAsync(s=>{
                var global=s.Copy();global.ImageDescriptionOverride=null;
                if(enabled && confirmed.Length>0 && DesktopObservationConsent.Destination(global)!=confirmed)throw new InvalidOperationException();
                s.DesktopObservationEnabled=enabled;
                if(enabled){s.DesktopObservationConsentVersion=confirmed.Length>0?DesktopObservationConsent.Version:0;s.DesktopObservationConsentDestination=confirmed;}
            }),"observation");
            if(saved){observationDraftEnabled=enabled;if(enabled)pet.SetObservationPaused(ActiveExclusionsDirty());}
            if(!saved)ShowNotice(()=>TextResources.Get("desktopObservation.saveFailed"));
            else if(enabled && confirmed.Length==0)ShowNotice(()=>TextResources.Get("desktopObservation.route.configure"));
            RefreshObservation();
        }
    }
}
