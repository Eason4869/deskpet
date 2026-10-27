using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu.ImChannels
{
    public sealed class QqAdapter:ImAdapter
    {
        string access="";long sequence;int heartbeat=30000;DateTimeOffset expires;
        readonly SemaphoreSlim tokenGate=new SemaphoreSlim(1,1);
        public QqAdapter(ImChannelProfile profile):base(profile){}
        async Task<string> Token(CancellationToken token)
        {
            await tokenGate.WaitAsync(token).ConfigureAwait(false);try{if(DateTimeOffset.UtcNow<expires)return access;
                var result=await Request("https://bots.qq.com/app/getAppAccessToken",new{appId=Profile.AppId,clientSecret=Profile.Secret},token).ConfigureAwait(false);
                access=ImProtocol.Str(result,"access_token");if(access.Length==0)throw new InvalidOperationException("im.authFailed");expires=DateTimeOffset.UtcNow.AddSeconds(Math.Max(1,ImProtocol.Num(result,"expires_in",7200)-120));return access;
            }finally{tokenGate.Release();}
        }
        public override async Task RunAsync(Func<ImMessage,bool> receive,Action<string> status,CancellationToken token)
        {
            Receive=receive;Status=status;var auth=await Token(token).ConfigureAwait(false);
            var gateway=await Request("https://api.sgroup.qq.com/gateway",null,token,"QQBot "+auth).ConfigureAwait(false);
            await Connect(ImProtocol.Str(gateway,"url"),token).ConfigureAwait(false);
            await Pump(async bytes=>{
                var frame=ParseBytes(bytes);long op=ImProtocol.Num(frame,"op");var data=ImProtocol.Get(frame,"d");
                if(ImProtocol.Get(frame,"s")!=null)sequence=ImProtocol.Num(frame,"s");
                if(op==10){heartbeat=(int)Math.Max(1000,Math.Min(60000,ImProtocol.Num(data,"heartbeat_interval",30000)));await Send(new{op=2,d=new{token="QQBot "+auth,intents=1<<25,shard=new[]{0,1},properties=new{os="DeskPet",browser="DeskPet",device="DeskPet"}}},token).ConfigureAwait(false);}
                else if(op==0){if(ImProtocol.Str(frame,"t")=="READY"||ImProtocol.Str(frame,"t")=="RESUMED")Status("im.connected");else {var message=Parse(frame);if(message!=null)Receive(message);}}
                else if(op==7||op==9)throw new InvalidOperationException("im.disconnected");
                else if(op==1)await Send(new{op=1,d=sequence},token).ConfigureAwait(false);
            },()=>Send(new{op=1,d=sequence},token),()=>heartbeat,token).ConfigureAwait(false);
        }
        public static ImMessage Parse(object frame)
        {
            string type=ImProtocol.Str(frame,"t");if(type!="C2C_MESSAGE_CREATE"&&type!="GROUP_AT_MESSAGE_CREATE")return null;
            var data=ImProtocol.Get(frame,"d");bool group=type=="GROUP_AT_MESSAGE_CREATE";string sender=ImProtocol.Str(ImProtocol.Get(data,"author"),group?"member_openid":"user_openid");
            return new ImMessage{Id=ImProtocol.Str(data,"id"),Sender=sender,Chat=group?ImProtocol.Str(data,"group_openid"):sender,IsGroup=group,Mentioned=group,Text=ImProtocol.CleanMention(ImProtocol.Str(data,"content")),Unsupported=ImProtocol.Array(data,"attachments").Length>0,Time=ImProtocol.Time(data,"timestamp")};
        }
        public override async Task SendAsync(ImMessage message,string text,CancellationToken token)
        {
            string target=message.IsGroup?"groups/":"users/";int seq=0;
            foreach(var chunk in ImProtocol.Chunks(Limited(text))) {token.ThrowIfCancellationRequested();var result=await Request("https://api.sgroup.qq.com/v2/"+target+Uri.EscapeDataString(message.Chat)+"/messages",new{content=chunk,msg_type=0,msg_id=message.Id,msg_seq=++seq},token,"QQBot "+await Token(token).ConfigureAwait(false)).ConfigureAwait(false);Success(result);}
        }
        public override void Dispose(){base.Dispose();tokenGate.Dispose();}
    }
}
