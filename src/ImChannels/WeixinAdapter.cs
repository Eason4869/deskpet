using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu.ImChannels
{
    public sealed class WeixinAdapter:ImAdapter
    {
        const string WireVersion="2.4.8";
        const string DefaultBase="https://ilinkai.weixin.qq.com";string cursor="";
        public WeixinAdapter(ImChannelProfile profile):base(profile){}
        internal WeixinAdapter(ImChannelProfile profile,System.Net.Http.HttpMessageHandler handler):base(profile,handler){}
        static object BaseInfo(){return new{channel_version=WireVersion,bot_agent="DeskPet/"+ReleaseInfo.DisplayVersion};}
        static Dictionary<string,string> Headers(bool auth)
        {
            var headers=new Dictionary<string,string>{{"iLink-App-Id","bot"},{"iLink-App-ClientVersion","132104"}};
            if(auth){headers["AuthorizationType"]="ilink_bot_token";headers["X-WECHAT-UIN"]=Convert.ToBase64String(Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)));}return headers;
        }
        public async Task<ImChannelProfile> LoginAsync(Action<string> qr,CancellationToken token)
        {
            var code=await Request(DefaultBase+"/ilink/bot/get_bot_qrcode?bot_type=3",new{local_token_list=new string[0]},token,null,Headers(true)).ConfigureAwait(false);
            string value=ImProtocol.Str(code,"qrcode"),display=ImProtocol.Str(code,"qrcode_img_content");if(value.Length==0||display.Length==0||display.Length>4096)throw new InvalidOperationException("im.protocolFailed");qr(display);
            string host=DefaultBase;using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)){deadline.CancelAfter(TimeSpan.FromMinutes(3));
                while(true){deadline.Token.ThrowIfCancellationRequested();Dictionary<string,object> result=null;
                    try{result=await Request(host+"/ilink/bot/get_qrcode_status?qrcode="+Uri.EscapeDataString(value),null,deadline.Token,null,Headers(false),45).ConfigureAwait(false);}catch(OperationCanceledException){deadline.Token.ThrowIfCancellationRequested();}
                    if(result==null){await Task.Delay(200,deadline.Token).ConfigureAwait(false);continue;}
                    string status=ImProtocol.Str(result,"status");
                    if(status=="need_verifycode"||status=="verify_code_blocked")throw new InvalidOperationException("im.verificationRequired");
                    if(status=="confirmed"){
                        string secret=ImProtocol.Str(result,"bot_token"),baseUrl=ImProtocol.Str(result,"baseurl");if(secret.Length==0)throw new InvalidOperationException("im.authFailed");if(baseUrl.Length==0)baseUrl=DefaultBase;var endpoint=ImProtocol.Endpoint(baseUrl,"weixin");if(endpoint.Scheme!="https"||endpoint.Query.Length>0)throw new InvalidOperationException("im.endpointRejected");
                        var saved=Profile.Copy();saved.Secret=secret;saved.ApiBase=endpoint.GetLeftPart(UriPartial.Authority);saved.AppId=ImProtocol.Str(result,"ilink_bot_id");return saved;
                    }
                    if(status=="expired"||status=="binded_redirect")throw new InvalidOperationException("im.qrExpired");
                    if(status=="scaned_but_redirect"){string redirect=ImProtocol.Str(result,"redirect_host");if(!redirect.Contains("://"))redirect="https://"+redirect;var endpoint=ImProtocol.Endpoint(redirect,"weixin");if(endpoint.Scheme!="https")throw new InvalidOperationException("im.endpointRejected");host=endpoint.GetLeftPart(UriPartial.Authority);}
                    await Task.Delay(1000,deadline.Token).ConfigureAwait(false);
                }
            }
        }
        string ApiBase{get{return String.IsNullOrEmpty(Profile.ApiBase)?DefaultBase:Profile.ApiBase;}}
        public override async Task RunAsync(Func<ImMessage,bool> receive,Action<string> status,CancellationToken token)
        {
            Receive=receive;Status=status;if(String.IsNullOrEmpty(Profile.Secret))throw new InvalidOperationException("im.authFailed");
            while(true){Dictionary<string,object> data=null;
                try{data=await Request(ApiBase+"/ilink/bot/getupdates",new{get_updates_buf=cursor,base_info=BaseInfo()},token,"Bearer "+Profile.Secret,Headers(true),45).ConfigureAwait(false);}catch(OperationCanceledException){token.ThrowIfCancellationRequested();}
                if(data==null){await Task.Delay(200,token).ConfigureAwait(false);continue;}
                if(ImProtocol.Num(data,"ret")==-14||ImProtocol.Num(data,"errcode")==-14)throw new InvalidOperationException("im.authFailed");Success(data,"ret");Success(data,"errcode");Status("im.connected");
                foreach(var raw in ImProtocol.Array(data,"msgs")){var message=Parse(raw);if(message!=null)Receive(message);}
                string next=ImProtocol.Str(data,"get_updates_buf");if(next.Length>65536)throw new InvalidOperationException("im.payloadTooLarge");if(next.Length>0)cursor=next;
                if(ImProtocol.Array(data,"msgs").Length==0)await Task.Delay(200,token).ConfigureAwait(false);
            }
        }
        public static ImMessage Parse(object data)
        {
            if(ImProtocol.Num(data,"message_type")!=1||ImProtocol.Str(data,"group_id").Length>0)return null;var items=ImProtocol.Array(data,"item_list");string sender=ImProtocol.Str(data,"from_user_id");
            return new ImMessage{Id=ImProtocol.Str(data,"message_id"),Sender=sender,Chat=sender,Text=String.Join("\n",items.Where(x=>ImProtocol.Num(x,"type")==1).Select(x=>ImProtocol.Str(ImProtocol.Get(x,"text_item"),"text"))),Unsupported=items.Any(x=>ImProtocol.Num(x,"type")!=1),ReplyToken=ImProtocol.Str(data,"context_token"),Time=ImProtocol.Time(data,"create_time_ms")};
        }
        public override async Task SendAsync(ImMessage message,string text,CancellationToken token)
        {
            if(String.IsNullOrEmpty(message.ReplyToken))throw new InvalidOperationException("im.replyExpired");
            foreach(var chunk in ImProtocol.Chunks(Limited(text),2000)){
                var result=await Request(ApiBase+"/ilink/bot/sendmessage",new{msg=new{from_user_id="",to_user_id=message.Sender,client_id=Guid.NewGuid().ToString("N"),message_type=2,message_state=2,context_token=message.ReplyToken,item_list=new[]{new{type=1,text_item=new{text=chunk}}}},base_info=BaseInfo()},token,"Bearer "+Profile.Secret,Headers(true)).ConfigureAwait(false);Success(result,"ret");Success(result,"errcode");
            }
        }
    }
}
