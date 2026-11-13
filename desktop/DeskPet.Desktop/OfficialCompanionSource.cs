using Dafeiyu;

namespace DeskPet.Desktop;

internal interface IOfficialCompanionSource : IDisposable
{
    long BeginSelection();
    bool OwnsSelection(long token);
    OfficialCompanionInstallation? Installed(string id);
    OfficialCompanionSnapshot? CaptureLocalSnapshot(string id, CancellationToken cancel) => null;
    Task<bool> AuditAsync(OfficialCompanionSnapshot snapshot, CancellationToken cancel) => Task.FromResult(true);
    Task<OfficialCompanionResult> RepairAsync(string id, OfficialDownloadRouting routing, IProgress<OfficialCompanionProgress>? progress, CancellationToken cancel) => EnsureAsync(id,routing,OfficialCompanionCheckMode.Cached,progress,cancel);
    Task<OfficialCompanionResult> UpdateAsync(string id, OfficialDownloadRouting routing, CancellationToken cancel,OfficialCompanionCheckMode mode=OfficialCompanionCheckMode.Cached) => EnsureAsync(id,routing,mode,null,cancel);
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
        downloads = new OfficialCompanionDownloads(data, version.Canonical, version.IsPreview ? UpdateChannel.Preview : UpdateChannel.Stable, transport, validate);
    }
    public long BeginSelection() => downloads.BeginSelection();
    public bool OwnsSelection(long token) => downloads.OwnsSelection(token);
    public OfficialCompanionInstallation? Installed(string id) => downloads.Installed(id);
    public Task<OfficialCompanionResult> EnsureAsync(string id, OfficialDownloadRouting routing, OfficialCompanionCheckMode mode, IProgress<OfficialCompanionProgress>? progress, CancellationToken cancel) => downloads.EnsureAsync(id, routing, mode, progress!, cancel);
    public Task<IList<OfficialCompanionResult>> EnsureAllAsync(OfficialDownloadRouting routing, IProgress<OfficialCompanionProgress>? progress, CancellationToken cancel) => downloads.EnsureAllAsync(routing, progress!, cancel);
    public OfficialCompanionSnapshot? CaptureLocalSnapshot(string id,CancellationToken cancel) => downloads.CaptureLocalSnapshot(id,cancel);
    public Task<bool> AuditAsync(OfficialCompanionSnapshot snapshot,CancellationToken cancel) => downloads.ValidateSnapshotInBackgroundAsync(snapshot,cancel);
    public Task<OfficialCompanionResult> RepairAsync(string id,OfficialDownloadRouting routing,IProgress<OfficialCompanionProgress>? progress,CancellationToken cancel) => downloads.EnsureAsync(id,routing,OfficialCompanionCheckMode.Cached,progress!,cancel,true,false);
    public Task<OfficialCompanionResult> UpdateAsync(string id,OfficialDownloadRouting routing,CancellationToken cancel,OfficialCompanionCheckMode mode=OfficialCompanionCheckMode.Cached) => downloads.EnsureAsync(id,routing,mode,null!,cancel,false,true);
    public void Dispose() { downloads.Dispose(); transport.Dispose(); }
}
