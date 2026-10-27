using System;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu.ImChannels
{
    public sealed class DingTalkAdapter:ImAdapter
    {
        public DingTalkAdapter(ImChannelProfile profile):base(profile){}
        public override async Task RunAsync(Func<ImMessage,bool> receive,Action<string> status,CancellationToken token)
        {
            Receive=receive;Status=status;
            var endpoint=await Request("https://api.dingtalk.com/v1.0/gateway/connections/open",new{clientId=Profile.AppId,clientSecret=Profile.Secret,ua="DeskPet/"+ReleaseInfo.DisplayVersion,subscriptions=new[]{new{type="CALLBACK",topic="/v1.0/im/bot/messages/get"}}},token).ConfigureAwait(false);
            await Connect(ImProtocol.Str(endpoint,"endpoint")+"?ticket="+Uri.EscapeDataString(ImProtocol.Str(endpoint,"ticket")),token).ConfigureAwait(false);
            while(true){var frame=ParseBytes(await Read(token).ConfigureAwait(false));string type=ImProtocol.Str(frame,"type");var headers=ImProtocol.Get(frame,"headers");string topic=ImProtocol.Str(headers,"topic");
                if(type=="SYSTEM"){
                    if(IsRegistered(frame))Status("im.connected");
                    if(topic=="disconnect")throw new InvalidOperationException("im.disconnected");
                    await Send(new{code=200,headers=headers,message="OK",data=ImProtocol.Str(frame,"data")},token).ConfigureAwait(false);
                }else if(type=="CALLBACK"){
                    if(topic=="/v1.0/im/bot/messages/get")Receive(Parse(ImProtocol.Json(ImProtocol.Str(frame,"data"))));
                    await Send(new{code=200,headers=new{contentType="application/json",messageId=ImProtocol.Str(headers,"messageId")},message="OK",data="{\"status\":\"SUCCESS\"}"},token).ConfigureAwait(false);
                }
            }
        }
        public static bool IsRegistered(object frame){return ImProtocol.Str(frame,"type")=="SYSTEM"&&ImProtocol.Str(ImProtocol.Get(frame,"headers"),"topic")=="REGISTERED";}
        public static ImMessage Parse(object data)
        {
            var message=new ImMessage{Id=ImProtocol.Str(data,"msgId"),Sender=ImProtocol.Str(data,"senderStaffId"),Chat=ImProtocol.Str(data,"conversationId"),IsGroup=ImProtocol.Str(data,"conversationType")=="2",Mentioned=ImProtocol.Bool(data,"isInAtList"),Text=ImProtocol.Str(ImProtocol.Get(data,"text"),"content").Trim(),Unsupported=ImProtocol.Str(data,"msgtype")!="text",ReplyToken=ImProtocol.Str(data,"sessionWebhook"),Time=ImProtocol.Time(data,"createAt")};
            if(ImProtocol.Get(data,"sessionWebhookExpiredTime")!=null)message.ReplyExpiry=ImProtocol.Time(data,"sessionWebhookExpiredTime");return message;
        }
        public override async Task SendAsync(ImMessage message,string text,CancellationToken token)
        {
            if(DateTimeOffset.UtcNow>=message.ReplyExpiry)throw new InvalidOperationException("im.replyExpired");
            var endpoint=ImProtocol.Endpoint(message.ReplyToken,"dingtalk");if(endpoint.Host!="oapi.dingtalk.com"||!endpoint.AbsolutePath.StartsWith("/robot/",StringComparison.Ordinal))throw new InvalidOperationException("im.endpointRejected");
            foreach(var chunk in ImProtocol.Chunks(Limited(text))){var result=await Request(endpoint.AbsoluteUri,new{msgtype="text",text=new{content=chunk}},token).ConfigureAwait(false);Success(result,"errcode");}
        }
    }
}
