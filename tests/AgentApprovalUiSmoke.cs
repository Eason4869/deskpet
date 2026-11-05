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
                a=new AgentApprovalSurface(provider,r=>r.SessionId=="session-A",owner);b=new AgentApprovalSurface(provider,r=>r.SessionId=="session-B",owner);
                var input=Ui.Field("继续补充任务要求…",true);input.MinHeight=88;panel.Children.Add(input);owner.Content=panel;owner.Show();
                int effects=0;var policy=new AgentPermissionRuntime();policy.Add(new AgentPermissionRule{Action="shell",Resource="**",Effect=AgentPermissionEffect.Ask});var runtime=new AgentToolRuntime(policy,null,provider);
                runtime.Register(new AgentToolDefinition{Name="exec_command"},(args,context,token)=>{effects++;return Task.FromResult("done");});
                for(int index=0;index<3;index++)using(var cancel=new CancellationTokenSource())
                {
                    var operation=runtime.ExecuteAsync("exec_command",new Dictionary<string,object>{{"command","git --version"},{"working_directory",@"E:\项目\代码目录"}},new AgentToolExecutionContext{SessionId="session-A",ProjectId="project-A",TaskId="task-A",CallId="call-"+index},cancel.Token);
                    Until(()=>a.Presentation!=null);var dialog=a.Presentation;dialog.UpdateLayout();
                    Check(dialog.Owner==owner&&b.Presentation==null&&!All<FrameworkElement>(owner).Any(e=>e.Name=="AgentApprovalCard"),"approval is owned by its actual conversation without duplicate inline or cross-session cards");
                    Check(dialog.WindowStyle==WindowStyle.None&&((Border)dialog.Content).CornerRadius.TopLeft==20&&dialog.WindowStartupLocation==WindowStartupLocation.CenterOwner,"approval uses the shared rounded popup centered over its operation window");
                    var bounds=DesktopInterop.Bounds(new System.Windows.Interop.WindowInteropHelper(dialog).Handle);var ownerBounds=DesktopInterop.Bounds(new System.Windows.Interop.WindowInteropHelper(owner).Handle);Check(Math.Abs(bounds.Left+bounds.Width/2-ownerBounds.Left-ownerBounds.Width/2)<4&&Math.Abs(bounds.Top+bounds.Height/2-ownerBounds.Top-ownerBounds.Height/2)<4,"approval actually opens at the center of its owning operation window");
                    var detail=All<Expander>(dialog).Single(e=>e.Name=="AgentApprovalDetails");detail.IsExpanded=true;dialog.UpdateLayout();
                    Check(All<TextBlock>(dialog).Any(t=>t.Text.Contains("task-A")&&t.Text.Contains("project-A")),"approval details retain bound task project and call");detail.IsExpanded=false;
                    Check(All<TextBox>(dialog).Single(t=>t.Name=="AgentApprovalArguments").Text=="git --version","command preview displays the command rather than JSON");
                    Check(All<Button>(dialog).Single(button=>button.Name=="RejectAgentOperation").IsDefault,"approval defaults keyboard confirmation to deny");
                    if(index==0)
                    {
                        ThemeManager.Apply("light");Capture(dialog,"approval-light.png");ThemeManager.Apply("dark");Capture(dialog,"approval-dark.png");
                        var field=All<TextBox>(dialog).Single(t=>t.Name=="AgentApprovalArguments");var bg=((SolidColorBrush)field.Background).Color;var fg=((SolidColorBrush)field.Foreground).Color;
                        Check(bg.R<90&&fg.R>150,"visible command field has a dark surface and readable text after live theme change");
                        dialog.Width=390;Capture(dialog,"approval-dark-narrow.png");Click(dialog,"AllowAgentOperation");
                    }
                    else if(index==1)Click(dialog,"RejectAgentOperation");else cancel.Cancel();
                    Until(()=>operation.IsCompleted);try{operation.GetAwaiter().GetResult();}catch(UnauthorizedAccessException){}catch(OperationCanceledException){}
                    Check(effects==1,"only explicit allow reaches the fixture effect");Until(()=>a.Presentation==null);
                }
                var ra=new AgentApprovalRequest{SessionId="session-A",CallId="first",ToolName="read_file",Arguments="{}"};var rb=new AgentApprovalRequest{SessionId="session-B",CallId="second",ToolName="read_file",Arguments="{}"};
                var first=provider.ApproveAsync(ra);var second=provider.ApproveAsync(rb);Until(()=>a.Presentation!=null&&b.Presentation!=null);ra.SessionId="session-B";
                Click(b.Presentation,"AllowAgentOperation");Until(()=>second.IsCompleted);Check(second.Result&&!first.IsCompleted,"a decision resolves its original request and cannot approve another conversation");Click(a.Presentation,"RejectAgentOperation");Until(()=>first.IsCompleted);Check(!first.Result,"request identity remains immutable while the caller changes its object");Until(()=>a.Presentation==null&&b.Presentation==null);
                var migrated=provider.ApproveAsync(new AgentApprovalRequest{SessionId="session-A",CallId="migration",Arguments="{}"});Until(()=>a.Presentation!=null);a.Dispose();Check(!migrated.IsCompleted,"disposing a stale conversation view preserves its pending decision");a=new AgentApprovalSurface(provider,r=>r.SessionId=="session-A",owner);Until(()=>a.Presentation!=null);
                using(var duplicate=new AgentApprovalSurface(provider,r=>r.SessionId=="session-A",owner)){Check(duplicate.Presentation==null&&app.Windows.OfType<Window>().Count(w=>w.Name=="AgentApprovalWindow")==1,"two views of one conversation never duplicate the same approval");Click(a.Presentation,"CloseDialog");Until(()=>migrated.IsCompleted);Check(!migrated.Result,"closing migrated approval rejects only its original request");}Until(()=>a.Presentation==null);
                var queuedFirst=provider.ApproveAsync(new AgentApprovalRequest{SessionId="session-A",CallId="queue-first",Arguments="{}"});var queuedSecond=provider.ApproveAsync(new AgentApprovalRequest{SessionId="session-A",CallId="queue-second",Arguments="{}"});Until(()=>a.Presentation!=null);Click(a.Presentation,"CloseDialog");Until(()=>queuedFirst.IsCompleted&&a.Presentation!=null&&Equals(a.Presentation.Tag,"queue-second"));Check(!queuedFirst.Result&&!queuedSecond.IsCompleted,"closing one approval leaves concurrent requests pending and presents the next");Click(a.Presentation,"RejectAgentOperation");Until(()=>queuedSecond.IsCompleted);Check(!queuedSecond.Result,"queued approval still requires its own explicit decision");Until(()=>a.Presentation==null);
                var alternate=new Window{Width=700,Height=500};alternate.Show();alternate.Activate();Until(()=>alternate.IsActive);using(var alternateView=new AgentApprovalSurface(provider,r=>r.SessionId=="session-A",alternate)){var activeRequest=provider.ApproveAsync(new AgentApprovalRequest{SessionId="session-A",CallId="active-host",Arguments="{}"});Until(()=>alternateView.Presentation!=null);Check(a.Presentation==null&&alternateView.Presentation.Owner==alternate,"shared conversation approval chooses its active operation host");Click(alternateView.Presentation,"CloseDialog");Until(()=>activeRequest.IsCompleted);Check(!activeRequest.Result,"closing active-host approval keeps default denial");}alternate.Close();owner.Activate();
                var closing=provider.ApproveAsync(new AgentApprovalRequest{SessionId="session-A",CallId="close",Arguments="{}"});Until(()=>a.Presentation!=null);owner.Close();Until(()=>closing.IsCompleted);Check(closing.IsCanceled,"closing the owner cancels pending approval without executing it");
                Console.WriteLine("Agent approval UI tests: "+count+" passed");return 0;
            }
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
            finally{if(a!=null)a.Dispose();if(b!=null)b.Dispose();foreach(var w in app.Windows.OfType<Window>().ToArray())w.Close();app.Shutdown();}
        }
    }
}
