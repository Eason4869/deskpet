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
    // Official app-registration device flow; the user authorizes creation in DingTalk.
    public sealed class DingTalkRegistration:IDisposable
    {
        readonly ImChannelProfile profile;
        readonly BotRegistrationHttp http;
        readonly Func<TimeSpan,CancellationToken,Task> delay;
        readonly TimeSpan maxWait;
        readonly CancellationTokenSource shutdown=new CancellationTokenSource();
        int started,disposed;
        public DingTalkRegistration(ImChannelProfile profile):this(profile,null,Task.Delay){}
        internal DingTalkRegistration(ImChannelProfile profile,HttpMessageHandler handler,Func<TimeSpan,CancellationToken,Task> delay,TimeSpan? maxWait=null)
        {
            if(profile==null||profile.Kind!="dingtalk")throw new InvalidOperationException("im.invalidConfig");
            this.profile=profile.Copy();http=new BotRegistrationHttp(handler);this.delay=delay??Task.Delay;this.maxWait=maxWait??TimeSpan.FromHours(2);
        }
        public async Task<ImChannelProfile> LoginAsync(Action<string> qr,CancellationToken token)
        {
            if(qr==null)throw new ArgumentNullException("qr");
            if(Volatile.Read(ref disposed)!=0)throw new ObjectDisposedException("DingTalkRegistration");
            if(Interlocked.Exchange(ref started,1)!=0)throw new InvalidOperationException("im.authFailed");
            using(var lifetime=CancellationTokenSource.CreateLinkedTokenSource(token,shutdown.Token)){
                lifetime.CancelAfter(maxWait);
                try{
                    var init=await Post("init",new{source="DING_DWS_CLAW"},lifetime.Token).ConfigureAwait(false);
                    var begin=await Post("begin",new{nonce=BotRegistrationHttp.Required(init,"nonce")},lifetime.Token).ConfigureAwait(false);
                    string device=BotRegistrationHttp.Required(begin,"device_code");
                    string address=BotRegistrationHttp.Qr(BotRegistrationHttp.Required(begin,"verification_uri_complete",4096),"dingtalk.com");
                    long expires=ImProtocol.Num(begin,"expires_in",7200),seconds=ImProtocol.Num(begin,"interval",3);
                    if(expires<=0||seconds<=0)throw new InvalidOperationException("im.protocolFailed");
                    lifetime.CancelAfter(TimeSpan.FromMilliseconds(Math.Min(maxWait.TotalMilliseconds,Math.Min(7200,expires)*1000)));
                    var interval=TimeSpan.FromSeconds(Math.Min(60,seconds));
                    lifetime.Token.ThrowIfCancellationRequested();qr(address);
                    while(true){
                        await delay(interval,lifetime.Token).ConfigureAwait(false);lifetime.Token.ThrowIfCancellationRequested();
                        var result=await Post("poll",new{device_code=device},lifetime.Token).ConfigureAwait(false);
                        switch(ImProtocol.Str(result,"status").Trim().ToUpperInvariant()){
                            case "WAITING":case "CREATING":break;
                            case "SUCCESS":return BotRegistrationHttp.Credentials(profile,ImProtocol.Str(result,"client_id"),ImProtocol.Str(result,"client_secret"));
                            case "FAIL":throw new InvalidOperationException("im.authFailed");
                            case "EXPIRED":throw new InvalidOperationException("im.qrExpired");
                            default:throw new InvalidOperationException("im.protocolFailed");
                        }
                    }
                }catch(OperationCanceledException){if(!token.IsCancellationRequested&&!shutdown.IsCancellationRequested)throw new InvalidOperationException("im.qrExpired");throw;}
            }
        }
        async Task<Dictionary<string,object>> Post(string action,object payload,CancellationToken token)
        {
            var result=await http.Post("https://oapi.dingtalk.com/app/registration/"+action,payload,token).ConfigureAwait(false);
            if(ImProtocol.Get(result,"errcode")!=null&&ImProtocol.Num(result,"errcode",-1)!=0)throw new InvalidOperationException("im.platformFailed");
            return result;
        }
        public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;shutdown.Cancel();http.Dispose();}
    }

    internal sealed class BotRegistrationHttp:IDisposable
    {
        readonly HttpClient http;
        internal BotRegistrationHttp(HttpMessageHandler handler){http=new HttpClient(handler??new HttpClientHandler{AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan};}
        internal async Task<Dictionary<string,object>> Post(string address,object body,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            // These are the only API origins used by the two registration flows.
            Uri endpoint; if(!Uri.TryCreate(address,UriKind.Absolute,out endpoint)||endpoint.Scheme!="https"||endpoint.Port!=443||endpoint.UserInfo.Length>0||endpoint.Fragment.Length>0||(endpoint.Host!="q.qq.com"&&endpoint.Host!="oapi.dingtalk.com"))throw new InvalidOperationException("im.endpointRejected");
            using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)){
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                try{
                    using(var request=new HttpRequestMessage(HttpMethod.Post,endpoint)){
                        request.Headers.TryAddWithoutValidation("Accept","application/json");
                        request.Content=new StringContent(ImProtocol.Encode(body),Encoding.UTF8,"application/json");
                        using(var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token).ConfigureAwait(false)){
                            if(!response.IsSuccessStatusCode)throw new InvalidOperationException(response.StatusCode==HttpStatusCode.Unauthorized||response.StatusCode==HttpStatusCode.Forbidden?"im.authFailed":"im.httpFailed");
                            using(var stream=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var bytes=new MemoryStream()){
                                var buffer=new byte[8192];int count;
                                while((count=await stream.ReadAsync(buffer,0,buffer.Length,deadline.Token).ConfigureAwait(false))>0){if(bytes.Length+count>ImProtocol.MaxBytes)throw new InvalidOperationException("im.payloadTooLarge");bytes.Write(buffer,0,count);}
                                try{var result=ImProtocol.Json(new UTF8Encoding(false,true).GetString(bytes.ToArray()));if(result==null)throw new InvalidOperationException("im.protocolFailed");return result;}
                                catch(Exception error){if(error is ArgumentException||error is InvalidCastException||error is FormatException||error is InvalidOperationException)throw new InvalidOperationException("im.protocolFailed");throw;}
                            }
                        }
                    }
                }catch(OperationCanceledException){if(!token.IsCancellationRequested)throw new InvalidOperationException("im.httpFailed");throw;}
                catch(HttpRequestException){throw new InvalidOperationException("im.httpFailed");}
            }
        }
        internal static string Required(object data,string field,int max=8192)
        {
            string value=ImProtocol.Str(data,field).Trim();if(String.IsNullOrWhiteSpace(value)||value.Length>max)throw new InvalidOperationException("im.protocolFailed");return value;
        }
        internal static string Qr(string address,string domain)
        {
            Uri uri;if(!Uri.TryCreate(address,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.Port!=443||uri.UserInfo.Length>0||uri.Fragment.Length>0||!(uri.Host.Equals(domain,StringComparison.OrdinalIgnoreCase)||uri.Host.EndsWith("."+domain,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("im.endpointRejected");return uri.AbsoluteUri;
        }
        internal static ImChannelProfile Credentials(ImChannelProfile profile,string appId,string secret)
        {
            appId=appId.Trim();secret=secret.Trim();
            if(String.IsNullOrWhiteSpace(appId)||appId.Length>512||String.IsNullOrWhiteSpace(secret)||secret.Length>8192)throw new InvalidOperationException("im.authFailed");
            var result=profile.Copy();result.AppId=appId;result.Secret=secret;return result;
        }
        public void Dispose(){http.Dispose();}
    }
}
