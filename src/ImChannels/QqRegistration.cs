using System;
using System.Collections.Generic;
using System.Net.Http;
#if !PORTABLE
using System.Runtime.InteropServices;
#endif
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu.ImChannels
{
    // QQ official bot binding returns AES-256-GCM encrypted app credentials.
    public sealed class QqRegistration:IDisposable
    {
        readonly ImChannelProfile profile;
        readonly BotRegistrationHttp http;
        readonly Func<TimeSpan,CancellationToken,Task> delay;
        readonly TimeSpan maxWait;
        readonly CancellationTokenSource shutdown=new CancellationTokenSource();
        int started,disposed;
        public QqRegistration(ImChannelProfile profile):this(profile,null,Task.Delay){}
        internal QqRegistration(ImChannelProfile profile,HttpMessageHandler handler,Func<TimeSpan,CancellationToken,Task> delay,TimeSpan? maxWait=null)
        {
            if(profile==null||profile.Kind!="qq")throw new InvalidOperationException("im.invalidConfig");
            this.profile=profile.Copy();http=new BotRegistrationHttp(handler);this.delay=delay??Task.Delay;this.maxWait=maxWait??TimeSpan.FromMinutes(5);
        }
        public async Task<ImChannelProfile> LoginAsync(Action<string> qr,CancellationToken token)
        {
            if(qr==null)throw new ArgumentNullException("qr");
            if(Volatile.Read(ref disposed)!=0)throw new ObjectDisposedException("QqRegistration");
            if(Interlocked.Exchange(ref started,1)!=0)throw new InvalidOperationException("im.authFailed");
            byte[] key=new byte[32];
            using(var lifetime=CancellationTokenSource.CreateLinkedTokenSource(token,shutdown.Token)){
                lifetime.CancelAfter(maxWait);
                try{
                    using(var random=RandomNumberGenerator.Create())random.GetBytes(key);
                    var begin=await Post("create_bind_task",new{key=Convert.ToBase64String(key)},lifetime.Token).ConfigureAwait(false);
                    string task=BotRegistrationHttp.Required(ImProtocol.Get(begin,"data"),"task_id",2048);
                    string address=BotRegistrationHttp.Qr("https://q.qq.com/qqbot/openclaw/connect.html?task_id="+Uri.EscapeDataString(task)+"&_wv=2","q.qq.com");
                    lifetime.Token.ThrowIfCancellationRequested();qr(address);
                    while(true){
                        await delay(TimeSpan.FromSeconds(2),lifetime.Token).ConfigureAwait(false);lifetime.Token.ThrowIfCancellationRequested();
                        var response=await Post("poll_bind_result",new{task_id=task},lifetime.Token).ConfigureAwait(false);
                        var result=ImProtocol.Get(response,"data");
                        switch(ImProtocol.Num(result,"status",-1)){
                            case 0:case 1:break;
                            case 2:return BotRegistrationHttp.Credentials(profile,ImProtocol.Str(result,"bot_appid"),DecryptSecret(ImProtocol.Str(result,"bot_encrypt_secret"),key));
                            case 3:throw new InvalidOperationException("im.qrExpired");
                            default:throw new InvalidOperationException("im.protocolFailed");
                        }
                    }
                }catch(OperationCanceledException){if(!token.IsCancellationRequested&&!shutdown.IsCancellationRequested)throw new InvalidOperationException("im.qrExpired");throw;}
                finally{Array.Clear(key,0,key.Length);}
            }
        }
        async Task<Dictionary<string,object>> Post(string action,object payload,CancellationToken token)
        {
            var result=await http.Post("https://q.qq.com/lite/"+action,payload,token).ConfigureAwait(false);
            if(ImProtocol.Get(result,"retcode")!=null&&ImProtocol.Num(result,"retcode",-1)!=0)throw new InvalidOperationException("im.platformFailed");return result;
        }
        internal static string DecryptSecret(string encrypted,byte[] key)
        {
            byte[] raw;try{raw=Convert.FromBase64String(encrypted);}catch(FormatException){throw new InvalidOperationException("im.authFailed");}
            if(key==null||key.Length!=32||raw.Length<=28||raw.Length>65536)throw new InvalidOperationException("im.authFailed");
            byte[] nonce=new byte[12],tag=new byte[16],cipher=new byte[raw.Length-28],plain=new byte[raw.Length-28];
            Buffer.BlockCopy(raw,0,nonce,0,nonce.Length);Buffer.BlockCopy(raw,12,cipher,0,cipher.Length);Buffer.BlockCopy(raw,raw.Length-16,tag,0,tag.Length);
#if PORTABLE
            try{
                using(var aes=new AesGcm(key,16))aes.Decrypt(nonce,cipher,tag,plain);
                try{return new UTF8Encoding(false,true).GetString(plain);}catch(DecoderFallbackException){throw new InvalidOperationException("im.authFailed");}
            }catch(CryptographicException){throw new InvalidOperationException("im.authFailed");}
            finally{Array.Clear(plain,0,plain.Length);}
#else
            IntPtr algorithm=IntPtr.Zero,handle=IntPtr.Zero,keyObject=IntPtr.Zero;int keyObjectSize=0;
            GCHandle noncePin=default(GCHandle),tagPin=default(GCHandle);
            try{
                Native.Check(Native.BCryptOpenAlgorithmProvider(out algorithm,"AES",null,0));
                byte[] mode=Encoding.Unicode.GetBytes("ChainingModeGCM\0");Native.Check(Native.BCryptSetProperty(algorithm,"ChainingMode",mode,mode.Length,0));
                byte[] size=new byte[4];int read;Native.Check(Native.BCryptGetProperty(algorithm,"ObjectLength",size,size.Length,out read,0));
                keyObjectSize=BitConverter.ToInt32(size,0);if(keyObjectSize<=0||keyObjectSize>1048576)throw new InvalidOperationException("im.authFailed");keyObject=Marshal.AllocHGlobal(keyObjectSize);
                Native.Check(Native.BCryptGenerateSymmetricKey(algorithm,out handle,keyObject,keyObjectSize,key,key.Length,0));
                noncePin=GCHandle.Alloc(nonce,GCHandleType.Pinned);tagPin=GCHandle.Alloc(tag,GCHandleType.Pinned);
                var auth=new Native.AuthInfo{Size=Marshal.SizeOf(typeof(Native.AuthInfo)),Version=1,Nonce=noncePin.AddrOfPinnedObject(),NonceLength=nonce.Length,Tag=tagPin.AddrOfPinnedObject(),TagLength=tag.Length};
                int written;Native.Check(Native.BCryptDecrypt(handle,cipher,cipher.Length,ref auth,IntPtr.Zero,0,plain,plain.Length,out written,0));
                if(written!=plain.Length)throw new InvalidOperationException("im.authFailed");
                try{return new UTF8Encoding(false,true).GetString(plain);}catch(DecoderFallbackException){throw new InvalidOperationException("im.authFailed");}
            }finally{
                if(handle!=IntPtr.Zero)Native.BCryptDestroyKey(handle);
                if(keyObject!=IntPtr.Zero){for(int i=0;i<keyObjectSize;i++)Marshal.WriteByte(keyObject,i,0);Marshal.FreeHGlobal(keyObject);}
                if(algorithm!=IntPtr.Zero)Native.BCryptCloseAlgorithmProvider(algorithm,0);
                if(noncePin.IsAllocated)noncePin.Free();if(tagPin.IsAllocated)tagPin.Free();Array.Clear(plain,0,plain.Length);
            }
#endif
        }
#if !PORTABLE
        static class Native
        {
            [StructLayout(LayoutKind.Sequential)] internal struct AuthInfo
            {
                internal int Size,Version;internal IntPtr Nonce;internal int NonceLength;internal IntPtr AuthData;internal int AuthDataLength;internal IntPtr Tag;internal int TagLength;internal IntPtr MacContext;internal int MacContextLength,AadLength;internal long DataLength;internal int Flags;
            }
            internal static void Check(int result){if(result!=0)throw new InvalidOperationException("im.authFailed");}
            [DllImport("bcrypt.dll",CharSet=CharSet.Unicode)] internal static extern int BCryptOpenAlgorithmProvider(out IntPtr algorithm,string algorithmId,string implementation,int flags);
            [DllImport("bcrypt.dll",CharSet=CharSet.Unicode)] internal static extern int BCryptSetProperty(IntPtr handle,string property,byte[] input,int length,int flags);
            [DllImport("bcrypt.dll",CharSet=CharSet.Unicode)] internal static extern int BCryptGetProperty(IntPtr handle,string property,byte[] output,int length,out int written,int flags);
            [DllImport("bcrypt.dll")] internal static extern int BCryptGenerateSymmetricKey(IntPtr algorithm,out IntPtr key,IntPtr keyObject,int keyObjectLength,byte[] secret,int secretLength,int flags);
            [DllImport("bcrypt.dll")] internal static extern int BCryptDecrypt(IntPtr key,byte[] input,int inputLength,ref AuthInfo auth,IntPtr iv,int ivLength,byte[] output,int outputLength,out int written,int flags);
            [DllImport("bcrypt.dll")] internal static extern int BCryptDestroyKey(IntPtr key);
            [DllImport("bcrypt.dll")] internal static extern int BCryptCloseAlgorithmProvider(IntPtr algorithm,int flags);
        }
#endif
        public void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;shutdown.Cancel();http.Dispose();}
    }
}
