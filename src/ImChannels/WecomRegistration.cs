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
    // Tencent's bot authorization flow returns Bot ID and Secret, rather than a web login cookie.
    // https://github.com/TencentCloud/Octop/blob/main/src/octop/infra/gateway/channels/qr_bind.py
    public sealed class WecomRegistration:IDisposable
    {
        const string ApiBase="https://work.weixin.qq.com/ai/qc/";
        readonly ImChannelProfile profile;
        readonly HttpClient http;
        readonly Func<TimeSpan,CancellationToken,Task> delay;
        readonly TimeSpan lifetime;
        public WecomRegistration(ImChannelProfile profile):this(profile,null,null,null){}
        internal WecomRegistration(ImChannelProfile profile,HttpMessageHandler handler,Func<TimeSpan,CancellationToken,Task> delay=null,TimeSpan? lifetime=null)
        {
            if(profile==null||profile.Kind!="wecom")throw new InvalidOperationException("im.invalidConfig");
            this.profile=profile.Copy();this.delay=delay??((duration,token)=>Task.Delay(duration,token));this.lifetime=lifetime??TimeSpan.FromMinutes(5);
            http=new HttpClient(handler??new HttpClientHandler{AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan};
        }
        static string Field(object data,string key){return ImProtocol.Get(data,key) as string??"";}
        async Task<Dictionary<string,object>> Get(string path,CancellationToken token)
        {
            try{
                using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)){
                    deadline.CancelAfter(TimeSpan.FromSeconds(15));
                    using(var request=new HttpRequestMessage(HttpMethod.Get,ImProtocol.Endpoint(ApiBase+path,"wecom"))){
                        request.Headers.TryAddWithoutValidation("Accept","application/json");
                        request.Headers.TryAddWithoutValidation("User-Agent","DeskPet/1.0");
                        using(var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token).ConfigureAwait(false)){
                            if(response.StatusCode==HttpStatusCode.Unauthorized||response.StatusCode==HttpStatusCode.Forbidden)throw new InvalidOperationException("im.authFailed");
                            if(!response.IsSuccessStatusCode)throw new InvalidOperationException("im.httpFailed");
                            if(response.Content==null)throw new InvalidOperationException("im.protocolFailed");
                            using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var data=new MemoryStream()){
                                var buffer=new byte[8192];int count;
                                while((count=await input.ReadAsync(buffer,0,buffer.Length,deadline.Token).ConfigureAwait(false))>0){
                                    if(data.Length+count>ImProtocol.MaxBytes)throw new InvalidOperationException("im.payloadTooLarge");data.Write(buffer,0,count);
                                }
                                Dictionary<string,object> raw;
                                try{raw=ImProtocol.Json(new UTF8Encoding(false,true).GetString(data.ToArray()));}
                                catch(ArgumentException){throw new InvalidOperationException("im.protocolFailed");}
                                catch(InvalidOperationException){throw new InvalidOperationException("im.protocolFailed");}
                                if(raw==null)throw new InvalidOperationException("im.protocolFailed");
                                if(ImProtocol.Num(raw,"errcode")!=0||ImProtocol.Num(raw,"code")!=0)throw new InvalidOperationException("im.platformFailed");
                                return raw;
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
            var begin=await Get("generate?source=deskpet&plat=2",token).ConfigureAwait(false);var data=ImProtocol.Get(begin,"data");
            string code=Field(data,"scode"),url=Field(data,"auth_url");
            if(code.Length==0||code.Length>4096||url.Length==0||url.Length>4096)throw new InvalidOperationException("im.protocolFailed");
            var address=ImProtocol.Endpoint(url,"wecom");if(address.Scheme!="https")throw new InvalidOperationException("im.endpointRejected");
            using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)){
                deadline.CancelAfter(lifetime);
                try{
                    qr(address.AbsoluteUri);
                    while(true){
                        deadline.Token.ThrowIfCancellationRequested();await delay(TimeSpan.FromSeconds(3),deadline.Token).ConfigureAwait(false);deadline.Token.ThrowIfCancellationRequested();
                        var result=await Get("query_result?scode="+Uri.EscapeDataString(code),deadline.Token).ConfigureAwait(false);
                        var state=ImProtocol.Get(result,"data");string status=Field(state,"status").ToLowerInvariant();
                        if(status=="success"){
                            var bot=ImProtocol.Get(state,"bot_info");string id=Field(bot,"botid"),secret=Field(bot,"secret");
                            if(id.Length==0||id.Length>512||secret.Length==0||secret.Length>8192)throw new InvalidOperationException("im.authFailed");
                            deadline.Token.ThrowIfCancellationRequested();var saved=profile.Copy();saved.AppId=id;saved.Secret=secret;saved.ApiBase="";return saved;
                        }
                        if(status=="expired"||status=="timeout")throw new InvalidOperationException("im.qrExpired");
                        if(status=="denied"||status=="cancelled"||status=="canceled")throw new InvalidOperationException("im.authFailed");
                        if(status=="error"||status=="failed"||status=="fail")throw new InvalidOperationException("im.platformFailed");
                        if(status.Length==0)throw new InvalidOperationException("im.protocolFailed");
                        // Tencent does not document intermediate statuses; wait for credentials until expiry.
                    }
                }catch(OperationCanceledException){token.ThrowIfCancellationRequested();throw new InvalidOperationException("im.qrExpired");}
            }
        }
        public void Dispose(){http.Dispose();}
    }
}
