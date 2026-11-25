using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public static class WaveData
    {
        public static byte[] EncodePcm(byte[] pcm)
        {
            if (pcm == null || pcm.Length % 2 != 0) throw new ArgumentException("16-bit PCM requires complete samples.");
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + pcm.Length); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000);
                writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(pcm.Length); writer.Write(pcm);
                return stream.ToArray();
            }
        }
        public static bool IsWave(byte[] bytes)
        {
            return bytes != null && bytes.Length >= 44 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WAVE";
        }
    }

    public interface IAudioRecorder : IDisposable
    {
        bool IsRecording { get; }
        void Start();
        Task<byte[]> FinishAsync();
    }
    public interface IPcmMicrophone : IDisposable
    {
        event Action<byte[]> PcmAvailable;
        event Action<string> Failed;
        void Start();
    }
    public interface ILiveAudioOutput : IDisposable
    {
        void Enqueue(byte[] bytes,int rate);
        void Interrupt();
        void SetVolume(int value);
    }
    public interface IGuardedLiveAudioOutput : ILiveAudioOutput
    {
        // The predicate is checked again after lazy device initialization, just before submission.
        bool TryEnqueue(byte[] bytes,int rate,Func<bool> permit);
        bool HasPendingAudio { get; }
    }
    public interface ILiveAudioFailureSource
    {
        event Action<string> Failed;
    }
    public interface IReplySpeech : IDisposable
    { Task SpeakAsync(string reply,PetSettings settings,CancellationToken cancel);void SetVolume(int volume);void Stop(); }
}
