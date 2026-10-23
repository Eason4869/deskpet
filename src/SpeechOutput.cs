using System;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
namespace Dafeiyu
{
    public sealed class SpeechOutput : IReplySpeech
    {
        private readonly ApiClient api;
        private readonly string cacheDirectory;
        private readonly Dispatcher dispatcher;
        private SpeechSynthesizer activeVoice;
        private MediaPlayer activePlayer;
        private CancellationTokenSource active;
        private volatile int currentVolume=50;
        private bool disposed;
        public SpeechOutput(ApiClient client,string directory,Dispatcher uiDispatcher){api=client;cacheDirectory=directory;dispatcher=uiDispatcher;}
        public void SetVolume(int volume)
        {
            currentVolume=Math.Max(0,Math.Min(100,volume));
            if(!dispatcher.CheckAccess()){if(!dispatcher.HasShutdownStarted)dispatcher.BeginInvoke(new Action(delegate{SetVolume(currentVolume);}));return;}
            if(activeVoice!=null)activeVoice.Volume=currentVolume;if(activePlayer!=null)activePlayer.Volume=currentVolume/100.0;
        }
        public Task SpeakAsync(string reply,PetSettings settings,CancellationToken cancel)
        {
            if(!dispatcher.CheckAccess() || !(SynchronizationContext.Current is DispatcherSynchronizationContext))
                return dispatcher.InvokeAsync(new Func<Task>(delegate {return SpeakOnUiAsync(reply,settings,cancel);})).Task.Unwrap();
            return SpeakOnUiAsync(reply,settings,cancel);
        }
        private async Task SpeakOnUiAsync(string reply,PetSettings settings,CancellationToken cancel)
        {
            if(disposed)throw new ObjectDisposedException("SpeechOutput");if(active!=null)throw new InvalidOperationException(TextResources.Get("speechOutput.speakOnUiAsync.text"));
            currentVolume=Math.Max(0,Math.Min(100,settings.Volume));
            string text=Regex.Replace(reply??"","```[\\s\\S]*?```",TextResources.Get("speech.codeOmitted"));text=Regex.Replace(text,"[\\*#`]|https?://\\S+","").Trim();
            if(text.Length>1800)text=TextResources.Format("speechOutput.speakOnUiAsync.textTextSubstring",text.Substring(0,1800));if(text.Length==0 || settings.Volume==0)return;
            using(var reading=CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                active=reading;reading.CancelAfter(TimeSpan.FromMinutes(4));
                try
                {
                    if(settings.UseCloudVoice){var wav=await api.SpeechAsync(settings.SpeechConnection(),settings.VoiceName,text,reading.Token);await PlayWave(wav,currentVolume,reading.Token);}
                    else await SpeakSystem(text,currentVolume,reading.Token);
                }
                finally {if(Object.ReferenceEquals(active,reading))active=null;}
            }
        }
        private void CancelOnUi(CancellationToken token,TaskCompletionSource<bool> completion,Action stop)
        {
            completion.TrySetCanceled();
            if(!dispatcher.HasShutdownStarted)dispatcher.BeginInvoke(new Action(delegate {if(token.IsCancellationRequested)stop();}));
        }
        private async Task SpeakSystem(string text,int volume,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();SpeechSynthesizer voice;
            try{voice=new SpeechSynthesizer();}
            catch(Exception e){if(!(e is InvalidOperationException || e is System.Runtime.InteropServices.COMException))throw;throw new InvalidOperationException(TextResources.Get("speechOutput.speakSystem.text"));}
            activeVoice=voice;var completion=new TaskCompletionSource<bool>();
            EventHandler<SpeakCompletedEventArgs> done=delegate(object sender,SpeakCompletedEventArgs e){if(e.Cancelled)completion.TrySetCanceled();else if(e.Error!=null)completion.TrySetException(new InvalidOperationException(TextResources.Get("speechOutput.speakSystem.completionTrySetException")));else completion.TrySetResult(true);};
            voice.SpeakCompleted+=done;
            try
            {
                var chinese=voice.GetInstalledVoices().FirstOrDefault(v=>v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName=="zh");if(chinese!=null)voice.SelectVoice(chinese.VoiceInfo.Name);
                voice.SetOutputToDefaultAudioDevice();voice.Volume=Math.Max(0,Math.Min(100,volume));
                using(token.Register(delegate {CancelOnUi(token,completion,delegate {if(Object.ReferenceEquals(activeVoice,voice))voice.SpeakAsyncCancelAll();});}))
                {token.ThrowIfCancellationRequested();voice.SpeakAsync(text);await completion.Task;}
            }
            finally
            {if(Object.ReferenceEquals(activeVoice,voice))activeVoice=null;voice.SpeakCompleted-=done;voice.SpeakAsyncCancelAll();voice.Dispose();}
        }
        private async Task PlayWave(byte[] bytes,int volume,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();Directory.CreateDirectory(cacheDirectory);string file=Path.Combine(cacheDirectory,"reply-"+Guid.NewGuid().ToString("N")+".wav");File.WriteAllBytes(file,bytes);
            var player=new MediaPlayer();activePlayer=player;player.Volume=Math.Max(0,Math.Min(100,volume))/100.0;var completion=new TaskCompletionSource<bool>();
            player.MediaOpened+=delegate {if(!token.IsCancellationRequested && Object.ReferenceEquals(activePlayer,player))player.Play();};
            player.MediaEnded+=delegate {completion.TrySetResult(true);};player.MediaFailed+=delegate {completion.TrySetException(new InvalidOperationException(TextResources.Get("speechOutput.playWave.completionTrySetException")));};
            try
            {
                using(token.Register(delegate {CancelOnUi(token,completion,delegate {if(Object.ReferenceEquals(activePlayer,player))player.Stop();});}))
                {token.ThrowIfCancellationRequested();player.Open(new Uri(file));await completion.Task;}
            }
            finally
            {if(Object.ReferenceEquals(activePlayer,player))activePlayer=null;player.Close();try{File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}}
        }
        public void Stop(){if(active!=null)try{active.Cancel();}catch(ObjectDisposedException){} }
        public void Dispose(){if(disposed)return;disposed=true;Stop();}
    }
}
