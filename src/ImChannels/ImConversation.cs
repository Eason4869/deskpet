using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu.ImChannels
{
    internal sealed class ImConversation:IDisposable
    {
        readonly ConversationSession session;
        internal int RetainedCharacters {get{return session.History.Messages.Sum(x=>x.Content.Length+(x.Reasoning??"").Length);}}
        void Trim(int messages,int characters)
        {
            var paired=session.History.ForRequest("","");paired.RemoveAt(paired.Count-1);
            foreach(var message in paired)message.SetReasoning("",0);
            while(paired.Count>0&&(paired.Count>messages||paired.Sum(x=>(long)x.Content.Length)>characters))paired.RemoveRange(0,2);
            session.History.Replace(paired);
        }
        internal ImConversation(Dafeiyu.Maintenance.DataMaintenanceGate maintenance,IConversationApi api=null)
        {
            session=new ConversationSession(api??new ApiClient(),new SilentSpeech(),()=>{throw new InvalidOperationException();},null,()=>false,maintenance:maintenance);
        }
        internal async Task<string> ReplyAsync(ImChannelProfile profile,PetSettings settings,ImMessage message,CancellationToken token)
        {
            var snapshot=settings.Copy();snapshot.AgentEnabled=false;snapshot.ReadReplies=false;snapshot.SaveChatHistory=false;snapshot.ShowPredictedMessages=false;snapshot.SystemPrompt=profile.Persona;snapshot.Text.StreamReplies=false;
            // A channel has no project, MCP connection, approval provider, microphone or desktop memory.
            session.SelectPersona(profile.PetId);
            if(message.Text=="/new"){session.Clear();return ImText.Get("im.contextCleared");}
            Trim(38,56000);
            token.ThrowIfCancellationRequested();var request=session.SendTextAsync(message.Text,snapshot,profile.PetName);
            using(token.Register(()=>{var ignored=session.StopAsync();})){
                try{await request.ConfigureAwait(false);token.ThrowIfCancellationRequested();
                    var reply=session.History.Messages.LastOrDefault(x=>x.Role=="assistant"&&x.Status=="complete");if(reply==null)throw new InvalidOperationException("im.modelFailed");return reply.Content;
                }finally{Trim(40,64000);}
            }
        }
        public void Dispose(){session.Dispose();}
        sealed class SilentSpeech:IReplySpeech
        {
            public Task SpeakAsync(string reply,PetSettings settings,CancellationToken cancel){return Task.FromResult(0);}
            public void SetVolume(int value){}public void Stop(){}public void Dispose(){}
        }
    }
}
