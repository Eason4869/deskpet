using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class DesktopWorkApproval(DesktopController owner):ICancellableAgentApprovalProvider
{
    private const double PreferredHeight = 570;
    private const double MinimumHeight = 180;

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
        owner.PlayNotificationCue(NotificationCue.Review,"approval:"+request.SessionId+":"+request.TaskId+":"+request.CallId+":"+request.Id);
        var dialog = LocalizedUi.Window(() => TextResources.Get("agent.work.approvalTitle"), new Border(), 620, PreferredHeight);
        dialog.Name = "WorkApprovalDialog";
        var buttons = Ui.Row(LocalizedUi.Button(() => TextResources.Get("agent.work.deny"), () => dialog.Close(false)),
            LocalizedUi.Button(() => TextResources.Get("agent.work.allow"), () => dialog.Close(true)));
        buttons.Margin = new Thickness(0, 12, 0, 0);
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        DialogUi.ApplyTitled(dialog, root, resizable: true);

        if (PlaceAboveComposer(dialog, host)) dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        bool placementPending = false;
        void UpdatePlacement(object? sender, EventArgs args)
        {
            if (placementPending) return;
            placementPending = true;
            // Let the owner finish arranging before updating the dialog's size and position.
            Dispatcher.UIThread.Post(() =>
            {
                placementPending = false;
                if (dialog.IsVisible) PlaceAboveComposer(dialog, host);
            }, DispatcherPriority.Loaded);
        }

        host.ComposerSurface.LayoutUpdated += UpdatePlacement;
        host.PositionChanged += UpdatePlacement;
        host.ScalingChanged += UpdatePlacement;
        dialog.Opened += UpdatePlacement;
        dialog.ScalingChanged += UpdatePlacement;
        try
        {
            using var cancellation = token.Register(() => Dispatcher.UIThread.Post(() => dialog.Close(false)));
            bool granted = await dialog.ShowDialog<bool>(host);
            return granted && !token.IsCancellationRequested && host.IsVisible && host.QuestionSessionId == request.SessionId;
        }
        finally
        {
            host.ComposerSurface.LayoutUpdated -= UpdatePlacement;
            host.PositionChanged -= UpdatePlacement;
            host.ScalingChanged -= UpdatePlacement;
            dialog.Opened -= UpdatePlacement;
            dialog.ScalingChanged -= UpdatePlacement;
        }
    }

    private static bool PlaceAboveComposer(Window dialog, ChatWindow host)
    {
        var composer = host.ComposerSurface;
        if (!composer.IsEffectivelyVisible || composer.Bounds.Width <= 0 || TopLevel.GetTopLevel(composer) != host)
            return false;

        var anchor = composer.PointToScreen(default);
        var rightEdge = composer.PointToScreen(new Point(composer.Bounds.Width, 0));
        var workArea = (host.Screens.ScreenFromPoint(anchor) ?? host.Screens.ScreenFromWindow(host))?.WorkingArea;
        double scale = dialog.IsVisible ? dialog.RenderScaling : host.RenderScaling;
        int gap = (int)Math.Round(8 * host.RenderScaling);
        int bottom = anchor.Y - gap;
        double maxHeight = PreferredHeight;
        double height = PreferredHeight;
        if (workArea is { } area)
        {
            maxHeight = Math.Clamp(area.Height / scale, 1, PreferredHeight);
            bottom = Math.Min(bottom, area.Bottom);
            height = Math.Clamp((bottom - area.Y) / scale, Math.Min(MinimumHeight, maxHeight), maxHeight);
        }

        dialog.CanResize = false;
        dialog.MinWidth = 0;
        dialog.MaxWidth = double.PositiveInfinity;
        dialog.Width = (rightEdge.X - anchor.X) / scale;
        dialog.MinHeight = Math.Min(MinimumHeight, maxHeight);
        dialog.MaxHeight = maxHeight;
        dialog.Height = height;
        int top = Math.Max(workArea?.Y ?? int.MinValue, bottom - (int)Math.Round(height * scale));
        var position = new PixelPoint(anchor.X, top);
        if (dialog.Position != position) dialog.Position = position;
        return true;
    }
}
