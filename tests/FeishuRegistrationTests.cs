using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Dafeiyu.ImChannels;

namespace Dafeiyu.Tests
{
    public static class FeishuRegistrationTests
    {
        sealed class Handler:HttpMessageHandler
        {
            internal Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> Respond;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){return Respond(request,token);}
        }
        static int checks;
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
        static HttpResponseMessage Json(string json,HttpStatusCode status=HttpStatusCode.OK){return new HttpResponseMessage(status){Content=new StringContent(json)};}
        const string Begin="{\"data\":{\"device_code\":\"device-fixture\",\"verification_uri_complete\":\"https://open.feishu.cn/page/cli?user_code=fixture\",\"expires_in\":300,\"interval\":1}}";
        static ImChannelProfile Profile(){return new ImChannelProfile{Kind="feishu",Name="fixture",PetName="pet",Secret="old-secret",AppId="old-app"};}
        static Task NoDelay(TimeSpan delay,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(0);}
        static async Task Reject(string response,string key,HttpStatusCode status=HttpStatusCode.OK)
        {
            int count=0;var handler=new Handler{Respond=(r,t)=>Task.FromResult(Json(++count==1?Begin:response,count==1?HttpStatusCode.OK:status))};
            using(var client=new FeishuRegistration(Profile(),handler,NoDelay)){
                bool rejected=false;try{await client.LoginAsync(q=>{},CancellationToken.None);}catch(InvalidOperationException e){rejected=e.Message==key;}
                Check(rejected,"safe failure classification "+key);
            }
        }
        public static async Task RunAll()
        {
            int calls=0;var delays=new List<TimeSpan>();var original=Profile();
            var handler=new Handler{Respond=async(r,t)=>{
                Check(r.Method==HttpMethod.Post&&r.RequestUri.AbsoluteUri=="https://accounts.feishu.cn/oauth/v1/app/registration","fixed official endpoint");
                Check(r.Content.Headers.ContentType.MediaType=="application/x-www-form-urlencoded","form encoding");
                string body=await r.Content.ReadAsStringAsync();calls++;
                if(calls==1){Check(body.Contains("action=begin")&&body.Contains("archetype=PersonalAgent")&&body.Contains("auth_method=client_secret")&&body.Contains("request_user_info=open_id+tenant_brand"),"app creation fields");return Json(Begin);}
                Check(body.Contains("action=poll")&&body.Contains("device_code=device-fixture"),"poll form");
                if(calls==2)return Json("{\"error\":\"authorization_pending\"}",HttpStatusCode.BadRequest);
                if(calls==3)return Json("{\"data\":{\"error\":\"slow_down\"}}",HttpStatusCode.BadRequest);
                return Json("{\"data\":{\"client_id\":\"cli_fixture\",\"client_secret\":\"fixture-secret\",\"user_info\":{\"tenant_brand\":\"feishu\"}}}");
            }};
            string qr="";using(var client=new FeishuRegistration(original,handler,(delay,t)=>{delays.Add(delay);return NoDelay(delay,t);})){var saved=await client.LoginAsync(q=>qr=q,CancellationToken.None);Check(saved.AppId=="cli_fixture"&&saved.Secret=="fixture-secret","credentials are required before success");Check(saved.Id==original.Id&&saved.PetName=="pet"&&original.Secret=="old-secret","copy preserves draft and source");}
            Check(qr.StartsWith("https://open.feishu.cn/page/cli?"),"real creation QR");Check(delays.Count==3&&delays[2]>delays[1],"slow_down increases poll interval");
            await Reject("{\"error\":\"access_denied\",\"error_description\":\"must not leak\"}","im.authFailed",HttpStatusCode.BadRequest);
            await Reject("{\"error\":\"expired_token\"}","im.qrExpired",HttpStatusCode.BadRequest);
            await Reject("{\"error\":\"invalid_grant\"}","im.qrExpired",HttpStatusCode.BadRequest);
            await Reject("{\"client_id\":\"cli_fixture\"}","im.authFailed");
            await Reject("{\"client_id\":\"cli_fixture\",\"client_secret\":\"fixture-secret\",\"tenant_brand\":\"lark\"}","im.platformFailed");
            await Reject("{\"error\":\"unknown\",\"error_description\":\"must not leak\"}","im.platformFailed");
            await Reject("{}","im.httpFailed",HttpStatusCode.InternalServerError);
            await Reject("not json","im.protocolFailed");
            await Reject("sensitive auth page","im.authFailed",HttpStatusCode.Unauthorized);
            await Reject("redirect is never followed","im.httpFailed",HttpStatusCode.Redirect);
            await Reject(new string('x',ImProtocol.MaxBytes+1),"im.payloadTooLarge");
            foreach(string url in new[]{"https://evil.test/login","https://open.feishu.cn.evil.test/login","http://open.feishu.cn/login","https://user@open.feishu.cn/login","https://open.feishu.cn:444/login"}){
                var bad=new Handler{Respond=(r,t)=>Task.FromResult(Json(ImProtocol.Encode(new{device_code="d",verification_uri_complete=url})))};
                using(var client=new FeishuRegistration(Profile(),bad,NoDelay)){bool rejected=false;try{await client.LoginAsync(q=>{throw new Exception("unsafe URL displayed");},CancellationToken.None);}catch(InvalidOperationException e){rejected=e.Message=="im.endpointRejected";}Check(rejected,"reject unsafe QR");}
            }
            using(var stop=new CancellationTokenSource())using(var client=new FeishuRegistration(Profile(),new Handler{Respond=(r,t)=>Task.FromResult(Json(Begin))},NoDelay)){
                bool cancelled=false;try{await client.LoginAsync(q=>stop.Cancel(),stop.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"user cancellation after QR does not poll");
            }
            foreach(bool duringBegin in new[]{true,false})using(var stop=new CancellationTokenSource()){
                int count=0;var slow=new Handler{Respond=async(r,t)=>{if(++count==1&&!duringBegin)return Json(Begin);stop.Cancel();await Task.Delay(-1,t);return Json("{}");}};
                using(var client=new FeishuRegistration(Profile(),slow,NoDelay)){bool cancelled=false;try{await client.LoginAsync(q=>{},stop.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"in-flight request respects user cancellation");}
            }
            using(var client=new FeishuRegistration(Profile(),new Handler{Respond=(r,t)=>{throw new HttpRequestException("sensitive URL must not leak");}},NoDelay)){
                bool safe=false;try{await client.LoginAsync(q=>{},CancellationToken.None);}catch(InvalidOperationException e){safe=e.Message=="im.httpFailed";}Check(safe,"network exceptions use safe key");
            }
            int expiryCalls=0;
            using(var client=new FeishuRegistration(Profile(),new Handler{Respond=async(r,t)=>{if(++expiryCalls==1)return Json(Begin.Replace("300","1"));await Task.Delay(-1,t);return Json("{}");}},NoDelay)){
                bool expired=false;try{await client.LoginAsync(q=>{},CancellationToken.None);}catch(InvalidOperationException e){expired=e.Message=="im.qrExpired";}Check(expired&&expiryCalls==2,"expiry during HTTP poll stays QR expiry");
            }
            using(var client=new FeishuRegistration(Profile(),new Handler{Respond=(r,t)=>Task.FromResult(Json("{\"device_code\":\"d\",\"user_code\":\"fixture\",\"expires_in\":1,\"interval\":5}"))},(delay,t)=>Task.Delay(delay,t))){bool expired=false;try{await client.LoginAsync(q=>Check(q=="https://open.feishu.cn/page/cli?user_code=fixture","fallback creation URL"),CancellationToken.None);}catch(InvalidOperationException e){expired=e.Message=="im.qrExpired";}Check(expired,"local QR deadline is expiry, not user cancel");}
            Console.WriteLine("FeishuRegistrationTests: "+checks+" checks passed");
        }
        public static int Main(){try{RunAll().GetAwaiter().GetResult();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    }
}
