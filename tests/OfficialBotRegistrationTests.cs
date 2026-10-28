using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dafeiyu.ImChannels;

namespace Dafeiyu.Tests
{
    public static class OfficialBotRegistrationTests
    {
        sealed class Handler:HttpMessageHandler
        {
            internal Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> Respond;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){return Respond(request,token);}
        }
        static int checks;
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
        static HttpResponseMessage Json(string json,HttpStatusCode status=HttpStatusCode.OK){return new HttpResponseMessage(status){Content=new StringContent(json)};}
        static ImChannelProfile Profile(string kind){return new ImChannelProfile{Kind=kind,Name="fixture",PetName="pet",Secret="old",AppId="old-app"};}
        static Task NoDelay(TimeSpan delay,CancellationToken token){token.ThrowIfCancellationRequested();return Task.FromResult(0);}
        const string Begin="{\"device_code\":\"fixture-device\",\"verification_uri_complete\":\"https://open-dev.dingtalk.com/registration?code=fixture\",\"expires_in\":300,\"interval\":1}";
        static async Task Failure(string kind,string response,string expected,HttpStatusCode status=HttpStatusCode.OK)
        {
            int calls=0;
            var handler=new Handler{Respond=(r,t)=>{calls++;return Task.FromResult(Json(kind=="qq"?(calls==1?"{\"data\":{\"task_id\":\"fixture\"}}":response):(calls==1?"{\"nonce\":\"fixture-nonce\"}":calls==2?Begin:response),(kind=="qq"?calls>1:calls>2)?status:HttpStatusCode.OK));}};
            bool failed=false;
            try{if(kind=="qq")using(var login=new QqRegistration(Profile(kind),handler,NoDelay)){await login.LoginAsync(q=>{},CancellationToken.None);}else using(var login=new DingTalkRegistration(Profile(kind),handler,NoDelay)){await login.LoginAsync(q=>{},CancellationToken.None);}}
            catch(InvalidOperationException e){failed=e.Message==expected;}
            Check(failed,kind+" failure "+expected);
        }
        static async Task DingTalk()
        {
            int calls=0;var original=Profile("dingtalk");string qr="";
            var handler=new Handler{Respond=async(r,t)=>{
                Check(r.Method==HttpMethod.Post&&r.RequestUri.Scheme=="https"&&r.RequestUri.Host=="oapi.dingtalk.com","DingTalk official HTTPS endpoint");
                var body=ImProtocol.Json(await r.Content.ReadAsStringAsync());calls++;
                if(calls==1){Check(r.RequestUri.AbsolutePath=="/app/registration/init"&&ImProtocol.Str(body,"source")=="DING_DWS_CLAW","DingTalk init source");return Json("{\"errcode\":0,\"nonce\":\"fixture-nonce\"}");}
                if(calls==2){Check(r.RequestUri.AbsolutePath=="/app/registration/begin"&&ImProtocol.Str(body,"nonce")=="fixture-nonce","DingTalk begin nonce");return Json(Begin);}
                Check(r.RequestUri.AbsolutePath=="/app/registration/poll"&&ImProtocol.Str(body,"device_code")=="fixture-device","DingTalk poll device");
                if(calls<5)return Json("{\"status\":\""+(calls==3?"WAITING":"CREATING")+"\"}");
                return Json("{\"status\":\"SUCCESS\",\"client_id\":\"fixture-app\",\"client_secret\":\"fixture-secret\"}");
            }};
            using(var login=new DingTalkRegistration(original,handler,NoDelay)){var saved=await login.LoginAsync(q=>qr=q,CancellationToken.None);Check(saved.AppId=="fixture-app"&&saved.Secret=="fixture-secret"&&saved.Id==original.Id&&saved.PetName=="pet"&&original.Secret=="old","DingTalk returns a credential copy");}
            Check(calls==5&&qr=="https://open-dev.dingtalk.com/registration?code=fixture","DingTalk QR and pending states");
            await Failure("dingtalk","{\"status\":\"FAIL\",\"fail_reason\":\"secret data\"}","im.authFailed");
            await Failure("dingtalk","{\"status\":\"EXPIRED\"}","im.qrExpired");
            await Failure("dingtalk","{\"status\":\"SUCCESS\",\"client_id\":\"fixture\"}","im.authFailed");
            await Failure("dingtalk","{\"status\":\"MYSTERY\"}","im.protocolFailed");
            await Failure("dingtalk","{\"errcode\":123,\"errmsg\":\"secret data\"}","im.platformFailed");
            await Failure("dingtalk","not json","im.protocolFailed");
            await Failure("dingtalk","{}","im.httpFailed",HttpStatusCode.InternalServerError);
            foreach(string url in new[]{"https://evil.test/login","https://dingtalk.com.evil.test/login","http://dingtalk.com/login","https://user@dingtalk.com/login","https://dingtalk.com:444/login","https://dingtalk.com/login#fragment"}){
                int count=0;var bad=new Handler{Respond=(r,t)=>Task.FromResult(Json(++count==1?"{\"nonce\":\"n\"}":ImProtocol.Encode(new{device_code="d",verification_uri_complete=url})))};
                using(var login=new DingTalkRegistration(Profile("dingtalk"),bad,NoDelay)){bool rejected=false;try{await login.LoginAsync(q=>{throw new Exception("unsafe QR displayed");},CancellationToken.None);}catch(InvalidOperationException e){rejected=e.Message=="im.endpointRejected";}Check(rejected,"reject unsafe DingTalk QR");}
            }
        }
        static async Task Qq()
        {
            int calls=0;string qr="";var original=Profile("qq");
            var handler=new Handler{Respond=async(r,t)=>{
                Check(r.Method==HttpMethod.Post&&r.RequestUri.Host=="q.qq.com"&&r.RequestUri.Scheme=="https","QQ official HTTPS endpoint");
                var body=ImProtocol.Json(await r.Content.ReadAsStringAsync());calls++;
                if(calls==1){Check(r.RequestUri.AbsolutePath=="/lite/create_bind_task"&&Convert.FromBase64String(ImProtocol.Str(body,"key")).Length==32,"QQ random AES256 bind key");return Json("{\"retcode\":0,\"data\":{\"task_id\":\"fixture&redirect=https://evil.test\"}}");}
                Check(r.RequestUri.AbsolutePath=="/lite/poll_bind_result"&&ImProtocol.Str(body,"task_id")=="fixture&redirect=https://evil.test","QQ poll binds task id");
                if(calls==2)return Json("{\"data\":{\"status\":0}}");
                if(calls==3)return Json("{\"data\":{\"status\":1}}");
                return Json("{\"data\":{\"status\":3}}");
            }};
            using(var login=new QqRegistration(original,handler,NoDelay)){bool expired=false;try{await login.LoginAsync(q=>qr=q,CancellationToken.None);}catch(InvalidOperationException e){expired=e.Message=="im.qrExpired";}Check(expired,"QQ expiry state");}
            Check(qr=="https://q.qq.com/qqbot/openclaw/connect.html?task_id=fixture%26redirect%3Dhttps%3A%2F%2Fevil.test&_wv=2","QQ task URL cannot inject host or query");
            await Failure("qq","{\"retcode\":123,\"msg\":\"secret data\"}","im.platformFailed");
            await Failure("qq","{\"data\":{\"status\":2,\"bot_appid\":\"fixture\"}}","im.authFailed");
            await Failure("qq","{\"data\":{\"status\":2,\"bot_appid\":\"fixture\",\"bot_encrypt_secret\":\"malformed\"}}","im.authFailed");
            await Failure("qq","{\"data\":{\"status\":99}}","im.protocolFailed");
            await Failure("qq","not json","im.protocolFailed");
            await Failure("qq","{}","im.httpFailed",HttpStatusCode.InternalServerError);
            // NIST AES-256-GCM known-answer vector: 96-bit zero nonce, 128-bit plaintext.
            byte[] key=new byte[32];
            string ciphertext="cea7403d4d606b6e074ec5d3baf39d18",tag="d0d1c8a799996bf0265b98b5d48ab919";
            byte[] raw=new byte[44];for(int i=0;i<16;i++){raw[12+i]=Convert.ToByte(ciphertext.Substring(i*2,2),16);raw[28+i]=Convert.ToByte(tag.Substring(i*2,2),16);}
            Check(QqRegistration.DecryptSecret(Convert.ToBase64String(raw),key)==new string('\0',16),"CNG AES256 GCM NIST vector");
            raw[43]^=1;bool invalid=false;try{QqRegistration.DecryptSecret(Convert.ToBase64String(raw),key);}catch(InvalidOperationException e){invalid=e.Message=="im.authFailed";}Check(invalid,"CNG rejects tampered authentication tag");
            calls=0;byte[] bindKey=null;
            handler=new Handler{Respond=async(r,t)=>{
                if(++calls==1){bindKey=Convert.FromBase64String(ImProtocol.Str(ImProtocol.Json(await r.Content.ReadAsStringAsync()),"key"));return Json("{\"data\":{\"task_id\":\"fixture\"}}");}
                return Json(ImProtocol.Encode(new{data=new{status=2,bot_appid="fixture-app",bot_encrypt_secret=EncryptFixture("fixture-secret",bindKey)}}));
            }};
            using(var login=new QqRegistration(original,handler,NoDelay)){var result=await login.LoginAsync(q=>{},CancellationToken.None);Check(result.AppId=="fixture-app"&&result.Secret=="fixture-secret"&&result.Id==original.Id&&original.Secret=="old","QQ random-key encrypted HTTP result returns credential copy");}
            byte[] secondKey=null;
            handler=new Handler{Respond=async(r,t)=>{secondKey=Convert.FromBase64String(ImProtocol.Str(ImProtocol.Json(await r.Content.ReadAsStringAsync()),"key"));return Json("{\"data\":{\"task_id\":\"fixture\"}}");}};
            using(var stop=new CancellationTokenSource())using(var login=new QqRegistration(Profile("qq"),handler,NoDelay)){try{await login.LoginAsync(q=>stop.Cancel(),stop.Token);}catch(OperationCanceledException){}}
            Check(Convert.ToBase64String(bindKey)!=Convert.ToBase64String(secondKey),"QQ registration creates a fresh binding key");
        }
        [StructLayout(LayoutKind.Sequential)] struct FixtureAuth
        {
            internal int Size,Version;internal IntPtr Nonce;internal int NonceLength;internal IntPtr Auth;internal int AuthLength;internal IntPtr Tag;internal int TagLength;internal IntPtr Mac;internal int MacLength,AadLength;internal long DataLength;internal int Flags;
        }
        [DllImport("bcrypt.dll",CharSet=CharSet.Unicode)] static extern int BCryptOpenAlgorithmProvider(out IntPtr algorithm,string id,string implementation,int flags);
        [DllImport("bcrypt.dll",CharSet=CharSet.Unicode)] static extern int BCryptSetProperty(IntPtr handle,string property,byte[] input,int length,int flags);
        [DllImport("bcrypt.dll")] static extern int BCryptGenerateSymmetricKey(IntPtr algorithm,out IntPtr key,IntPtr keyObject,int objectLength,byte[] secret,int secretLength,int flags);
        [DllImport("bcrypt.dll")] static extern int BCryptEncrypt(IntPtr key,byte[] input,int length,ref FixtureAuth auth,IntPtr iv,int ivLength,byte[] output,int capacity,out int written,int flags);
        [DllImport("bcrypt.dll")] static extern int BCryptDestroyKey(IntPtr key);
        [DllImport("bcrypt.dll")] static extern int BCryptCloseAlgorithmProvider(IntPtr algorithm,int flags);
        static string EncryptFixture(string secret,byte[] key)
        {
#if NET10_0_OR_GREATER
            byte[] nonce=new byte[12],tag=new byte[16],plain=Encoding.UTF8.GetBytes(secret),cipher=new byte[Encoding.UTF8.GetByteCount(secret)];
            using(var aes=new System.Security.Cryptography.AesGcm(key,16))aes.Encrypt(nonce,plain,cipher,tag);
            byte[] raw=new byte[12+cipher.Length+16];Buffer.BlockCopy(nonce,0,raw,0,12);Buffer.BlockCopy(cipher,0,raw,12,cipher.Length);Buffer.BlockCopy(tag,0,raw,12+cipher.Length,16);return Convert.ToBase64String(raw);
#else
            IntPtr algorithm=IntPtr.Zero,handle=IntPtr.Zero;byte[] nonce=new byte[12],tag=new byte[16],plain=Encoding.UTF8.GetBytes(secret),cipher=new byte[Encoding.UTF8.GetByteCount(secret)];
            GCHandle noncePin=default(GCHandle),tagPin=default(GCHandle);
            try{
                Check(BCryptOpenAlgorithmProvider(out algorithm,"AES",null,0)==0,"fixture AES provider");byte[] mode=Encoding.Unicode.GetBytes("ChainingModeGCM\0");
                Check(BCryptSetProperty(algorithm,"ChainingMode",mode,mode.Length,0)==0,"fixture GCM mode");
                Check(BCryptGenerateSymmetricKey(algorithm,out handle,IntPtr.Zero,0,key,key.Length,0)==0,"fixture bind key");
                noncePin=GCHandle.Alloc(nonce,GCHandleType.Pinned);tagPin=GCHandle.Alloc(tag,GCHandleType.Pinned);
                var auth=new FixtureAuth{Size=Marshal.SizeOf(typeof(FixtureAuth)),Version=1,Nonce=noncePin.AddrOfPinnedObject(),NonceLength=12,Tag=tagPin.AddrOfPinnedObject(),TagLength=16};int written;
                Check(BCryptEncrypt(handle,plain,plain.Length,ref auth,IntPtr.Zero,0,cipher,cipher.Length,out written,0)==0&&written==cipher.Length,"fixture authenticated ciphertext");
                byte[] raw=new byte[12+cipher.Length+16];Buffer.BlockCopy(nonce,0,raw,0,12);Buffer.BlockCopy(cipher,0,raw,12,cipher.Length);Buffer.BlockCopy(tag,0,raw,12+cipher.Length,16);return Convert.ToBase64String(raw);
            }finally{if(handle!=IntPtr.Zero)BCryptDestroyKey(handle);if(algorithm!=IntPtr.Zero)BCryptCloseAlgorithmProvider(algorithm,0);if(noncePin.IsAllocated)noncePin.Free();if(tagPin.IsAllocated)tagPin.Free();}
#endif
        }
        static async Task CancellationAndDeadline(string kind)
        {
            int calls=0;var handler=new Handler{Respond=(r,t)=>{calls++;return Task.FromResult(Json(kind=="qq"?"{\"data\":{\"task_id\":\"fixture\"}}":calls==1?"{\"nonce\":\"fixture\"}":Begin));}};
            using(var stop=new CancellationTokenSource()){
                bool cancelled=false;try{if(kind=="qq")using(var login=new QqRegistration(Profile(kind),handler,NoDelay)){await login.LoginAsync(q=>stop.Cancel(),stop.Token);}else using(var login=new DingTalkRegistration(Profile(kind),handler,NoDelay)){await login.LoginAsync(q=>stop.Cancel(),stop.Token);}}catch(OperationCanceledException){cancelled=true;}
                Check(cancelled&&calls==(kind=="qq"?1:2),kind+" cancellation after QR never polls");
            }
            calls=0;handler=new Handler{Respond=(r,t)=>{calls++;return Task.FromResult(Json(kind=="qq"?"{\"data\":{\"task_id\":\"fixture\"}}":calls==1?"{\"nonce\":\"fixture\"}":Begin));}};
            bool expired=false;try{if(kind=="qq")using(var login=new QqRegistration(Profile(kind),handler,Task.Delay,TimeSpan.FromMilliseconds(50))){await login.LoginAsync(q=>{},CancellationToken.None);}else using(var login=new DingTalkRegistration(Profile(kind),handler,Task.Delay,TimeSpan.FromMilliseconds(50))){await login.LoginAsync(q=>{},CancellationToken.None);}}catch(InvalidOperationException e){expired=e.Message=="im.qrExpired";}
            Check(expired&&calls==(kind=="qq"?1:2),kind+" local deadline stops polling");
            handler=new Handler{Respond=async(r,t)=>{await Task.Delay(Timeout.Infinite,t);return Json("{}");}};
            using(var stop=new CancellationTokenSource(50)){
                bool cancelled=false;try{if(kind=="qq")using(var login=new QqRegistration(Profile(kind),handler,NoDelay)){await login.LoginAsync(q=>{},stop.Token);}else using(var login=new DingTalkRegistration(Profile(kind),handler,NoDelay)){await login.LoginAsync(q=>{},stop.Token);}}catch(OperationCanceledException){cancelled=true;}Check(cancelled,kind+" in-flight HTTP cancellation");
            }
        }
        public static async Task RunAll(){await DingTalk();await Qq();await CancellationAndDeadline("qq");await CancellationAndDeadline("dingtalk");Console.WriteLine("OfficialBotRegistrationTests: "+checks+" checks passed");}
        public static int Main(){try{RunAll().GetAwaiter().GetResult();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    }
}
