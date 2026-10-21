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

namespace Dafeiyu.Tests
{
    public static class ChatAtmosphereTests
    {
        static int checks;
        static void Check(bool value,string message) { if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message); }
        static void Pump() { Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(delegate{})); }
        static RenderTargetBitmap Render(FrameworkElement element)
        {
            element.UpdateLayout();var image=new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),(int)Math.Ceiling(element.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(element);return image;
        }
        static void Save(BitmapSource image,string path) { var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var file=File.Create(path))encoder.Save(file); }
        static byte[] Pixels(BitmapSource image) { var result=new byte[image.PixelWidth*image.PixelHeight*4];image.CopyPixels(result,image.PixelWidth*4,0);return result; }
        static double Linear(byte component) { double c=component/255.0;return c<=.04045?c/12.92:Math.Pow((c+.055)/1.055,2.4); }
        static double Luminance(byte r,byte g,byte b) { return .2126*Linear(r)+.7152*Linear(g)+.0722*Linear(b); }
        static TextBlock Text(string text,double size) { return new TextBlock{Text=text,FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=size,Foreground=Ui.Ink,TextWrapping=TextWrapping.Wrap}; }
        static Int32Rect VisibleBounds(BitmapSource image)
        {
            byte[] pixels=Pixels(image);int left=image.PixelWidth,top=image.PixelHeight,right=-1,bottom=-1;
            for(int y=0;y<image.PixelHeight;y++)for(int x=0;x<image.PixelWidth;x++)
                if(pixels[(y*image.PixelWidth+x)*4+3]>0){left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);}
            return right<0?Int32Rect.Empty:new Int32Rect(left,top,right-left+1,bottom-top+1);
        }
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject
        {
            if(root is T)yield return (T)root;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in All<T>(VisualTreeHelper.GetChild(root,i)))yield return item;
        }
        static Point ArtworkCorner(Image artwork,FrameworkElement surface)
        {
            var drawing=new DrawingVisual();using(var context=drawing.RenderOpen())context.DrawRectangle(new VisualBrush(artwork),null,new Rect(0,0,artwork.ActualWidth,artwork.ActualHeight));
            var rendered=new RenderTargetBitmap((int)Math.Ceiling(artwork.ActualWidth),(int)Math.Ceiling(artwork.ActualHeight),96,96,PixelFormats.Pbgra32);rendered.Render(drawing);
            var bounds=VisibleBounds(rendered);Check(!bounds.IsEmpty,"real artwork contains rendered nontransparent character pixels");return artwork.TranslatePoint(new Point(bounds.X+bounds.Width,bounds.Y+bounds.Height),surface);
        }
        static void RealConversationAnchoring(string output)
        {
            PetWindow pet=null;ChatWindow chat=null;
            try
            {
                string folder=Path.Combine(output,"atmosphere-conversation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
                var cfg=new PetSettings{AgentEnabled=true,AgentWorkspace=folder,AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,SaveChatHistory=false,ShowPredictedMessages=false,OnboardingState="completed"};foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(folder,"settings.json")),null);
                var workspace=pet.Workspaces.CreateWorkspace(new ProjectDefinition{Id="artwork-fixture",Name="立绘位置",Workspace=folder},new ConversationDefinition{Id=Guid.NewGuid().ToString("N"),Name="审批出现后立绘应留在窗口底部",Mode="work"});
                for(int i=0;i<8;i++)workspace.Session.History.Commit("本地问题 "+i,"本地测试回复。对话背景固定在窗口右下方，审批只改变前景布局。\n\n继续检查实际窗口中的位置。");
                chat=new ChatWindow(pet,workspace){Width=1800,MinWidth=1800,Height=920,ShowActivated=false};chat.Show();Pump();chat.UpdateLayout();Pump();
                var surface=(FrameworkElement)typeof(ChatWindow).GetField("conversationSurface",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(chat);
                var artwork=All<Image>(chat).Single(i=>i.Name=="ChatAtmosphereArtwork");double bottom=ArtworkCorner(artwork,surface).Y;
                var approval=((AgentApprovalController)workspace.Session.ApprovalProvider).ApproveAsync(new AgentApprovalRequest{SessionId=workspace.Session.KernelSession.SessionId,ProjectId=workspace.ProjectId,TaskId="artwork-approval",CallId="artwork-call",ToolName="exec_command",Action="shell",Resource=folder,Arguments="{\"command\":\"git --version\"}"});Pump();chat.UpdateLayout();Pump();
                Check(All<Border>(chat).Any(b=>b.Name=="AgentApprovalCard"&&b.IsVisible),"real conversation fixture displays a genuine inline approval");
                double pendingBottom=ArtworkCorner(artwork,surface).Y;
                Check(Math.Abs(pendingBottom-bottom)<=1,"real inline approval does not push visible artwork upward (before "+bottom.ToString("F1")+", pending "+pendingBottom.ToString("F1")+")");
                Check(surface.ActualHeight-pendingBottom>=14&&surface.ActualHeight-pendingBottom<=30,"real artwork anchors near the conversation surface bottom above the disclaimer");
                foreach(string theme in new[]{"light","dark"}){ThemeManager.Apply(theme);Pump();Save(Render((FrameworkElement)chat.Content),Path.Combine(output,"atmosphere-real-approval-"+theme+".png"));}
                cfg.Text.BaseUrl="http://127.0.0.1:1/v1";cfg.Text.Model="fixture";
                // Enqueue and pause in one dispatcher turn, before the queue's yielded
                // continuation can run. This exercises real rows without sending.
                Dispatcher.CurrentDispatcher.Invoke(new Action(delegate{workspace.Queue.Enqueue("等待中的本地测试消息一");workspace.Queue.Hold();workspace.Queue.Enqueue("等待中的本地测试消息二");}));Pump();chat.UpdateLayout();Pump();
                Check(All<Grid>(chat).Count(g=>g.Name=="QueuedMessageRow"&&g.IsVisible)==2&&!workspace.Session.IsBusy,"real paused queue expands two rows without submitting a request");
                Check(Math.Abs(ArtworkCorner(artwork,surface).Y-bottom)<=1,"queue expansion alongside approval preserves the artwork bottom anchor");Save(Render((FrameworkElement)chat.Content),Path.Combine(output,"atmosphere-real-approval-queue-dark.png"));
                string preview=Path.Combine(folder,"preview.md");File.WriteAllText(preview,"# 本地工具预览");chat.OpenFilePreview(preview);Pump();chat.UpdateLayout();Pump();
                Point split=ArtworkCorner(artwork,surface);Check(Math.Abs(surface.ActualWidth-split.X-20)<=2&&Math.Abs(surface.ActualHeight-split.Y-22)<=2,"split tool layout anchors actual character pixels to the conversation surface bottom-right");
                chat.ToggleFullTabView();Pump();((PreviewWorkspace)typeof(ChatWindow).GetField("filePreview",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(chat)).SelectConversation();Pump();chat.UpdateLayout();Pump();
                Point full=ArtworkCorner(artwork,surface);Check(Math.Abs(surface.ActualWidth-full.X-20)<=2&&Math.Abs(surface.ActualHeight-full.Y-22)<=2,"full conversation tab retains actual character pixels at its bottom-right");
                All<Button>(chat).Single(b=>b.Name=="RejectAgentOperation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();chat.UpdateLayout();Pump();Check(approval.IsCompleted&&Math.Abs(ArtworkCorner(artwork,surface).Y-full.Y)<=1,"dismissing approval retains the artwork's visible bottom anchor");
            }
            finally{if(chat!=null)chat.Close();if(pet!=null)pet.Close();ThemeManager.Apply("light");Pump();}
        }
        static void ArtworkAnchoring(string output)
        {
            // Real preview padding must not lift visible feet
            // above the history's bottom edge when the surrounding workspace changes.
            var shell=new Grid{Width=1280,Height=900};shell.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(0)});shell.ColumnDefinitions.Add(new ColumnDefinition());shell.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(0)});
            var page=new Grid();page.RowDefinitions.Add(new RowDefinition());page.RowDefinitions.Add(new RowDefinition{Height=new GridLength(100)});Grid.SetColumn(page,1);shell.Children.Add(page);
            var history=new ScrollViewer{Content=new Border{Height=2400},VerticalScrollBarVisibility=ScrollBarVisibility.Hidden};page.Children.Add(history);var layer=ChatAtmosphere.Attach(history);layer.ArtworkOpacity=1;history.Opacity=0;
            string[] ids={"e-bao","dafeiyu","marfu"};string[] atlas={"Dafeiyu.EbaoAtlas","Dafeiyu.Atlas","Dafeiyu.MarfuAtlas"};string[] preview={"Dafeiyu.EbaoPreview","Dafeiyu.Preview","Dafeiyu.MarfuPreview"};
            for(int skin=0;skin<ids.Length;skin++)
            {
                layer.SetSkin(new SkinDefinition(ids[skin],ids[skin],atlas[skin],preview[skin],true));
                for(int layout=0;layout<4;layout++)
                {
                    shell.ColumnDefinitions[0].Width=new GridLength(layout==1?220:0);shell.ColumnDefinitions[2].Width=new GridLength(layout==2?360:0);page.RowDefinitions[1].Height=new GridLength(layout==3?500:100);
                    shell.Measure(new Size(shell.Width,shell.Height));shell.Arrange(new Rect(0,0,shell.Width,shell.Height));shell.UpdateLayout();Pump();
                    var rendered=Render(layer);Int32Rect bounds=VisibleBounds(rendered);Save(rendered,Path.Combine(output,"atmosphere-anchor-"+ids[skin]+"-"+layout+".png"));
                    double bottomGap=rendered.PixelHeight-(bounds.Y+bounds.Height),rightGap=rendered.PixelWidth-(bounds.X+bounds.Width);
                    Check(!bounds.IsEmpty&&Math.Abs(bottomGap-2)<=2&&Math.Abs(rightGap-20)<=2,"visible "+ids[skin]+" artwork anchors to history bottom-right in layout "+layout+" (bottom "+bottomGap+", right "+rightGap+")");
                    history.ScrollToEnd();Pump();Check(VisibleBounds(Render(layer))==bounds,"visible artwork pixels stay fixed when history scrolls in layout "+layout);history.ScrollToTop();
                }
            }
        }
        static void Reply(StackPanel messages,string heading,string body)
        {
            var section=new StackPanel{Margin=new Thickness(2,0,16,24)};var title=Text(heading,13);title.FontWeight=FontWeights.SemiBold;title.Margin=new Thickness(0,0,0,8);section.Children.Add(title);section.Children.Add(new MarkdownView{Text=body,FontSize=14});messages.Children.Add(section);
        }
        static void Comparison(string output,SkinDefinition skin)
        {
            // Each column is a complete live WPF history, not a cropped or edited screenshot.
            var board=new Grid{Width=2460,Height=920,Background=Ui.Brush("#EDF1F7")};
            for(int i=0;i<3;i++)board.ColumnDefinitions.Add(new ColumnDefinition());
            string[] labels={"6% · 轻柔","10% · 适中","14% · 明显"};double[] opacities={.06,.10,.14};
            for(int i=0;i<3;i++)
            {
                var page=new Grid{Background=Brushes.White};page.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});page.RowDefinitions.Add(new RowDefinition());page.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
                var title=new StackPanel{Margin=new Thickness(0,0,0,24)};var label=Text(labels[i],30);label.FontWeight=FontWeights.SemiBold;title.Children.Add(label);var note=Text("E包 · 相同立绘与文字 · 文字附近渐隐",13);note.Foreground=Ui.Muted;note.Margin=new Thickness(0,8,0,0);title.Children.Add(note);page.Children.Add(title);
                var messages=new StackPanel{Margin=new Thickness(2,8,4,8)};
                messages.Children.Add(new Border{HorizontalAlignment=HorizontalAlignment.Right,Background=Ui.Pale,CornerRadius=new CornerRadius(16),Padding=new Thickness(16,11,16,11),Margin=new Thickness(0,0,0,28),Child=Text("帮我整理一下今天的工作，再给一个代码示例。",14)});
                Reply(messages,"E包","今天先把最重要的事情写清楚，再逐项确认。\n\n下面展示中文正文、代码块与立绘叠加的实际阅读效果。背景固定在历史区右下角，位于文字下层。");
                Reply(messages,"工作安排","1. 上午整理需求和已有资料。\n2. 下午完成重点改动并检查结果。\n3. 晚上留一点时间，回顾今天的进展。\n\n```csharp\nvar tasks = new[] { \"整理需求\", \"完成改动\", \"检查结果\" };\nforeach (var task in tasks)\n    Console.WriteLine(task);\n```\n\n这段代码继续使用原有 Markdown 底色，正文使用清晰的深色文字。");
                Reply(messages,"阅读对照","这是一个靠近立绘的正文段落。中文、数字 0123456789 和 English text 都应保持自然清晰。\n\n需要交付时，再检查文件名、路径与最终结果。先把事情做好，然后再考虑下一步。\n\n**待你选定透明度后，再应用到日常聊天。**");
                var scroll=Ui.Scroll(messages);Grid.SetRow(scroll,1);page.Children.Add(scroll);var atmosphere=ChatAtmosphere.Attach(scroll);atmosphere.SetSkin(skin);atmosphere.ArtworkOpacity=opacities[i];
                var input=new Border{Background=Ui.Brush("#F6F8FC"),BorderBrush=Ui.Line,BorderThickness=new Thickness(1),Padding=new Thickness(14),MinHeight=66,Margin=new Thickness(0,20,0,0),Child=Text("这里是输入区，立绘不会铺到这里。",14)};Grid.SetRow(input,2);page.Children.Add(input);
                var card=new Border{Background=Brushes.White,Padding=new Thickness(22),Margin=new Thickness(12),Child=page};Grid.SetColumn(card,i);board.Children.Add(card);
            }
            board.Measure(new Size(board.Width,board.Height));board.Arrange(new Rect(0,0,board.Width,board.Height));board.UpdateLayout();Pump();Save(Render(board),Path.Combine(output,"atmosphere-comparison.png"));
        }
        [STAThread]
        public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Window window=null;
            try
            {
                string output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"permissions");Directory.CreateDirectory(output);
                ArtworkAnchoring(output);
                RealConversationAnchoring(output);
                var root=new Grid{Background=Brushes.White};root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
                var heading=Text("E包 · 对话立绘透明度预览",21);heading.FontWeight=FontWeights.SemiBold;heading.Margin=new Thickness(0,0,0,8);var header=new StackPanel{Margin=new Thickness(0,0,0,22)};header.Children.Add(heading);var subtitle=Text("仅聊天记录区 · 右下角 · 文字下层 · 输入区保持独立",12);subtitle.Foreground=Ui.Muted;header.Children.Add(subtitle);root.Children.Add(header);
                var messages=new StackPanel{Margin=new Thickness(2,8,8,12)};
                Reply(messages,"更早的记录","我们先确认这次的显示范围：\n\n立绘只停留在聊天记录区域右下角，正文与代码位于它上方。\n\n窗口变窄时，背景会自动减弱；很窄时则隐藏。\n\n下面继续看今天的工作安排。");
                messages.Children.Add(new Border{HorizontalAlignment=HorizontalAlignment.Right,Background=Ui.Pale,CornerRadius=new CornerRadius(16),Padding=new Thickness(16,11,16,11),Margin=new Thickness(0,0,0,26),Child=Text("帮我整理一下今天的工作，再给一个代码示例。",14)});
                Reply(messages,"E包","今天先把最重要的事情写清楚，再逐项确认。\n\n下面展示中文长段落、链接与代码在立绘上方的实际阅读效果。背景固定在历史区右下角，滚动记录时，它不会跟着文字移动，也不会进入输入区。");
                Reply(messages,"工作安排","1. 上午整理需求和已有资料。\n2. 下午完成重点改动并检查结果。\n3. 晚上留一点时间，回顾今天的进展。\n\n```csharp\nvar tasks = new[] { \"整理需求\", \"完成改动\", \"检查结果\" };\nforeach (var task in tasks)\n    Console.WriteLine(task);\n```\n\n这段代码区域使用原有 Markdown 控件的底色，正文继续使用清晰的深色文字。");
                Reply(messages,"阅读对照","这是一个靠近立绘的正文段落。中文、数字 0123456789 和 English text 都应保持自然清晰。\n\n需要交付时，再检查文件名、路径与最终结果。先把事情做好，然后再考虑下一步。\n\n**待你选定透明度后，再应用到日常聊天。**");
                var scroll=Ui.Scroll(messages);scroll.Name="ChatHistoryScroll";Grid.SetRow(scroll,1);root.Children.Add(scroll);
                var input=new TextBox{Text="这里是输入区，立绘不会铺到这里。",FontSize=14,MinHeight=78,TextWrapping=TextWrapping.Wrap,Padding=new Thickness(14),Background=Ui.Brush("#F6F8FC"),BorderBrush=Ui.Line,BorderThickness=new Thickness(1)};var composer=new StackPanel{Margin=new Thickness(0,18,0,0)};composer.Children.Add(input);var hint=Text("Enter 发送     Shift + Enter 换行",11);hint.Margin=new Thickness(2,9,0,0);hint.Foreground=Ui.Muted;composer.Children.Add(hint);Grid.SetRow(composer,2);root.Children.Add(composer);
                var surface=new Border{Background=Brushes.White,Padding=new Thickness(24),Child=root};var atmosphere=ChatAtmosphere.Attach(scroll);Check(ReferenceEquals(ChatAtmosphere.Attach(scroll),atmosphere),"attaching twice retains one history wrapper");Check(Grid.GetRow(atmosphere)==1 && ReferenceEquals(scroll.Parent,atmosphere),"history wrapper retains its grid position and original scroll viewer");Check(atmosphere.ArtworkOpacity==0,"production default remains disabled pending opacity selection");
                var skin=new SkinDefinition("e-bao","E包","Dafeiyu.EbaoAtlas","Dafeiyu.EbaoPreview",true);atmosphere.SetSkin(skin);var artwork=atmosphere.Children.OfType<Image>().Single();Check(artwork.Source is CroppedBitmap && ((CroppedBitmap)artwork.Source).Source.PixelWidth==480,"artwork loads the actual standing preview with transparent padding cropped");Check(!artwork.IsHitTestVisible && atmosphere.ClipToBounds && scroll.IsHitTestVisible,"artwork is clipped and never intercepts history interaction");
                window=new Window{Title="DeskPet atmosphere isolated fixture",Content=surface,Width=920,Height=880,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.CenterScreen};window.Show();Pump();scroll.ScrollToEnd();Pump();
                Check(scroll.ScrollableHeight>0,"real history remains scrollable");var baseline=Render(surface);Save(baseline,Path.Combine(output,"atmosphere-disabled.png"));byte[] baselinePixels=Pixels(baseline);Point footer=composer.TranslatePoint(new Point(0,0),surface);
                foreach(double opacity in new[]{.06,.10,.14,.20})
                {
                    atmosphere.ArtworkOpacity=opacity;heading.Text="E包 · 立绘透明度 "+opacity.ToString("P0");Pump();var rendered=Render(surface);Save(rendered,Path.Combine(output,"atmosphere-"+((int)(opacity*100)).ToString("00")+"-wide.png"));byte[] current=Pixels(rendered);int changes=0,footerChanges=0;
                    for(int y=0;y<rendered.PixelHeight;y++)for(int x=0;x<rendered.PixelWidth;x++){int i=(y*rendered.PixelWidth+x)*4;if(current[i]!=baselinePixels[i]||current[i+1]!=baselinePixels[i+1]||current[i+2]!=baselinePixels[i+2]){if(y>footer.Y+1)footerChanges++;else if(y>110)changes++;}}
                    Check(changes>200,"standing artwork visibly renders at "+opacity.ToString("P0"));Check(footerChanges==0,"input and hint pixels remain identical at "+opacity.ToString("P0"));Check(Math.Abs(artwork.Opacity-opacity)<.0001,"wide view preserves the selected preview opacity");
                    // Render the same artwork under the text positions to measure the actual
                    // non-white backdrop, excluding glyph antialiasing and opaque code panels.
                    scroll.Opacity=0;var backdrop=Render(surface);byte[] backdropPixels=Pixels(backdrop);Point historyTop=atmosphere.TranslatePoint(new Point(0,0),surface);double darkest=1;
                    for(int y=(int)Math.Ceiling(historyTop.Y);y<(int)Math.Floor(historyTop.Y+atmosphere.ActualHeight);y++)for(int x=1;x<backdrop.PixelWidth-1;x++){int i=(y*backdrop.PixelWidth+x)*4;darkest=Math.Min(darkest,Luminance(backdropPixels[i+2],backdropPixels[i+1],backdropPixels[i]));}
                    scroll.Opacity=1;double ink=Luminance(0x20,0x37,0x54),contrast=(darkest+.05)/(ink+.05);Check(contrast>=4.5,"rendered illustration backdrop preserves at least 4.5:1 body-text contrast at "+opacity.ToString("P0")+" (measured "+contrast.ToString("F2")+":1)");
                }
                Check(input.InputHitTest(new Point(20,20))!=null,"input remains independently hit-testable");
                atmosphere.ArtworkOpacity=.20;heading.Text="E包 · 立绘透明度 20% · 滚动记录";Point standingPosition=artwork.TranslatePoint(new Point(0,0),surface);scroll.ScrollToTop();Pump();Check(scroll.VerticalOffset<1 && scroll.ScrollableHeight>0,"selected 20% history still scrolls to earlier records");Check(standingPosition==artwork.TranslatePoint(new Point(0,0),surface),"selected 20% standing artwork remains fixed when messages scroll");Save(Render(surface),Path.Combine(output,"atmosphere-20-scrolled.png"));scroll.ScrollToEnd();Pump();Check(scroll.ScrollableHeight-scroll.VerticalOffset<1,"selected 20% history returns to the latest reply");
                atmosphere.ArtworkOpacity=.10;heading.Text="E包 · 立绘透明度 10% · 滚动记录";scroll.ScrollToTop();Pump();Check(scroll.VerticalOffset<1,"history scroll-to-top still works with the artwork layer");Save(Render(surface),Path.Combine(output,"atmosphere-10-scrolled.png"));
                window.Width=560;Pump();Check(artwork.Visibility==Visibility.Visible && artwork.Opacity>0 && artwork.Opacity<.10,"compact history automatically reduces opacity");heading.Text="E包 · 10% 档 · 窄窗口自动减弱";scroll.ScrollToEnd();Pump();Save(Render(surface),Path.Combine(output,"atmosphere-10-compact.png"));
                window.Width=430;Pump();Check(artwork.Visibility==Visibility.Collapsed,"very narrow history automatically hides artwork");heading.Text="窄窗口 · 自动隐藏立绘";Save(Render(surface),Path.Combine(output,"atmosphere-10-narrow-hidden.png"));
                atmosphere.ArtworkOpacity=0;window.Width=920;Pump();Check(artwork.Visibility==Visibility.Collapsed,"zero opacity reliably disables the standing artwork after resize");
                Comparison(output,skin);
                Console.WriteLine("Chat atmosphere tests: "+checks+" passed; screenshots: "+output);return 0;
            }
            catch(Exception error){Console.Error.WriteLine(error);return 1;}
            finally{if(window!=null)window.Close();app.Shutdown();}
        }
    }
}
