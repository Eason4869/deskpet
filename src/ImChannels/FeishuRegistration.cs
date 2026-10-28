using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu.ImChannels
{
    // App creation device flow, not web login. Only a returned app credential pair completes it.
    // https://github.com/AstrBotDevs/AstrBot/blob/master/astrbot/core/platform/sources/lark/app_registration.py
    public sealed class FeishuRegistration:IDisposable
    {
        const string RegistrationUrl="https://accounts.feishu.cn/oauth/v1/app/registration";
        readonly ImChannelProfile profile;
        readonly HttpClient http;
        readonly Func<TimeSpan,CancellationToken,Task> delay;
        sealed class Response
        {
            internal HttpStatusCode Status;
            internal Dictionary<string,object> Raw;
            internal object Data{get{return ImProtocol.Get(Raw,"data") as IDictionary<string,object>??Raw;}}
        }
        public FeishuRegistration(ImChannelProfile profile):this(profile,null,null){}
        internal FeishuRegistration(ImChannelProfile profile,HttpMessageHandler handler,Func<TimeSpan,CancellationToken,Task> delay=null)
        {
            if(profile==null||profile.Kind!="feishu")throw new InvalidOperationException("im.invalidConfig");
            this.profile=profile.Copy();this.delay=delay??((duration,token)=>Task.Delay(duration,token));
            http=new HttpClient(handler??new HttpClientHandler{AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan};
        }
        static string Field(object data,string key){return ImProtocol.Get(data,key) as string??"";}
        static string Error(Response response){string value=Field(response.Raw,"error");return value.Length>0?value:Field(response.Data,"error");}
        static void Check(Response response)
        {
            string error=Error(response);
            if(error=="access_denied")throw new InvalidOperationException("im.authFailed");
            if(error=="expired_token"||error=="invalid_grant")throw new InvalidOperationException("im.qrExpired");
            if(error.Length>0)throw new InvalidOperationException("im.platformFailed");
            int status=(int)response.Status;
            if(status==401||status==403)throw new InvalidOperationException("im.authFailed");
            if(status<200||status>=300)throw new InvalidOperationException("im.httpFailed");
            if(ImProtocol.Num(response.Raw,"code")!=0||ImProtocol.Num(response.Data,"code")!=0)throw new InvalidOperationException("im.platformFailed");
        }
        async Task<Response> Post(Dictionary<string,string> form,CancellationToken token)
        {
            try{
                using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)){
                    deadline.CancelAfter(TimeSpan.FromSeconds(15));
                    using(var request=new HttpRequestMessage(HttpMethod.Post,ImProtocol.Endpoint(RegistrationUrl,"feishu"))){
                        request.Headers.TryAddWithoutValidation("Accept","application/json");
                        request.Content=new FormUrlEncodedContent(form);
                        using(var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token).ConfigureAwait(false)){
                            if(response.StatusCode==HttpStatusCode.Unauthorized||response.StatusCode==HttpStatusCode.Forbidden)throw new InvalidOperationException("im.authFailed");
                            if((int)response.StatusCode>=300&&(int)response.StatusCode!=400)throw new InvalidOperationException("im.httpFailed");
                            if(response.Content==null)throw new InvalidOperationException("im.protocolFailed");
                            using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var data=new MemoryStream()){
                                var buffer=new byte[8192];int count;
                                while((count=await input.ReadAsync(buffer,0,buffer.Length,deadline.Token).ConfigureAwait(false))>0){
                                    if(data.Length+count>ImProtocol.MaxBytes)throw new InvalidOperationException("im.payloadTooLarge");
                                    data.Write(buffer,0,count);
                                }
                                Dictionary<string,object> raw;
                                try{raw=ImProtocol.Json(new UTF8Encoding(false,true).GetString(data.ToArray()));}
                                catch(ArgumentException){throw new InvalidOperationException("im.protocolFailed");}
                                catch(InvalidOperationException){throw new InvalidOperationException("im.protocolFailed");}
                                if(raw==null)throw new InvalidOperationException("im.protocolFailed");
                                return new Response{Status=response.StatusCode,Raw=raw};
                            }
                        }
                    }
                }
            }catch(HttpRequestException){throw new InvalidOperationException("im.httpFailed");}
            catch(IOException){throw new InvalidOperationException("im.httpFailed");}
            catch(OperationCanceledException){token.ThrowIfCancellationRequested();throw new InvalidOperationException("im.httpFailed");}
        }
        public async Task<ImChannelProfile> LoginAsync(Action<string> qr,CancellationToken token)
        {
            if(qr==null)throw new ArgumentNullException("qr");token.ThrowIfCancellationRequested();
            var begin=await Post(new Dictionary<string,string>{{"action","begin"},{"archetype","PersonalAgent"},{"auth_method","client_secret"},{"request_user_info","open_id tenant_brand"}},token).ConfigureAwait(false);
            Check(begin);string device=Field(begin.Data,"device_code"),url=Field(begin.Data,"verification_uri_complete");
            string userCode=Field(begin.Data,"user_code");
            if(url.Length==0&&userCode.Length>0&&userCode.Length<=1024)url="https://open.feishu.cn/page/cli?user_code="+Uri.EscapeDataString(userCode);
            if(device.Length==0||device.Length>4096||url.Length==0||url.Length>4096)throw new InvalidOperationException("im.protocolFailed");
            var address=ImProtocol.Endpoint(url,"feishu");
            if(address.Scheme!="https"||!(address.DnsSafeHost.Equals("feishu.cn",StringComparison.OrdinalIgnoreCase)||address.DnsSafeHost.EndsWith(".feishu.cn",StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("im.endpointRejected");
            long seconds=ImProtocol.Num(begin.Data,"expires_in",300),interval=ImProtocol.Num(begin.Data,"interval",5);
            if(seconds<=0)throw new InvalidOperationException("im.qrExpired");
            seconds=Math.Min(seconds,900);interval=Math.Max(1,Math.Min(interval,60));
            using(var lifetime=CancellationTokenSource.CreateLinkedTokenSource(token)){
                lifetime.CancelAfter(TimeSpan.FromSeconds(seconds));
                try{
                    qr(address.AbsoluteUri);
                    while(true){
                        lifetime.Token.ThrowIfCancellationRequested();await delay(TimeSpan.FromSeconds(interval),lifetime.Token).ConfigureAwait(false);lifetime.Token.ThrowIfCancellationRequested();
                        var response=await Post(new Dictionary<string,string>{{"action","poll"},{"device_code",device}},lifetime.Token).ConfigureAwait(false);
                        string error=Error(response);
                        if(error=="authorization_pending")continue;
                        if(error=="slow_down"){interval=Math.Min(60,interval+5);continue;}
                        Check(response);
                        string app=Field(response.Data,"client_id"),secret=Field(response.Data,"client_secret");
                        if(app.Length==0||app.Length>512||secret.Length==0||secret.Length>8192)throw new InvalidOperationException("im.authFailed");
                        string brand=Field(ImProtocol.Get(response.Data,"user_info"),"tenant_brand");if(brand.Length==0)brand=Field(response.Data,"tenant_brand");
                        // The current adapter connects to Feishu APIs; Lark credentials cannot be used there.
                        if(brand.Length>0&&brand!="feishu")throw new InvalidOperationException("im.platformFailed");
                        lifetime.Token.ThrowIfCancellationRequested();var saved=profile.Copy();saved.AppId=app;saved.Secret=secret;saved.ApiBase="";return saved;
                    }
                }catch(OperationCanceledException){token.ThrowIfCancellationRequested();throw new InvalidOperationException("im.qrExpired");}
            }
        }
        public void Dispose(){http.Dispose();}
    }
}
