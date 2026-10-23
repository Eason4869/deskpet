using Avalonia.Controls;
using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal enum CompanionSelectionOrigin { Settings, PetMenu, StartupRestore }

internal sealed partial class DesktopController
{
    private readonly CancellationTokenSource companionLifetime = new();
    private readonly OfficialCompanionNativeValidator companionNative = new();
    private IOfficialCompanionSource officialCompanions = null!;
    private CancellationTokenSource? companionSelection;
    private string? companionRequestKey;
    private PetSkin embeddedEbao = null!;
    private readonly Dictionary<string, HolidayCatalog> companionCatalogs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (PetSkin Skin, HolidayCatalog Catalog, PetSkin Previous, long Token)> pendingAppearances = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string? Version, long Order)> acceptedAppearances = new(StringComparer.Ordinal);
    private long appearanceOperation;
    private bool applyingAppearance;
    internal bool CompanionAppearanceSafe => !GuideLocked && !HolidayBusinessBusy && Pet.CompanionSourceRefreshSafe;
    private OfficialDownloadRouting CompanionRouting() => new(Settings.UpdateUseMirror, Settings.UpdateMirrorAddress, Settings.UpdateUseProxy, Settings.UpdateProxyAddress);
    internal bool ExplainGuideCompanions()
    {
        if (!GuideLocked) return false;
        var notice = Ui.Window("选择桌面伙伴", Ui.Text("完成使用引导后可选择伙伴并下载完整角色包。引导期间只演示设置，不切换伙伴。"), 400, 180);
        notice.Name = "GuideCompanionNotice"; notice.ShowActivated = false; notice.ShowInTaskbar = false; notice.Show();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (_, _) => { timer.Stop(); notice.Close(); }; notice.Closed += (_, _) => timer.Stop(); timer.Start();
        return true;
    }
    private void InitializeCompanions(Func<Func<OfficialCompanionInstallation, CancellationToken, Task>, IOfficialCompanionSource>? factory)
    {
        officialCompanions = factory?.Invoke(companionNative.ValidateAsync) ?? new OfficialCompanionSource(DataDirectory, companionNative.ValidateAsync);
    }
    private PetSkin? ResolveSavedCompanion()
    {
        if (Settings.SelectedCompanionKey.Length != 0) return Skins.FirstOrDefault(s => s.PickerKey == Settings.SelectedCompanionKey);
        // Old untrusted custom data keeps its identity when a manifest ID is ambiguous.
        return Skins.FirstOrDefault(s => s.OfficialCharacterId == null && s.Id == Settings.SkinId)
            ?? Skins.FirstOrDefault(s => s.Id == Settings.SkinId || s.OfficialCharacterId == Settings.SkinId);
    }
    private (PetSkin Skin, HolidayCatalog Catalog) ReadPublished(OfficialCompanionInstallation installation, string persona)
    {
        var skin = SpriteAssets.ReadOfficial(installation, persona);
        lock (companionCatalogs)
        {
            if (!companionCatalogs.TryGetValue(installation.RootDirectory, out var catalog) || catalog.Outfits.Any(o => !o.Ready))
                companionCatalogs[installation.RootDirectory] = catalog = new HolidayCatalog(installation.HolidayRootDirectory);
            while (companionCatalogs.Count > 10) companionCatalogs.Remove(companionCatalogs.Keys.First());
            skin.SourceKey += ":" + catalog.Outfits.FirstOrDefault()?.SourceKey;
            return (skin, catalog);
        }
    }
    private async Task<(PetSkin Skin, HolidayCatalog Catalog)?> InstalledCompanionAsync(PetSkin entry, CancellationToken cancel)
    {
        var installation = await Task.Run(() => officialCompanions.Installed(entry.OfficialCharacterId!), cancel);
        if (installation == null) return null;
        await companionNative.ValidateAsync(installation, cancel);
        return await Task.Run(() => ReadPublished(installation, entry.Id), cancel);
    }
    private void MergeCompanionCatalog(string id, HolidayCatalog catalog) => Holidays.ReplaceCatalog(Holidays.Catalog.WithCharacter(id, catalog));
    private async Task WaitForGuideAsync(long token, CancellationToken cancel)
    {
        while (GuideLocked && officialCompanions.OwnsSelection(token)) await Task.Delay(100, cancel);
    }
    private async Task WaitForAutomaticRestoreAsync(long token, CancellationToken cancel)
    {
        while (!CompanionAppearanceSafe && officialCompanions.OwnsSelection(token)) await Task.Delay(100, cancel);
    }

    public Task SelectSkinAsync(PetSkin skin) => SelectSkinAsync(skin, CompanionSelectionOrigin.Settings);
    internal async Task SelectSkinAsync(PetSkin requested, CompanionSelectionOrigin origin)
    {
        if (ExplainGuideCompanions() || exiting) return;
        long token = officialCompanions.BeginSelection();
        latestCompanionToken = token; companionSelectionGeneration++;
        companionSelection?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(companionLifetime.Token);
        companionSelection = cancellation;
        companionRequestKey = requested.PickerKey;
        if (origin != CompanionSelectionOrigin.StartupRestore) { Settings.PendingOfficialCompanionId = ""; Save(); }
        bool Owns() => !exiting && !cancellation.IsCancellationRequested && officialCompanions.OwnsSelection(token);
        try
        {
            var next = requested;
            HolidayCatalog? nextCatalog = null;
            if (requested.OfficialCharacterId != null)
            {
                var installed = await InstalledCompanionAsync(requested, cancellation.Token);
                if (!Owns()) return;
                if (installed != null) { next = installed.Value.Skin; nextCatalog = installed.Value.Catalog; }
                else if (requested.IsPlaceholder)
                {
                    using var progress = new OfficialCompanionProgressView(origin != CompanionSelectionOrigin.Settings, settingsWindow ?? (Window)Pet, Settings.Topmost, cancellation);
                    while (true)
                    {
                        try
                        {
                            var result = await officialCompanions.EnsureAsync(requested.OfficialCharacterId, CompanionRouting(), origin == CompanionSelectionOrigin.StartupRestore ? OfficialCompanionCheckMode.AfterApplicationUpdate : OfficialCompanionCheckMode.Cached, progress, cancellation.Token);
                            if (!Owns()) return;
                            var published = await Task.Run(() => ReadPublished(result.Installation, requested.Id), cancellation.Token);
                            next = published.Skin; nextCatalog = published.Catalog; break;
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception error) { if (!Owns() || !await progress.RetryAsync(error)) return; }
                    }
                }
            }
            if (!Owns()) return;
            await WaitForGuideAsync(token, cancellation.Token); if (!Owns()) return;
            if (origin == CompanionSelectionOrigin.StartupRestore)
            {
                // Automatic arrival cannot interrupt work started on fallback E.
                await WaitForAutomaticRestoreAsync(token, cancellation.Token); if (!Owns()) return;
            }
            else
            {
                // Guide can open or a newer real selection can arrive during a stop.
                await Queue.PauseAsync(); await WaitForGuideAsync(token, cancellation.Token); if (!Owns()) return;
                await Dictation.CancelAsync(); await WaitForGuideAsync(token, cancellation.Token); if (!Owns()) return;
                await Live.StopAsync(); await WaitForGuideAsync(token, cancellation.Token); if (!Owns()) return;
                await Session.StopAsync(); await WaitForGuideAsync(token, cancellation.Token); if (!Owns()) return;
            }
            if (next.OfficialCharacterId != null && !CanPublishOfficial(next)) return;
            Pet.CancelHolidayForSkinChange();
            PersonaProfiles.SaveVoices(Settings); Skin = next;
            PersonaProfiles.Activate(Settings, next.Id, next.Prompt, next.Voices); Session.SelectPersona(next.Id);
            Settings.SelectedCompanionKey = next.PickerKey; Settings.PendingOfficialCompanionId = "";
            ReplaceCompanionEntry(next); if (nextCatalog != null) MergeCompanionCatalog(next.OfficialCharacterId!, nextCatalog);
            Pet.SelectSkin(); Save(); if (tray != null) tray.ToolTipText = "DeskPet · " + next.Name;
            if (next.OfficialCharacterId != null && !next.BuiltIn)
                Run(() => RefreshOfficialAsync(next, token, origin == CompanionSelectionOrigin.StartupRestore ? OfficialCompanionCheckMode.AfterApplicationUpdate : OfficialCompanionCheckMode.Cached));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested || !officialCompanions.OwnsSelection(token)) { }
        finally { if (ReferenceEquals(companionSelection, cancellation)) { companionSelection = null; companionRequestKey = null; } cancellation.Dispose(); }
    }
    internal void CompanionHidden(PetSkin hidden)
    {
        if (companionRequestKey == hidden.PickerKey || hidden.OfficialCharacterId != null && Settings.PendingOfficialCompanionId == hidden.OfficialCharacterId)
        {
            latestCompanionToken = officialCompanions.BeginSelection(); companionSelectionGeneration++;
            companionSelection?.Cancel(); Settings.PendingOfficialCompanionId = ""; Save();
        }
    }
    private void ReplaceCompanionEntry(PetSkin next)
    {
        var index = Skins.FindIndex(s => s.PickerKey == next.PickerKey);
        if (index < 0) Skins.Add(next); else Skins[index] = next;
    }
    private bool CanPublishOfficial(PetSkin next) => Skins.Any(s => s.PickerKey == next.PickerKey)
        && (next.OfficialCharacterId == "e-bao" || !CompanionPackStorage.Hidden(DataDirectory).Contains(next.PickerKey));
    private async Task RefreshOfficialAsync(PetSkin previous, long token, OfficialCompanionCheckMode mode)
    {
        long order = ++appearanceOperation;
        try
        {
            var result = await officialCompanions.EnsureAsync(previous.OfficialCharacterId!, CompanionRouting(), mode, null, companionLifetime.Token);
            if (exiting || !officialCompanions.OwnsSelection(token) || !ReferenceEquals(Skin, previous)) return;
            var next = await Task.Run(() => ReadPublished(result.Installation, previous.Id), companionLifetime.Token);
            if (next.Skin.SourceKey == previous.SourceKey) return;
            QueueAppearance(next.Skin, next.Catalog, previous, token, order);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Background refresh preserves the usable source. */ }
    }
    private bool AcceptAppearance(PetSkin next, long order)
    {
        if (acceptedAppearances.TryGetValue(next.PickerKey, out var accepted))
        {
            int comparison = ReleaseVersion.TryParse(next.PackVersion, out var incoming) && ReleaseVersion.TryParse(accepted.Version, out var prior) ? incoming.CompareTo(prior) : 0;
            if (comparison < 0 || comparison == 0 && order < accepted.Order) return false;
        }
        acceptedAppearances[next.PickerKey] = (next.PackVersion, order); return true;
    }
    private void QueueAppearance(PetSkin next, HolidayCatalog catalog, PetSkin previous, long token, long order)
    {
        if (exiting || !officialCompanions.OwnsSelection(token) || !CanPublishOfficial(next)) return;
        if (!ReferenceEquals(Skin, previous))
        {
            // A same-role bulk refresh can replace the active object without
            // advancing the selection token. Its newer source must stay paired
            // with its picker entry and holiday catalog.
            if (Skin.OfficialCharacterId == next.OfficialCharacterId) return;
            if (!AcceptAppearance(next, order)) return;
            ReplaceCompanionEntry(next); MergeCompanionCatalog(next.OfficialCharacterId!, catalog); Notify(); return;
        }
        if (!AcceptAppearance(next, order)) return;
        pendingAppearances[next.PickerKey] = (next, catalog, previous, token); ApplyPendingCompanionAppearance();
    }
    internal void ApplyPendingCompanionAppearance()
    {
        if (applyingAppearance || exiting || !CompanionAppearanceSafe) return;
        applyingAppearance = true;
        try
        {
            foreach (var item in pendingAppearances.ToArray())
            {
                var next = item.Value; pendingAppearances.Remove(item.Key);
                if (!officialCompanions.OwnsSelection(next.Token) || !ReferenceEquals(Skin, next.Previous)) continue;
                // Same persona/source refresh: no Activate, SelectPersona or history switch.
                Pet.CancelHolidayForSkinChange(); Skin = next.Skin; ReplaceCompanionEntry(next.Skin);
                MergeCompanionCatalog(next.Skin.OfficialCharacterId!, next.Catalog); Pet.SelectSkin(); Notify();
            }
        }
        finally { applyingAppearance = false; }
    }
    internal async Task DownloadAllOfficialAsync()
    {
        if (ExplainGuideCompanions() || exiting) return;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(companionLifetime.Token);
        using var progress = new OfficialCompanionProgressView(false, settingsWindow ?? (Window)Pet, Settings.Topmost, cancel);
        var captured = Skin;
        // Bulk does not advance selection. Captured lease ownership is observed
        // via object identity plus the token snapshot updated by real choices.
        long generation = companionSelectionGeneration;
        long order = ++appearanceOperation;
        while (true)
        {
            try
            {
                var results = await officialCompanions.EnsureAllAsync(CompanionRouting(), progress, cancel.Token);
                foreach (var result in results) await PublishBulkResultAsync(result.Installation, captured, generation, order, cancel.Token);
                return;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                // Aggregate failure still leaves completed installations usable.
                foreach (var entry in Skins.Where(s => s.OfficialCharacterId != null).ToArray())
                {
                    var installation = await Task.Run(() => officialCompanions.Installed(entry.OfficialCharacterId!), cancel.Token);
                    if (installation != null) { await companionNative.ValidateAsync(installation, cancel.Token); await PublishBulkResultAsync(installation, captured, generation, order, cancel.Token); }
                }
                if (!await progress.RetryAsync(error)) return;
            }
        }
    }
    private long companionSelectionGeneration;
    private async Task PublishBulkResultAsync(OfficialCompanionInstallation installation, PetSkin captured, long generation, long order, CancellationToken cancel)
    {
        var entry = Skins.FirstOrDefault(s => s.OfficialCharacterId == installation.Asset.Id);
        if (entry == null) return;
        var next = await Task.Run(() => ReadPublished(installation, entry.Id), cancel);
        if (exiting || !CanPublishOfficial(next.Skin)) return;
        if (Skin.OfficialCharacterId != installation.Asset.Id) { if (AcceptAppearance(next.Skin, order)) { ReplaceCompanionEntry(next.Skin); MergeCompanionCatalog(installation.Asset.Id, next.Catalog); Notify(); } return; }
        if (generation != companionSelectionGeneration || !ReferenceEquals(Skin, captured) || next.Skin.SourceKey == Skin.SourceKey) return;
        // Use the latest real-selection token without advancing it for bulk.
        QueueAppearance(next.Skin, next.Catalog, captured, latestCompanionToken, order);
    }
    private long latestCompanionToken;
    private async Task StartOfficialCompanionsAsync()
    {
        if (GuideLocked || companionSelection != null || showAutomaticGuide && Settings.OnboardingState is not ("completed" or "skipped")) return;
        var pending = Settings.PendingOfficialCompanionId;
        if (pending.Length != 0)
        {
            var entry = Skins.FirstOrDefault(s => s.OfficialCharacterId == pending);
            if (entry != null) await SelectSkinAsync(entry, CompanionSelectionOrigin.StartupRestore);
        }
        else if (Skin.OfficialCharacterId != null)
        {
            var current = Skin; var token = officialCompanions.BeginSelection(); latestCompanionToken = token;
            long order = ++appearanceOperation;
            try
            {
                var installed = await InstalledCompanionAsync(current, companionLifetime.Token);
                if (exiting || !officialCompanions.OwnsSelection(token) || !ReferenceEquals(Skin, current)) return;
                if (installed != null && installed.Value.Skin.SourceKey != current.SourceKey)
                    QueueAppearance(installed.Value.Skin, installed.Value.Catalog, current, token, order);
                var active = Skin;
                if (installed != null || !active.BuiltIn)
                    await RefreshOfficialAsync(active, token, OfficialCompanionCheckMode.AfterApplicationUpdate);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { /* Embedded/legacy source remains usable offline. */ }
        }
    }
}
