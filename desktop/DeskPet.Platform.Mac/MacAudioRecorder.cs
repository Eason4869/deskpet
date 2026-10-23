using System.Diagnostics;
using Dafeiyu;

namespace DeskPet.Platform.Mac;

internal sealed class MacAudioRecorder(bool streaming, IMacMicrophone? capture = null) : IAudioRecorder, IPcmMicrophone
{
    private readonly IMacMicrophone capture = capture ?? new NativeMacMicrophone();
    private readonly object lifecycle = new();
    private readonly MemoryStream pcm = new();
    private IntPtr device;
    private Thread? worker;
    private volatile bool stopping;
    private bool started, finished, disposed;
    private Exception? failure;
    public event Action<byte[]>? PcmAvailable;
    public event Action<string>? Failed;
    public bool IsRecording => device != IntPtr.Zero && !stopping;

    public void Start()
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (started || finished || device != IntPtr.Zero) throw new InvalidOperationException(TextResources.Get("macAudioRecorder.start.text"));
            if (capture.Access != MicrophonePermission.Authorized)
                throw new InvalidOperationException(TextResources.Get("macAudioRecorder.start.text.alternativeA"));
            device = capture.Start(out var error);
            if (device == IntPtr.Zero) throw new InvalidOperationException(TextResources.Format("macAudioRecorder.start.template",error));
            started = true;
            worker = new Thread(Drain) { IsBackground = true, Name = "DeskPet Mac microphone" };
            worker.Start();
        }
    }
    private void Drain()
    {
        var buffer = new byte[8192];
        var progress = Stopwatch.StartNew();
        try
        {
            while (!stopping)
            {
                var count = capture.Read(device, buffer);
                if (count < 0) throw new InvalidOperationException(TextResources.Get("macAudioRecorder.drain.text"));
                if (count > 0)
                {
                    progress.Restart();
                    if (!streaming) count = Math.Min(count, 60 * 32000 - (int)pcm.Length);
                    if (count > 0)
                    {
                        var bytes = buffer.AsSpan(0, count).ToArray();
                        if (!streaming) pcm.Write(bytes);
                        PcmAvailable?.Invoke(bytes);
                    }
                    if (stopping) break; // A consumer may dispose synchronously in PcmAvailable.
                    if (!streaming && pcm.Length >= 60 * 32000) break;
                }
                else if (progress.Elapsed > TimeSpan.FromSeconds(5)) throw new InvalidOperationException(TextResources.Get("macAudioRecorder.drain.text.alternativeA"));
                Thread.Sleep(20);
            }
        }
        catch (Exception ex)
        {
            failure = ex;
            var handler = Failed;
            if (handler != null) ThreadPool.QueueUserWorkItem(_ => handler(TextResources.Get("macAudioRecorder.drain.handler")));
        }
        finally
        {
            stopping = true;
            // The capture worker owns the handle until joined, including failure paths.
            // Device disappearance must release AVAudioEngine even before the UI reacts.
            if (device != IntPtr.Zero) { capture.Stop(device); device = IntPtr.Zero; }
        }
    }
    public Task<byte[]> FinishAsync() => Task.Run(() =>
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Release();
            if (failure != null) throw new InvalidOperationException(TextResources.Get("macAudioRecorder.finishAsync.taskRun"), failure);
            var bytes = pcm.ToArray();
            if (bytes.Length < 12800) throw new InvalidOperationException(TextResources.Get("macAudioRecorder.finishAsync.taskRun.alternativeA"));
            return WaveData.EncodePcm(bytes);
        }
    });
    private void Release()
    {
        if (finished) return;
        stopping = true;
        if (worker != Thread.CurrentThread) worker?.Join();
        if (device != IntPtr.Zero) { capture.Stop(device); device = IntPtr.Zero; }
        finished = true;
    }
    public void Dispose() { lock (lifecycle) { if (disposed) return; Release(); pcm.Dispose(); disposed = true; } }
}
