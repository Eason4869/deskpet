using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class AgentApprovalUiSmoke
    {
        static int count;static void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;Console.WriteLine("PASS "+why);}
        static IEnumerable<T> All<T>(DependencyObject node)where T:DependencyObject{if(node is T)yield return (T)node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var item in All<T>(VisualTreeHelper.GetChild(node,i)))yield return item;}
        static void Until(Func<bool> done){var end=DateTime.UtcNow.AddSeconds(4);while(!done()&&DateTime.UtcNow<end){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(delegate{}));Thread.Sleep(5);}Check(done(),"UI operation completes");}
        [STAThread] public static int Main(){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var owner=new Window{Width=300,Height=200};try{owner.Show();var provider=new AgentApprovalController(owner);int effects=0;var policy=new AgentPermissionRuntime();policy.Add(new AgentPermissionRule{Action="write",Resource="**",Effect=AgentPermissionEffect.Ask});var runtime=new AgentToolRuntime(policy,null,provider);runtime.Register(new AgentToolDefinition{Name="write_file"},(a,c,t)=>{effects++;return Task.FromResult("done");});
            for(int index=0;index<4;index++)using(var cancel=new CancellationTokenSource()){
                var operation=runtime.ExecuteAsync("write_file",new Dictionary<string,object>{{"path","隔离测试.txt"},{"content","fixture"}},new AgentToolExecutionContext{SessionId="session",ProjectId="project-A",TaskId="task-A",CallId="call-"+index},cancel.Token);
                Until(()=>app.Windows.OfType<Window>().Any(w=>w.Name=="AgentApprovalWindow"));var window=app.Windows.OfType<Window>().Single(w=>w.Name=="AgentApprovalWindow");
                Check(All<TextBlock>(window).Any(t=>t.Text.Contains("task-A")&&t.Text.Contains("project-A")),"approval shows bound task and project");
                if(index<2)All<Button>(window).Single(b=>b.Name==(index==0?"AllowAgentOperation":"RejectAgentOperation")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));else if(index==2)cancel.Cancel();else window.Close();
                Until(()=>operation.IsCompleted);try{operation.GetAwaiter().GetResult();}catch(UnauthorizedAccessException){}catch(OperationCanceledException){}
                Check(effects==1,"only explicit allow reaches fixture effect");Until(()=>!app.Windows.OfType<Window>().Any(w=>w.Name=="AgentApprovalWindow"));
            }
            Console.WriteLine("Agent approval UI tests: "+count+" passed");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}finally{foreach(var w in app.Windows.OfType<Window>().ToArray())w.Close();app.Shutdown();}}
    }
}
