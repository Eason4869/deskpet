using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu.ImChannels
{
    public sealed class FeishuAdapter:ImAdapter
    {
        string access="",bot="";int service,heartbeat=90000;readonly FeishuFragments fragments=new FeishuFragments();DateTimeOffset expires;
        readonly SemaphoreSlim tokenGate=new SemaphoreSlim(1,1);
        public FeishuAdapter(ImChannelProfile profile):base(profile){}
        internal FeishuAdapter(ImChannelProfile profile,System.Net.Http.HttpMessageHandler handler):base(profile,handler){}
        async Task<string> Token(CancellationToken token)
        {
            await tokenGate.WaitAsync(token).ConfigureAwait(false);try{
                if(DateTimeOffset.UtcNow<expires)return access;
                var auth=await Request("https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal",new{app_id=Profile.AppId,app_secret=Profile.Secret},token).ConfigureAwait(false);long code=ImProtocol.Num(auth,"code");if(code==10005||code==10014||code==10015)throw new InvalidOperationException("im.authFailed");Success(auth);access=ImProtocol.Str(auth,"tenant_access_token");if(access.Length==0)throw new InvalidOperationException("im.authFailed");expires=DateTimeOffset.UtcNow.AddSeconds(Math.Max(1,ImProtocol.Num(auth,"expire",7200)-120));return access;
            }finally{tokenGate.Release();}
        }
        public override async Task RunAsync(Func<ImMessage,bool> receive,Action<string> status,CancellationToken token)
        {
            Receive=receive;Status=status;
            var auth=await Token(token).ConfigureAwait(false);
            var info=await Request("https://open.feishu.cn/open-apis/bot/v3/info",null,token,"Bearer "+auth).ConfigureAwait(false);Success(info);bot=ImProtocol.Str(ImProtocol.Get(info,"bot"),"open_id");if(bot.Length==0)throw new InvalidOperationException("im.authFailed");
            var discovery=await Request("https://open.feishu.cn/callback/ws/endpoint",new{AppID=Profile.AppId,AppSecret=Profile.Secret},token).ConfigureAwait(false);Success(discovery);
            var data=ImProtocol.Get(discovery,"data");string url=ImProtocol.Str(data,"URL");var uri=ImProtocol.Endpoint(url,"feishu");
            foreach(string part in uri.Query.TrimStart('?').Split('&')){var pair=part.Split('=');if(pair.Length==2&&pair[0]=="service_id")Int32.TryParse(pair[1],out service);}
            heartbeat=(int)Math.Max(1000,Math.Min(90000,ImProtocol.Num(ImProtocol.Get(data,"ClientConfig"),"PingInterval",30)*1000));
            await Connect(url,token).ConfigureAwait(false);Status("im.connected");
            await Pump(async bytes=>{
                var frame=FeishuFrame.Decode(bytes);heartbeat=PingInterval(frame,heartbeat);if(frame.Method!=1||frame.Header("type")!="event")return;
                if(frame.Encoding.Length>0&&frame.Encoding!="json")throw new InvalidOperationException("im.protocolFailed");var payload=fragments.Add(frame);if(payload==null)return;
                var message=Parse(ImProtocol.Json(Encoding.UTF8.GetString(payload)),bot);if(message!=null)Receive(message);
                frame.Payload=Encoding.UTF8.GetBytes("{\"code\":200}");await SendBytes(frame.Encode(),System.Net.WebSockets.WebSocketMessageType.Binary,token).ConfigureAwait(false);
            },()=>{var ping=new FeishuFrame{Service=service};ping.Headers["type"]="ping";return SendBytes(ping.Encode(),System.Net.WebSockets.WebSocketMessageType.Binary,token);},()=>heartbeat,token).ConfigureAwait(false);
        }
        public static int PingInterval(FeishuFrame frame,int current)
        {
            if(frame.Method!=0||frame.Header("type")!="pong"||frame.Payload.Length==0)return current;
            try{long seconds=ImProtocol.Num(ImProtocol.Json(Encoding.UTF8.GetString(frame.Payload)),"PingInterval");return seconds>=1&&seconds<=90?(int)seconds*1000:current;}catch(ArgumentException){return current;}
        }
        public static ImMessage Parse(object envelope,string botId="")
        {
            if(ImProtocol.Str(ImProtocol.Get(envelope,"header"),"event_type")!="im.message.receive_v1")return null;var data=ImProtocol.Get(envelope,"event");var raw=ImProtocol.Get(data,"message");bool group=ImProtocol.Str(raw,"chat_type")=="group";
            var sender=ImProtocol.Get(data,"sender");if(ImProtocol.Str(sender,"sender_type")=="app")return null;
            bool mentioned=!String.IsNullOrEmpty(botId)&&ImProtocol.Array(raw,"mentions").Any(x=>ImProtocol.Str(ImProtocol.Get(x,"id"),"open_id")==botId);
            string text="";bool unsupported=ImProtocol.Str(raw,"message_type")!="text";if(!unsupported)text=ImProtocol.Str(ImProtocol.Json(ImProtocol.Str(raw,"content")),"text");
            return new ImMessage{Id=ImProtocol.Str(raw,"message_id"),Sender=ImProtocol.Str(ImProtocol.Get(sender,"sender_id"),"open_id"),Chat=ImProtocol.Str(raw,"chat_id"),IsGroup=group,Mentioned=mentioned,Text=ImProtocol.CleanMention(text),Unsupported=unsupported,Time=ImProtocol.Time(raw,"create_time")};
        }
        public override async Task SendAsync(ImMessage message,string text,CancellationToken token)
        {
            foreach(var chunk in ImProtocol.Chunks(Limited(text))) {var result=await Request("https://open.feishu.cn/open-apis/im/v1/messages/"+Uri.EscapeDataString(message.Id)+"/reply",new{msg_type="text",content=ImProtocol.Encode(new{text=chunk}),uuid=Guid.NewGuid().ToString("N")},token,"Bearer "+await Token(token).ConfigureAwait(false)).ConfigureAwait(false);Success(result);}
        }
        public override void Dispose(){base.Dispose();tokenGate.Dispose();}
    }
}
