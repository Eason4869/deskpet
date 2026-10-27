using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dafeiyu.ImChannels;

namespace Dafeiyu.Tests
{
    public static class ImChannelAuditTests
    {
        sealed class Handler:HttpMessageHandler
        {
            internal Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> Respond;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel){return Respond(request,cancel);}
        }
        static HttpResponseMessage Json(string value){return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(value,Encoding.UTF8,"application/json")};}
        static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
        sealed class CancelOnDecrypt:ISecretProtector
        {
            internal ISecretProtector Inner;internal Action Cancel;
            public byte[] Protect(byte[] value){return Inner.Protect(value);}
            public byte[] Unprotect(byte[] value){Cancel();return Inner.Unprotect(value);}
        }
        sealed class WaitingApi:IConversationApi
        {
            public async Task<string> ChatAsync(ConnectionSettings config,IList<ChatMessage> messages,CancellationToken cancel){await Task.Delay(-1,cancel);return "never";}
            public Task<string> TranscribeAsync(ConnectionSettings config,byte[] bytes,CancellationToken cancel){throw new NotSupportedException();}
            public Task<byte[]> SpeechAsync(ConnectionSettings config,string voice,string text,CancellationToken cancel){throw new NotSupportedException();}
            public void Dispose(){}
        }
        public static async Task RunAll(ISecretProtector original)
        {
            Console.WriteLine("ImChannel audit: cancellation during validation");
            if(original!=null)using(var cancel=new CancellationTokenSource()){
                var cfg=new PetSettings();cfg.Text.BaseUrl="https://example.com/v1";cfg.Text.Model="test";cfg.Text.ApiKey="fixture";
                try{SecretProtection.Configure(new CancelOnDecrypt{Inner=original,Cancel=()=>cancel.Cancel()});
                    using(var conversation=new ImConversation(new Dafeiyu.Maintenance.DataMaintenanceGate(),new WaitingApi())){
                        var task=conversation.ReplyAsync(new ImChannelProfile{PetName="test",Persona="test"},cfg,new ImMessage{Text="cancel during connection validation"},cancel.Token);
                        Check(await Task.WhenAny(task,Task.Delay(500))==task,"cancellation during validation cannot miss the new model operation");try{await task;}catch(OperationCanceledException){}
                    }
                }finally{SecretProtection.Configure(original);}
            }
            Console.WriteLine("ImChannel audit: Weixin poll timeout and verification states");
            var profile=new ImChannelProfile{Kind="weixin",Name="test",PetName="test",Secret="fixture"};
            int requests=0;using(var stop=new CancellationTokenSource(TimeSpan.FromSeconds(5))){
                var handler=new Handler{Respond=async (request,cancel)=>{
                    requests++;var body=ImProtocol.Json(await request.Content.ReadAsStringAsync());
                    if(requests==1)return Json("{\"ret\":0,\"msgs\":[],\"get_updates_buf\":\"keep-cursor\"}");
                    if(requests==2)throw new TaskCanceledException("request deadline");
                    Check(ImProtocol.Str(body,"get_updates_buf")=="keep-cursor","Weixin timeout must preserve cursor");stop.Cancel();throw new OperationCanceledException(stop.Token);
                }};
                using(var adapter=new WeixinAdapter(profile,handler))try{await adapter.RunAsync(m=>false,s=>{},stop.Token);}catch(OperationCanceledException){}
                Check(requests==3,"normal poll timeout must not end adapter lifetime");
            }
            foreach(string status in new[]{"need_verifycode","verify_code_blocked"}){
                var handler=new Handler{Respond=(request,cancel)=>Task.FromResult(Json(request.RequestUri.AbsolutePath.EndsWith("get_bot_qrcode")?"{\"qrcode\":\"q\",\"qrcode_img_content\":\"qr\"}":"{\"status\":\""+status+"\"}"))};
                bool rejected=false;using(var deadline=new CancellationTokenSource(1000))using(var adapter=new WeixinAdapter(profile,handler))try{await adapter.LoginAsync(q=>{},deadline.Token);}catch(InvalidOperationException error){rejected=error.Message=="im.verificationRequired";}catch(OperationCanceledException){}
                Check(rejected,"additional verification must be reported immediately");
            }
            var feishu=new ImChannelProfile{Kind="feishu",Name="test",PetName="test",AppId="app",Secret="fixture"};
            using(var adapter=new FeishuAdapter(feishu,new Handler{Respond=(r,c)=>Task.FromResult(Json("{\"code\":10015,\"msg\":\"sensitive payload must not be surfaced\"}"))})){
                bool auth=false;try{await adapter.RunAsync(m=>false,s=>{},CancellationToken.None);}catch(InvalidOperationException error){auth=error.Message=="im.authFailed";}Check(auth,"Feishu wrong secret is terminal authentication failure");
            }
            Check(!DingTalkAdapter.IsRegistered(ImProtocol.Json("{\"type\":\"SYSTEM\",\"headers\":{\"topic\":\"CONNECTED\"}}")),"DingTalk transport is not yet subscribed");
            Check(DingTalkAdapter.IsRegistered(ImProtocol.Json("{\"type\":\"SYSTEM\",\"headers\":{\"topic\":\"REGISTERED\"}}")),"DingTalk subscription ready");
            var pong=new FeishuFrame{Payload=Encoding.UTF8.GetBytes("{\"PingInterval\":2}")};pong.Headers["type"]="pong";
            Check(FeishuAdapter.PingInterval(pong,30000)==2000,"Feishu applies server pong heartbeat policy");pong.Payload=Encoding.UTF8.GetBytes("{\"PingInterval\":0}");Check(FeishuAdapter.PingInterval(pong,30000)==30000,"invalid ping interval cannot create busy loop");
            foreach(string code in new[]{"zh_CN","zh_TW","ja_JP","ko_KR","fr_FR","de_DE","hi_IN","id_ID","it_IT","pt_BR","es_419","es_ES"})foreach(string key in new[]{"im.subtitle","im.allowHint","im.localOnly","im.help","im.storageFailed","im.connectionReplaced"}){
                string value=TextResources.Get(key,TextResources.PrepareLanguage(code));Check(!value.StartsWith("[")&&value!=TextResources.Get(key,TextResources.PrepareLanguage("en_US")),code+" must translate "+key);
            }
            bool qrFailed=false;try{ImLoginQr.Png(new string('x',4096));}catch(InvalidOperationException error){qrFailed=error.Message=="im.protocolFailed";}Check(qrFailed,"oversized QR must produce a safe error");
            Console.WriteLine("ImChannelAuditTests: polling, auth, registration, heartbeat, complete translations and QR boundaries passed");
        }
    }
}
