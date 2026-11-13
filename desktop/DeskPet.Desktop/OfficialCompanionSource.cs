using Dafeiyu;

namespace DeskPet.Desktop;

internal interface IOfficialCompanionSource : IDisposable
{
    Task MaintainAfterStartupAsync(CancellationToken cancel) => Task.CompletedTask;
    Task ConfirmBundleReadyAsync(OfficialCompanionBundle bundle,CancellationToken cancel) => Task.CompletedTask;
    long BeginSelection();
    bool OwnsSelection(long token);
    OfficialCompanionInstallation? Installed(string id);
    OfficialCompanionSnapshot? CaptureLocalSnapshot(string id, CancellationToken cancel) => null;
    Task<bool> AuditAsync(OfficialCompanionSnapshot snapshot, CancellationToken cancel) => Task.FromResult(true);
    Task<OfficialCompanionResult> RepairAsync(string id, OfficialDownloadRouting routing, IProgress<OfficialCompanionProgress>? progress, CancellationToken cancel,bool background=false) => EnsureAsync(id,routing,OfficialCompanionCheckMode.Cached,progress,cancel);
    async Task<OfficialCompanionResult?> UpdateAsync(string id, OfficialDownloadRouting routing, CancellationToken cancel,OfficialCompanionCheckMode mode=OfficialCompanionCheckMode.Cached,string? availableVersion=null) => await EnsureAsync(id,routing,mode,null,cancel);
    Task<OfficialCompanionResult> EnsureAsync(string id, OfficialDownloadRouting routing, OfficialCompanionCheckMode mode, IProgress<OfficialCompanionProgress>? progress, CancellationToken cancel);
    Task<IList<OfficialCompanionResult>> EnsureAllAsync(OfficialDownloadRouting routing, IProgress<OfficialCompanionProgress>? progress, CancellationToken cancel);
}

internal sealed class OfficialCompanionSource : IOfficialCompanionSource
{
    private readonly GitHubUpdates transport = new();
    private readonly OfficialCompanionDownloads downloads;
    internal OfficialCompanionSource(string data, Func<OfficialCompanionInstallation, CancellationToken, Task> validate)
    {
        var version = ReleaseVersion.Parse(ReleaseInfo.DisplayVersion);
        downloads = new OfficialCompanionDownloads(data, version.Canonical, version.IsPreview ? UpdateChannel.Preview : UpdateChannel.Stable, transport, validate, OfficialCompanionReadMode.Snapshots);
    }
    public Task MaintainAfterStartupAsync(CancellationToken cancel) => downloads.MaintainAfterStartupAsync(cancel);
    public Task ConfirmBundleReadyAsync(OfficialCompanionBundle bundle,CancellationToken cancel) => downloads.ConfirmBundleReadyAsync(bundle,cancel);
    public long BeginSelection() => downloads.BeginSelection();
    public bool OwnsSelection(long token) => downloads.OwnsSelection(token);
    public OfficialCompanionInstallation? Installed(string id) => downloads.Installed(id);
    public Task<OfficialCompanionResult> EnsureAsync(string id, OfficialDownloadRouting routing, OfficialCompanionCheckMode mode, IProgress<OfficialCompanionProgress>? progress, CancellationToken cancel) => downloads.EnsureAsync(id, routing, mode, progress!, cancel);
    public Task<IList<OfficialCompanionResult>> EnsureAllAsync(OfficialDownloadRouting routing, IProgress<OfficialCompanionProgress>? progress, CancellationToken cancel) => downloads.EnsureAllAsync(routing, progress!, cancel);
    public OfficialCompanionSnapshot? CaptureLocalSnapshot(string id,CancellationToken cancel) => downloads.CaptureLocalSnapshot(id,cancel);
    public Task<bool> AuditAsync(OfficialCompanionSnapshot snapshot,CancellationToken cancel) => downloads.ValidateSnapshotInBackgroundAsync(snapshot,cancel);
    public Task<OfficialCompanionResult> RepairAsync(string id,OfficialDownloadRouting routing,IProgress<OfficialCompanionProgress>? progress,CancellationToken cancel,bool background=false) => downloads.EnsureAsync(id,routing,OfficialCompanionCheckMode.Cached,progress!,cancel,true,background);
    public async Task<OfficialCompanionResult?> UpdateAsync(string id,OfficialDownloadRouting routing,CancellationToken cancel,OfficialCompanionCheckMode mode=OfficialCompanionCheckMode.Cached,string? availableVersion=null) => availableVersion==null?await downloads.EnsureAsync(id,routing,mode,null!,cancel,false,true):await downloads.UpdateBeyondVersionAsync(id,availableVersion,routing,mode,cancel);
    public void Dispose() { downloads.Dispose(); transport.Dispose(); }
}
