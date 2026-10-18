using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Runtime.CompilerServices;
namespace Dafeiyu
{
    internal sealed class FilePreviewPane:DockPanel,IDisposable
    {
        readonly ContentControl content=new ContentControl{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};readonly TextBlock notice=Ui.Text("",11,AgentUi.Muted);readonly Button preview,source;readonly CheckBox network;readonly Action close;
        HtmlFilePreview html;bool disposed,sourceMode;int revision;
        internal readonly string FilePath;
        internal bool IsHtml{get{return new[]{".html",".htm"}.Contains(Path.GetExtension(FilePath).ToLowerInvariant());}}
        internal bool SourceMode{get{return sourceMode;}}
        internal Task Ready{get;private set;}
        internal FilePreviewPane(string path,Action close)
        {
            FilePath=Path.GetFullPath(path);this.close=close;Name="FilePreviewPane";Background=Ui.Brush("#F8F9FB");LastChildFill=true;
            var header=new DockPanel{Margin=new Thickness(14,12,12,8)};var exit=UiIcons.Button("close","关闭预览");exit.Name="CloseFilePreview";exit.Click+=delegate{close();};DockPanel.SetDock(exit,Dock.Right);header.Children.Add(exit);var open=UiIcons.Button("expand","用系统应用打开");open.Name="OpenPreviewExternally";open.Click+=delegate{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(FilePath){UseShellExecute=true});}catch(Exception){notice.Text="无法打开文件，请检查文件是否仍在原位置。";}};DockPanel.SetDock(open,Dock.Right);header.Children.Add(open);var title=Ui.Text(Path.GetFileName(FilePath),14,AgentUi.Ink);title.Name="PreviewFileName";title.FontWeight=FontWeights.SemiBold;title.TextWrapping=TextWrapping.NoWrap;title.TextTrimming=TextTrimming.CharacterEllipsis;title.VerticalAlignment=VerticalAlignment.Center;title.ToolTip=FilePath;header.Children.Add(title);DockPanel.SetDock(header,Dock.Top);Children.Add(header);
            var tools=new DockPanel{Margin=new Thickness(12,0,12,8)};preview=AgentUi.Nav("预览","ShowFilePreview");source=AgentUi.Nav("源码","ShowFileSource");preview.Padding=source.Padding=new Thickness(12,4,12,4);preview.MinHeight=source.MinHeight=28;var tabs=new StackPanel{Orientation=Orientation.Horizontal};tabs.Children.Add(preview);tabs.Children.Add(source);DockPanel.SetDock(tabs,Dock.Left);tools.Children.Add(tabs);var refresh=UiIcons.Button("back","重新加载文件");refresh.Name="ReloadFilePreview";refresh.Click+=delegate{Ready=LoadAsync(sourceMode);};DockPanel.SetDock(refresh,Dock.Right);tools.Children.Add(refresh);network=Ui.Toggle("外部资源",false,delegate{});network.Name="PreviewExternalResources";network.Visibility=IsHtml?Visibility.Visible:Visibility.Collapsed;network.ToolTip="允许此网页加载外部图片、脚本及网络数据。默认只读取当前文件夹中的资源。";network.Checked+=delegate{Ready=LoadAsync(sourceMode);};network.Unchecked+=delegate{Ready=LoadAsync(sourceMode);};tools.Children.Add(network);DockPanel.SetDock(tools,Dock.Top);Children.Add(tools);
            notice.Name="FilePreviewNotice";notice.TextWrapping=TextWrapping.Wrap;notice.Margin=new Thickness(14,2,14,8);DockPanel.SetDock(notice,Dock.Bottom);Children.Add(notice);Children.Add(content);
            preview.Click+=delegate{Ready=LoadAsync(false);};source.Click+=delegate{Ready=LoadAsync(true);};Ready=Task.FromResult(0);Loaded+=delegate{if(revision==0)Ready=LoadAsync(false);};
        }
        internal async Task LoadAsync(bool asSource)
        {
            if(disposed)return;int current=++revision;sourceMode=asSource;notice.Text="";if(html!=null){html.Dispose();html=null;}content.Content=null;preview.Background=asSource?Brushes.Transparent:AgentUi.Soft;source.Background=asSource?AgentUi.Soft:Brushes.Transparent;
            try{
                if(!File.Exists(FilePath))throw new FileNotFoundException("文件已移动或删除。");string extension=Path.GetExtension(FilePath).ToLowerInvariant();
                if(!asSource&&IsHtml){WebPreviewRuntime.Ensure();await ShowHtmlAsync(current);return;}
                if(!asSource&&new[]{".png",".jpg",".jpeg",".gif",".bmp",".webp"}.Contains(extension)){var image=new System.Windows.Media.Imaging.BitmapImage();using(var stream=new FileStream(FilePath,FileMode.Open,FileAccess.Read,FileShare.Read)){if(stream.Length>32*1024*1024)throw new IOException("图片过大，请用系统应用打开。");image.BeginInit();image.CacheOption=System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;image.DecodePixelWidth=1800;image.StreamSource=stream;image.EndInit();image.Freeze();}content.Content=Ui.Scroll(new Image{Source=image,Stretch=Stretch.Uniform});return;}
                ReplyFileReference reference;if(!ReplyFileReference.TryCreate(FilePath,null,out reference)||!reference.TextFile){content.Content=EmptyPreview("此文件暂不支持直接预览。\n可在右键菜单选择打开或另存为。");return;}
                string text=ReadSource(FilePath);if(!asSource&&extension==".md")content.Content=Ui.Scroll(new MarkdownView{Name="MarkdownFilePreview",Text=text,Margin=new Thickness(18,10,18,14)});else{content.Content=SourceView(text);SelectSource();}
            }catch(Exception e){if(disposed||current!=revision)return;if(!(e is NotSupportedException||e is FormatException||e is IOException||e is UnauthorizedAccessException||e is ArgumentException||e is InvalidOperationException||e is System.Runtime.InteropServices.COMException||e is DllNotFoundException||e is TypeInitializationException))throw;notice.Text=IsHtml?"网页预览不可用，可查看源码或用系统应用打开。":"预览未完成，请检查文件或使用系统应用打开。";content.Content=EmptyPreview(notice.Text);}
        }
        void SelectSource(){sourceMode=true;preview.Background=Brushes.Transparent;source.Background=AgentUi.Soft;}
        [MethodImpl(MethodImplOptions.NoInlining)]async Task ShowHtmlAsync(int current){var next=new HtmlFilePreview(FilePath,network.IsChecked==true);html=next;content.Content=next;try{await next.InitializeAsync();}catch(Exception){if(!disposed&&current==revision){next.Dispose();html=null;content.Content=SourceView(ReadSource(FilePath));SelectSource();notice.Text="网页预览组件不可用，已显示源码；可用系统应用打开。";}}}
        string ReadSource(string path){using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))using(var reader=new StreamReader(stream,new UTF8Encoding(false,true),true)){var buffer=new char[250001];int length=0,read;while(length<buffer.Length&&(read=reader.Read(buffer,length,buffer.Length-length))>0)length+=read;if(length>250000){notice.Text="仅预览前 25 万字符，完整内容可用系统应用打开。";length=250000;}return new string(buffer,0,length);}}
        static FrameworkElement EmptyPreview(string message){var text=Ui.Text(message,13,AgentUi.Muted);text.TextAlignment=TextAlignment.Center;text.VerticalAlignment=VerticalAlignment.Center;text.HorizontalAlignment=HorizontalAlignment.Center;text.Margin=new Thickness(24);return text;}
        static FrameworkElement SourceView(string text)
        {
            var grid=new Grid{Name="SourceFilePreview",Background=Ui.Brush("#F8F9FB")};grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});grid.ColumnDefinitions.Add(new ColumnDefinition());var numbers=new TextBlock{Name="SourceLineNumbers",Text=String.Join("\n",Enumerable.Range(1,text.Count(c=>c=='\n')+1)),FontFamily=new FontFamily("Consolas"),FontSize=12,Foreground=Ui.Brush("#A1A6AE"),TextAlignment=TextAlignment.Right,Margin=new Thickness(12,0,12,0)};var gutter=new ScrollViewer{Content=numbers,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,IsHitTestVisible=false,Background=Ui.Brush("#F2F4F6")};grid.Children.Add(gutter);var code=new TextBox{Name="PreviewSourceText",Text=text,FontFamily=new FontFamily("Consolas"),FontSize=12,IsReadOnly=true,AcceptsReturn=true,TextWrapping=TextWrapping.NoWrap,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(8,0,8,0),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(code,1);grid.Children.Add(code);code.AddHandler(ScrollViewer.ScrollChangedEvent,new ScrollChangedEventHandler(delegate(object sender,ScrollChangedEventArgs e){gutter.ScrollToVerticalOffset(e.VerticalOffset);}));return grid;
        }
        internal Task<string> EvaluateHtmlAsync(string script){if(html==null)throw new InvalidOperationException("HTML 预览尚未就绪。");return html.EvaluateAsync(script);}
        internal Task CaptureHtmlAsync(Stream stream){if(html==null)throw new InvalidOperationException("HTML 预览尚未就绪。");return html.CaptureAsync(stream);}
        public void Dispose(){if(disposed)return;disposed=true;revision++;if(html!=null){html.Dispose();html=null;}content.Content=null;}
    }
}
