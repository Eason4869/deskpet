using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
namespace Dafeiyu
{
    internal sealed class HtmlFilePreview:Grid,IDisposable
    {
        readonly WebView2 browser=new WebView2();readonly string path,root,host="deskpet-"+Guid.NewGuid().ToString("N")+".invalid";readonly bool external;bool disposed;CoreWebView2Environment environment;TaskCompletionSource<bool> navigation;
        internal HtmlFilePreview(string path,bool external){this.path=path;root=Path.GetDirectoryName(Path.GetFullPath(path));this.external=external;Children.Add(browser);}
        internal async Task InitializeAsync()
        {
            if(root.TrimEnd('\\','/')==Path.GetPathRoot(root).TrimEnd('\\','/'))throw new InvalidOperationException(TextResources.Get("htmlFilePreview.initializeAsync.text"));
            WebPreviewRuntime.ConfigureNativeLoader();
            environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(WebPreviewRuntime.DirectoryPath,"profile"),new CoreWebView2EnvironmentOptions{AllowSingleSignOnUsingOSPrimaryAccount=false});if(disposed)return;
            await browser.EnsureCoreWebView2Async(environment);if(disposed)return;var web=browser.CoreWebView2;web.Settings.AreHostObjectsAllowed=false;web.Settings.IsWebMessageEnabled=false;web.Settings.AreDefaultScriptDialogsEnabled=false;web.Settings.AreDevToolsEnabled=false;web.Settings.IsStatusBarEnabled=false;
            web.LaunchingExternalUriScheme+=delegate(object sender,CoreWebView2LaunchingExternalUriSchemeEventArgs e){e.Cancel=true;};web.PermissionRequested+=delegate(object sender,CoreWebView2PermissionRequestedEventArgs e){e.State=CoreWebView2PermissionState.Deny;};web.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;};web.DownloadStarting+=delegate(object sender,CoreWebView2DownloadStartingEventArgs e){e.Cancel=true;};
            web.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e){Uri uri;if(e.Uri!="about:blank"&&(!Uri.TryCreate(e.Uri,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.Host!=host))e.Cancel=true;};
            // File previews do not need peer connections. Apply before scripts in
            // every frame; HTTP interception alone cannot constrain STUN/ICE.
            await web.AddScriptToExecuteOnDocumentCreatedAsync("for(const key of ['RTCPeerConnection','webkitRTCPeerConnection']){Object.defineProperty(globalThis,key,{value:undefined,writable:false,configurable:false});}");if(disposed)return;
            web.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All,CoreWebView2WebResourceRequestSourceKinds.All);web.WebResourceRequested+=ResourceRequested;
            navigation=new TaskCompletionSource<bool>();EventHandler<CoreWebView2NavigationCompletedEventArgs> completed=null;completed=delegate(object sender,CoreWebView2NavigationCompletedEventArgs e){web.NavigationCompleted-=completed;if(e.IsSuccess)navigation.TrySetResult(true);else navigation.TrySetException(new IOException(TextResources.Get("htmlFilePreview.initializeAsync.navigationTrySetException")));};web.NavigationCompleted+=completed;
            web.Navigate("https://"+host+"/"+Uri.EscapeDataString(Path.GetFileName(path)));await navigation.Task;
        }
        void ResourceRequested(object sender,CoreWebView2WebResourceRequestedEventArgs e)
        {
            try{
                Uri uri;if(!Uri.TryCreate(e.Request.Uri,UriKind.Absolute,out uri)){Block(e);return;}
                if(uri.Scheme=="https"&&uri.Host==host){string file=ResolveResource(root,uri.AbsolutePath);if(file==null){Block(e);return;}byte[] bytes;using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read)){ValidateOpenedFile(stream,file);if(stream.Length>32*1024*1024){Block(e);return;}using(var memory=new MemoryStream()){stream.CopyTo(memory);bytes=memory.ToArray();}}
                    string sources=external?"'self' https: http: data: blob:":"'self' data: blob:";
                    string policy="default-src "+sources+"; script-src "+sources+" 'unsafe-inline' 'unsafe-eval'; style-src "+sources+" 'unsafe-inline'; connect-src "+(external?"'self' https: http: wss: ws:":"'self'")+"; object-src 'none'; base-uri 'self'; form-action 'none'; webrtc 'block'";
                    e.Response=environment.CreateWebResourceResponse(new MemoryStream(bytes,false),200,"OK","Content-Type: "+Mime(file)+"\r\nContent-Security-Policy: "+policy+"\r\nX-Content-Type-Options: nosniff\r\nCache-Control: no-store");return;}
                if(!external||uri.Scheme!="http"&&uri.Scheme!="https")Block(e);
            }catch(Exception error){if(!(error is IOException||error is UnauthorizedAccessException||error is ArgumentException||error is NotSupportedException))throw;Block(e);}
        }
        void Block(CoreWebView2WebResourceRequestedEventArgs e){e.Response=environment.CreateWebResourceResponse(new MemoryStream(new byte[0]),403,"Blocked","Content-Type: text/plain");}
        internal static string ResolveResource(string root,string urlPath)
        {
            try{string relative=Uri.UnescapeDataString(urlPath).TrimStart('/');if(relative.Contains('\\')||relative.Contains(':')||relative.Split('/').Any(s=>s.StartsWith(".",StringComparison.Ordinal)))return null;string file=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));string prefix=Path.GetFullPath(root).TrimEnd('\\','/')+Path.DirectorySeparatorChar;if(!file.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||!File.Exists(file))return null;for(FileSystemInfo item=new FileInfo(file);item!=null;item=item is FileInfo?(FileSystemInfo)((FileInfo)item).Directory:((DirectoryInfo)item).Parent)if((item.Attributes&FileAttributes.ReparsePoint)!=0)return null;
                if(!new[]{".html",".htm",".css",".js",".mjs",".json",".txt",".png",".jpg",".jpeg",".gif",".webp",".svg",".ico",".woff",".woff2",".ttf",".otf",".mp3",".ogg",".wav",".mp4",".webm",".wasm",".pdf"}.Contains(Path.GetExtension(file).ToLowerInvariant()))return null;AgentSandboxDirectories.ValidateFile(file);using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read))ValidateOpenedFile(stream,file);return file;
            }catch(IOException){return null;}catch(ArgumentException){return null;}catch(UnauthorizedAccessException){return null;}
        }
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern uint GetFinalPathNameByHandle(SafeFileHandle handle,StringBuilder path,uint length,uint flags);
        static void ValidateOpenedFile(FileStream stream,string expected)
        {
            var actual=new StringBuilder(32768);uint length=GetFinalPathNameByHandle(stream.SafeFileHandle,actual,(uint)actual.Capacity,0);if(length==0||length>=actual.Capacity)throw new UnauthorizedAccessException(TextResources.Get("htmlFilePreview.validateOpenedFile.text"));string path=actual.ToString();if(path.StartsWith("\\\\?\\",StringComparison.Ordinal))path=path.Substring(4);if(!String.Equals(path,expected,StringComparison.OrdinalIgnoreCase)||path.Split('\\','/').Skip(1).Any(part=>part.StartsWith(".",StringComparison.Ordinal)))throw new UnauthorizedAccessException(TextResources.Get("htmlFilePreview.validateOpenedFile.text.alternativeA"));
        }
        static string Mime(string file){switch(Path.GetExtension(file).ToLowerInvariant()){case ".html":case ".htm":return "text/html; charset=utf-8";case ".css":return "text/css; charset=utf-8";case ".js":case ".mjs":return "text/javascript; charset=utf-8";case ".json":return "application/json";case ".png":return "image/png";case ".jpg":case ".jpeg":return "image/jpeg";case ".gif":return "image/gif";case ".svg":return "image/svg+xml";case ".webp":return "image/webp";case ".ico":return "image/x-icon";case ".woff":return "font/woff";case ".woff2":return "font/woff2";case ".ttf":return "font/ttf";case ".otf":return "font/otf";case ".mp3":return "audio/mpeg";case ".ogg":return "audio/ogg";case ".wav":return "audio/wav";case ".mp4":return "video/mp4";case ".webm":return "video/webm";case ".wasm":return "application/wasm";case ".pdf":return "application/pdf";default:return "text/plain; charset=utf-8";}}
        internal Task<string> EvaluateAsync(string script){if(disposed||browser.CoreWebView2==null)throw new InvalidOperationException(TextResources.Get("htmlFilePreview.evaluateAsync.text"));return browser.CoreWebView2.ExecuteScriptAsync(script);}
        internal Task CaptureAsync(Stream stream){return browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);}
        public void Dispose(){if(disposed)return;disposed=true;if(navigation!=null)navigation.TrySetCanceled();browser.Dispose();Children.Clear();}
    }
}
