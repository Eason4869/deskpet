using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dafeiyu.Tests
{
    public static class BrowserPaneTests
    {
        static int checks,requests,bridgeMessages;static Application app;
        static void MessageReceived(object sender,EventArgs e){bridgeMessages++;}
        static void Check(bool value,string label){if(!value)throw new Exception(label);checks++;Console.WriteLine("PASS "+label);}
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var node in All<T>(VisualTreeHelper.GetChild(root,i)))yield return node;}
        static Button FindButton(BrowserPane pane,string name){return All<Button>(pane).Single(b=>b.Name==name);}
        static async Task Ready(BrowserPane pane){Task task=pane.Ready;if(await Task.WhenAny(task,Task.Delay(25000))!=task)throw new Exception("Browser navigation timed out");await task;await Task.Delay(100);}
        static async Task Eventually(BrowserPane pane,string expression){for(int i=0;i<80;i++){if(await pane.EvaluateAsync(expression)=="true")return;await Task.Delay(100);}throw new Exception("Browser condition failed: "+expression);}
        static async Task Run(bool native)
        {
            var pane=new BrowserPane();var host=new Window{Content=pane,Width=980,Height=760};host.Show();await Task.Delay(100);await CaptureBlankSearch(host);await Ready(pane);
            Check(WebPreviewRuntime.DirectoryPath==null&&!All<FrameworkElement>(pane).Any(e=>e.GetType().Name=="WebView2"),"empty browser creates no WebView, profile or automatic connection");
            Check(FindButton(pane,"BrowserBack").IsEnabled==false&&FindButton(pane,"BrowserForward").IsEnabled==false&&FindButton(pane,"BrowserReload").IsEnabled==false,"blank browser has disabled history and refresh controls");
            Check(BrowserPane.NormalizeAddress("example.test/path").AbsoluteUri=="https://example.test/path","bare domain receives HTTPS");Check(BrowserPane.NormalizeAddress("example.test:8443/path").Port==8443,"bare host with port remains a web address");Check(BrowserPane.NormalizeAddress("查找 C# 示例").AbsoluteUri=="https://www.bing.com/search?q="+Uri.EscapeDataString("查找 C# 示例"),"search terms normalize without issuing a request");
            foreach(string forbidden in new[]{"file:///C:/Windows/win.ini","javascript:alert(1)","deskpet://input/echo","data:text/html,hello","C:\\Windows\\win.ini","C:/fixture/file.html","\\\\server\\fixture\\file.html"}){pane.Navigate(forbidden);Check(All<TextBlock>(pane).Single(t=>t.Name=="BrowserNotice").Text.Contains("HTTP"),"rejects non-web address "+forbidden);}
            Check(WebPreviewRuntime.DirectoryPath==null,"blocked URLs do not initialize a browser engine");
            if(native)await Native(pane,host);
            pane.Dispose();host.Content=null;host.Close();Console.WriteLine("Browser pane tests: "+checks+" passed"+(native?" (native WebView2 included)":" (offline controls only)"));
        }
        static async Task CaptureBlankSearch(Window host)
        {
            object original=host.Content;double width=host.Width;var font=host.FontFamily;string submitted=null;var blank=new BlankPreviewPage(value=>submitted=value,()=>{},()=>{},()=>{},()=>{});host.Content=blank;host.FontFamily=new FontFamily("Microsoft YaHei UI");host.Width=460;host.UpdateLayout();await Task.Delay(80);var field=All<TextBox>(blank).Single(t=>t.Name=="NewTabAddress");field.Focus();host.UpdateLayout();string folder=Path.Combine(Directory.GetCurrentDirectory(),"work","search-alignment-evidence");Directory.CreateDirectory(folder);
            Action<string> save=delegate(string name){var surface=(FrameworkElement)field.Parent;surface.UpdateLayout();var bmp=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth),(int)Math.Ceiling(surface.ActualHeight),96,96,PixelFormats.Pbgra32);var visual=new DrawingVisual();using(var drawing=visual.RenderOpen())drawing.DrawRectangle(new VisualBrush(surface),null,new Rect(0,0,surface.ActualWidth,surface.ActualHeight));bmp.Render(visual);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));using(var file=File.Create(Path.Combine(folder,name)))png.Save(file);};save("new-tab-empty-focused.png");field.Text="测试搜索内容";field.CaretIndex=2;host.UpdateLayout();save("new-tab-text.png");field.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(host),0,System.Windows.Input.Key.Enter){RoutedEvent=System.Windows.Input.Keyboard.KeyDownEvent});Check(submitted=="测试搜索内容","new-tab search still submits its exact input with Enter");field.Text="";host.Width=700;host.UpdateLayout();save("new-tab-wide.png");host.Content=original;host.Width=width;host.FontFamily=font;await Task.Delay(50);
        }
        static async Task Native(BrowserPane pane,Window host)
        {
            var tcp=new TcpListener(IPAddress.Loopback,0);tcp.Start();int port=((IPEndPoint)tcp.LocalEndpoint).Port;tcp.Stop();var server=new HttpListener();string origin="http://127.0.0.1:"+port;server.Prefixes.Add(origin+"/");server.Start();
            var serving=Task.Run(async delegate{try{while(server.IsListening){var request=await server.GetContextAsync();int count=Interlocked.Increment(ref requests);string path=request.Request.Url.AbsolutePath;string page="<!doctype html><meta charset='utf-8'><title>"+(path=="/two"?"two":"one")+"</title><style>body{font:17px sans-serif;background:#f6f8fc;color:#243044;padding:38px}h1{color:#315ff4}button,a{padding:12px}</style><h1>内置浏览器 · "+(path=="/two"?"第二页":"第一页")+"</h1><p id='script'>等待脚本</p><p>这是本机 HTTP 夹具，没有请求公网。</p><a id='next' href='/two'>下一页</a><button id='action' onclick=\"this.textContent='已点击'\">交互按钮</button><script>window.fixtureCount="+count+";document.querySelector('#script').textContent='JavaScript 已运行';</script>";if(path=="/download")request.Response.Headers.Add("Content-Disposition","attachment; filename=fixture.txt");byte[] bytes=Encoding.UTF8.GetBytes(page);request.Response.ContentType=path=="/download"?"application/octet-stream":"text/html; charset=utf-8";request.Response.ContentLength64=bytes.Length;await request.Response.OutputStream.WriteAsync(bytes,0,bytes.Length);request.Response.Close();}}catch(HttpListenerException){}catch(ObjectDisposedException){}});
            string browserProfile=null;
            try
            {
                All<TextBox>(pane).Single(t=>t.Name=="BrowserAddress").Text=origin+"/one";FindButton(pane,"BrowserGo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Ready(pane);await Eventually(pane,"document.title==='one' && document.querySelector('#script').textContent==='JavaScript 已运行'");Check(All<TextBox>(pane).Single(t=>t.Name=="BrowserAddress").Text==origin+"/one","address bar Go control performs actual HTTP navigation");Check(await pane.EvaluateAsync("document.querySelector('#action').click();document.querySelector('#action').textContent")=="\"已点击\"","native page renders and runs interactive JavaScript");
                var web=All<FrameworkElement>(pane).Single(e=>e.GetType().Name=="WebView2");var core=web.GetType().GetProperty("CoreWebView2").GetValue(web,null);var settings=core.GetType().GetProperty("Settings").GetValue(core,null);Check(!(bool)settings.GetType().GetProperty("IsWebMessageEnabled").GetValue(settings,null)&&!(bool)settings.GetType().GetProperty("AreHostObjectsAllowed").GetValue(settings,null),"browser disables native web-message and host-object bridges");
                var messageEvent=core.GetType().GetEvent("WebMessageReceived");var handler=Delegate.CreateDelegate(messageEvent.EventHandlerType,typeof(BrowserPaneTests).GetMethod("MessageReceived",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic));messageEvent.AddEventHandler(core,handler);await pane.EvaluateAsync("try{chrome.webview.postMessage({type:'input',data:'fixture-only'});}catch(e){};true");await Task.Delay(150);Check(bridgeMessages==0&&await pane.EvaluateAsync("typeof window.term==='undefined'")=="true","real page message attempt cannot reach host or terminal bridge");messageEvent.RemoveEventHandler(core,handler);var environment=core.GetType().GetProperty("Environment").GetValue(core,null);string profile=(string)environment.GetType().GetProperty("UserDataFolder").GetValue(environment,null);browserProfile=profile;Check(Path.GetFileName(profile).StartsWith("browser-profile-"),"browser uses its own profile, separate from terminal and file preview");
                await pane.EvaluateAsync("document.querySelector('#next').click();true");await Eventually(pane,"document.title==='two'");Check(FindButton(pane,"BrowserBack").IsEnabled,"page links update native back history");FindButton(pane,"BrowserBack").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Ready(pane);await Eventually(pane,"document.title==='one'");Check(FindButton(pane,"BrowserForward").IsEnabled,"back navigation exposes forward history");FindButton(pane,"BrowserForward").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Ready(pane);await Eventually(pane,"document.title==='two'");Check(true,"forward control restores second page");
                int before=requests;FindButton(pane,"BrowserReload").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Ready(pane);Check(requests>before&&await pane.EvaluateAsync("document.title==='two'")=="true","refresh performs a real HTTP request and preserves the page");
                pane.Navigate("file:///C:/Windows/win.ini");await Task.Delay(150);Check(await pane.EvaluateAsync("document.title==='two'")=="true","address input rejects file access without replacing current page");core.GetType().GetMethod("Navigate").Invoke(core,new object[]{"file:///C:/Windows/win.ini"});await Task.Delay(250);Check(await pane.EvaluateAsync("document.title==='two'")=="true","native navigation guard also blocks file URLs");
                await pane.EvaluateAsync("window.open('/one','new-window');true");await Eventually(pane,"document.title==='one'");Check(app.Windows.OfType<Window>().Count()==1,"HTTP popup navigates the same pane without external window");
                await pane.EvaluateAsync("window.permissionResult='waiting';navigator.geolocation.getCurrentPosition(()=>window.permissionResult='allowed',e=>window.permissionResult=e.code===1?'denied':'other');true");await Eventually(pane,"window.permissionResult==='denied'");Check(true,"native geolocation permission request is denied");
                Directory.CreateDirectory(Path.Combine("work","permissions"));using(var capture=File.Create(Path.Combine("work","permissions","browser-pane-http.png")))await pane.CaptureAsync(capture);
                pane.Navigate(origin+"/download");await Task.Delay(800);Check(All<TextBlock>(pane).Single(t=>t.Name=="BrowserNotice").Text.Contains("下载"),"downloads are canceled with a visible explanation");
            }
            finally{server.Close();}await serving;
            pane.Dispose();for(int i=0;i<100&&Directory.Exists(browserProfile);i++)await Task.Delay(100);
            Check(!Directory.Exists(browserProfile),"closing the native pane reclaims its owned profile after the browser exits");
        }
        [STAThread]public static int Main(string[] args){app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int result=1;app.Dispatcher.BeginInvoke(new Action(async delegate{try{await Run(args.Contains("--native"));result=0;}catch(Exception e){Console.Error.WriteLine(e);}finally{foreach(var window in app.Windows.OfType<Window>().ToArray())window.Close();app.Shutdown();}}));app.Run();return result;}
    }
}
