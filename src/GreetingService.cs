using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu
{
    // Shared entry point for companion context. Today memory is completed chat
    // history; a future memory store can extend this without changing greetings.
    public sealed class CompanionMemory
    {
        readonly Func<ConversationSession> conversation;
        public CompanionMemory(Func<ConversationSession> source){conversation=source;}
        public IList<ChatMessage> Capture(string companionId)
        {
            var session=conversation();
            return session!=null && session.PersonaId==companionId ? Recent(session.History.Messages) : new List<ChatMessage>();
        }
        public static IList<ChatMessage> Recent(IList<ChatMessage> history)
        {
            var result=new List<ChatMessage>();if(history==null)return result;
            // Only completed user/assistant pairs; never expose pending replies.
            for(int i=0;i+1<history.Count;i++) {
                var user=history[i];var reply=history[i+1];
                if(user==null || reply==null || user.Role!="user" || reply.Role!="assistant" || user.TurnId!=reply.TurnId || user.Status!="complete" || reply.Status!="complete")continue;
                result.Add(new ChatMessage("user",Limit(user.Content,600),user.TurnId,"complete",user.Source));result.Add(new ChatMessage("assistant",Limit(reply.Content,600),reply.TurnId,"complete",reply.Source));i++;
            }
            while(result.Count>8)result.RemoveRange(0,2);
            return result.AsReadOnly();
        }
        static string Limit(string value,int count){value=value??"";return value.Length<=count?value:value.Substring(0,count);}
    }

    public sealed class GreetingService
    {
        readonly Func<IConversationApi> factory;
        readonly TimeSpan timeout;
        public GreetingService():this(()=>new ApiClient(TimeSpan.FromSeconds(8)),TimeSpan.FromSeconds(8)){}
        public GreetingService(Func<IConversationApi> transport,TimeSpan deadline){factory=transport;timeout=deadline;}
        public static string Key(string text){return new string((text??"").Where(c=>!Char.IsWhiteSpace(c) && !Char.IsPunctuation(c)).ToArray());}
        public static string Fallback(string id,string name,DateTime time,int start,IList<string> recent)
        {return Fallback(id,name,time,start,recent,null);}
        public static string Fallback(string id,string name,DateTime time,int start,IList<string> recent,HolidayGreetingContext holiday)
        {return Fallback(id,name,time,start,recent,holiday,null);}
        public static string Fallback(string id,string name,DateTime time,int start,IList<string> recent,HolidayGreetingContext holiday,string stageWelcome)
        {
            string special=holiday!=null && holiday.Date==time.Date ? HolidayGreetingPhrases.Choose(holiday,start,recent):null;
            if(special!=null)return special;
            if(!String.IsNullOrWhiteSpace(stageWelcome))return stageWelcome;
            for(int offset=0;offset<GreetingPhrases.Count;offset++) {
                string line=GreetingPhrases.Choose(id,name,time,start+offset);
                if(recent==null || !recent.Any(s=>Key(s)==Key(line)))return line;
            }
            return GreetingPhrases.Choose(id,name,time,start);
        }
        public async Task<string> GenerateAsync(PetSettings settings,string id,string name,IList<ChatMessage> memory,DateTime time,int phrase,IList<string> recent,CancellationToken cancel)
        {return await GenerateAsync(settings,id,name,memory,time,phrase,recent,cancel,null).ConfigureAwait(false);}
        public async Task<string> GenerateAsync(PetSettings settings,string id,string name,IList<ChatMessage> memory,DateTime time,int phrase,IList<string> recent,CancellationToken cancel,HolidayGreetingContext holiday)
        {return await GenerateAsync(settings,id,name,memory,time,phrase,recent,cancel,holiday,null,null).ConfigureAwait(false);}
        public async Task<string> GenerateAsync(PetSettings settings,string id,string name,IList<ChatMessage> memory,DateTime time,int phrase,IList<string> recent,CancellationToken cancel,HolidayGreetingContext holiday,string stageWelcome,string stageContext)
        {
            using(UsageTracking.BeginScope("greeting"))
            {
            cancel.ThrowIfCancellationRequested();
            var snapshot=settings.Copy();var previous=recent==null?new List<string>():recent.ToList();
            string fallback=Fallback(id,name,time,phrase,previous,holiday,stageWelcome);
            if(!snapshot.AiGreetingEnabled)return fallback;
            try {ApiClient.ValidateConnection(snapshot.Text,ApiClient.ChatPath(snapshot.Text));}
            catch(ApiException){return fallback;}
            var context=new StringBuilder();
            foreach(var message in CompanionMemory.Recent(memory)) {
                if(message==null || (message.Role!="user" && message.Role!="assistant") || message.Status!="complete")continue;
                context.Append(message.Role=="user"?"用户：":"伙伴：").AppendLine(message.Content);
            }
            string persona=PersonaProfiles.LocalizeDefaultPrompt(snapshot.SystemPrompt??"").Replace("{pet_name}",name??TextResources.Get("app.displayName"));
            var messages=new List<ChatMessage> {
                new ChatMessage("system",ReplyLanguagePolicy.Apply(persona+"\n"+TextResources.Get("greetingService.instructions.greeting"))),
                new ChatMessage("user","当前本地时间："+time.ToString("HH:mm")+HolidayGreetingPhrases.ContextText(holiday,time)+(String.IsNullOrEmpty(stageContext)?"":"\n"+stageContext)+"\n最近聊天背景：\n"+(context.Length==0?"（暂无）":context.ToString())+"\n最近已显示的问候（避免重复）：\n"+String.Join("\n",previous)+"\n请以当前伙伴的语气写一句新的主动问候。")
            };
            using(var bounded=CancellationTokenSource.CreateLinkedTokenSource(cancel))
            using(var api=factory()) {
                try {
                    var pending=api.ChatAsync(snapshot.Text,messages,bounded.Token);
                    if(await Task.WhenAny(pending,Task.Delay(timeout,cancel)).ConfigureAwait(false)!=pending) {
                        bounded.Cancel();Observe(pending);cancel.ThrowIfCancellationRequested();return fallback;
                    }
                    string reply=(await pending.ConfigureAwait(false)??"").Trim().Trim('"','“','”');
                    cancel.ThrowIfCancellationRequested();
                    if(reply.Length==0 || reply.Length>80 || reply.IndexOfAny(new[]{'\r','\n','`'})>=0 || reply.Any(Char.IsControl) || previous.Any(s=>Key(s)==Key(reply)))return fallback;
                    return reply;
                }
                catch(OperationCanceledException){cancel.ThrowIfCancellationRequested();return fallback;}
                catch(Exception error) {
                    if(!(error is ApiException || error is HttpRequestException || error is IOException || error is ObjectDisposedException || error is InvalidOperationException))throw;
                    cancel.ThrowIfCancellationRequested();return fallback;
                }
            }

            }
        }
        static void Observe(Task task){task.ContinueWith(t=>{var observed=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted,TaskScheduler.Default);}
    }
}
