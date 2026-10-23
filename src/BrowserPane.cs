using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Runtime.CompilerServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Dafeiyu
{
    // A user-operated browser. An empty pane never initializes Chromium or connects.
    internal sealed class BrowserPane : DockPanel,IDisposable
    {
        readonly TextBox address;
        readonly Button back,forward,reload;
        readonly TextBlock notice=Ui.Text("",11,AgentUi.Muted);
        readonly ContentControl content=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
        BrowserView view;Task initializing;bool disposed;bool downloadNotice;string initialAddress;int revision;
        internal Task Ready{get;private set;}

        internal BrowserPane(string address=null)
        {
            Name="BrowserPane";Background=Ui.Brush("#F8F9FB");LastChildFill=true;initialAddress=address;Ready=Task.FromResult(0);
            var bar=new DockPanel{Margin=new Thickness(10,10,10,8)};
            back=NavigationButton("‹",TextResources.Get("browserPane.browserPane.navigationButton"),"BrowserBack");forward=NavigationButton("›",TextResources.Get("browserPane.browserPane.navigationButton.alternativeA"),"BrowserForward");reload=NavigationButton("↻",TextResources.Get("browserPane.browserPane.navigationButton.alternativeB"),"BrowserReload");
            back.Click+=delegate{if(view!=null)Observe(view.Back());};forward.Click+=delegate{if(view!=null)Observe(view.Forward());};reload.Click+=delegate{if(view!=null)Observe(view.Reload());};
            bar.Children.Add(back);bar.Children.Add(forward);bar.Children.Add(reload);
            var go=Ui.Button(TextResources.Get("browserPane.browserPane.text"));go.Name="BrowserGo";go.MinHeight=34;go.Padding=new Thickness(12,4,12,4);go.Margin=new Thickness(8,0,0,0);go.Click+=delegate{Navigate(this.address.Text);};DockPanel.SetDock(go,Dock.Right);bar.Children.Add(go);
            this.address=Ui.Field();this.address.Name="BrowserAddress";this.address.MinWidth=80;this.address.MinHeight=34;this.address.Padding=new Thickness(11,5,11,5);this.address.ToolTip=TextResources.Get("browserPane.browserPane.thisAddressToolTip");this.address.KeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Enter){e.Handled=true;Navigate(this.address.Text);}};bar.Children.Add(this.address);
            DockPanel.SetDock(bar,Dock.Top);Children.Add(bar);notice.Name="BrowserNotice";notice.Margin=new Thickness(14,0,14,8);DockPanel.SetDock(notice,Dock.Bottom);Children.Add(notice);Children.Add(content);ShowEmpty();UpdateButtons();
            Loaded+=delegate{if(!String.IsNullOrWhiteSpace(initialAddress)){string next=initialAddress;initialAddress=null;Navigate(next);}};
        }

        static Button NavigationButton(string text,string tooltip,string name)
        {
            var button=Ui.Button(text);button.Name=name;button.ToolTip=tooltip;button.Width=34;button.MinHeight=34;button.FontSize=20;button.Padding=new Thickness(0);button.Margin=new Thickness(0,0,4,0);DockPanel.SetDock(button,Dock.Left);return button;
        }
        void ShowEmpty()
        {
            var text=Ui.Text(TextResources.Get("browserPane.showEmpty.text"),16,AgentUi.Muted);text.TextAlignment=TextAlignment.Center;text.VerticalAlignment=VerticalAlignment.Center;text.HorizontalAlignment=HorizontalAlignment.Center;text.Margin=new Thickness(24);content.Content=text;
        }
        internal static Uri NormalizeAddress(string value)
        {
            string text=(value??"").Trim();if(text.Length==0)return null;
            if(text.Length>8192)throw new ArgumentException(TextResources.Get("browserPane.normalizeAddress.text"));
            Uri uri;
            if(Uri.TryCreate(text,UriKind.Absolute,out uri)&&uri.IsFile)throw new ArgumentException(TextResources.Get("browserPane.normalizeAddress.text.alternativeA"));
            // Try bare host:port before Uri interprets the hostname as a custom scheme.
            if(!text.Contains("://")&&text.IndexOfAny(new[]{' ','\t','\r','\n'})<0&&(text.Contains(".")||text.StartsWith("localhost",StringComparison.OrdinalIgnoreCase)||text.StartsWith("[",StringComparison.Ordinal)))
            {
                if(Uri.TryCreate("https://"+text,UriKind.Absolute,out uri)&&IsWebAddress(uri))return uri;
            }
            if(Uri.TryCreate(text,UriKind.Absolute,out uri))
            {
                if(!IsWebAddress(uri))throw new ArgumentException(TextResources.Get("browserPane.normalizeAddress.text.alternativeA"));return uri;
            }
            if(text.Contains("://"))throw new ArgumentException(TextResources.Get("browserPane.normalizeAddress.text.alternativeB"));
            return new Uri("https://www.bing.com/search?q="+Uri.EscapeDataString(text));
        }
        static bool IsWebAddress(Uri uri){return uri!=null&&(uri.Scheme==Uri.UriSchemeHttp||uri.Scheme==Uri.UriSchemeHttps)&&!String.IsNullOrEmpty(uri.Host);}

        internal void Navigate(string value)
        {
            if(disposed)return;downloadNotice=false;initialAddress=null;Uri uri;
            try{uri=NormalizeAddress(value);}catch(ArgumentException error){notice.Text=error.Message;return;}
            if(uri==null){notice.Text=TextResources.Get("browserPane.navigate.notice");return;}
            address.Text=uri.AbsoluteUri;notice.Text=TextResources.Get("browserPane.navigate.notice.alternativeA");int current=++revision;Ready=NavigateAsync(uri.AbsoluteUri,current);
        }
        async Task NavigateAsync(string uri,int current)
        {
            try
            {
                if(initializing==null){WebPreviewRuntime.Ensure();initializing=CreateViewAsync();}await initializing;
                if(disposed||current!=revision)return;await view.NavigateAsync(uri);
            }
            catch(Exception error){ShowError(error,current);}
        }
        [MethodImpl(MethodImplOptions.NoInlining)]async Task CreateViewAsync()
        {
            var next=new BrowserView(OnStarted,OnAddressChanged,OnNotice,OnDownloadNotice,Navigate,UpdateButtons);view=next;content.Content=next;await next.InitializeAsync();
        }
        void OnStarted(Task task,string uri){if(disposed)return;address.Text=uri;notice.Text=TextResources.Get("browserPane.onStarted.notice");Observe(task);}
        void OnAddressChanged(string uri){if(!disposed){address.Text=uri;UpdateButtons();}}
        void OnNotice(string text){if(!disposed)notice.Text=text;}
        void OnDownloadNotice(string text){downloadNotice=true;OnNotice(text);}
        void Observe(Task task){if(disposed)return;Ready=ObserveAsync(task,revision);}
        async Task ObserveAsync(Task task,int current)
        {
            try{await task;if(!disposed&&current==revision)notice.Text="";}
            catch(Exception error){ShowError(error,current);}
            finally{UpdateButtons();}
        }
        void ShowError(Exception error,int current)
        {
            if(disposed||current!=revision||error is OperationCanceledException)return;
            if(!(error.GetType().FullName=="Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException"||error is NotSupportedException||error is IOException||error is InvalidOperationException||error is ArgumentException||error is UnauthorizedAccessException||error is System.Runtime.InteropServices.COMException||error is DllNotFoundException||error is TypeInitializationException))throw error;
            if(!downloadNotice)notice.Text=TextResources.Get("browserPane.showError.notice");
        }
        void UpdateButtons(){if(disposed)return;back.IsEnabled=view!=null&&view.CanGoBack;forward.IsEnabled=view!=null&&view.CanGoForward;reload.IsEnabled=view!=null&&view.IsReady;}
        internal Task<string> EvaluateAsync(string script){if(disposed||view==null)throw new InvalidOperationException(TextResources.Get("browserPane.evaluateAsync.text"));return view.EvaluateAsync(script);}
        internal Task CaptureAsync(Stream stream){if(disposed||view==null)throw new InvalidOperationException(TextResources.Get("browserPane.captureAsync.text"));return view.CaptureAsync(stream);}
        public void Dispose(){if(disposed)return;disposed=true;revision++;if(view!=null)view.Dispose();content.Content=null;}

        // Native types remain behind the lazy boundary so an empty tab needs no runtime.
        sealed class BrowserView : Grid,IDisposable
        {
            readonly WebView2 browser=new WebView2();readonly Action<Task,string> started;readonly Action<string> addressChanged,notice,downloadNotice,navigate;readonly Action historyChanged;
            CoreWebView2Environment environment;TaskCompletionSource<bool> navigation;bool disposed,initiated,browserExited,cleanupScheduled;ulong navigationId;string profile;
            internal BrowserView(Action<Task,string> started,Action<string> addressChanged,Action<string> notice,Action<string> downloadNotice,Action<string> navigate,Action historyChanged){this.started=started;this.addressChanged=addressChanged;this.notice=notice;this.downloadNotice=downloadNotice;this.navigate=navigate;this.historyChanged=historyChanged;Children.Add(browser);}
            internal bool IsReady{get{return !disposed&&browser.CoreWebView2!=null;}}
            internal bool CanGoBack{get{return IsReady&&browser.CoreWebView2.CanGoBack;}}
            internal bool CanGoForward{get{return IsReady&&browser.CoreWebView2.CanGoForward;}}
            internal async Task InitializeAsync()
            {
                WebPreviewRuntime.ConfigureNativeLoader();profile=Path.Combine(WebPreviewRuntime.DirectoryPath,"browser-profile-"+Guid.NewGuid().ToString("N"));environment=await CoreWebView2Environment.CreateAsync(null,profile,new CoreWebView2EnvironmentOptions{AllowSingleSignOnUsingOSPrimaryAccount=false,AreBrowserExtensionsEnabled=false,AdditionalBrowserArguments="--disable-background-networking --disable-component-update --disable-sync --no-first-run --no-default-browser-check"});
                environment.BrowserProcessExited+=delegate{browserExited=true;if(disposed)CleanupProfile();};if(disposed)return;
                await browser.EnsureCoreWebView2Async(environment);if(disposed)return;var web=browser.CoreWebView2;
                web.Settings.AreHostObjectsAllowed=false;web.Settings.IsWebMessageEnabled=false;web.Settings.AreDefaultScriptDialogsEnabled=false;web.Settings.AreDevToolsEnabled=false;web.Settings.IsStatusBarEnabled=false;
                web.PermissionRequested+=delegate(object sender,CoreWebView2PermissionRequestedEventArgs e){e.State=CoreWebView2PermissionState.Deny;};
                web.LaunchingExternalUriScheme+=delegate(object sender,CoreWebView2LaunchingExternalUriSchemeEventArgs e){e.Cancel=true;notice(TextResources.Get("browserPane.initializeAsync.notice"));};
                web.DownloadStarting+=delegate(object sender,CoreWebView2DownloadStartingEventArgs e){e.Cancel=true;e.Handled=true;downloadNotice(TextResources.Get("browserPane.initializeAsync.downloadNotice"));};
                web.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;Uri uri;if(Uri.TryCreate(e.Uri,UriKind.Absolute,out uri)&&IsWebAddress(uri))navigate(uri.AbsoluteUri);else notice(TextResources.Get("browserPane.initializeAsync.notice.alternativeA"));};
                web.NavigationStarting+=NavigationStarting;
                web.FrameNavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e){Uri uri;if(e.Uri!="about:blank"&&(!Uri.TryCreate(e.Uri,UriKind.Absolute,out uri)||!IsWebAddress(uri)))e.Cancel=true;};
                web.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All,CoreWebView2WebResourceRequestSourceKinds.All);
                web.WebResourceRequested+=delegate(object sender,CoreWebView2WebResourceRequestedEventArgs e){Uri uri;if(!Uri.TryCreate(e.Request.Uri,UriKind.Absolute,out uri)||!(IsWebAddress(uri)||uri.Scheme=="data"||uri.Scheme=="blob"||e.Request.Uri=="about:blank"))e.Response=environment.CreateWebResourceResponse(new MemoryStream(new byte[0]),403,"Blocked","Content-Type: text/plain");};
                web.SourceChanged+=delegate{if(!disposed)addressChanged(web.Source);};web.HistoryChanged+=delegate{if(!disposed)historyChanged();};
                web.NavigationCompleted+=delegate(object sender,CoreWebView2NavigationCompletedEventArgs e){if(navigation!=null&&e.NavigationId==navigationId){if(e.IsSuccess)navigation.TrySetResult(true);else navigation.TrySetException(new IOException(TextResources.Get("browserPane.initializeAsync.navigationTrySetException")));}historyChanged();};historyChanged();
            }
            void NavigationStarting(object sender,CoreWebView2NavigationStartingEventArgs e)
            {
                Uri uri;if(!Uri.TryCreate(e.Uri,UriKind.Absolute,out uri)||!IsWebAddress(uri)){e.Cancel=true;notice(TextResources.Get("browserPane.navigationStarting.notice"));if(initiated&&navigation!=null){initiated=false;navigation.TrySetCanceled();}return;}
                if(!e.IsRedirected){if(!initiated){if(navigation!=null)navigation.TrySetCanceled();navigation=new TaskCompletionSource<bool>();}initiated=false;navigationId=e.NavigationId;}
                started(navigation.Task,e.Uri);
            }
            Task Begin(Action operation)
            {
                if(disposed||!IsReady)return Task.FromResult(0);if(navigation!=null)navigation.TrySetCanceled();navigation=new TaskCompletionSource<bool>();initiated=true;operation();return navigation.Task;
            }
            internal Task NavigateAsync(string uri){return Begin(()=>browser.CoreWebView2.Navigate(uri));}
            internal Task Back(){return CanGoBack?Begin(()=>browser.CoreWebView2.GoBack()):Task.FromResult(0);}
            internal Task Forward(){return CanGoForward?Begin(()=>browser.CoreWebView2.GoForward()):Task.FromResult(0);}
            internal Task Reload(){return Begin(()=>browser.CoreWebView2.Reload());}
            internal Task<string> EvaluateAsync(string script){if(!IsReady)throw new InvalidOperationException(TextResources.Get("browserPane.evaluateAsync.text"));return browser.CoreWebView2.ExecuteScriptAsync(script);}
            internal Task CaptureAsync(Stream stream){if(!IsReady)throw new InvalidOperationException(TextResources.Get("browserPane.captureAsync.text"));return browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);}
            void CleanupProfile(){if(cleanupScheduled||profile==null)return;cleanupScheduled=true;string root=WebPreviewRuntime.DirectoryPath,target=profile;Task.Run(()=>WebPreviewRuntime.TryDeleteExitedBrowserProfile(root,target));}
            public void Dispose(){if(disposed)return;disposed=true;if(navigation!=null)navigation.TrySetCanceled();try{browser.Dispose();}finally{Children.Clear();if(browserExited)CleanupProfile();}}
        }
    }
}
