using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    internal static class AgentJobTestSupport
    {
        internal sealed class TemporaryDirectory : IDisposable
        {
            readonly string parent;
            internal string Path { get; private set; }
            internal TemporaryDirectory()
            {
                parent=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"agent-job-fixtures"));
                Path=System.IO.Path.Combine(parent,"task 中文 "+Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }
            public void Dispose()
            {
                string full=System.IO.Path.GetFullPath(Path);
                if(!full.StartsWith(parent+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Fixture escaped its test root");
                if(Directory.Exists(full))Directory.Delete(full,true);
            }
        }
        internal sealed class OfflineApi : IConversationApi
        {
            public Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,CancellationToken token){throw new InvalidOperationException("Unexpected model call in job fixture");}
            public Task<string> TranscribeAsync(ConnectionSettings settings,byte[] wave,CancellationToken token){throw new InvalidOperationException("Unexpected transcription call in job fixture");}
            public Task<byte[]> SpeechAsync(ConnectionSettings settings,string voice,string text,CancellationToken token){throw new InvalidOperationException("Unexpected speech call in job fixture");}
            public void Dispose(){}
        }
        internal sealed class SilentSpeech : IReplySpeech
        {
            public Task SpeakAsync(string text,PetSettings settings,CancellationToken token){return Task.FromResult(0);}
            public void SetVolume(int value){} public void Stop(){} public void Dispose(){}
        }
    }
}
