using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed partial class DesktopController
{
    internal DesktopObservationHost Observation {get;private set;}=null!;
    internal bool ObservationPaused {get;private set;}
    internal ObservationReason ObservationReason {get;set;}=ObservationReason.Disabled;
    private bool observationSaving;
    internal event Action? ObservationChanged;
    private void ObservationLanguageChanged(object? sender,EventArgs args)=>Observation.Invalidate();
    internal PetSettings GlobalObservationSettings(){var settings=Settings.Copy();settings.ImageDescriptionOverride=null;return settings;}
    internal void PauseObservation(bool value){ObservationPaused=value;Observation.Invalidate();ObservationChanged?.Invoke();}
    internal void RefreshObservationStatus()=>ObservationChanged?.Invoke();
    internal Task<bool> RequestScreenCaptureAccessAsync()=>runtime.RequestScreenCaptureAccessAsync();
    internal async Task<bool> SaveObservationSettingsAsync(Action<PetSettings> change)
    {
        Dispatcher.UIThread.VerifyAccess();
        if(exiting||observationSaving||DataMaintenance.IsMaintenanceActive)return false;
        using var activity=DataMaintenance.EnterActivity();using var publication=store.TryBeginExplicitSave();
        if(publication==null)return false;
        observationSaving=true;Observation.Invalidate();
        try
        {
            // Replay only the submitted observation intent over the latest live state.
            // Existing provider editors retain their own drafts throughout this transaction.
            do{deferredSettingsSave=false;var candidate=Settings.Copy();change(candidate);await store.SaveDetachedSnapshotAsync(candidate);}while(deferredSettingsSave);
            change(Settings);Observation.Invalidate();ObservationChanged?.Invoke();return true;
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Runtime.Serialization.SerializationException)
        {return false;}
        finally{observationSaving=false;}
    }
}
