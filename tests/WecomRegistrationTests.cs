using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Dafeiyu.ImChannels;

namespace Dafeiyu.Tests
{
    public static class WecomRegistrationTests
    {
        sealed class Handler:HttpMessageHandler
        {
            internal Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> Respond;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){return Respond(request,token);}
        }
        static int checks;
        const string Begin="{\"errcode\":0,\"data\":{\"scode\":\"fixture code\",\"auth_url\":\"https://work.weixin.qq.com/ai/qc/gen?scode=fixture\"}}";
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
        static HttpResponseMessage Json(string value,HttpStatusCode status=HttpStatusCode.OK){return new HttpResponseMessage(status){Content=new StringContent(value)};}
        static ImChannelProfile Profile(){return new ImChannelProfile{Kind="wecom",Name="fixture",PetName="pet",AppId="old",Secret="old-secret"};}
        static Task NoDelay(TimeSpan delay,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(0);}
        static async Task Reject(string response,string key,HttpStatusCode status=HttpStatusCode.OK)
        {
            int count=0;using(var client=new WecomRegistration(Profile(),new Handler{Respond=(r,t)=>Task.FromResult(Json(++count==1?Begin:response,count==1?HttpStatusCode.OK:status))},NoDelay)){
                bool rejected=false;try{await client.LoginAsync(q=>{},CancellationToken.None);}catch(InvalidOperationException e){rejected=e.Message==key;}Check(rejected,"failure "+key);
            }
        }
        public static async Task RunAll()
        {
            int count=0;var original=Profile();string qr="";
            using(var client=new WecomRegistration(original,new Handler{Respond=(r,t)=>{
                Check(r.Method==HttpMethod.Get&&r.Content==null,"GET without credentials in request body");
                Check(r.RequestUri.Host=="work.weixin.qq.com"&&r.RequestUri.Scheme=="https","official API host");
                if(++count==1){Check(r.RequestUri.AbsolutePath=="/ai/qc/generate"&&r.RequestUri.Query.Contains("plat=2"),"create scan request on Windows");return Task.FromResult(Json(Begin));}
                Check(r.RequestUri.AbsolutePath=="/ai/qc/query_result"&&r.RequestUri.Query.Contains("scode=fixture%20code"),"escaped poll session");
                return Task.FromResult(Json(count==2?"{\"data\":{\"status\":\"pending\"}}":"{\"data\":{\"status\":\"success\",\"bot_info\":{\"botid\":\"bot-fixture\",\"secret\":\"secret-fixture\"}}}"));
            }},NoDelay)){var saved=await client.LoginAsync(value=>qr=value,CancellationToken.None);Check(saved.AppId=="bot-fixture"&&saved.Secret=="secret-fixture","require actual bot credentials");Check(original.Secret=="old-secret"&&saved.Id==original.Id&&saved.PetName=="pet","draft copied");}
            Check(qr.StartsWith("https://work.weixin.qq.com/ai/qc/gen?"),"creation authorization URL displayed");
            await Reject("{\"data\":{\"status\":\"success\",\"bot_info\":{\"botid\":\"b\"}}}","im.authFailed");
            await Reject("{\"data\":{\"status\":\"expired\"}}","im.qrExpired");
            await Reject("{\"data\":{\"status\":\"denied\"}}","im.authFailed");
            await Reject("{\"data\":{\"status\":\"error\"}}","im.platformFailed");
            await Reject("{\"errcode\":40001,\"errmsg\":\"sensitive must not leak\"}","im.platformFailed");
            await Reject("{}","im.protocolFailed");
            await Reject("not JSON","im.protocolFailed");
            await Reject("private auth page","im.authFailed",HttpStatusCode.Forbidden);
            await Reject("redirect","im.httpFailed",HttpStatusCode.Redirect);
            await Reject(new string('x',ImProtocol.MaxBytes+1),"im.payloadTooLarge");
            foreach(string url in new[]{"https://evil.test/qr","https://work.weixin.qq.com.evil.test/qr","http://work.weixin.qq.com/qr","https://user@work.weixin.qq.com/qr"}){
                using(var client=new WecomRegistration(Profile(),new Handler{Respond=(r,t)=>Task.FromResult(Json(ImProtocol.Encode(new{data=new{scode="d",auth_url=url}})))},NoDelay)){
                    bool rejected=false;try{await client.LoginAsync(q=>{throw new Exception("unsafe QR displayed");},CancellationToken.None);}catch(InvalidOperationException e){rejected=e.Message=="im.endpointRejected";}Check(rejected,"reject unsafe QR URL");
                }
            }
            using(var stop=new CancellationTokenSource())using(var client=new WecomRegistration(Profile(),new Handler{Respond=(r,t)=>Task.FromResult(Json(Begin))},NoDelay)){
                bool cancelled=false;try{await client.LoginAsync(q=>stop.Cancel(),stop.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"QR cancellation stops polling");
            }
            using(var client=new WecomRegistration(Profile(),new Handler{Respond=(r,t)=>Task.FromResult(Json(Begin))},(duration,t)=>Task.Delay(duration,t),TimeSpan.FromMilliseconds(30))){bool expired=false;try{await client.LoginAsync(q=>{},CancellationToken.None);}catch(InvalidOperationException e){expired=e.Message=="im.qrExpired";}Check(expired,"local expiry classified");}
            int expiryCalls=0;
            using(var client=new WecomRegistration(Profile(),new Handler{Respond=async(r,t)=>{if(++expiryCalls==1)return Json(Begin);await Task.Delay(-1,t);return Json("{}");}},NoDelay,TimeSpan.FromMilliseconds(30))){bool expired=false;try{await client.LoginAsync(q=>{},CancellationToken.None);}catch(InvalidOperationException e){expired=e.Message=="im.qrExpired";}Check(expired&&expiryCalls==2,"expiry during HTTP poll stays QR expiry");}
            Console.WriteLine("WecomRegistrationTests: "+checks+" checks passed");
        }
        public static int Main(){try{RunAll().GetAwaiter().GetResult();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    }
}
