using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
namespace Dafeiyu
{
    // Each window belongs to one immutable request, never to a global current chat.
    internal sealed class AgentApprovalController:ICancellableAgentApprovalProvider
    {
        readonly Window owner;
        internal AgentApprovalController(Window owner){this.owner=owner;}
        public Task<bool> ApproveAsync(AgentApprovalRequest request){return ApproveAsync(request,CancellationToken.None);}
        public async Task<bool> ApproveAsync(AgentApprovalRequest request,CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();var result=new TaskCompletionSource<bool>();Window window=null;
            Action show=delegate
            {
                if(cancel.IsCancellationRequested || !owner.IsLoaded){result.TrySetCanceled();return;}
                window=new Window{Name="AgentApprovalWindow",Owner=owner,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.CenterScreen};Ui.Window(window,"确认 Agent 操作",640,560);
                var panel=new StackPanel{Margin=new Thickness(24)};panel.Children.Add(Ui.Text("是否允许本次操作？",20));
                var operation=Ui.Text(request.ToolName+"\n"+request.Resource,14);operation.TextWrapping=TextWrapping.Wrap;operation.Margin=new Thickness(0,16,0,12);panel.Children.Add(operation);
                var scope=Ui.Text("项目："+request.ProjectId+"\n任务："+request.TaskId+"\n调用："+request.CallId,11,Ui.Muted);scope.TextWrapping=TextWrapping.Wrap;panel.Children.Add(scope);
                if(request.Action=="shell"){var isolation=Ui.Text("将在当前账户下执行。工作目录不提供文件或网络隔离；请核对命令及其可能访问的资源。",12);isolation.TextWrapping=TextWrapping.Wrap;isolation.Margin=new Thickness(0,12,0,8);panel.Children.Add(isolation);}
                var detail=new TextBox{Name="AgentApprovalArguments",Text=request.Arguments??"",IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,MaxHeight=220,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(0,10,0,12)};panel.Children.Add(detail);
                var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
                var reject=Ui.Button("拒绝");reject.Name="RejectAgentOperation";reject.Click+=delegate{result.TrySetResult(false);window.Close();};buttons.Children.Add(reject);
                var allow=Ui.Button("仅本次允许",true);allow.Name="AllowAgentOperation";allow.Margin=new Thickness(10,0,0,0);allow.Click+=delegate{if(!cancel.IsCancellationRequested)result.TrySetResult(true);else result.TrySetCanceled();window.Close();};buttons.Children.Add(allow);panel.Children.Add(buttons);
                window.Content=Ui.Scroll(panel);window.Closed+=delegate{result.TrySetResult(false);};window.Show();
            };
            var pendingShow=owner.Dispatcher.BeginInvoke(show);
            using(cancel.Register(delegate{result.TrySetCanceled();owner.Dispatcher.BeginInvoke(new Action(delegate{if(window!=null)window.Close();}));}))
                return await result.Task.ConfigureAwait(false);
        }
    }
}
