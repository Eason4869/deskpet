using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu.ImChannels
{
    public interface IImAdapter:IDisposable
    {
        Task RunAsync(Func<ImMessage,bool> receive,Action<string> status,CancellationToken token);
        Task SendAsync(ImMessage message,string text,CancellationToken token);
    }
    public abstract class ImAdapter:IImAdapter
    {
        protected readonly ImChannelProfile Profile;
        readonly HttpClient http;
        protected ClientWebSocket Socket;readonly SemaphoreSlim sendGate=new SemaphoreSlim(1,1);
        protected Action<string> Status=delegate{};protected Func<ImMessage,bool> Receive=delegate{return false;};
        protected ImAdapter(ImChannelProfile profile,HttpMessageHandler handler=null){Profile=profile.Copy();http=new HttpClient(handler??new HttpClientHandler{AllowAutoRedirect=false}){Timeout=Timeout.InfiniteTimeSpan};}
        public abstract Task RunAsync(Func<ImMessage,bool> receive,Action<string> status,CancellationToken token);
        public abstract Task SendAsync(ImMessage message,string text,CancellationToken token);
        protected async Task<Dictionary<string,object>> Request(string address,object body,CancellationToken token,string auth=null,IDictionary<string,string> headers=null,int seconds=30)
        {
            using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)){
                deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
                using(var request=new HttpRequestMessage(body==null?HttpMethod.Get:HttpMethod.Post,ImProtocol.Endpoint(address,Profile.Kind))){
                    request.Headers.TryAddWithoutValidation("User-Agent","DeskPet/"+ReleaseInfo.DisplayVersion);request.Headers.TryAddWithoutValidation("Accept","application/json");
                    if(auth!=null)request.Headers.TryAddWithoutValidation("Authorization",auth);
                    if(headers!=null)foreach(var pair in headers)request.Headers.TryAddWithoutValidation(pair.Key,pair.Value);
                    if(body!=null)request.Content=new StringContent(ImProtocol.Encode(body),Encoding.UTF8,"application/json");
                    using(var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token).ConfigureAwait(false)){
                        if(!response.IsSuccessStatusCode)throw new InvalidOperationException(response.StatusCode==System.Net.HttpStatusCode.Unauthorized||response.StatusCode==System.Net.HttpStatusCode.Forbidden?"im.authFailed":"im.httpFailed");
                        using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var data=new MemoryStream()){
                            var buffer=new byte[8192];int count;while((count=await input.ReadAsync(buffer,0,buffer.Length,deadline.Token).ConfigureAwait(false))>0){if(data.Length+count>ImProtocol.MaxBytes)throw new InvalidOperationException("im.payloadTooLarge");data.Write(buffer,0,count);}return ImProtocol.Json(new UTF8Encoding(false,true).GetString(data.ToArray()));
                        }
                    }
                }
            }
        }
        protected static void Success(object result,string field="code"){if(ImProtocol.Num(result,field)!=0)throw new InvalidOperationException("im.platformFailed");}
        protected async Task Connect(string address,CancellationToken token)
        {
            var endpoint=ImProtocol.Endpoint(address,Profile.Kind);if(endpoint.Scheme!="wss")throw new InvalidOperationException("im.endpointRejected");
            Socket=new ClientWebSocket();Socket.Options.KeepAliveInterval=TimeSpan.FromSeconds(20);
            using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)){deadline.CancelAfter(TimeSpan.FromSeconds(30));await Socket.ConnectAsync(endpoint,deadline.Token).ConfigureAwait(false);}
        }
        protected Task Send(object data,CancellationToken token){return SendBytes(Encoding.UTF8.GetBytes(ImProtocol.Encode(data)),WebSocketMessageType.Text,token);}
        protected async Task SendBytes(byte[] data,WebSocketMessageType type,CancellationToken token)
        {
            using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)){deadline.CancelAfter(TimeSpan.FromSeconds(15));await sendGate.WaitAsync(deadline.Token).ConfigureAwait(false);try{if(Socket==null||Socket.State!=WebSocketState.Open)throw new InvalidOperationException("im.disconnected");await Socket.SendAsync(new ArraySegment<byte>(data),type,true,deadline.Token).ConfigureAwait(false);}finally{sendGate.Release();}}
        }
        protected async Task<byte[]> Read(CancellationToken token)
        {
            using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)){deadline.CancelAfter(TimeSpan.FromSeconds(100));
                using(var data=new MemoryStream()){var buffer=new byte[8192];WebSocketReceiveResult result;
                    do{result=await Socket.ReceiveAsync(new ArraySegment<byte>(buffer),deadline.Token).ConfigureAwait(false);if(result.MessageType==WebSocketMessageType.Close)throw new InvalidOperationException("im.disconnected");if(data.Length+result.Count>ImProtocol.MaxBytes)throw new InvalidOperationException("im.payloadTooLarge");data.Write(buffer,0,result.Count);}while(!result.EndOfMessage);return data.ToArray();}
            }
        }
        protected async Task Pump(Func<byte[],Task> frame,Func<Task> heartbeat,Func<int> interval,CancellationToken token)
        {
            using(var lifetime=CancellationTokenSource.CreateLinkedTokenSource(token)){
                var beat=Task.Run(async delegate{while(true){await Task.Delay(Math.Max(1000,interval()),lifetime.Token).ConfigureAwait(false);try{await heartbeat().ConfigureAwait(false);}catch{Socket.Abort();throw;}}});
                Exception failure=null;
                try{while(true){lifetime.Token.ThrowIfCancellationRequested();await frame(await Read(lifetime.Token).ConfigureAwait(false)).ConfigureAwait(false);}}
                catch(Exception error){failure=error;}
                lifetime.Cancel();Socket.Abort();try{await beat.ConfigureAwait(false);}catch(OperationCanceledException){}catch{if(failure==null)throw;}
                if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        protected static Dictionary<string,object> ParseBytes(byte[] bytes){return ImProtocol.Json(new UTF8Encoding(false,true).GetString(bytes));}
        protected static string Limited(string text,int limit=12000){return text.Length<=limit?text:text.Substring(0,limit);}
        public virtual void Dispose(){if(Socket!=null){Socket.Abort();Socket.Dispose();}http.Dispose();sendGate.Dispose();}
    }
}
