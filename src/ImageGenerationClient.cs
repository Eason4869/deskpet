using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Dafeiyu
{
    public sealed class ImageGenerationClient : IDisposable
    {
        const int MaximumResponseBytes=72*1024*1024;
        readonly GeneratedImageStore store;
        readonly HttpMessageHandler serviceHandler,downloadHandler;
        readonly CancellationTokenSource lifetime=new CancellationTokenSource();
        bool disposed;
        public ImageGenerationClient(GeneratedImageStore store):this(store,null,null){}
        public ImageGenerationClient(GeneratedImageStore store,HttpMessageHandler serviceHandler,HttpMessageHandler downloadHandler)
        {if(store==null)throw new ArgumentNullException("store");this.store=store;this.serviceHandler=serviceHandler;this.downloadHandler=downloadHandler;}
        public async Task<GeneratedImage> GenerateAsync(ImageGenerationRequest request,CancellationToken token)
        {
            if(disposed)throw new ObjectDisposedException("ImageGenerationClient");
            if(request==null || request.Provider==null)throw new InvalidOperationException("请先配置并选择图片生成服务。");
            var provider=request.Provider.Copy();provider.Normalize();
            string prompt=(request.Prompt??"").Trim();if(prompt.Length==0 || prompt.Length>16000)throw new InvalidOperationException("请填写图片提示词，最多 16,000 字。");
            Uri endpoint;if(!Uri.TryCreate(provider.Connection.BaseUrl,UriKind.Absolute,out endpoint) || (endpoint.Scheme!="http" && endpoint.Scheme!="https") || endpoint.UserInfo.Length>0 || endpoint.Query.Length>0 || endpoint.Fragment.Length>0)throw new InvalidOperationException("图片服务地址无效，请填写 HTTP 或 HTTPS Base URL。");
            if(String.IsNullOrWhiteSpace(provider.Connection.Model))throw new InvalidOperationException("请填写图片生成模型名称。");
            string size=request.Size==null?provider.Size:request.Size.Trim(),quality=request.Quality==null?provider.Quality:request.Quality.Trim();if(size.Length>64 || quality.Length>64)throw new InvalidOperationException("图片尺寸或质量参数过长。");
            byte[] reference=request.ReferenceImageBytes==null?null:(byte[])request.ReferenceImageBytes.Clone();GeneratedImageFormat referenceFormat=null;
            if(reference!=null){if(!provider.SupportsEdits)throw new InvalidOperationException("当前图片服务未启用参考图编辑，请在模型配置中确认编辑能力。");referenceFormat=GeneratedImageValidation.Validate(reference);if(!String.IsNullOrEmpty(request.ReferenceMediaType) && request.ReferenceMediaType!=referenceFormat.MediaType)throw new InvalidDataException("参考图类型与实际图片格式不符。");}
            // Snapshot all mutable input before the first await; completion belongs to its origin.
            var metadata=new GeneratedImage{Prompt=prompt,ProviderId=provider.Id,Model=provider.Connection.Model,Size=size,Quality=quality,SessionId=request.SessionId,TaskId=request.TaskId,MessageId=request.MessageId,ReferenceImageId=request.ReferenceImageId};bool persistent=request.Persistent;
            using(var cancel=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token)){
                cancel.CancelAfter(TimeSpan.FromSeconds(provider.TimeoutSeconds));
                try{
                    byte[] bytes;
                    using(var message=ImageGenerationProtocols.CreateRequest(provider,endpoint,prompt,size,quality,reference,referenceFormat==null?null:referenceFormat.MediaType))using(var http=ServiceHttp(provider)){
                        using(var response=await http.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,cancel.Token).ConfigureAwait(false)){
                            CheckStatus(response,false);byte[] payload=await ReadLimited(response.Content,MaximumResponseBytes,cancel.Token).ConfigureAwait(false);Dictionary<string,object> parsed;
                            try{parsed=new JavaScriptSerializer{MaxJsonLength=MaximumResponseBytes,RecursionLimit=32}.DeserializeObject(Encoding.UTF8.GetString(payload)) as Dictionary<string,object>;}catch(ArgumentException){throw new InvalidDataException("图片服务响应不是有效的 JSON。");}
                            var result=ImageGenerationProtocols.Parse(provider.Protocol,parsed);string base64=result.Base64;
                            if(!String.IsNullOrEmpty(base64)){
                                if(base64.Length>(GeneratedImageStore.MaximumBytes+2L)/3*4)throw new InvalidDataException("生成图片超过 50 MB。");
                                try{bytes=Convert.FromBase64String(base64);}catch(FormatException){throw new InvalidDataException("图片服务返回的图片编码无效。");}
                            }else{
                                Uri address=await ImageDownloadSecurity.ValidateAddressAsync(result.Url,endpoint,cancel.Token).ConfigureAwait(false);
                                using(var download=DownloadHttp(provider))using(var get=new HttpRequestMessage(HttpMethod.Get,address))using(var output=await download.SendAsync(get,HttpCompletionOption.ResponseHeadersRead,cancel.Token).ConfigureAwait(false)){
                                    CheckStatus(output,true);string type=output.Content.Headers.ContentType==null?"":output.Content.Headers.ContentType.MediaType;
                                    if(type!="image/png" && type!="image/jpeg" && type!="image/gif" && type!="image/webp")throw new InvalidDataException("图片下载返回了不支持的图片类型。");
                                    bytes=await ReadLimited(output.Content,GeneratedImageStore.MaximumBytes,cancel.Token).ConfigureAwait(false);var actual=GeneratedImageValidation.Validate(bytes);if(actual.MediaType!=type)throw new InvalidDataException("图片下载类型与实际图片格式不符。");
                                }
                            }
                            if(!String.IsNullOrEmpty(result.MediaType) && GeneratedImageValidation.Validate(bytes).MediaType!=result.MediaType)throw new InvalidDataException("图片服务响应类型与实际图片格式不符。");
                        }
                    }
                    cancel.Token.ThrowIfCancellationRequested();
                    GeneratedImageValidation.Validate(bytes);
                    if(reference!=null){
                        bool existing=ReferenceMatches(metadata.ReferenceImageId,reference,persistent);
                        if(!existing)metadata.ReferenceImageId=store.SaveReference(reference,new GeneratedImage{Prompt=prompt,ProviderId=provider.Id,Model=provider.Connection.Model,SessionId=metadata.SessionId,TaskId=metadata.TaskId,MessageId=metadata.MessageId},persistent).Id;
                    }
                    try{return store.Save(bytes,metadata,persistent);}catch(IOException){throw new IOException("图片保存失败，原文件如已落盘可在图库恢复；请检查存储空间和权限。");}catch(UnauthorizedAccessException){throw new IOException("图片保存失败，请检查存储目录权限。");}
                }catch(OperationCanceledException){if(token.IsCancellationRequested || lifetime.IsCancellationRequested)throw new OperationCanceledException("图片生成已取消。",token);throw new TimeoutException("图片生成超时，请检查服务状态或增加超时时间。");}
                catch(HttpRequestException){cancel.Token.ThrowIfCancellationRequested();throw new IOException("无法连接图片服务或下载图片，请检查服务地址、网络及独立代理设置。");}
            }
        }
        bool ReferenceMatches(string id,byte[] reference,bool persistent)
        {
            if(String.IsNullOrEmpty(id))return false;
            try{var image=store.Get(id);if(image==null || (persistent && image.Temporary))return false;byte[] bytes=store.Read(id);if(bytes.Length!=reference.Length)return false;for(int i=0;i<bytes.Length;i++)if(bytes[i]!=reference[i])return false;return true;}
            catch(ArgumentException){return false;}catch(IOException){return false;}
        }
        HttpClient ServiceHttp(ImageGenerationProvider provider)
        {
            if(serviceHandler!=null)return new HttpClient(serviceHandler,false){Timeout=Timeout.InfiniteTimeSpan};
            var handler=new HttpClientHandler{UseCookies=false,AllowAutoRedirect=false,UseProxy=provider.Connection.UseProxy};if(provider.Connection.UseProxy)handler.Proxy=new WebProxy(ProxyConfiguration.Endpoint(provider.Connection.ProxyAddress),false);
            return new HttpClient(handler){Timeout=Timeout.InfiniteTimeSpan};
        }
        HttpClient DownloadHttp(ImageGenerationProvider provider)
        {
            if(downloadHandler!=null)return new HttpClient(downloadHandler,false){Timeout=Timeout.InfiniteTimeSpan};
            var handler=new HttpClientHandler{UseCookies=false,AllowAutoRedirect=false,UseProxy=provider.Connection.UseProxy};if(provider.Connection.UseProxy)handler.Proxy=new WebProxy(ProxyConfiguration.Endpoint(provider.Connection.ProxyAddress),false);
            return new HttpClient(handler){Timeout=Timeout.InfiniteTimeSpan};
        }
        static void CheckStatus(HttpResponseMessage response,bool download)
        {
            int code=(int)response.StatusCode;if(code>=200 && code<300)return;
            if(code==401 || code==403)throw new InvalidOperationException("图片服务鉴权失败（"+code+"），请检查 API Key 和模型权限。");
            if(code==429)throw new InvalidOperationException("图片服务限流（429），请稍后手动重试。");
            if(code>=300 && code<400)throw new InvalidDataException("图片"+(download?"下载":"服务")+"地址发生重定向；为保护密钥与本机网络，请使用直接地址。");
            throw new InvalidOperationException("图片"+(download?"下载":"生成")+"失败（HTTP "+code+"），请检查服务状态。");
        }
        internal static async Task<byte[]> ReadLimited(HttpContent content,int maximum,CancellationToken token)
        {
            if(content==null)throw new InvalidDataException("图片服务响应为空。");
            if(content.Headers.ContentLength.HasValue && content.Headers.ContentLength.Value>maximum)throw new InvalidDataException("图片服务响应超过大小限制。");
            using(var input=await content.ReadAsStreamAsync().ConfigureAwait(false))using(var registration=token.Register(delegate{input.Dispose();}))using(var memory=new MemoryStream()){
                byte[] buffer=new byte[32768];try{while(true){token.ThrowIfCancellationRequested();int read=await input.ReadAsync(buffer,0,buffer.Length,token).ConfigureAwait(false);if(read==0)break;if(memory.Length+read>maximum)throw new InvalidDataException("图片服务响应超过大小限制。");memory.Write(buffer,0,read);}}catch(ObjectDisposedException){token.ThrowIfCancellationRequested();throw;}catch(IOException){token.ThrowIfCancellationRequested();throw;}return memory.ToArray();
            }
        }
        public void Dispose(){if(disposed)return;disposed=true;lifetime.Cancel();if(serviceHandler!=null)serviceHandler.Dispose();if(downloadHandler!=null && downloadHandler!=serviceHandler)downloadHandler.Dispose();/* Keep the CTS alive for canceled in-flight operations to observe its token. */}
    }
}
