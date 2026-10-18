using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
namespace Dafeiyu.Tests
{
    public static class FinalOutputFilesTests
    {
        static int checks;static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        static IEnumerable<T> All<T>(DependencyObject p)where T:DependencyObject{if(p is T)yield return(T)p;for(int i=0;i<VisualTreeHelper.GetChildrenCount(p);i++)foreach(var t in All<T>(VisualTreeHelper.GetChild(p,i)))yield return t;}
        [STAThread]public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
            try{
                string root=Path.Combine(Directory.GetCurrentDirectory(),"work","permissions","outputs-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string zip=Path.Combine(root,"交付 (最终).zip"),html=Path.Combine(root,"背景板.html");File.WriteAllBytes(zip,new byte[]{80,75,3,4,0,255});File.WriteAllText(html,"<h1>测试交付</h1>");
                var cfg=new PetSettings{AutoUpdate=false,GreetingEnabled=false,ShowPredictedMessages=false,OnboardingState="completed",AgentWorkspace=root};foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"data","settings.json")),null);
                var message=new ChatMessage("assistant","文件已准备好。\n\n[下载压缩包](<"+new Uri(zip).AbsoluteUri+">)\n\n[网页](<"+new Uri(html).AbsoluteUri+">)");var history=new StackPanel();var host=new Window{Content=history,Width=720,Height=650};var view=new ChatMessageView(message,history,pet,host);history.Children.Add(view);host.Show();host.UpdateLayout();Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background,new Action(delegate{}));
                var cards=All<Border>(view).Where(b=>b.Name=="OutputFileCard").ToArray();Check(cards.Length==2,"final local ZIP and HTML links render two deliverable cards");
                foreach(var card in cards){var names=card.ContextMenu.Items.OfType<MenuItem>().Select(i=>i.Header as string).ToArray();Check(new[]{"打开文件","打开方式…","另存为…","复制路径","复制文件内容","在资源管理器中打开"}.All(names.Contains),"output card retains all existing file actions");}
                Check(!cards[0].ContextMenu.Items.OfType<MenuItem>().Single(i=>(string)i.Header=="复制文件内容").IsEnabled,"binary ZIP keeps copy-content entry disabled");
                var summary=new AgentChangeSummaryCard(p=>{});var task=new AgentTaskState{Workspace=root,Status="complete"};task.Changes.Add(new AgentChange{Path=html,Status="modified",AddedLines=1,RemovedLines=0});summary.Refresh(task);view.SetChangeSummary(summary);host.UpdateLayout();
                Check(cards[0].TranslatePoint(new Point(),view).Y>=summary.TranslatePoint(new Point(0,summary.ActualHeight),view).Y&&All<StackPanel>(view).Single(p=>p.Name=="ReplyActions").TranslatePoint(new Point(),view).Y>=cards[1].TranslatePoint(new Point(0,cards[1].ActualHeight),view).Y,"delivery cards appear after edited-files summary and before reply actions");
                var bitmap=new RenderTargetBitmap((int)host.ActualWidth,(int)host.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(host);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(Directory.GetCurrentDirectory(),"work","permissions","final-output-files.png")))png.Save(output);
                ReplyFileReference reference;Check(ReplyFileReference.TryCreate(new Uri(html).AbsoluteUri,null,out reference)&&reference.ReadText()=="<h1>测试交付</h1>","copy-content backend reads actual UTF-8 delivery file");
                var large=new byte[ChatAttachment.MaximumBytes+1024];large[0]=80;large[large.Length-1]=255;File.WriteAllBytes(zip,large);Check(ReplyFileReference.TryCreate(new Uri(zip).AbsoluteUri,null,out reference),"large binary delivery is a reference, not an upload attachment");string saved=Path.Combine(root,"另存.zip");reference.SaveTo(saved);Check(File.ReadAllBytes(saved).SequenceEqual(large),"save-as backend preserves binary bytes beyond upload limits");
                string bad="[web](https://example.com/a.zip) [remote](file://server/share/a.zip) [duplicate](<"+new Uri(zip).AbsoluteUri+">) [same](<"+new Uri(zip).AbsoluteUri+">)\n\n```md\n[example](<"+new Uri(html).AbsoluteUri+">)\n```";Check(ReplyFileReference.Parse(bad).Count==1,"reference parser skips remote URLs, examples in code and duplicate paths");
                var store=new HistoryStore(Path.Combine(root,"history"));store.SaveAsync("fixture",new[]{message}).GetAwaiter().GetResult();var restored=store.Load("fixture").Single();Check(ReplyFileReference.Parse(restored.Content).Count==2,"delivery links survive encrypted history reopen without uploading file bytes");
                var pending=new ChatMessage("assistant",message.Content,"pending","pending","text");view.Update(pending);Check(!All<Border>(view).Any(b=>b.Name=="OutputFileCard"),"streaming output does not expose partial deliverables");view.Update(message);view.Update(message.Copy());Check(All<Border>(view).Count(b=>b.Name=="OutputFileCard")==2,"completion publishes delivery cards once");
                File.Delete(html);history.Children.Clear();var reopened=new ChatMessageView(restored,history,pet,host);history.Children.Add(reopened);host.UpdateLayout();var unavailable=All<Border>(reopened).Single(b=>b.Name=="OutputFileCard"&&(b.ToolTip as string)==html);Check(!unavailable.ContextMenu.Items.OfType<MenuItem>().Single(i=>(string)i.Header=="打开文件").IsEnabled&&unavailable.ContextMenu.Items.OfType<MenuItem>().Single(i=>(string)i.Header=="复制路径").IsEnabled,"missing historical files remain visible with unavailable actions");
                int invoked=0;var menu=FileActionsMenu.Create(()=>invoked++,()=>invoked++,()=>invoked++,()=>invoked++,()=>invoked++,()=>invoked++);foreach(MenuItem item in menu.Items)item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Check(invoked==6,"shared six-action menu invokes each supplied action exactly once");
                var picture=new ChatMessage("assistant","图片已准备好");picture.SetAttachments(new[]{ChatAttachment.CreateImage("fixture.png",new byte[]{137,80,78,71,13,10,26,10})});view.Update(picture);Check(!All<Border>(view).Single(b=>b.Name=="OutputFileCard").ContextMenu.Items.OfType<MenuItem>().Single(i=>(string)i.Header=="复制文件内容").IsEnabled,"assistant binary image attachment cannot copy encoded content as text");
                Console.WriteLine("Final output files: "+checks+" passed");return 0;
            }catch(Exception e){Console.Error.WriteLine(e);return 1;}finally{foreach(var window in app.Windows.OfType<Window>().ToArray())window.Close();if(pet!=null)pet.Close();app.Shutdown();}
        }
    }
}
