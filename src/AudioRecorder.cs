using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu
{
    public sealed class AudioRecorder : IAudioRecorder,IPcmMicrophone
    {
        private const int BufferBytes = 8192;
        private const int MaximumPcm = 60 * 32000;
        private IntPtr device;
        private readonly AutoResetEvent ready = new AutoResetEvent(false);
        private readonly List<Buffer> buffers = new List<Buffer>();
        private readonly MemoryStream pcm = new MemoryStream();
        private Thread worker;
        private int nextBufferIndex;
        private volatile bool stopping;
        private bool finished;
        private bool disposed;
        private readonly object lifecycle = new object(), deviceGate = new object();
        private Exception workerError;
        private readonly bool streaming;
        public event Action<byte[]> PcmAvailable;
        public event Action<string> Failed;
        public AudioRecorder() : this(false) { }
        public AudioRecorder(bool continuous) { streaming=continuous; }
        public bool IsRecording { get { return device != IntPtr.Zero && !stopping; } }

        public void Start()
        {
            lock(lifecycle)
            {
            if (device != IntPtr.Zero || finished) throw new InvalidOperationException("Recorder is already used.");
            var format = new WaveFormat { FormatTag = 1, Channels = 1, SamplesPerSec = 16000, AvgBytesPerSec = 32000, BlockAlign = 2, BitsPerSample = 16, Size = 0 };
            uint result = waveInOpen(out device, UInt32.MaxValue, ref format, ready.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, 0x50000);
            if (result != 0) { device = IntPtr.Zero; throw new InvalidOperationException(TextResources.Get("audioRecorder.start.text")); }
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    var buffer = new Buffer(streaming ? 2048 : BufferBytes); buffers.Add(buffer);
                    Check(waveInPrepareHeader(device, buffer.Header, HeaderSize)); buffer.Prepared = true;
                    Check(waveInAddBuffer(device, buffer.Header, HeaderSize));
                }
                stopping = false; worker = new Thread(Drain) { IsBackground = true, Name = "Dafeiyu microphone" }; worker.Start();
                Check(waveInStart(device));
            }
            catch { Release(); throw; }
            }
        }
        private void Drain()
        {
            try
            {
                while (!stopping) { ready.WaitOne(250); DrainReady(true); }
                DrainReady(false);
            }
            catch (Exception e)
            {
                bool report;
                lock(deviceGate)
                {
                    // Stop/reset can race with collection. Only an unexpected
                    // failure while actively capturing is a recording error.
                    report=!stopping;
                    if(report){workerError=e;stopping=true;}
                }
                var failed=Failed;
                if(report && failed!=null)ThreadPool.QueueUserWorkItem(delegate {
                    failed(streaming?TextResources.Get("audioRecorder.drain.failed"):TextResources.Get("audioRecorder.drain.failed.alternativeA"));
                });
            }
        }
        private void DrainReady(bool requeue)
        {
            for(int visited=0;visited<buffers.Count;visited++)
            {
                Buffer buffer=buffers[nextBufferIndex];
                var header = (WaveHeader)Marshal.PtrToStructure(buffer.Header, typeof(WaveHeader));
                if ((header.Flags & 1) == 0 || buffer.Collected) break;
                int count = streaming ? (int)header.BytesRecorded : Math.Min((int)header.BytesRecorded, MaximumPcm - (int)pcm.Length);
                if (count > 0) { byte[] bytes = new byte[count]; Marshal.Copy(buffer.Data, bytes, 0, count); if(!streaming)pcm.Write(bytes,0,bytes.Length);if(!stopping && PcmAvailable!=null)PcmAvailable(bytes); }
                buffer.Collected = true;
                nextBufferIndex=(nextBufferIndex+1)%buffers.Count;
                lock(deviceGate)
                {
                    if (requeue && !stopping && (streaming || pcm.Length < MaximumPcm))
                    { Check(waveInAddBuffer(device, buffer.Header, HeaderSize)); buffer.Collected=false; }
                }
            }
        }
        public Task<byte[]> FinishAsync() { return Task.Run((Func<byte[]>)Finish); }
        private byte[] Finish()
        {
            lock(lifecycle)
            {
            if(disposed) throw new InvalidOperationException(TextResources.Get("audioRecorder.finish.text"));
            Release();
            if (workerError != null) throw new InvalidOperationException(TextResources.Get("audioRecorder.finish.text.alternativeA"));
            byte[] bytes = pcm.ToArray();
            if (bytes.Length < 12800) throw new InvalidOperationException(TextResources.Get("audioRecorder.finish.text.alternativeB"));
            return WaveData.EncodePcm(bytes);
            }
        }
        private void Release()
        {
            lock(lifecycle)
            {
            if (finished) return;
            lock(deviceGate)
            { stopping = true; if (device != IntPtr.Zero) { waveInStop(device); waveInReset(device); } }
            ready.Set();
            if (worker != null && worker.IsAlive) worker.Join();
            if (device != IntPtr.Zero)
            {
                foreach (Buffer buffer in buffers) if (buffer.Prepared) waveInUnprepareHeader(device, buffer.Header, HeaderSize);
                waveInClose(device); device = IntPtr.Zero;
            }
            foreach (Buffer buffer in buffers) buffer.Dispose(); buffers.Clear(); finished = true;
            }
        }
        public void Dispose() { lock(lifecycle) { if(disposed) return; Release(); ready.Dispose(); pcm.Dispose(); disposed=true; } }
        private static void Check(uint code) { if (code != 0) throw new InvalidOperationException(TextResources.Get("audioRecorder.check.text")); }
        private static readonly uint HeaderSize = (uint)Marshal.SizeOf(typeof(WaveHeader));
        private sealed class Buffer : IDisposable
        {
            public IntPtr Data;
            public IntPtr Header = Marshal.AllocHGlobal((int)HeaderSize);
            public bool Prepared;
            public bool Collected;
            public Buffer() : this(BufferBytes) { }
            public Buffer(int size) { Data=Marshal.AllocHGlobal(size); Marshal.StructureToPtr(new WaveHeader { Data = Data, BufferLength = (uint)size }, Header, false); }
            public void Dispose() { Marshal.FreeHGlobal(Header); Marshal.FreeHGlobal(Data); }
        }
        [StructLayout(LayoutKind.Sequential, Pack = 2)] private struct WaveFormat
        {
            public ushort FormatTag, Channels; public uint SamplesPerSec, AvgBytesPerSec; public ushort BlockAlign, BitsPerSample, Size;
        }
        [StructLayout(LayoutKind.Sequential)] private struct WaveHeader
        {
            public IntPtr Data; public uint BufferLength, BytesRecorded; public IntPtr User; public uint Flags, Loops; public IntPtr Next, Reserved;
        }
        [DllImport("winmm.dll")] private static extern uint waveInOpen(out IntPtr handle, uint deviceId, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] private static extern uint waveInPrepareHeader(IntPtr handle, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveInUnprepareHeader(IntPtr handle, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveInAddBuffer(IntPtr handle, IntPtr header, uint size);
        [DllImport("winmm.dll")] private static extern uint waveInStart(IntPtr handle);
        [DllImport("winmm.dll")] private static extern uint waveInStop(IntPtr handle);
        [DllImport("winmm.dll")] private static extern uint waveInReset(IntPtr handle);
        [DllImport("winmm.dll")] private static extern uint waveInClose(IntPtr handle);
    }
}
