using System.Diagnostics;
using Dafeiyu;

namespace DeskPet.Platform.Mac;

internal sealed class MacPcmOutput : ILiveAudioOutput, IAudioLevelSource, ILiveAudioFailureSource
{
    private readonly object gate = new();
    private readonly CancellationTokenSource stop = new();
    private readonly Task monitor;
    private readonly Stopwatch progress = new();
    private IntPtr device;
    private int rate, volume = 50;
    private long previousCompleted;
    private double level;
    private bool disposed;
    private string? failure;
    public event Action<string>? Failed;
    internal MacPcmOutput() { MacServices.EnsureMac(); monitor = Task.Run(Monitor); }
    public double AudioLevel { get { lock (gate) return device == IntPtr.Zero || Native.OutputPending(device) <= 0 ? 0 : level * volume / 100.0; } }
    public void Enqueue(byte[] bytes, int sampleRate)
    {
        if (bytes == null || bytes.Length == 0 || bytes.Length % 2 != 0 || sampleRate is not (16000 or 24000 or 48000)) throw new ArgumentException(TextResources.Get("macPcmOutput.enqueue.text"));
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (failure != null) throw new InvalidOperationException(failure);
            var pending = device == IntPtr.Zero ? 0 : Native.OutputPending(device);
            if (pending < 0) throw new InvalidOperationException(TextResources.Get("macPcmOutput.enqueue.text.alternativeA"));
            if (pending + bytes.Length > sampleRate * 2L * 120) throw new InvalidOperationException(TextResources.Get("macPcmOutput.enqueue.text.alternativeB"));
            if (device != IntPtr.Zero && rate != sampleRate)
            {
                if (pending > 0) throw new InvalidOperationException(TextResources.Get("macPcmOutput.enqueue.text.alternativeC"));
                Native.OutputStop(device); device = IntPtr.Zero;
            }
            if (device == IntPtr.Zero)
            {
                device = Native.OutputStart(sampleRate, out var error);
                if (device == IntPtr.Zero) throw new InvalidOperationException(TextResources.Format("macPcmOutput.enqueue.template",error));
                rate = sampleRate; Native.OutputVolume(device, volume / 100f);
                previousCompleted = 0;
            }
            if (pending == 0) progress.Restart();
            level = Dafeiyu.AudioLevel.Pcm16(bytes);
            // Bound each scheduled buffer so the monitor observes actual playback progress.
            for (var offset = 0; offset < bytes.Length;)
            {
                var chunk = bytes.AsSpan(offset, Math.Min(sampleRate * 2 / 5, bytes.Length - offset)).ToArray();
                if (Native.OutputEnqueue(device, chunk, chunk.Length) != 0) throw new InvalidOperationException(TextResources.Get("macPcmOutput.enqueue.text.alternativeD"));
                offset += chunk.Length;
            }
        }
    }
    private async Task Monitor()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(100, stop.Token);
                string? error = null;
                lock (gate)
                {
                    if (disposed) return;
                    if (device == IntPtr.Zero) continue;
                    var pending = Native.OutputPending(device);
                    var completed = Native.OutputCompleted(device);
                    if (pending < 0) error = TextResources.Get("macPcmOutput.monitor.error");
                    else if (pending == 0) progress.Reset();
                    else if (completed > previousCompleted) progress.Restart();
                    else if (progress.Elapsed > TimeSpan.FromSeconds(10)) error = TextResources.Get("macPcmOutput.monitor.error.alternativeA");
                    previousCompleted = completed;
                    if (error != null) { failure = error; Native.OutputStop(device); device = IntPtr.Zero; level = 0; }
                }
                if (error != null) { var handler = Failed; if (handler != null) ThreadPool.QueueUserWorkItem(_ => handler(error)); return; }
            }
        }
        catch (OperationCanceledException) { }
    }
    public void SetVolume(int value) { lock (gate) { volume = Math.Clamp(value, 0, 100); if (device != IntPtr.Zero) Native.OutputVolume(device, volume / 100f); } }
    public void Interrupt() { lock (gate) { if (device != IntPtr.Zero && Native.OutputInterrupt(device) != 0) throw new InvalidOperationException(TextResources.Get("macPcmOutput.interrupt.text")); progress.Reset(); level = 0; } }
    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; stop.Cancel(); if (device != IntPtr.Zero) { Native.OutputStop(device); device = IntPtr.Zero; } }
        monitor.GetAwaiter().GetResult(); stop.Dispose();
    }
}
