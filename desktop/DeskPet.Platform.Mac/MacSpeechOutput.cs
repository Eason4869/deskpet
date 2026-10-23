using System.Text.RegularExpressions;
using Dafeiyu;

namespace DeskPet.Platform.Mac;

internal sealed class MacSpeechOutput(ApiClient api) : IReplySpeech
{
    private readonly object gate = new();
    private CancellationTokenSource? active;
    private readonly SayProcess say = new();
    private IntPtr wave;
    private int volume = 50;
    private bool disposed;
    public void SetVolume(int value) { lock (gate) { volume = Math.Clamp(value, 0, 100); if (wave != IntPtr.Zero) Native.WaveVolume(wave, volume / 100f); } }
    public async Task SpeakAsync(string reply, PetSettings settings, CancellationToken cancel)
    {
        MacServices.EnsureMac();
        var text = Regex.Replace(reply ?? "", "```[\\s\\S]*?```", "（代码内容请看聊天窗口）");
        text = Regex.Replace(text, "[\\*#`]|https?://\\S+", "").Trim();
        if (text.Length > 1800) text = TextResources.Format("macSpeechOutput.speakAsync.textText",text[..1800]);
        if (text.Length == 0 || settings.Volume == 0) return;
        using var reading = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        lock (gate) { ObjectDisposedException.ThrowIf(disposed, this); if (active != null) throw new InvalidOperationException(TextResources.Get("macSpeechOutput.speakAsync.text")); active = reading; volume = Math.Clamp(settings.Volume, 0, 100); }
        reading.CancelAfter(TimeSpan.FromMinutes(4));
        try
        {
            if (settings.UseCloudVoice)
            {
                var bytes = await api.SpeechAsync(settings.SpeechConnection(), settings.VoiceName, text, reading.Token);
                reading.Token.ThrowIfCancellationRequested();
                lock (gate) { wave = Native.WaveStart(bytes, bytes.Length, volume / 100f); if (wave == IntPtr.Zero) throw new InvalidOperationException(TextResources.Get("macSpeechOutput.speakAsync.text.alternativeA")); }
                while (true)
                {
                    reading.Token.ThrowIfCancellationRequested();
                    int status; lock (gate) status = Native.WaveStatus(wave);
                    if (status < 0) throw new InvalidOperationException(TextResources.Get("macSpeechOutput.speakAsync.text.alternativeB"));
                    if (status == 0) break;
                    await Task.Delay(50, reading.Token);
                }
            }
            else await SpeakSystem(text, reading.Token);
        }
        finally
        {
            lock (gate)
            {
                if (wave != IntPtr.Zero) { Native.WaveStop(wave); wave = IntPtr.Zero; }
                if (ReferenceEquals(active, reading)) active = null;
            }
        }
    }
    private async Task SpeakSystem(string text, CancellationToken token)
    {
        foreach (var phrase in SayProcess.SplitPhrases(text))
        {
            token.ThrowIfCancellationRequested();
            int currentVolume; lock (gate) currentVolume = volume;
            await say.SpeakAsync(phrase, currentVolume, token);
        }
    }
    public void Stop() { lock (gate) { try { active?.Cancel(); } catch (ObjectDisposedException) { } } }
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; Stop(); } }
}
