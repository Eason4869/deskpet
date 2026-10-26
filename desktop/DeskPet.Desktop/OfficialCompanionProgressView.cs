using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class OfficialCompanionProgressView : IDisposable, IProgress<OfficialCompanionProgress>
{
    private readonly Window window;
    private readonly TextBlock label = LocalizedUi.Text(()=>(TextResources.Get("officialCompanionProgressView.label.text")));
    private readonly ProgressBar bar = new() { Minimum = 0, Maximum = 100, IsIndeterminate = true, Height = 8 };
    private readonly Button retry;
    private readonly CancellationTokenSource cancel;
    private readonly bool passive, lazy;
    private readonly Window? parent;
    private readonly Func<bool>? isCurrent;
    private bool shown;
    private TaskCompletionSource<bool>? retryAnswer;
    private bool disposed, closing;
    internal OfficialCompanionProgressView(bool passive, Window? parent, bool topmost, CancellationTokenSource cancel, bool lazy=false, Func<bool>? isCurrent=null)
    {
        this.passive = passive; this.cancel = cancel;this.lazy=lazy;this.parent=parent;this.isCurrent=isCurrent;
        retry = LocalizedUi.Button(()=>(TextResources.Get("common.retry")), () => retryAnswer?.TrySetResult(true)); retry.IsVisible = false;
        window = LocalizedUi.Window(()=>(passive ? TextResources.Get("officialCompanionProgressView.officialCompanionProgressView.window") : TextResources.Get("officialCompanionProgressView.officialCompanionProgressView.window.alternativeA")), passive ? label : Ui.Column(label, bar, Ui.Row(retry, LocalizedUi.Button(()=>(TextResources.Get("common.cancel")), () => { cancel.Cancel(); retryAnswer?.TrySetResult(false); window?.Close(); }))), passive ? 350 : 460, passive ? 140 : 240);
        window.Name = passive ? "OfficialCompanionDownloadBubble" : "OfficialCompanionProgressDialog";
        if (passive) { window.MinHeight = 0; window.Height = 140; window.CanResize = false; }
        window.ShowInTaskbar = false; window.Topmost = topmost;
        window.ShowActivated = !passive; window.Focusable = !passive;
        window.Closing += (_, _) => { if (!closing) { cancel.Cancel(); retryAnswer?.TrySetResult(false); } };
        if(passive)LocalizedUi.Set(label,"Text",()=>(TextResources.Get("companions.package.updating")));
        if(!lazy){if (parent?.IsVisible == true && !passive) window.Show(parent); else window.Show();shown=true;}
    }
    public void Report(OfficialCompanionProgress progress) => Dispatcher.UIThread.Post(() =>
    {
        if(disposed)return;
        if(isCurrent?.Invoke()==false){Dispose();return;}
        if(lazy&&!shown){if(!progress.RequiresUpdateNotice)return;Show();}
        if(passive)return;
        var name = OfficialCompanionProfiles.Find(progress.CharacterId)?.DisplayName ?? TextResources.Get("officialCompanionProgressView.report.nameOfficialCompanionProfilesFind");
        var stage = progress.Stage switch { "metadata" => TextResources.Get("officialCompanionProgressView.report.progressStageSwitch"), "download" => TextResources.Get("officialCompanionProgressView.report.progressStageSwitch.alternativeA"), "validate" => TextResources.Get("officialCompanionProgressView.report.progressStageSwitch.alternativeB"), "install" => TextResources.Get("officialCompanionProgressView.report.progressStageSwitch.alternativeC"), _ => TextResources.Get("officialCompanionProgressView.report.progressStageSwitch.alternativeD") };
        var route = progress.RouteKind==OfficialCompanionRoute.Mirror ? TextResources.Get("officialCompanionProgressView.report.progressRouteContains") : progress.RouteKind==OfficialCompanionRoute.Proxy ? TextResources.Get("officialCompanionProgressView.report.progressRouteContains.alternativeA") : progress.RouteKind==OfficialCompanionRoute.Direct ? TextResources.Get("officialCompanionProgressView.report.progressRouteContains.alternativeB") : "";
        label.Text = name + " · " + stage + route + (progress.TotalBytes > 0 ? $"\n{progress.CompletedBytes / 1048576d:0.0} / {progress.TotalBytes / 1048576d:0.0} MB" : "");
        bar.IsIndeterminate = progress.TotalBytes <= 0; bar.Value = progress.Percent;
    });
    private void Show(){if(parent?.IsVisible==true&&!passive)window.Show(parent);else window.Show();shown=true;}
    internal async Task<bool> RetryAsync(Exception error)
    {
        if (passive || disposed) return false;
        if(!shown)Show();
        var failed = error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : new[] { error }.AsEnumerable();
        var names = OfficialCompanionProfiles.All.Where(p => failed.Any(e => e.Message.StartsWith(p.Id + "：", StringComparison.Ordinal) || e.Message.StartsWith(p.Id + ":", StringComparison.Ordinal))).Select(p => p.DisplayName).ToArray();
        LocalizedUi.Set(label,"Text",()=>((names.Length > 0 ? TextResources.Format("officialCompanionProgressView.retryAsync.label",string.Join("、", names)) : TextResources.Get("officialCompanionProgressView.retryAsync.label.alternativeA")) + TextResources.Get("officialCompanionProgressView.retryAsync.label.alternativeB")));
        bar.IsIndeterminate = false; retry.IsVisible = true;
        retryAnswer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancel.Token.Register(() => retryAnswer.TrySetResult(false));
        bool again = await retryAnswer.Task;
        retry.IsVisible = false; retryAnswer = null; return again;
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; closing = true; retryAnswer?.TrySetResult(false); window.Close();
    }
}
