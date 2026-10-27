using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
namespace Dafeiyu
{
    // WinMM accepts raw PCM directly; queued headers remain pinned until WHDR_DONE.
    public sealed class PcmOutput : ILiveAudioOutput, IAudioLevelSource, ILiveAudioFailureSource
    {
        readonly object gate=new object(); readonly Queue<Buffer> buffers=new Queue<Buffer>();
        readonly AutoResetEvent ready=new AutoResetEvent(false); readonly Thread worker;
        readonly Stopwatch progress=new Stopwatch();
        // Live can generate faster than real-time playback. Bound memory separately
        // from the time the output device has actually stopped making progress.
        const int MaximumBufferedSeconds=120,StallSeconds=10;
        IntPtr device; int rate,volume=50,queued; bool disposed; volatile bool stopping;
        string failure;
        public event Action<string> Failed;
        public PcmOutput(){worker=new Thread(Drain){IsBackground=true,Name="DeskPet Live playback"};worker.Start();}
        public double AudioLevel {get{lock(gate){return disposed || buffers.Count==0?0:buffers.Peek().Level*volume/100.0;}}}
        public void Enqueue(byte[] bytes,int sampleRate)
        {
            if(bytes==null || bytes.Length==0 || bytes.Length%2!=0 || (sampleRate!=16000 && sampleRate!=24000 && sampleRate!=48000)) throw new ApiException(TextResources.Get("pcmOutput.enqueue.text"));
            lock(gate)
            {
                if(disposed)return; Collect();
                if(failure!=null)throw new InvalidOperationException(failure);
                if((long)bytes.Length+queued>sampleRate*2*MaximumBufferedSeconds) throw new ApiException(TextResources.Get("pcmOutput.enqueue.text.alternativeA"));
                if(device!=IntPtr.Zero && sampleRate!=rate)
                { if(buffers.Count>0) throw new ApiException(TextResources.Get("pcmOutput.enqueue.text.alternativeB")); Check(waveOutClose(device)); device=IntPtr.Zero; }
                if(device==IntPtr.Zero)
                {
                    var format=new WaveFormat {FormatTag=1,Channels=1,SamplesPerSec=(uint)sampleRate,AvgBytesPerSec=(uint)(sampleRate*2),BlockAlign=2,BitsPerSample=16};
                    uint result=waveOutOpen(out device,UInt32.MaxValue,ref format,ready.SafeWaitHandle.DangerousGetHandle(),IntPtr.Zero,0x50000);
                    if(result!=0){device=IntPtr.Zero;throw new InvalidOperationException(TextResources.Get("pcmOutput.enqueue.text.alternativeC"));}rate=sampleRate;ApplyVolume();
                }
                if(buffers.Count==0)progress.Restart();
                // Short headers let completion measure real playback progress even
                // when the service sends a whole long reply in a single message.
                for(int offset=0;offset<bytes.Length;)
                {
                    int length=Math.Min(sampleRate*2/5,bytes.Length-offset);
                    var buffer=new Buffer(bytes,offset,length,Dafeiyu.AudioLevel.Pcm16(bytes,offset,length));
                    try {Check(waveOutPrepareHeader(device,buffer.Header,HeaderSize));buffer.Prepared=true;Check(waveOutWrite(device,buffer.Header,HeaderSize));buffers.Enqueue(buffer);queued+=length;}
                    catch {if(buffer.Prepared)waveOutUnprepareHeader(device,buffer.Header,HeaderSize);buffer.Dispose();throw;}
                    offset+=length;
                }
            }
        }
        public void SetVolume(int value){lock(gate){volume=Math.Max(0,Math.Min(100,value));if(device!=IntPtr.Zero)ApplyVolume();}}
        void ApplyVolume(){uint channel=(uint)Math.Round(volume*65535.0/100);waveOutSetVolume(device,channel|(channel<<16));}
        void Drain()
        {
            while(!stopping)
            {
                ready.WaitOne(100);string stalled=null;
                lock(gate)
                {
                    if(stopping)return;
                    if(device!=IntPtr.Zero)Collect();
                    if(buffers.Count>0 && progress.Elapsed.TotalSeconds>=StallSeconds)
                        stalled=failure=TextResources.Get("pcmOutput.drain.failure");
                }
                // A failure handler can stop the session and dispose this output;
                // notify off the playback worker so Dispose never joins itself.
                if(stalled!=null){var handler=Failed;if(handler!=null)ThreadPool.QueueUserWorkItem(delegate{handler(stalled);});return;}
            }
        }
        void Collect()
        {
            while(buffers.Count>0)
            {
                Buffer buffer=buffers.Peek();var header=(WaveHeader)Marshal.PtrToStructure(buffer.Header,typeof(WaveHeader));if((header.Flags&1)==0)break;
                if(waveOutUnprepareHeader(device,buffer.Header,HeaderSize)!=0)break;
                buffers.Dequeue();queued-=buffer.Length;buffer.Dispose();progress.Restart();
            }
            if(buffers.Count==0)progress.Reset();
        }
        public void Interrupt(){lock(gate){if(device!=IntPtr.Zero){waveOutReset(device);Collect();}}}
        public void Dispose()
        {
            lock(gate){if(disposed)return;disposed=true;stopping=true;if(device!=IntPtr.Zero)waveOutReset(device);}ready.Set();worker.Join();
            lock(gate){if(device!=IntPtr.Zero){Collect();waveOutClose(device);device=IntPtr.Zero;}while(buffers.Count>0)buffers.Dequeue().Dispose();queued=0;}ready.Dispose();
        }
        static void Check(uint result){if(result!=0)throw new InvalidOperationException(TextResources.Get("pcmOutput.check.text"));}
        static readonly uint HeaderSize=(uint)Marshal.SizeOf(typeof(WaveHeader));
        sealed class Buffer : IDisposable
        {
            public IntPtr Data,Header;public int Length;public bool Prepared;public readonly double Level;
            public Buffer(byte[] bytes,int offset,int length,double level){Level=level;Length=length;Data=Marshal.AllocHGlobal(Length);Header=Marshal.AllocHGlobal((int)HeaderSize);Marshal.Copy(bytes,offset,Data,Length);Marshal.StructureToPtr(new WaveHeader{Data=Data,BufferLength=(uint)Length},Header,false);}
            public void Dispose(){Marshal.FreeHGlobal(Header);Marshal.FreeHGlobal(Data);}
        }
        [StructLayout(LayoutKind.Sequential,Pack=2)] struct WaveFormat{public ushort FormatTag,Channels;public uint SamplesPerSec,AvgBytesPerSec;public ushort BlockAlign,BitsPerSample,Size;}
        [StructLayout(LayoutKind.Sequential)] struct WaveHeader{public IntPtr Data;public uint BufferLength,BytesRecorded;public IntPtr User;public uint Flags,Loops;public IntPtr Next,Reserved;}
        [DllImport("winmm.dll")]static extern uint waveOutOpen(out IntPtr handle,uint deviceId,ref WaveFormat format,IntPtr callback,IntPtr instance,uint flags);
        [DllImport("winmm.dll")]static extern uint waveOutPrepareHeader(IntPtr handle,IntPtr header,uint size);
        [DllImport("winmm.dll")]static extern uint waveOutUnprepareHeader(IntPtr handle,IntPtr header,uint size);
        [DllImport("winmm.dll")]static extern uint waveOutWrite(IntPtr handle,IntPtr header,uint size);
        [DllImport("winmm.dll")]static extern uint waveOutReset(IntPtr handle);
        [DllImport("winmm.dll")]static extern uint waveOutClose(IntPtr handle);
        [DllImport("winmm.dll")]static extern uint waveOutSetVolume(IntPtr handle,uint volume);
    }
}
