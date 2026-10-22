using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class AgentQuestionUiSmoke
    {
        static int count;
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;Console.WriteLine("PASS "+message);}
        static IEnumerable<T> All<T>(DependencyObject node) where T:DependencyObject{if(node is T)yield return (T)node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var item in All<T>(VisualTreeHelper.GetChild(node,i)))yield return item;}
        static void Until(Func<bool> done){var end=DateTime.UtcNow.AddSeconds(5);while(!done()&&DateTime.UtcNow<end){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(delegate{}));Thread.Sleep(5);}Check(done(),"UI operation completes");}
        static AgentQuestionRequest Request(string session,string call){return new AgentQuestionRequest{SessionId=session,TaskId="task",CallId=call,Question="需要修改哪个项目？",Options=new[]{"当前工作区","另一个项目"},AllowFreeInput=true};}
        static void Capture(Window owner,string name){owner.UpdateLayout();var image=new RenderTargetBitmap((int)owner.ActualWidth,(int)owner.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(owner);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));Directory.CreateDirectory("work/question-evidence");using(var file=File.Create("work/question-evidence/"+name))encoder.Save(file);}
        [STAThread] public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var owner=new Window{Width=600,Height=440,Title="任务信息",Background=ThemeManager.Surface};AgentQuestionSurface first=null,other=null;
            using(var controller=new AgentQuestionController())try
            {
                first=new AgentQuestionSurface(controller,r=>r.SessionId=="first");other=new AgentQuestionSurface(controller,r=>r.SessionId=="other");var body=new StackPanel{Margin=new Thickness(24)};body.Children.Add(first);body.Children.Add(other);owner.Content=body;owner.Show();
                var answer=controller.AskAsync(Request("first","choice"),CancellationToken.None);Until(()=>first.IsVisible);Check(!other.IsVisible&&app.Windows.Count==1,"question remains in its own conversation");Check(!All<FrameworkElement>(owner).Any(e=>e.Name=="AllowAgentOperation"),"question card contains no approval action");
                ThemeManager.Apply("light");Capture(owner,"question-light.png");ThemeManager.Apply("dark");Capture(owner,"question-dark.png");owner.Width=390;Capture(owner,"question-dark-narrow.png");
                All<Button>(first).Single(b=>b.Name=="AgentQuestionOption1").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Until(()=>answer.IsCompleted);Check(answer.Result.Text=="另一个项目"&&answer.Result.OptionIndex==1,"choice click resumes original call");Until(()=>!first.IsVisible);
                var pendingOther=controller.AskAsync(Request("other","free"),CancellationToken.None);Until(()=>other.IsVisible);owner.UpdateLayout();All<TextBox>(other).Single(t=>t.Name=="AgentQuestionAnswer").Text="指定自定义目录";All<Button>(other).Single(b=>b.Name=="SubmitAgentQuestion").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Until(()=>pendingOther.IsCompleted);Check(pendingOther.Result.Text=="指定自定义目录","free answer resumes matching session");
                using(var cancel=new CancellationTokenSource()){var pending=controller.AskAsync(Request("first","cancel"),cancel.Token);Until(()=>first.IsVisible);cancel.Cancel();Until(()=>pending.IsCanceled&&!first.IsVisible);}
                var closing=controller.AskAsync(Request("first","closing"),CancellationToken.None);Until(()=>first.IsVisible);owner.Close();controller.Dispose();Until(()=>closing.IsCanceled);Check(controller.Requests.Count==0,"closing owner cancels all pending question waits");Console.WriteLine("AgentQuestionUiSmoke: "+count+" passed");return 0;
            }
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
            finally{if(first!=null)first.Dispose();if(other!=null)other.Dispose();foreach(var window in app.Windows.OfType<Window>().ToArray())window.Close();app.Shutdown();}
        }
    }
}
