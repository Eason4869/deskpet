using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        Task<bool> explicitSettingsSave;FrameworkElement explicitSettingsOwner;string explicitSettingsAction;
        IDisposable explicitSettingsMaintenance;
        public Task<bool> SaveSettingsAsync()
        {
            Dispatcher.VerifyAccess();
            if(explicitSettingsSave!=null){deferredSettingsSave=true;return explicitSettingsSave;}
            return RunSettingsEditAsync(null,()=>PersistSettingsAsync(delegate{}));
        }
        internal Task<bool> ChangeLanguageAsync(string code,bool guide=false)
        {
            return RunSettingsEditAsync(null,async delegate{
                try{
                    var prepared=await Task.Run(()=>TextResources.PrepareLanguage(code));
                    await Store.SaveLanguageSelectionAsync(Settings.Copy(),code,guide);
                    Settings.UiLanguage=UiLanguages.Normalize(code);if(guide){Settings.OnboardingLessonId="welcome";Settings.OnboardingStep=0;Settings.OnboardingState="deferred";}
                    TextResources.ApplyLanguage(prepared);if(deferredSettingsSave)return await PersistSettingsAsync(delegate{});return true;
                }catch(Exception error){if(!SettingsSaveError(error))throw;Notice=TextResources.Get("petWindow.saveSettings.notice");return false;}
            });
        }
        internal Task<bool> RunSettingsEditAsync(FrameworkElement owner,Func<Task<bool>> operation,string action="save")
        {
            Dispatcher.VerifyAccess();
            // Embedded hosts may enter on the UI thread before Application.Run.
            // Establish the dispatcher context before callers capture an await.
            if(!(System.Threading.SynchronizationContext.Current is System.Windows.Threading.DispatcherSynchronizationContext))System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(Dispatcher));
            if(explicitSettingsSave!=null){
                if(owner!=null&&ReferenceEquals(owner,explicitSettingsOwner)&&action==explicitSettingsAction)return explicitSettingsSave;
                Notice=TextResources.Get("dataMaintenance.busy");return Task.FromResult(false);
            }
            if(closing||DataMaintenance.IsMaintenanceActive){Notice=TextResources.Get("dataMaintenance.busy");return Task.FromResult(false);}
            if(explicitSettingsMaintenance==null)explicitSettingsMaintenance=DataMaintenance.Register(()=>Dispatcher.InvokeAsync(new Func<Task>(DrainExplicitSettingsAsync)).Task.Unwrap());
            IDisposable publication=Store.TryBeginExplicitSave();
            if(publication==null){Notice=TextResources.Get("dataMaintenance.busy");return Task.FromResult(false);}
            var completion=new TaskCompletionSource<bool>();explicitSettingsSave=completion.Task;explicitSettingsOwner=owner;explicitSettingsAction=action;
            CompleteSettingsEditAsync(owner,operation,publication,completion);return completion.Task;
        }
        async void CompleteSettingsEditAsync(FrameworkElement owner,Func<Task<bool>> operation,IDisposable publication,TaskCompletionSource<bool> completion)
        {
            bool enabled=owner==null||owner.IsEnabled;bool saved=false;Exception failure=null;
            if(owner!=null)owner.IsEnabled=false;
            try{saved=await operation();if(saved&&deferredSettingsSave)saved=await PersistSettingsAsync(delegate{});}catch(Exception error){failure=error;}
            // The transaction includes the editor's rollback/acknowledgement, not
            // just the worker. Maintenance cannot observe a half-published save.
            if(owner!=null)owner.IsEnabled=enabled;
            publication.Dispose();explicitSettingsOwner=null;explicitSettingsAction=null;explicitSettingsSave=null;
            if(failure!=null)completion.SetException(failure);else completion.SetResult(saved);
        }
        async Task DrainExplicitSettingsAsync()
        {var pending=explicitSettingsSave;if(pending!=null&&!await pending)throw new IOException(TextResources.Get("dataManagement.pendingSaveFailed"));}
        internal bool CommitSettingsNow(Action<PetSettings> change)
        {
            Dispatcher.VerifyAccess();if(closing||DataMaintenance.IsMaintenanceActive||explicitSettingsSave!=null){Notice=TextResources.Get("dataMaintenance.busy");return false;}
            settingsSaveRevision++;
            try{var candidate=Settings.Copy();change(candidate);Store.Save(candidate);change(Settings);Notice=Store.Warning;return true;}
            catch(Exception error){if(!SettingsSaveError(error))throw;Notice=TextResources.Get("petWindow.saveSettings.notice");return false;}
        }
        static bool SettingsSaveError(Exception error)
        {return error is IOException||error is UnauthorizedAccessException||error is System.Runtime.Serialization.SerializationException||error is ArgumentException||error is InvalidOperationException;}
        internal async Task<bool> PersistSettingsAsync(Action<PetSettings> change)
        {
            Dispatcher.VerifyAccess();long revision=++settingsSaveRevision;PreferenceChangeSet submittedPreferences=null;
            try{
                do{
                    deferredSettingsSave=false;CompleteScaleLayout();submittedPreferences=pendingPreferences;
                    var candidate=Settings.Copy();change(candidate);
                    await Store.SaveDetachedSnapshotAsync(candidate);
                }while(deferredSettingsSave);
                // Publish only the submitted fields. Other runtime settings may
                // have changed while the worker was committing this candidate.
                change(Settings);
                if(ReferenceEquals(pendingPreferences,submittedPreferences)){pendingPreferences=null;if(preferenceSaves!=null)preferenceSaves.MarkSaved();}
                if(imageTasks!=null)imageTasks.Concurrency=Settings.ImageGenerationConcurrency;
                if(revision==settingsSaveRevision){Notice=Store.Warning;if(preferences!=null)preferences.ShowNotice(Notice);}return true;
            }catch(Exception error){
                if(!SettingsSaveError(error))throw;
                if(revision==settingsSaveRevision){Notice=TextResources.Get("petWindow.saveSettings.notice");if(preferences!=null)preferences.ShowNotice(Notice);}return false;
            }
        }
    }
}
