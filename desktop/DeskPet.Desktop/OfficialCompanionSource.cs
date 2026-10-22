using Dafeiyu;

namespace DeskPet.Desktop;

internal interface IOfficialCompanionSource : IDisposable
{
    long BeginSelection();
    bool OwnsSelection(long token);
    OfficialCompanionInstallation? Installed(string id);
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
    public void Dispose() { downloads.Dispose(); transport.Dispose(); }
}
