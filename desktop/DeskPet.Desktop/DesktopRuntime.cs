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
    public virtual TimeSpan ObservationIdleTime=>MacServices.IsSupported?MacServices.GetIdleTime():TimeSpan.MaxValue;
    public virtual bool ObservationFullscreen=>MacServices.IsSupported&&MacServices.IsFrontApplicationFullscreen();
    public virtual ulong? SystemPowerGeneration => null;
}

internal sealed class MacRuntime : DesktopRuntime
{
    private bool systemPowerUnavailable;
    public override string DataDirectory => MacServices.DataDirectory;
    public override ulong? SystemPowerGeneration
    {
        get
        {
            if (systemPowerUnavailable || !MacServices.IsSupported) return null;
            try { return MacServices.SystemPowerGeneration; }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
            {
                systemPowerUnavailable = true;
                System.Diagnostics.Trace.TraceWarning(error.GetType().Name);
                return null;
            }
        }
    }
    public override void InitializeProtection()
    {
        MacServices.InstallSecretProtection();
        FileOcrService.Configure(MacServices.RecognizeFileText);
        MediaFileReader.ConfigureFrameExtractor((path,milliseconds,token) =>
        {
            var frame=MacServices.ExtractVideoFrame(path,milliseconds,token);
            return new FileReadResult("",
                FileContentService.FormatFrameTime(milliseconds,frame.ActualMilliseconds),null,frame.Image,"image/png");
        });
    }
    public override IReplySpeech CreateSpeech(ApiClient api) => MacServices.CreateSpeech(api);
    public override IAudioRecorder CreateRecorder() => MacServices.CreateRecorder();
    public override IPcmMicrophone CreateMicrophone() => MacServices.CreateMicrophone();
    public override ILiveAudioOutput CreateLiveOutput() => MacServices.CreateLiveOutput();
}
