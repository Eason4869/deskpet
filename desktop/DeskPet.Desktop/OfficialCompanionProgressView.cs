using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class OfficialCompanionProgressView : IDisposable, IProgress<OfficialCompanionProgress>
{
    private readonly Window window;
    private readonly TextBlock label = Ui.Text("正在下载角色包，请稍后…");
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
        retry = Ui.Button("重试", () => retryAnswer?.TrySetResult(true)); retry.IsVisible = false;
        window = Ui.Window(passive ? "角色包" : "下载角色包", passive ? label : Ui.Column(label, bar, Ui.Row(retry, Ui.Button("取消", () => { cancel.Cancel(); retryAnswer?.TrySetResult(false); window?.Close(); }))), passive ? 350 : 460, passive ? 140 : 240);
        window.Name = passive ? "OfficialCompanionDownloadBubble" : "OfficialCompanionProgressDialog";
        if (passive) { window.MinHeight = 0; window.Height = 140; window.CanResize = false; }
        window.ShowInTaskbar = false; window.Topmost = topmost;
        window.ShowActivated = !passive; window.Focusable = !passive;
        window.Closing += (_, _) => { if (!closing) { cancel.Cancel(); retryAnswer?.TrySetResult(false); } };
        if(passive)label.Text="正在更新角色包，请稍后...";
        if(!lazy){if (parent?.IsVisible == true && !passive) window.Show(parent); else window.Show();shown=true;}
    }
    public void Report(OfficialCompanionProgress progress) => Dispatcher.UIThread.Post(() =>
    {
        if(disposed)return;
        if(isCurrent?.Invoke()==false){Dispose();return;}
        if(lazy&&!shown){if(!progress.RequiresUpdateNotice)return;Show();}
        if(passive)return;
        var name = OfficialCompanionProfiles.Find(progress.CharacterId)?.Name ?? "所有伙伴";
        var stage = progress.Stage switch { "metadata" => "检查兼容版本", "download" => "下载角色包", "validate" => "检查角色素材", "install" => "安装角色包", _ => "准备角色包" };
        var route = progress.Route.Contains("镜像", StringComparison.Ordinal) ? " · GitHub 加速" : progress.Route.Contains("代理", StringComparison.Ordinal) ? " · 网络代理" : progress.Route.Contains("直", StringComparison.Ordinal) ? " · 直接下载" : "";
        label.Text = name + " · " + stage + route + (progress.TotalBytes > 0 ? $"\n{progress.CompletedBytes / 1048576d:0.0} / {progress.TotalBytes / 1048576d:0.0} MB" : "");
        bar.IsIndeterminate = progress.TotalBytes <= 0; bar.Value = progress.Percent;
    });
    private void Show(){if(parent?.IsVisible==true&&!passive)window.Show(parent);else window.Show();shown=true;}
    internal async Task<bool> RetryAsync(Exception error)
    {
        if (passive || disposed) return false;
        if(!shown)Show();
        var failed = error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : new[] { error }.AsEnumerable();
        var names = OfficialCompanionProfiles.All.Where(p => failed.Any(e => e.Message.StartsWith(p.Id + "：", StringComparison.Ordinal) || e.Message.StartsWith(p.Id + ":", StringComparison.Ordinal))).Select(p => p.Name).ToArray();
        label.Text = (names.Length > 0 ? string.Join("、", names) + "未完成。" : "角色包下载未完成。") + "\n已安装的素材会保留，请检查网络后重试。";
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
