using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace Dafeiyu
{
    // One temporary layer in the existing chat window. It never replaces the
    // conversation tree or opens another window, and owns no attachment files.
    internal sealed class ChatImagePreview : Border
    {
        internal const int MaximumRemoteBytes=16*1024*1024;
        const int MaximumDimension=4096;
        static readonly DependencyProperty ActiveProperty=DependencyProperty.RegisterAttached("ActiveImagePreview",typeof(ChatImagePreview),typeof(ChatImagePreview));
        readonly Window host;readonly Grid root;readonly IInputElement previousFocus;
        static readonly ControlTemplate previewButtonTemplate=(ControlTemplate)ThemeManager.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='9'><ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Background' Value='#E9ECF0'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='surface' Property='Background' Value='#DDE2E8'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.4'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
        readonly Image image=new Image{Name="ChatPreviewImage",Stretch=Stretch.Fill,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
        readonly TextBlock status=new TextBlock{Name="ChatPreviewStatus",Foreground=Ui.Brush("#777E88"),FontSize=12,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(20)};
        readonly ScrollViewer scroll=new ScrollViewer{HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        readonly TextBlock percentage=new TextBlock{Foreground=Ui.Brush("#555E69"),FontSize=12,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Center,MinWidth=48,Margin=new Thickness(2,0,2,0)};
        readonly CancellationTokenSource cancel=new CancellationTokenSource();
        readonly Dictionary<UIElement,System.Windows.Media.Effects.Effect> backdropEffects=new Dictionary<UIElement,System.Windows.Media.Effects.Effect>();
        BitmapSource bitmap;double zoom=1;bool closed,limited;Point? drag;double dragX,dragY;
        StackPanel imageActions;

        ChatImagePreview(Window window,string title)
        {
            host=window;root=window.Content as Grid;
            if(root==null)throw new InvalidOperationException(TextResources.Get("chatImagePreview.chatImagePreview.text"));
            var old=window.GetValue(ActiveProperty) as ChatImagePreview;if(old!=null)old.Close();
            previousFocus=Keyboard.FocusedElement;Name="ChatImagePreviewOverlay";Background=Ui.Brush("#56EEF0F3");ClipToBounds=true;Focusable=true;
            KeyboardNavigation.SetTabNavigation(this,KeyboardNavigationMode.Cycle);WindowChrome.SetIsHitTestVisibleInChrome(this,true);
            var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition());layout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            Child=layout;
            var heading=new Grid();heading.ColumnDefinitions.Add(new ColumnDefinition());heading.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});heading.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var label=Ui.InlineText(String.IsNullOrWhiteSpace(title)?TextResources.Get("chatImagePreview.chatImagePreview.inlineText"):title.Replace('\r',' ').Replace('\n',' '),13,Ui.Brush("#252A31"));label.Margin=new Thickness(0,0,12,0);heading.Children.Add(label);
            var escape=Ui.InlineText("Esc",10,Ui.Brush("#7B8490"));var escapePill=new Border{Child=escape,Background=Ui.Brush("#F1F3F5"),CornerRadius=new CornerRadius(5),Padding=new Thickness(5,3,5,3),Margin=new Thickness(0,0,5,0),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(escapePill,1);heading.Children.Add(escapePill);
            var close=ShellIcons.Button("close",()=>(TextResources.Get("chatImagePreview.chatImagePreview.shellIcons")),Close);close.Name="ChatPreviewClose";close.Template=previewButtonTemplate;close.Content=ShellIcons.Image("close",Ui.Brush("#59616D"),18);Grid.SetColumn(close,2);heading.Children.Add(close);
            var header=new Border{Name="ChatPreviewHeading",Child=heading,Background=ThemeManager.Surface,CornerRadius=new CornerRadius(14),BorderBrush=Ui.Brush("#E6E8EC"),BorderThickness=new Thickness(1),Padding=new Thickness(14,4,4,4),HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(16,12,16,8),MaxWidth=520,Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=14,ShadowDepth=2,Opacity=.06,Color=Colors.Black}};layout.Children.Add(header);
            var imageLayout=new Grid();imageLayout.RowDefinitions.Add(new RowDefinition());imageLayout.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            image.Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=22,ShadowDepth=4,Opacity=.14,Color=Colors.Black};image.SizeChanged+=delegate{image.Clip=new RectangleGeometry(new Rect(0,0,Math.Max(0,image.ActualWidth),Math.Max(0,image.ActualHeight)),12,12);};
            scroll.Content=image;scroll.Margin=new Thickness(20,8,20,4);scroll.HorizontalContentAlignment=HorizontalAlignment.Center;scroll.VerticalContentAlignment=VerticalAlignment.Center;LocalizedUi.Set(scroll,"ToolTip",()=>(TextResources.Get("chatImagePreview.chatImagePreview.scrollToolTip")));imageLayout.Children.Add(scroll);imageLayout.Children.Add(status);
            var imageArea=new Border{Name="ChatPreviewImageArea",Background=Brushes.Transparent,Child=imageLayout};Grid.SetRow(imageArea,1);layout.Children.Add(imageArea);
            var tools=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};imageActions=tools;
            var minus=Control("−","ChatPreviewZoomOut",()=>ZoomBy(1/1.25));LocalizedUi.Set(minus,"ToolTip",()=>(TextResources.Get("chatImagePreview.chatImagePreview.minusToolTip")));minus.FontSize=20;tools.Children.Add(minus);tools.Children.Add(percentage);var plus=Control("+","ChatPreviewZoomIn",()=>ZoomBy(1.25));LocalizedUi.Set(plus,"ToolTip",()=>(TextResources.Get("chatImagePreview.chatImagePreview.plusToolTip")));plus.FontSize=20;tools.Children.Add(plus);tools.Children.Add(new Border{Width=1,Height=16,Background=Ui.Brush("#DCE0E5"),Margin=new Thickness(8,0,8,0),VerticalAlignment=VerticalAlignment.Center});tools.Children.Add(Control(()=>(TextResources.Get("chatImagePreview.chatImagePreview.control")),"ChatPreviewFit",Fit));
            var toolbar=new Border{Name="ChatPreviewToolbar",Child=tools,Background=ThemeManager.Surface,BorderBrush=Ui.Brush("#E6E8EC"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(21),Padding=new Thickness(4),HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(10,8,10,14),Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=14,ShadowDepth=2,Opacity=.07,Color=Colors.Black}};Grid.SetRow(toolbar,2);layout.Children.Add(toolbar);
            SizeChanged+=delegate{header.MaxWidth=Math.Max(80,Math.Min(520,ActualWidth-32));scroll.Margin=new Thickness(ActualWidth<640?12:24,8,ActualWidth<640?12:24,4);};
            MouseUp+=delegate(object sender,MouseButtonEventArgs e){if(e.ChangedButton==MouseButton.Left&&!IsPreviewControl(e.OriginalSource as DependencyObject)){e.Handled=true;Close();}};
            scroll.SizeChanged+=delegate{ResizeImage();};scroll.PreviewMouseWheel+=delegate(object sender,MouseWheelEventArgs e){if(bitmap==null)return;ZoomBy(e.Delta>0?1.2:1/1.2);e.Handled=true;};
            scroll.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(bitmap==null||!(e.OriginalSource is Image))return;drag=e.GetPosition(scroll);dragX=scroll.HorizontalOffset;dragY=scroll.VerticalOffset;scroll.CaptureMouse();scroll.Cursor=Cursors.SizeAll;e.Handled=true;};
            scroll.PreviewMouseMove+=delegate(object sender,MouseEventArgs e){if(!drag.HasValue)return;Point point=e.GetPosition(scroll);scroll.ScrollToHorizontalOffset(dragX+drag.Value.X-point.X);scroll.ScrollToVerticalOffset(dragY+drag.Value.Y-point.Y);e.Handled=true;};
            scroll.PreviewMouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e){bool wasDragging=drag.HasValue;EndDrag();if(wasDragging)e.Handled=true;};scroll.LostMouseCapture+=delegate{drag=null;scroll.Cursor=Cursors.Arrow;};
            int firstRow=WindowChrome.GetWindowChrome(window)!=null&&root.RowDefinitions.Count>1?1:0;Grid.SetRow(this,firstRow);Grid.SetRowSpan(this,Math.Max(1,root.RowDefinitions.Count-firstRow));Grid.SetColumnSpan(this,Math.Max(1,root.ColumnDefinitions.Count));Panel.SetZIndex(this,Int32.MaxValue);root.Children.Add(this);window.SetValue(ActiveProperty,this);
            var blur=new System.Windows.Media.Effects.BlurEffect{Radius=8};blur.Freeze();foreach(UIElement child in root.Children)if(!ReferenceEquals(child,this)){backdropEffects.Add(child,child.Effect);child.SetCurrentValue(UIElement.EffectProperty,blur);}
            window.AddHandler(Keyboard.PreviewKeyDownEvent,new KeyEventHandler(HostKeyDown),true);window.Closed+=HostClosed;
            // Shell shortcuts are registered before this temporary layer. Handle
            // real keyboard input before routed host shortcuts can change chats.
            InputManager.Current.PreProcessInput+=BeforeInput;
            close.Focus();Loaded+=delegate{if(!closed)close.Focus();};
        }
        bool IsPreviewControl(DependencyObject source)
        {
            for(var current=source;current!=null&&!ReferenceEquals(current,this);)
            {
                var element=current as FrameworkElement;
                if(current is Image||current is System.Windows.Controls.Primitives.ButtonBase||current is System.Windows.Controls.Primitives.ScrollBar||element!=null&&(element.Name=="ChatPreviewToolbar"||element.Name=="ChatPreviewHeading"||element.Name=="GeneratedImageDetails"))return true;
                current=current is Visual||current is System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(current):LogicalTreeHelper.GetParent(current);
            }
            return false;
        }
        static Button Control(string text,string name,Action action)
        {
            var button=new Button{Name=name,Content=text,Template=previewButtonTemplate,MinHeight=32,MinWidth=32,Padding=new Thickness(text.Length>2?10:5,0,text.Length>2?10:5,0),Margin=new Thickness(0),Background=Brushes.Transparent,Foreground=Ui.Brush("#323943"),BorderBrush=Brushes.Transparent,FontSize=12,Cursor=Cursors.Hand,ToolTip=text,FocusVisualStyle=null};System.Windows.Automation.AutomationProperties.SetName(button,text);button.Click+=delegate{action();};return button;
        }
        internal static ChatImagePreview OpenAttachment(Window host,ChatAttachment file)
        {
            var preview=new ChatImagePreview(host,file.Name);
            try{preview.SetImage(DecodeBase64(file.Data,ChatAttachment.MaximumBytes));}catch(Exception error){preview.ShowError(error);}return preview;
        }
        internal static ChatImagePreview OpenGenerated(Window host,GeneratedImageStore store,GeneratedImage asset,bool showDetails=false,Action<GeneratedImage> regenerate=null)
        {
            var preview=new ChatImagePreview(host,TextResources.Get("chatImagePreview.openGenerated.previewChatImagePreview"));
            try{preview.SetGeneratedImage(store.Read(asset.Id));}catch(Exception error){preview.ShowError(error);}
            preview.imageActions.Children.Add(Control(()=>(TextResources.Get("chatImagePreview.openGenerated.control")),"ChatPreviewSaveOriginal",delegate{try{GeneratedImageCard.SaveOriginal(host,store,asset);}catch(Exception error){LocalizedUi.Set(preview.status,"Text",()=>(error is IOException||error is UnauthorizedAccessException?TextResources.Get("chatImagePreview.openGenerated.errorIsUnauthorizedAccessException"):TextResources.Get("chatImagePreview.openGenerated.errorIsUnauthorizedAccessException.alternativeA")));}}));
            preview.imageActions.Children.Add(Control(()=>(TextResources.Get("chatImagePreview.openGenerated.control.alternativeA")),"ChatPreviewCopyImage",delegate{try{GeneratedImageCard.CopyOriginal(store,asset);}catch(Exception){LocalizedUi.Set(preview.status,"Text",()=>(TextResources.Get("chatImagePreview.openGenerated.previewStatus")));}}));
            if(showDetails)preview.ShowGeneratedDetails(store,asset,regenerate);return preview;
        }
        void ShowGeneratedDetails(GeneratedImageStore store,GeneratedImage asset,Action<GeneratedImage> regenerate)
        {
            var layout=(Grid)Child;var area=(Border)layout.Children[1];var visual=area.Child as FrameworkElement;area.Child=null;
            var split=new Grid();split.ColumnDefinitions.Add(new ColumnDefinition());split.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(280)});split.RowDefinitions.Add(new RowDefinition());split.RowDefinitions.Add(new RowDefinition{Height=new GridLength(0)});area.Child=split;split.Children.Add(visual);
            var fields=new StackPanel();fields.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("chatImagePreview.showGeneratedDetails.text")),18,Ui.Ink));fields.Children.Add(LocalizedUi.Text(()=>(TextResources.Get("chatImagePreview.showGeneratedDetails.text.alternativeA")),11,Ui.Muted));var prompt=Ui.Field(asset.Prompt??"",true);prompt.Name="GeneratedImageDetailPrompt";prompt.IsReadOnly=true;prompt.MaxHeight=240;fields.Children.Add(prompt);fields.Children.Add(Ui.Text(TextResources.Format("chatImagePreview.showGeneratedDetails.template",asset.Model,asset.Width,asset.Height,asset.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")),12,Ui.Muted));
            var actions=new WrapPanel{Margin=new Thickness(0,12,0,0)};var folder=LocalizedUi.Button(()=>(TextResources.Get("chatImagePreview.showGeneratedDetails.text.alternativeB")));folder.Click+=delegate{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe","/select,\""+store.PathFor(asset.Id)+"\""){UseShellExecute=true});}catch(Exception){LocalizedUi.Set(status,"Text",()=>(TextResources.Get("chatImagePreview.showGeneratedDetails.status")));}};actions.Children.Add(folder);
            if(regenerate!=null){var again=LocalizedUi.Button(()=>(TextResources.Get("chatImagePreview.showGeneratedDetails.text.alternativeC")));again.Name="ChatPreviewRegenerate";again.Click+=delegate{Close();regenerate(asset.Copy());};actions.Children.Add(again);}fields.Children.Add(actions);
            var detail=Ui.Scroll(fields);detail.Name="GeneratedImageDetails";detail.Margin=new Thickness(12,8,20,8);split.Children.Add(detail);
            Action adapt=delegate{bool narrow=ActualWidth<800;split.ColumnDefinitions[1].Width=new GridLength(narrow?0:280);split.RowDefinitions[1].Height=narrow?GridLength.Auto:new GridLength(0);Grid.SetColumn(visual,0);Grid.SetRow(visual,0);Grid.SetColumnSpan(visual,narrow?2:1);Grid.SetRowSpan(visual,narrow?1:2);Grid.SetColumn(detail,narrow?0:1);Grid.SetRow(detail,narrow?1:0);Grid.SetColumnSpan(detail,narrow?2:1);Grid.SetRowSpan(detail,narrow?1:2);detail.MaxHeight=narrow?210:Double.PositiveInfinity;};SizeChanged+=delegate{adapt();};adapt();
        }
        internal static ChatImagePreview OpenRemote(Window host,Uri uri,string title)
        {
            var preview=new ChatImagePreview(host,title);LocalizedUi.Set(preview.status,"Text",()=>(TextResources.Get("chatImagePreview.openRemote.previewStatus")));preview.LoadRemote(uri);return preview;
        }
        async void LoadRemote(Uri uri)
        {
            byte[] bytes=null;Exception failure=null;
            try{bytes=await ReadRemoteAsync(uri,cancel.Token).ConfigureAwait(false);}
            catch(Exception error){failure=error;}
            PresentRemote(bytes,failure);
        }
        void PresentRemote(byte[] bytes,Exception failure)
        {
            // Programmatic opens (including automation) need not have an ambient
            // DispatcherSynchronizationContext. Always return to our owning UI.
            if(Dispatcher.HasShutdownStarted||Dispatcher.HasShutdownFinished)return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if(closed)return;
                if(failure is OperationCanceledException){LocalizedUi.Set(status,"Text",()=>(TextResources.Get("chatImagePreview.presentRemote.status")));return;}
                if(failure!=null){ShowError(failure);return;}
                try{SetImage(bytes);}catch(Exception error){ShowError(error);}
            }));
        }
        internal static byte[] DecodeBase64(string data,int maximum)
        {
            if(String.IsNullOrEmpty(data)||data.Length>((long)maximum+2)/3*4)throw new InvalidDataException(TextResources.Get("chatImagePreview.decodeBase64.text"));
            byte[] bytes=Convert.FromBase64String(data);if(bytes.Length>maximum)throw new InvalidDataException(TextResources.Get("chatImagePreview.decodeBase64.text.alternativeA"));return bytes;
        }
        // Read headers and each body chunk under the same deadline and byte cap.
        // This client has no application API key, cookies or default credentials.
        internal static async Task<byte[]> ReadRemoteAsync(Uri uri,CancellationToken token)
        {
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token))
            using(var handler=new HttpClientHandler{UseCookies=false,UseDefaultCredentials=false,Credentials=null,AllowAutoRedirect=false,AutomaticDecompression=System.Net.DecompressionMethods.None})
            using(var client=new HttpClient(handler))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(15));client.Timeout=System.Threading.Timeout.InfiniteTimeSpan;
                for(int redirects=0;redirects<=3;redirects++)
                {
                    if(uri==null||!uri.IsAbsoluteUri||(uri.Scheme!="http"&&uri.Scheme!="https")||!String.IsNullOrEmpty(uri.UserInfo))throw new InvalidDataException(TextResources.Get("chatImagePreview.readRemoteAsync.text"));
                    using(var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false))
                    {
                        int code=(int)response.StatusCode;if(code>=300&&code<=399){var location=response.Headers.Location;if(location==null||redirects==3)throw new InvalidDataException(TextResources.Get("chatImagePreview.readRemoteAsync.text.alternativeA"));uri=location.IsAbsoluteUri?location:new Uri(uri,location);continue;}
                        response.EnsureSuccessStatusCode();long? length=response.Content.Headers.ContentLength;if(length.HasValue&&length.Value>MaximumRemoteBytes)throw new InvalidDataException(TextResources.Get("chatImagePreview.readRemoteAsync.text.alternativeB"));
                        using(timeout.Token.Register(()=>response.Dispose()))
                        try
                        {
                            using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using(var output=new MemoryStream())
                            {var buffer=new byte[32768];while(true){int count=await input.ReadAsync(buffer,0,buffer.Length,timeout.Token).ConfigureAwait(false);if(count==0)break;if(output.Length+count>MaximumRemoteBytes)throw new InvalidDataException(TextResources.Get("chatImagePreview.readRemoteAsync.text.alternativeB"));output.Write(buffer,0,count);}return output.ToArray();}
                        }
                        catch(Exception){timeout.Token.ThrowIfCancellationRequested();throw;}
                    }
                }
                throw new InvalidDataException(TextResources.Get("chatImagePreview.readRemoteAsync.text.alternativeC"));
            }
        }
        void SetImage(byte[] bytes)
        {
            int width,height;bitmap=DecodeImage(bytes,MaximumDimension,out width,out height,out limited);image.Source=bitmap;Grid.SetRow(status,1);status.FontSize=11;status.Margin=new Thickness(12,0,12,10);LocalizedUi.Set(status,"Text",()=>(width+" × "+height+(limited?TextResources.Get("chatImagePreview.setImage.statusWidthHeight"):"")));Fit();
        }
        void SetGeneratedImage(byte[] bytes)
        {
            int width,height;bitmap=DecodeGeneratedImage(bytes,MaximumDimension,out width,out height,out limited);image.Source=bitmap;Grid.SetRow(status,1);status.FontSize=11;status.Margin=new Thickness(12,0,12,10);LocalizedUi.Set(status,"Text",()=>(width+" × "+height+(limited?TextResources.Get("chatImagePreview.setGeneratedImage.statusWidthHeight"):"")));Fit();
        }
        internal static BitmapSource DecodeImage(byte[] bytes,int maximumDimension,out int width,out int height,out bool limited)
        {return DecodeImageCore(bytes,maximumDimension,MaximumRemoteBytes,out width,out height,out limited);}
        internal static BitmapSource DecodeGeneratedImage(byte[] bytes,int maximumDimension,out int width,out int height,out bool limited)
        {return DecodeImageCore(bytes,maximumDimension,GeneratedImageStore.MaximumBytes,out width,out height,out limited);}
        static BitmapSource DecodeImageCore(byte[] bytes,int maximumDimension,int maximumBytes,out int width,out int height,out bool limited)
        {
            if(bytes==null||bytes.Length==0||bytes.Length>maximumBytes)throw new InvalidDataException(TextResources.Get("chatImagePreview.decodeImageCore.text"));
            using(var metadata=new MemoryStream(bytes,false))
            {var decoder=BitmapDecoder.Create(metadata,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);if(decoder.Frames.Count==0)throw new InvalidDataException(TextResources.Get("chatImagePreview.decodeImageCore.text.alternativeA"));width=decoder.Frames[0].PixelWidth;height=decoder.Frames[0].PixelHeight;}
            if(width<1||height<1)throw new InvalidDataException(TextResources.Get("chatImagePreview.decodeImageCore.text.alternativeB"));
            double factor=Math.Min(1,Math.Min((double)maximumDimension/Math.Max(width,height),Math.Sqrt(16000000.0/width/height)));limited=factor<1;
            var decoded=new BitmapImage();using(var input=new MemoryStream(bytes,false)){decoded.BeginInit();decoded.CacheOption=BitmapCacheOption.OnLoad;decoded.StreamSource=input;if(limited){decoded.DecodePixelWidth=Math.Max(1,(int)(width*factor));decoded.DecodePixelHeight=Math.Max(1,(int)(height*factor));}decoded.EndInit();decoded.Freeze();}
            return decoded;
        }
        void ShowError(Exception error)
        {
            if(!(error is IOException||error is FormatException||error is NotSupportedException||error is ArgumentException||error is InvalidOperationException||error is HttpRequestException||error is System.Runtime.InteropServices.ExternalException||error is System.Net.WebException))throw error;
            Grid.SetRow(status,0);status.FontSize=12;status.Margin=new Thickness(20);LocalizedUi.Set(status,"Text",()=>(TextResources.Get("chatImagePreview.showError.status")));image.Source=null;bitmap=null;percentage.Text="";
        }
        internal void ZoomBy(double factor){if(bitmap==null)return;zoom=Math.Max(.1,Math.Min(20,zoom*factor));ResizeImage();}
        internal void Fit(){zoom=1;scroll.ScrollToHome();ResizeImage();}
        void ResizeImage()
        {
            if(bitmap==null)return;double width=Math.Max(1,scroll.ActualWidth-20),height=Math.Max(1,scroll.ActualHeight-20);double fit=Math.Min(1,Math.Min(width/bitmap.PixelWidth,height/bitmap.PixelHeight));image.Width=Math.Max(1,bitmap.PixelWidth*fit*zoom);image.Height=Math.Max(1,bitmap.PixelHeight*fit*zoom);percentage.Text=Math.Round(fit*zoom*100)+"%";
        }
        void BeforeInput(object sender,PreProcessInputEventArgs e)
        {
            var mouse=e.StagingItem.Input as MouseButtonEventArgs;if(mouse!=null&&(mouse.ChangedButton==MouseButton.XButton1||mouse.ChangedButton==MouseButton.XButton2)&&host.IsMouseOver){mouse.Handled=true;return;}
            var key=e.StagingItem.Input as KeyEventArgs;var focused=Keyboard.FocusedElement as DependencyObject;if(key==null||key.RoutedEvent!=Keyboard.PreviewKeyDownEvent||focused==null||Window.GetWindow(focused)!=host)return;HostKeyDown(host,key);
            if(!key.Handled&&!IsKeyboardFocusWithin){Focus();key.Handled=true;return;}
            if(!key.Handled&&(Keyboard.Modifiers&(ModifierKeys.Control|ModifierKeys.Alt|ModifierKeys.Windows))!=0)key.Handled=true;
        }
        void HostKeyDown(object sender,KeyEventArgs e)
        {
            if(closed||e.Handled)return;if(e.Key==Key.Escape){e.Handled=true;Close();return;}if(e.Key==Key.Add||e.Key==Key.OemPlus){ZoomBy(1.25);e.Handled=true;}else if(e.Key==Key.Subtract||e.Key==Key.OemMinus){ZoomBy(1/1.25);e.Handled=true;}else if(e.Key==Key.D0||e.Key==Key.NumPad0){Fit();e.Handled=true;}
        }
        void EndDrag(){drag=null;if(scroll.IsMouseCaptured)scroll.ReleaseMouseCapture();scroll.Cursor=Cursors.Arrow;}
        void HostClosed(object sender,EventArgs e){Close();}
        internal void Close()
        {
            if(closed)return;closed=true;cancel.Cancel();EndDrag();InputManager.Current.PreProcessInput-=BeforeInput;host.RemoveHandler(Keyboard.PreviewKeyDownEvent,new KeyEventHandler(HostKeyDown));host.Closed-=HostClosed;root.Children.Remove(this);foreach(var entry in backdropEffects)entry.Key.SetCurrentValue(UIElement.EffectProperty,entry.Value);backdropEffects.Clear();if(ReferenceEquals(host.GetValue(ActiveProperty),this))host.ClearValue(ActiveProperty);image.Source=null;bitmap=null;
            var previous=previousFocus as UIElement;if(host.IsVisible&&previous!=null&&previous.IsVisible&&previous.IsEnabled)Keyboard.Focus(previousFocus);
        }
static Button Control(System.Func<string> text,string name,Action action){return LocalizedUi.With(Control(text(),name,action),"Content",text);}    }
}
