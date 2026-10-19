using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class ProcessAvatarTests
    {
        static int checks;
        static readonly BindingFlags hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in All<T>(VisualTreeHelper.GetChild(root,i)))yield return item;}
        static void Pump(){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{}));}
        static void Pump(int milliseconds){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(milliseconds)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static void Capture(Window host,string name){host.UpdateLayout();var bitmap=new RenderTargetBitmap((int)host.ActualWidth,(int)host.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(host);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(Directory.GetCurrentDirectory(),"work","permissions",name)))png.Save(output);}
        static object Field(ChatMessageView view,string name){return typeof(ChatMessageView).GetField(name,hidden).GetValue(view);}
        static void Motion(ChatMessageView view,bool enabled){typeof(ChatMessageView).GetMethod("RefreshProcessMotion",hidden).Invoke(view,new object[]{enabled});}
        sealed class Silent:IReplySpeech{public System.Threading.Tasks.Task SpeakAsync(string text,PetSettings settings,System.Threading.CancellationToken cancel){return System.Threading.Tasks.Task.FromResult(0);}public void SetVolume(int volume){}public void Stop(){}public void Dispose(){}}
        sealed class Immediate:IConversationApi
        {
            public System.Threading.Tasks.Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,System.Threading.CancellationToken cancel){return System.Threading.Tasks.Task.FromResult("身份测试答复");}
            public System.Threading.Tasks.Task<string> TranscribeAsync(ConnectionSettings settings,byte[] wave,System.Threading.CancellationToken cancel){throw new InvalidOperationException("No microphone in this test");}
            public System.Threading.Tasks.Task<byte[]> SpeechAsync(ConnectionSettings settings,string voice,string text,System.Threading.CancellationToken cancel){throw new InvalidOperationException("No audio in this test");}
            public void Dispose(){}
        }
        [STAThread]public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
            try
            {
                Check(typeof(ChatMessage).GetProperty("CompanionSkinId")!=null,"message exposes optional persisted companion identity");
                string root=Path.Combine(Directory.GetCurrentDirectory(),"work","permissions","process-avatar-fixture");Directory.CreateDirectory(root);
                var cfg=new PetSettings{AutoUpdate=false,GreetingEnabled=false,ShowPredictedMessages=false,ReadReplies=false,OnboardingState="completed",AgentDetailedProcess=true};foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"settings.json")),null);
                using(var session=new ConversationSession(new Immediate(),new Silent(),null))
                {
                    session.SelectPersona("dafeiyu");var sourceLog=new ConversationLog();ChatMessage sent;var message=sourceLog.BeginTurn("任务","text",out sent);message=sourceLog.StartProcessing(message.Id,true,skinId:"dafeiyu");Check(message.CompanionSkinId=="dafeiyu"&&message.Copy().CompanionSkinId=="dafeiyu","conversation processing stores companion identity in copied messages");message.SetTools(new[]{new AgentToolEvent{Id="tool",Name="read_file",Summary="读取 fixture.txt\n真实工具详情",Status="running"}});
                    var history=new StackPanel();var host=new Window{Content=history,Width=640,Height=700};var view=new ChatMessageView(message,history,pet,host,session);history.Children.Add(view);host.Show();host.UpdateLayout();Pump();
                    var avatar=All<Border>(view).SingleOrDefault(b=>b.Name=="ProcessAvatar");Check(avatar!=null,"process header renders a pet avatar");
                    var face=(Image)avatar.Child;var label=All<TextBlock>(view).Single(t=>t.Name=="ProcessStatus");var record=All<Expander>(view).Single(e=>e.Name=="ReasoningDetails");var process=All<StackPanel>(view).Single(p=>p.Name=="ThinkingProcess");var actions=All<StackPanel>(view).Single(p=>p.Name=="ReplyActions");
                    var activity=All<Border>(view).Single(b=>b.Name=="ProcessActivityIcon");
                    foreach(string toolName in new[]{"read_file","write_file","run_command","search_files","fetch_url"})
                    {
                        message.SetTools(new[]{new AgentToolEvent{Id="tool",Name=toolName,Summary="fixture.txt",Status="running"}});view.Update(message);host.UpdateLayout();
                        Rect iconBounds=activity.Child.TransformToAncestor(activity).TransformBounds(new Rect(activity.Child.RenderSize));
                        Check(iconBounds.Left>=-.1&&iconBounds.Top>=-.1&&iconBounds.Right<=activity.ActualWidth+.1&&iconBounds.Bottom<=activity.ActualHeight+.1,"entire activity glyph fits its slot: "+toolName);
                    }
                    message.SetTools(new[]{new AgentToolEvent{Id="tool",Name="read_file",Summary="读取 fixture.txt\n真实工具详情",Status="running"}});view.Update(message);host.UpdateLayout();
                    Check(avatar.ActualWidth==20&&avatar.ActualHeight==20&&avatar.CornerRadius.TopLeft==4&&face.Clip is RectangleGeometry,"avatar uses a real 20px rounded image");
                    Check(avatar.TranslatePoint(new Point(20,10),view).X<label.TranslatePoint(new Point(0,0),view).X,"avatar is left of process status text");
                    Check(face.Source==SkinCatalog.GetAvatar(pet.Catalog.Skins.Single(s=>s.Id=="dafeiyu")),"avatar follows message session persona instead of currently selected pet");var originalFace=face.Source;
                    Check(process.IsVisible&&((StackPanel)view.Child).Children.IndexOf(process)<((StackPanel)view.Child).Children.IndexOf(view.Body),"running process stays above reply body");
                    if(SystemParameters.ClientAreaAnimation)
                    {
                        var glow=(DropShadowEffect)avatar.Effect;Check(glow.HasAnimatedProperties&&Field(view,"shimmerBrush")!=null,"loaded pending message starts subtle glow with existing text shimmer");
                        Pump(100);double firstGlow=glow.Opacity;Pump(350);Check(glow.Opacity>firstGlow+.005&&glow.Opacity<=.28&&glow.BlurRadius==6&&glow.ShadowDepth==0,"real WPF breathing clock progresses within a restrained opacity range");
                    }
                    Capture(host,"process-avatar-running.png");
                    Motion(view,false);Check(!((DropShadowEffect)avatar.Effect).HasAnimatedProperties&&Field(view,"shimmerBrush")==null&&label.Foreground==AgentUi.Muted,"reduced motion stops glow and shimmer and restores static text");
                    Motion(view,true);if(SystemParameters.ClientAreaAnimation)Check(((DropShadowEffect)avatar.Effect).HasAnimatedProperties,"allowed motion restarts while pending");
                    history.Children.Remove(view);Pump();Check(!((DropShadowEffect)avatar.Effect).HasAnimatedProperties&&Field(view,"shimmerBrush")==null,"unloading releases both animation clocks");
                    Check(!(bool)Field(view,"processMotionSubscribed"),"unloading detaches system motion change subscription");history.Children.Add(view);Pump();if(SystemParameters.ClientAreaAnimation)Check(((DropShadowEffect)avatar.Effect).HasAnimatedProperties,"reloading pending process resumes its glow");
                    message.Update("正在输出正文","pending");view.Update(message);host.UpdateLayout();Check(process.IsVisible&&((StackPanel)view.Child).Children.IndexOf(process)<((StackPanel)view.Child).Children.IndexOf(view.Body),"streaming body retains running status above output");
                    session.SelectPersona("marfu");view.Update(message);Check(ReferenceEquals(face.Source,originalFace),"existing message avatar remains stable when session persona changes");
                    message.ToolEvents[0].Status="complete";message.SetProcessingTime(2400);message.Update("结果已经准备完成。","complete");view.Update(message);host.UpdateLayout();
                    var store=new HistoryStore(Path.Combine(root,"history"));store.SaveAsync("avatar",new[]{message}).GetAwaiter().GetResult();var reopenedMessage=new HistoryStore(store.DirectoryPath).Load("avatar").Single();Check(reopenedMessage.CompanionSkinId=="dafeiyu","companion identity survives encrypted history save and a fresh store reopening");
                    var reopened=new ChatMessageView(reopenedMessage,history,pet,host,session);history.Children.Add(reopened);host.UpdateLayout();Pump();Check(All<Image>(reopened).Single(i=>i.Name=="ProcessAvatarImage").Source==originalFace,"reopened history retains its original avatar under a different active persona");Check(!All<Expander>(reopened).Single(e=>e.Name=="ReasoningDetails").IsExpanded,"reopened completed history defaults to a folded process record");history.Children.Remove(reopened);
                    var legacy=new ChatMessage("assistant","旧版本答复");legacy.SetReasoning("旧版本思考",100);store.SaveAsync("legacy",new[]{legacy}).GetAwaiter().GetResult();var legacyMessage=new HistoryStore(store.DirectoryPath).Load("legacy").Single();Check(legacyMessage.CompanionSkinId==null,"encrypted legacy history without optional identity remains readable");var legacyView=new ChatMessageView(legacyMessage,history,pet,host,session);history.Children.Add(legacyView);host.UpdateLayout();Check(All<Image>(legacyView).Single(i=>i.Name=="ProcessAvatarImage").Source==SkinCatalog.GetAvatar(pet.Catalog.Skins.Single(s=>s.Id=="marfu")),"legacy history falls back to its current session companion");history.Children.Remove(legacyView);
                    Check(process.IsVisible&&process.TranslatePoint(new Point(),view).Y>=actions.TranslatePoint(new Point(0,actions.ActualHeight),view).Y,"completed process record moves below reply actions");
                    Check(!record.IsExpanded&&label.Text=="处理记录","completed work folds its retained processing record even after detailed live mode");
                    Check(!((DropShadowEffect)avatar.Effect).HasAnimatedProperties&&Field(view,"shimmerBrush")==null,"completed avatar and status text remain static");
                    record.IsExpanded=true;host.UpdateLayout();All<Expander>(view).Single(e=>e.Name=="AgentToolDetails").IsExpanded=true;host.UpdateLayout();Check(All<TextBox>(view).Any(t=>t.Text.Contains("真实工具详情")&&t.IsVisible),"completed record preserves expandable real tool details");
                    Check(All<Button>(view).Count(b=>new[]{"CopyReply","ReadReply","MoreReply"}.Contains(b.Name))==3,"copy read and more reply actions remain available");
                    cfg.AgentDetailedProcess=false;var chat=new ChatMessage("assistant","聊天答复");chat.SetReasoning("保留真实思考内容",1000);view.Update(chat);host.UpdateLayout();record.IsExpanded=true;host.UpdateLayout();Check(label.Text=="思考记录"&&All<TextBox>(view).Any(t=>t.Text=="保留真实思考内容"&&t.IsVisible),"chat retains actual reasoning in its completed record");
                    var timed=new ChatMessage("assistant","已完成");timed.StartProcessing(true);timed.SetProcessingTime(800);view.Update(timed);record.IsExpanded=true;host.UpdateLayout();Check(process.IsVisible&&All<TextBlock>(view).Any(t=>t.Name=="ProcessRecordSummary"&&t.Text.Contains("处理时间")&&t.IsVisible),"work with timing only keeps an honest expandable duration record");
                    view.Update(new ChatMessage("assistant","没有过程信息"));Check(process.Visibility==Visibility.Collapsed,"reply without recorded process does not invent a process record");
                    var user=new ChatMessageView(new ChatMessage("user","用户消息"),history,pet,host,session);history.Children.Add(user);host.UpdateLayout();Check(!All<Border>(user).Any(b=>b.Name=="ProcessAvatar")&&!All<StackPanel>(user).Any(p=>p.Name=="ThinkingProcess"),"user message stays free of assistant process UI");
                    history.Children.Remove(user);view.Update(message);record.IsExpanded=false;Capture(host,"process-avatar.png");
                    var request=cfg.Copy();request.AgentEnabled=false;request.ReadReplies=false;request.SkinId="dafeiyu";request.Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:9876/v1",Model="fixture"};session.SendTextAsync("正常生成",request,"fixture").GetAwaiter().GetResult();var generated=session.History.Messages.Last();Check(generated.CompanionSkinId=="dafeiyu"&&session.PersonaId=="marfu","normal generation records the settings snapshot companion instead of session persona");request.SkinId="marfu";session.RegenerateAsync(generated.Id,request,"fixture").GetAwaiter().GetResult();Check(session.History.Messages.Last().CompanionSkinId=="marfu","regeneration records the companion from its new operation snapshot");
                    host.Close();Pump();
                }
                Console.WriteLine("Process avatar tests: "+checks+" passed");return 0;
            }
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
            finally{foreach(var window in app.Windows.OfType<Window>().ToArray())window.Close();if(pet!=null)pet.Close();app.Shutdown();}
        }
    }
}
