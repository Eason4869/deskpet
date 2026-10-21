using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class AgentApprovalUiSmoke
    {
        static int count;static void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;Console.WriteLine("PASS "+why);}
        static IEnumerable<T> All<T>(DependencyObject node)where T:DependencyObject{if(node is T)yield return(T)node;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)foreach(var item in All<T>(VisualTreeHelper.GetChild(node,i)))yield return item;}
        static void Until(Func<bool> done){var end=DateTime.UtcNow.AddSeconds(5);while(!done()&&DateTime.UtcNow<end){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(delegate{}));Thread.Sleep(5);}Check(done(),"UI operation completes");}
        static void Click(DependencyObject root,string name){All<Button>(root).Single(b=>b.Name==name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        static void Capture(Window owner,string name){owner.UpdateLayout();var image=new RenderTargetBitmap((int)owner.ActualWidth,(int)owner.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(owner);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));Directory.CreateDirectory("work/approval-evidence");using(var file=File.Create("work/approval-evidence/"+name))png.Save(file);}
        [STAThread] public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var owner=new Window{Width=760,Height=660,Title="对话中的操作确认",Background=ThemeManager.Surface};AgentApprovalSurface a=null,b=null;
            try
            {
                var panel=new StackPanel{Margin=new Thickness(24),VerticalAlignment=VerticalAlignment.Bottom};var provider=new AgentApprovalController(owner);
                a=new AgentApprovalSurface(provider,r=>r.SessionId=="session-A");b=new AgentApprovalSurface(provider,r=>r.SessionId=="session-B");panel.Children.Add(a);panel.Children.Add(b);
                var input=Ui.Field("继续补充任务要求…",true);input.MinHeight=88;panel.Children.Add(input);owner.Content=panel;owner.Show();
                int effects=0;var policy=new AgentPermissionRuntime();policy.Add(new AgentPermissionRule{Action="shell",Resource="**",Effect=AgentPermissionEffect.Ask});var runtime=new AgentToolRuntime(policy,null,provider);
                runtime.Register(new AgentToolDefinition{Name="exec_command"},(args,context,token)=>{effects++;return Task.FromResult("done");});
                for(int index=0;index<3;index++)using(var cancel=new CancellationTokenSource())
                {
                    var operation=runtime.ExecuteAsync("exec_command",new Dictionary<string,object>{{"command","git --version"},{"working_directory",@"E:\项目\代码目录"}},new AgentToolExecutionContext{SessionId="session-A",ProjectId="project-A",TaskId="task-A",CallId="call-"+index},cancel.Token);
                    Until(()=>a.Visibility==Visibility.Visible);owner.UpdateLayout();
                    Check(app.Windows.Count==1&&!All<FrameworkElement>(b).Any(e=>e.Name=="AgentApprovalCard"),"approval stays in its conversation without another window or cross-session card");
                    var detail=All<Expander>(a).Single(e=>e.Name=="AgentApprovalDetails");detail.IsExpanded=true;owner.UpdateLayout();
                    Check(All<TextBlock>(a).Any(t=>t.Text.Contains("task-A")&&t.Text.Contains("project-A")),"approval details retain bound task project and call");detail.IsExpanded=false;
                    Check(All<TextBox>(a).Single(t=>t.Name=="AgentApprovalArguments").Text=="git --version","command preview displays the command rather than JSON");
                    Check(a.TranslatePoint(new Point(0,a.ActualHeight),owner).Y<=input.TranslatePoint(new Point(0,0),owner).Y+.5,"approval remains above the input within the owner");
                    if(index==0)
                    {
                        ThemeManager.Apply("light");Capture(owner,"approval-light.png");ThemeManager.Apply("dark");Capture(owner,"approval-dark.png");
                        var field=All<TextBox>(a).Single(t=>t.Name=="AgentApprovalArguments");var bg=((SolidColorBrush)field.Background).Color;var fg=((SolidColorBrush)field.Foreground).Color;
                        Check(bg.R<90&&fg.R>150,"visible command field has a dark surface and readable text after live theme change");
                        owner.Width=390;Capture(owner,"approval-dark-narrow.png");owner.Width=760;Click(a,"AllowAgentOperation");
                    }
                    else if(index==1)Click(a,"RejectAgentOperation");else cancel.Cancel();
                    Until(()=>operation.IsCompleted);try{operation.GetAwaiter().GetResult();}catch(UnauthorizedAccessException){}catch(OperationCanceledException){}
                    Check(effects==1,"only explicit allow reaches the fixture effect");Until(()=>a.Visibility==Visibility.Collapsed);
                }
                var ra=new AgentApprovalRequest{SessionId="session-A",CallId="first",ToolName="read_file",Arguments="{}"};var rb=new AgentApprovalRequest{SessionId="session-B",CallId="second",ToolName="read_file",Arguments="{}"};
                var first=provider.ApproveAsync(ra);var second=provider.ApproveAsync(rb);Until(()=>a.IsVisible&&b.IsVisible);ra.SessionId="session-B";
                Click(b,"AllowAgentOperation");Until(()=>second.IsCompleted);Check(second.Result&&!first.IsCompleted,"a decision resolves its original request and cannot approve another conversation");Click(a,"RejectAgentOperation");Until(()=>first.IsCompleted);Check(!first.Result,"request identity remains immutable while the caller changes its object");
                var closing=provider.ApproveAsync(new AgentApprovalRequest{SessionId="session-A",CallId="close",Arguments="{}"});Until(()=>a.IsVisible);owner.Close();Until(()=>closing.IsCompleted);Check(closing.IsCanceled,"closing the owner cancels pending approval without executing it");
                Console.WriteLine("Agent approval UI tests: "+count+" passed");return 0;
            }
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
            finally{if(a!=null)a.Dispose();if(b!=null)b.Dispose();foreach(var w in app.Windows.OfType<Window>().ToArray())w.Close();app.Shutdown();}
        }
    }
}
