using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class DesktopWorkApproval(DesktopController owner):ICancellableAgentApprovalProvider
{
    public Task<bool> ApproveAsync(AgentApprovalRequest request)=>ApproveAsync(request,CancellationToken.None);
    public async Task<bool> ApproveAsync(AgentApprovalRequest request,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return await Dispatcher.UIThread.InvokeAsync(()=>ShowAsync(request,token));
    }
    private async Task<bool> ShowAsync(AgentApprovalRequest request,CancellationToken token)
    {
        var host=owner.WorkApprovalHost(request.SessionId);if(host==null||token.IsCancellationRequested)return false;
        var details=TextResources.Format("agent.work.approvalSource",request.ProjectId,request.SessionId,request.TaskId,request.CallId,request.ToolName)+"\n\n"+
            TextResources.Format("agent.work.approvalPayload",request.Arguments,request.Resource,request.IsolationDescription,request.PayloadHash);
        var content=new TextBox{Name="WorkApprovalDetails",Text=details,IsReadOnly=true,AcceptsReturn=true,TextWrapping=Avalonia.Media.TextWrapping.Wrap};
        var dialog=Ui.Window(TextResources.Get("agent.work.approvalTitle"),new Border(),620,570);dialog.Name="WorkApprovalDialog";
        var buttons=Ui.Row(Ui.Button(TextResources.Get("agent.work.deny"),()=>dialog.Close(false)),Ui.Button(TextResources.Get("agent.work.allow"),()=>dialog.Close(true)));
        var root=new DockPanel{Margin=new Thickness(20)};DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);root.Children.Add(new ScrollViewer{Content=content});dialog.Content=root;
        using var cancellation=token.Register(()=>Dispatcher.UIThread.Post(()=>dialog.Close(false)));
        bool granted=await dialog.ShowDialog<bool>(host);return granted&&!token.IsCancellationRequested&&host.IsVisible&&host.QuestionSessionId==request.SessionId;
    }
}
