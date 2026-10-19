using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
namespace Dafeiyu.Tests
{
    public static class ChatImagePreviewTests
    {
        static int checks;static readonly BindingFlags methods=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        static IEnumerable<T> All<T>(DependencyObject p)where T:DependencyObject{if(p is T)yield return(T)p;for(int i=0;i<VisualTreeHelper.GetChildrenCount(p);i++)foreach(var t in All<T>(VisualTreeHelper.GetChild(p,i)))yield return t;}
        static void Capture(Window window,string name){window.UpdateLayout();string folder=Path.Combine(Directory.GetCurrentDirectory(),"work","image-preview-evidence");Directory.CreateDirectory(folder);var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(folder,name)))png.Save(file);}
        static void Pump(){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{}));}
        static byte[] Picture(int width=640,int height=480){var pixels=new byte[width*height*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=40;pixels[i+1]=120;pixels[i+2]=220;pixels[i+3]=255;}var source=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(source));using(var stream=new MemoryStream()){encoder.Save(stream);return stream.ToArray();}}
        static object Invoke(Type type,object target,string name,params object[] arguments){return type.GetMethod(name,methods).Invoke(target,arguments);}
        static void ClickBlankOrImage(UIElement element){element.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,0,System.Windows.Input.MouseButton.Left){RoutedEvent=System.Windows.Input.Mouse.MouseUpEvent});}
        static void Until(Func<bool> condition,Action diagnostic=null){var stop=DateTime.UtcNow.AddSeconds(5);while(!condition()&&DateTime.UtcNow<stop){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(15)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}bool complete=condition();if(!complete&&diagnostic!=null)diagnostic();Check(complete,"asynchronous image request finishes within fixture deadline");}
        sealed class RemoteFixture:IDisposable
        {
            readonly TcpListener listener;internal readonly Uri Url;internal readonly Task Serve;internal string Headers="";internal volatile bool Connected;
            internal RemoteFixture(byte[] bytes,int declared,bool hang)
            {
                listener=new TcpListener(IPAddress.Loopback,0);listener.Start();Url=new Uri("http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/image.png");
                Serve=Task.Run(async delegate{try{using(var client=await listener.AcceptTcpClientAsync()){Connected=true;using(var stream=client.GetStream()){var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);string line;while(!String.IsNullOrEmpty(line=await reader.ReadLineAsync()))Headers+=line+"\n";byte[] header=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: image/png\r\n"+(declared>=0?"Content-Length: "+declared+"\r\n":"")+"Connection: close\r\n\r\n");await stream.WriteAsync(header,0,header.Length);if(hang){var pending=new byte[1];await stream.ReadAsync(pending,0,1);}else if(bytes.Length>0)await stream.WriteAsync(bytes,0,bytes.Length);}}}catch(ObjectDisposedException){}catch(SocketException){}catch(IOException){}});
            }
            public void Dispose(){listener.Stop();}
        }
        static int NetworkOnly()
        {
            var type=typeof(ChatMessageView).Assembly.GetType("Dafeiyu.ChatImagePreview");byte[] bytes=Picture();
            using(var remote=new RemoteFixture(bytes,bytes.Length,false))
            {try{var read=(Task<byte[]>)Invoke(type,null,"ReadRemoteAsync",remote.Url,CancellationToken.None);byte[] result=read.GetAwaiter().GetResult();Check(result.SequenceEqual(bytes),"console-only original HTTP image bytes match");Check(remote.Serve.Wait(2000),"console-only HTTP fixture completes");return 0;}catch(Exception error){Console.Error.WriteLine("Network failure: "+error.GetType().FullName+" / "+error.Message+"; connected="+remote.Connected+"; server="+remote.Serve.Status);return 1;}}
        }
        [STAThread]public static int Main(string[] arguments)
        {
            if(arguments.Contains("--network-only"))return NetworkOnly();
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Window host=null,appHost=null;PetWindow pet=null;
            app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e){Console.Error.WriteLine("DISPATCHER UNHANDLED: "+e.Exception.GetType().FullName+" / "+e.Exception.Message);Console.Error.WriteLine(e.Exception.StackTrace);};
            AppDomain.CurrentDomain.UnhandledException+=delegate(object sender,UnhandledExceptionEventArgs e){var error=e.ExceptionObject as Exception;Console.Error.WriteLine("DOMAIN UNHANDLED: "+(error==null?"unknown":error.GetType().FullName+" / "+error.Message));if(error!=null)Console.Error.WriteLine(error.StackTrace);};
            try{
                Type previewType=typeof(ChatMessageView).Assembly.GetType("Dafeiyu.ChatImagePreview");Check(previewType!=null,"chat images have an in-app original-image preview implementation");
                var originalEffect=new System.Windows.Media.Effects.DropShadowEffect{Opacity=.12,BlurRadius=6};var history=new StackPanel{Effect=originalEffect};var grid=new Grid();grid.Children.Add(history);var draft=new TextBox{Text="未发送的草稿",VerticalAlignment=VerticalAlignment.Bottom};grid.Children.Add(draft);host=new Window{Content=grid,Width=720,Height=560,ShowActivated=false,ShowInTaskbar=false};Ui.Window(host,"图片预览自动验证",720,560);host.MinWidth=host.MinHeight=0;host.WindowStartupLocation=WindowStartupLocation.Manual;host.Left=-10000;host.Top=0;host.Show();Pump();int windows=app.Windows.Count;
                var attachment=ChatAttachment.CreateImage("original.png",Picture());var preview=(FrameworkElement)Invoke(previewType,null,"OpenAttachment",host,attachment);host.UpdateLayout();Pump();
                Check(app.Windows.Count==windows&&Object.ReferenceEquals(host.Content,grid),"preview stays in existing host without replacing its content or creating a window");
                Check(history.Effect is System.Windows.Media.Effects.BlurEffect&&draft.Effect is System.Windows.Media.Effects.BlurEffect&&preview.Effect==null,"preview blurs existing content while its own layer stays sharp");
                Capture(host,"image-fit.png");var image=All<Image>(preview).Single(i=>i.Name=="ChatPreviewImage");var bitmap=(BitmapSource)image.Source;Check(bitmap.PixelWidth==640&&bitmap.PixelHeight==480,"preview decodes original resolution rather than the 160-pixel thumbnail");
                var plus=All<Button>(preview).Single(b=>b.Name=="ChatPreviewZoomIn");double initial=image.Width;plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));host.UpdateLayout();Check(image.Width>initial,"zoom button increases displayed image size");Capture(host,"image-zoom.png");
                All<Button>(preview).Single(b=>b.Name=="ChatPreviewFit").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));host.UpdateLayout();Check(Math.Abs(image.Width-initial)<1,"fit resets image zoom");
                host.Width=360;host.Height=400;host.UpdateLayout();Pump();Check(preview.ActualWidth<=grid.ActualWidth+1&&preview.ActualHeight<=grid.ActualHeight+1,"overlay fits resized host");Capture(host,"image-narrow.png");
                All<Button>(preview).Single(b=>b.Name=="ChatPreviewClose").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();Check(!grid.Children.Contains((UIElement)preview)&&draft.Text=="未发送的草稿","close removes overlay and preserves draft");
                Check(Object.ReferenceEquals(history.Effect,originalEffect)&&draft.Effect==null,"close restores exact existing effects including null");
                preview=(FrameworkElement)Invoke(previewType,null,"OpenAttachment",host,attachment);host.UpdateLayout();Pump();ClickBlankOrImage(All<Image>(preview).Single(i=>i.Name=="ChatPreviewImage"));Check(grid.Children.Contains((UIElement)preview),"clicking image keeps preview open");ClickBlankOrImage(All<Border>(preview).Single(b=>b.Name=="ChatPreviewToolbar"));Check(grid.Children.Contains((UIElement)preview),"clicking toolbar empty space keeps preview open");ClickBlankOrImage(All<Border>(preview).Single(b=>b.Name=="ChatPreviewImageArea"));Check(!grid.Children.Contains((UIElement)preview)&&Object.ReferenceEquals(history.Effect,originalEffect),"clicking image-area blank closes preview and restores blur");
                preview=(FrameworkElement)Invoke(previewType,null,"OpenAttachment",host,attachment);var replacement=(FrameworkElement)Invoke(previewType,null,"OpenAttachment",host,attachment);Check(!grid.Children.Contains((UIElement)preview)&&history.Effect is System.Windows.Media.Effects.BlurEffect,"replacing preview leaves only one layer and keeps backdrop blurred");ClickBlankOrImage(replacement);Check(Object.ReferenceEquals(history.Effect,originalEffect)&&draft.Effect==null,"backdrop click on replacement restores original effects exactly");
                preview=(FrameworkElement)Invoke(previewType,null,"OpenAttachment",host,attachment);var key=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,System.Windows.PresentationSource.FromVisual(host),0,System.Windows.Input.Key.Escape){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent};host.RaiseEvent(key);Check(!grid.Children.Contains((UIElement)preview)&&key.Handled,"Escape closes the preview and consumes the key");
                var invalid=ChatAttachment.CreateImage("broken.png",new byte[]{137,80,78,71,13,10,26,10});preview=(FrameworkElement)Invoke(previewType,null,"OpenAttachment",host,invalid);Check(All<TextBlock>(preview).Any(t=>t.Name=="ChatPreviewStatus"&&t.Text.Contains("无法")),"invalid image stays in a useful error state without crashing");Invoke(previewType,preview,"Close");
                object[] decodeArguments={Picture(5000,10),4096,0,0,false};var bounded=(BitmapSource)Invoke(previewType,null,"DecodeImage",decodeArguments);Check(bounded.PixelWidth<=4096&&bounded.PixelHeight<=4096&&(bool)decodeArguments[4],"oversized image dimensions are bounded before creating the displayed bitmap");
                decodeArguments=new object[]{Picture(10,5000),160,0,0,false};bounded=(BitmapSource)Invoke(previewType,null,"DecodeImage",decodeArguments);Check(bounded.PixelWidth<=160&&bounded.PixelHeight<=160,"portrait thumbnail caps both dimensions even at extreme aspect ratio");
                string root=Path.Combine(Directory.GetCurrentDirectory(),"work","image-preview-"+Guid.NewGuid().ToString("N"));var cfg=new PetSettings{AutoUpdate=false,GreetingEnabled=false,ShowPredictedMessages=false,OnboardingState="completed"};foreach(string hotkey in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[hotkey]="";pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"settings.json")),null);
                appHost=new ChatWindow(pet){ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-10000,Top=0,Width=1000,Height=720};appHost.Show();Pump();var appRoot=appHost.Content;int appWindows=app.Windows.Count;var samplePath=Path.Combine(Directory.GetCurrentDirectory(),"assets","pet-preview-hd.png");var sample=File.Exists(samplePath)?ChatAttachment.CreateImage("伙伴预览.png",File.ReadAllBytes(samplePath)):attachment;var appPreview=(FrameworkElement)Invoke(previewType,null,"OpenAttachment",appHost,sample);Pump();Capture(appHost,"image-real-chat.png");Check(Object.ReferenceEquals(appHost.Content,appRoot)&&app.Windows.Count==appWindows,"real chat app shell hosts same inline floating preview without another window");Invoke(previewType,appPreview,"Close");appHost.Close();appHost=null;
                foreach(string role in new[]{"user","assistant"}){var message=new ChatMessage(role,"image");message.SetAttachments(new[]{attachment});var view=new ChatMessageView(message,history,pet,host);history.Children.Add(view);host.UpdateLayout();var card=All<Border>(view).Single(b=>b.Name==(role=="user"?"SentAttachmentCard":"OutputFileCard"));Check(card.Focusable&&card.Cursor==System.Windows.Input.Cursors.Hand&&card.ContextMenu!=null,role+" image attachment has keyboard-accessible preview and keeps file actions");var activation=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,System.Windows.PresentationSource.FromVisual(host),0,System.Windows.Input.Key.Enter){RoutedEvent=System.Windows.Input.Keyboard.KeyDownEvent};card.RaiseEvent(activation);Check(grid.Children.OfType<FrameworkElement>().Any(e=>e.Name=="ChatImagePreviewOverlay"),role+" attachment opens preview with Enter");var active=grid.Children.OfType<FrameworkElement>().Single(e=>e.Name=="ChatImagePreviewOverlay");Invoke(previewType,active,"Close");history.Children.Clear();}
                foreach(bool quickDraft in new[]{false,true})
                {
                    Window composerHost=quickDraft?(Window)new QuickChatWindow(pet):new ChatWindow(pet);
                    composerHost.ShowActivated=false;composerHost.ShowInTaskbar=false;composerHost.WindowStartupLocation=WindowStartupLocation.Manual;composerHost.Left=-10000;composerHost.Top=0;
                    try
                    {
                        composerHost.Show();Pump();var composer=All<ConversationComposer>(composerHost).Single();composer.AddAttachments(new[]{attachment});composerHost.UpdateLayout();Pump();
                        var input=All<MarkdownEditor>(composerHost).Single();input.Text="保留未发送的草稿";
                        var card=All<Border>(composerHost).Single(b=>b.Name=="AttachmentCard");
                        var thumbnail=All<Image>(card).Single(i=>i.Name=="AttachmentThumbnail");ClickBlankOrImage(thumbnail);Pump();
                        var active=All<FrameworkElement>(composerHost).SingleOrDefault(e=>e.Name=="ChatImagePreviewOverlay");
                        Check(active!=null,"unsent image thumbnail opens inline preview: "+composerHost.GetType().Name);Invoke(previewType,active,"Close");
                        Check(input.Text=="保留未发送的草稿"&&All<Border>(composerHost).Count(b=>b.Name=="AttachmentCard")==1,"preview keeps draft and attachment intact");
                        card.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,System.Windows.PresentationSource.FromVisual(composerHost),0,System.Windows.Input.Key.Enter){RoutedEvent=System.Windows.Input.Keyboard.KeyDownEvent});
                        active=All<FrameworkElement>(composerHost).SingleOrDefault(e=>e.Name=="ChatImagePreviewOverlay");Check(active!=null,"unsent image supports keyboard preview");Invoke(previewType,active,"Close");
                        All<Button>(card).Single(b=>b.Name=="RemoveAttachment").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();
                        Check(!All<FrameworkElement>(composerHost).Any(e=>e.Name=="ChatImagePreviewOverlay")&&!All<Border>(composerHost).Any(b=>b.Name=="AttachmentCard"),"removing draft image never opens its preview");
                    }
                    finally{composerHost.Close();Pump();}
                }
                byte[] original=Picture();
                using(var remote=new RemoteFixture(original,original.Length,false))
                {
                    var markdown=new MarkdownView{Text="![AI 返回的图片]("+remote.Url.AbsoluteUri+")"};history.Children.Add(markdown);host.UpdateLayout();Pump();Check(!remote.Connected,"rendering markdown image text makes no eager network request");
                    var link=((Paragraph)markdown.Document.Blocks.FirstBlock).Inlines.OfType<Hyperlink>().Single();Check(Window.GetWindow(link)==host,"markdown image hyperlink resolves its existing host window");link.RaiseEvent(new System.Windows.Navigation.RequestNavigateEventArgs(remote.Url,""){RoutedEvent=Hyperlink.RequestNavigateEvent});Check(grid.Children.OfType<FrameworkElement>().Any(e=>e.Name=="ChatImagePreviewOverlay"),"markdown image click immediately opens its loading overlay");Until(()=>remote.Serve.IsCompleted&&grid.Children.OfType<FrameworkElement>().Any(e=>e.Name=="ChatImagePreviewOverlay"&&All<Image>(e).Any(i=>i.Source!=null)),delegate{Console.Error.WriteLine("Remote diagnostic: connected="+remote.Connected+"; server="+remote.Serve.Status+"; status="+String.Join(" | ",grid.Children.OfType<FrameworkElement>().Where(e=>e.Name=="ChatImagePreviewOverlay").SelectMany(e=>All<TextBlock>(e)).Where(t=>t.Name=="ChatPreviewStatus").Select(t=>t.Text)));});Check(!remote.Headers.Contains("Authorization:")&&!remote.Headers.Contains("Cookie:"),"explicit image click uses no API authorization or cookies");
                    var active=grid.Children.OfType<FrameworkElement>().Single(e=>e.Name=="ChatImagePreviewOverlay");Check(((BitmapSource)All<Image>(active).Single(i=>i.Name=="ChatPreviewImage").Source).PixelWidth==640,"AI markdown image click loads original pixels into same-host preview");Invoke(previewType,active,"Close");history.Children.Clear();
                }
                using(var remote=new RemoteFixture(new byte[0],16*1024*1024+1,false))
                {var read=(Task<byte[]>)Invoke(previewType,null,"ReadRemoteAsync",remote.Url,CancellationToken.None);bool rejected=false;try{read.GetAwaiter().GetResult();}catch(InvalidDataException){rejected=true;}Check(rejected,"remote oversized Content-Length is rejected before buffering body");}
                using(var remote=new RemoteFixture(new byte[16*1024*1024+1],-1,false))
                {var read=(Task<byte[]>)Invoke(previewType,null,"ReadRemoteAsync",remote.Url,CancellationToken.None);bool rejected=false;try{read.GetAwaiter().GetResult();}catch(InvalidDataException){rejected=true;}Check(rejected,"remote body without Content-Length is capped by actual streamed bytes");}
                var blocked=(Task<byte[]>)Invoke(previewType,null,"ReadRemoteAsync",new Uri("file:///C:/Windows/win.ini"),CancellationToken.None);bool blockedLocal=false;try{blocked.GetAwaiter().GetResult();}catch(InvalidDataException){blockedLocal=true;}Check(blockedLocal,"image URL loader rejects arbitrary local filesystem addresses");
                using(var remote=new RemoteFixture(new byte[0],100000,true))using(var cancellation=new CancellationTokenSource())
                {var read=(Task<byte[]>)Invoke(previewType,null,"ReadRemoteAsync",remote.Url,cancellation.Token);Until(()=>remote.Connected);cancellation.Cancel();Until(()=>read.IsCompleted);bool cancelled=false;try{read.GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"cancelling a stalled remote image aborts the body read promptly");}
                using(var remote=new RemoteFixture(new byte[0],100000,true))
                {var active=(FrameworkElement)Invoke(previewType,null,"OpenRemote",host,remote.Url,"cancel fixture");Until(()=>remote.Connected);Invoke(previewType,active,"Close");Until(()=>remote.Serve.IsCompleted);Check(!grid.Children.Contains((UIElement)active),"closing remote preview cancels its request and removes all input coverage");}
                Console.WriteLine("Chat image preview: "+checks+" passed");return 0;
            }catch(Exception error){Console.Error.WriteLine(error.GetType().FullName+": "+error.Message);Console.Error.WriteLine(error.StackTrace);return 1;}finally{if(appHost!=null)appHost.Close();if(host!=null){var panel=host.Content as Panel;if(panel!=null)foreach(var overlay in panel.Children.OfType<FrameworkElement>().Where(e=>e.Name=="ChatImagePreviewOverlay").ToArray())Invoke(overlay.GetType(),overlay,"Close");Pump();host.Close();}if(pet!=null)pet.Close();app.Shutdown();}
        }
    }
}
