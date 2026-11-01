using System.Runtime.CompilerServices;
using Dafeiyu;
using DeskPet.Platform.Mac;

[assembly: InternalsVisibleTo("DeskPet.Desktop.Tests")]

namespace DeskPet.Desktop;

// The boundary also permits UI tests to run with isolated, in-memory services.
// Production always constructs MacRuntime and never selects a fallback protector.
internal abstract class DesktopRuntime
{
    public abstract string DataDirectory { get; }
    public virtual bool ShowTray => true;
    public abstract void InitializeProtection();
    public abstract IReplySpeech CreateSpeech(ApiClient api);
    public abstract IAudioRecorder CreateRecorder();
    public abstract IPcmMicrophone CreateMicrophone();
    public abstract ILiveAudioOutput CreateLiveOutput();
    public virtual Task<bool> RequestMicrophoneAccessAsync() => MacServices.RequestMicrophoneAccessAsync();
    public virtual IDesktopObservationCapture CreateDesktopObservationCapture()=>MacServices.CreateDesktopObservationCapture();
    public virtual Task<bool> RequestScreenCaptureAccessAsync()=>MacServices.RequestScreenCaptureAccessAsync();
}

internal sealed class MacRuntime : DesktopRuntime
{
    public override string DataDirectory => MacServices.DataDirectory;
    public override void InitializeProtection() => MacServices.InstallSecretProtection();
    public override IReplySpeech CreateSpeech(ApiClient api) => MacServices.CreateSpeech(api);
    public override IAudioRecorder CreateRecorder() => MacServices.CreateRecorder();
    public override IPcmMicrophone CreateMicrophone() => MacServices.CreateMicrophone();
    public override ILiveAudioOutput CreateLiveOutput() => MacServices.CreateLiveOutput();
}
