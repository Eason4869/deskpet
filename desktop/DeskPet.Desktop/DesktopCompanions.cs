using Avalonia.Controls;
using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal enum CompanionSelectionOrigin { Settings, PetMenu, StartupRestore, Onboarding }

internal sealed partial class DesktopController
{
    private readonly CancellationTokenSource companionLifetime = new();
    private readonly OfficialCompanionNativeValidator companionNative = new();
    private IOfficialCompanionSource officialCompanions = null!;
    private CancellationTokenSource? companionSelection;
    private OfficialCompanionProgressView? companionRefreshProgress;
    private string? companionRequestKey;
    private PetSkin embeddedEbao = null!;
    private readonly Dictionary<string, HolidayCatalog> companionCatalogs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (PetSkin Skin, HolidayCatalog Catalog, PetSkin Previous, long Token)> pendingAppearances = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string? Version, long Order)> acceptedAppearances = new(StringComparer.Ordinal);
    private long appearanceOperation;
    private bool applyingAppearance;
    internal bool CompanionAppearanceSafe => (!GuideLocked||guide?.AllowsCompanionPreparation==true) && !HolidayBusinessBusy && Pet.CompanionSourceRefreshSafe;
    private OfficialDownloadRouting CompanionRouting() => new(Settings.UpdateUseMirror, Settings.UpdateMirrorAddress, Settings.UpdateUseProxy, Settings.UpdateProxyAddress);
    internal bool ExplainGuideCompanions()
    {
        if (!GuideLocked) return false;
        var notice = LocalizedUi.Window(()=>(TextResources.Get("desktopCompanions.explainGuideCompanions.window")), LocalizedUi.Text(()=>(TextResources.Get("desktopCompanions.explainGuideCompanions.text"))), 400, 180);
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
    private PetSkin RestoreCachedStartupCompanion(PetSkin requested)
    {
        if(requested.OfficialCharacterId==null && requested.PackageId!=null && requested.Folder!=null && OfficialCompanionRules.Ids.Contains(requested.PackageId)) {
            try {
                var inventory=Task.Run(()=>OfficialCompanionNativeValidator.ValidateLocalPackage(requested.Folder,companionLifetime.Token)).GetAwaiter().GetResult();
                requested.PackageHolidayCatalog=inventory.HolidayCatalog;
                RebuildCompanionCatalogFor(requested);
            }catch(Exception error) when(error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or AggregateException){requested.PackageHolidayCatalog=null;}
            return requested;
        }
        if(requested.OfficialCharacterId == null||!requested.IsPlaceholder)return requested;
        try
        {
            var installed=Task.Run(()=>InstalledCompanionAsync(requested,companionLifetime.Token)).GetAwaiter().GetResult();
            if(installed == null)return requested;
            var restored=installed.Value.Skin;
            int index=Skins.FindIndex(s=>s.PickerKey==requested.PickerKey);
            if(index>=0)Skins[index]=restored;
            MergeCompanionCatalog(restored.OfficialCharacterId!,installed.Value.Catalog);
            if(Settings.PendingOfficialCompanionId==restored.OfficialCharacterId)Settings.PendingOfficialCompanionId="";
            return restored;
        }
        catch(Exception error) when(error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException or ApiException or AggregateException)
        {return requested;}
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
    private HolidayCatalog? embeddedHolidayCatalog;
    private readonly Dictionary<string,HolidayCatalog> officialHolidayCatalogs=new(StringComparer.Ordinal);
    private void MergeCompanionCatalog(string id, HolidayCatalog catalog)
    {
        officialHolidayCatalogs[id]=catalog;RebuildCompanionCatalog();
    }
    private void RebuildCompanionCatalog()=>RebuildCompanionCatalogFor(Skin);
    private void RebuildCompanionCatalogFor(PetSkin? selected)
    {
        embeddedHolidayCatalog??=Holidays.Catalog;
        var merged=embeddedHolidayCatalog;
        foreach(var item in officialHolidayCatalogs)merged=merged.WithCharacter(item.Key,item.Value);
        if(selected?.PackageHolidayCatalog is { } local)merged=merged.WithCharacter(selected.OutfitCharacterId!,local);
        Holidays.ReplaceCatalog(merged);
    }
    private async Task WaitForGuideAsync(long token, CancellationToken cancel)
    {
        while (GuideLocked && guide?.AllowsCompanionPreparation!=true && officialCompanions.OwnsSelection(token)) await Task.Delay(100, cancel);
    }
    private async Task WaitForAutomaticRestoreAsync(long token, CancellationToken cancel)
    {
        while (!CompanionAppearanceSafe && officialCompanions.OwnsSelection(token)) await Task.Delay(100, cancel);
    }

    public Task SelectSkinAsync(PetSkin skin) => SelectSkinAsync(skin, CompanionSelectionOrigin.Settings);
    internal async Task SelectSkinAsync(PetSkin requested, CompanionSelectionOrigin origin)
    {
        if ((origin!=CompanionSelectionOrigin.Onboarding&&ExplainGuideCompanions()) || exiting) return;
        long token = officialCompanions.BeginSelection();
        latestCompanionToken = token; companionSelectionGeneration++;
        companionSelection?.Cancel(); companionRefreshProgress?.Dispose(); companionRefreshProgress=null;
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
                    using var progress = new OfficialCompanionProgressView(origin!=CompanionSelectionOrigin.Settings, settingsWindow ?? (Window)Pet, Settings.Topmost, cancellation,lazy:true);
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
            if (requested.OfficialCharacterId == null && requested.PackageId != null && requested.Folder != null && OfficialCompanionRules.Ids.Contains(requested.PackageId))
            {
                nextCatalog = await Task.Run(() => {
                    var root = Path.Combine(requested.Folder, "holidays"); if (!Directory.Exists(root)) return null;
                    var result = new HolidayCatalog(root);
                    if (!string.IsNullOrEmpty(result.Warning) || result.Outfits.Any(o => !o.Ready || o.CharacterId != requested.PackageId)) throw new InvalidDataException(TextResources.Get("officialCompanionNativeValidator.validateRoot.text"));
                    return result;
                }, cancellation.Token);
                next.PackageHolidayCatalog = nextCatalog;
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
            using var admission=DataMaintenance.EnterActivity();
            if (next.OfficialCharacterId != null && !CanPublishOfficial(next)) return;
            Pet.CancelHolidayForSkinChange();
            PersonaProfiles.SaveVoices(Settings); Skin = next;
            PersonaProfiles.Activate(Settings, next.Id, next.Prompt, next.Voices, next.OfficialCharacterId ?? next.BuiltinCharacterId); Session.SelectPersona(next.Id);
            Settings.SelectedCompanionKey = next.PickerKey; Settings.PendingOfficialCompanionId = "";
            ReplaceCompanionEntry(next); if (nextCatalog != null && next.OfficialCharacterId != null) MergeCompanionCatalog(next.OfficialCharacterId, nextCatalog); else RebuildCompanionCatalog();
            Pet.SelectSkin(); Save(); if (tray != null) tray.ToolTipText = "DeskPet · " + next.Name;
            if (next.OfficialCharacterId != null && !next.BuiltIn)
                Run(() => RefreshOfficialAsync(next, token, origin == CompanionSelectionOrigin.StartupRestore ? OfficialCompanionCheckMode.AfterApplicationUpdate : OfficialCompanionCheckMode.Cached,origin));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested || !officialCompanions.OwnsSelection(token)) { }
        finally { if (ReferenceEquals(companionSelection, cancellation)) { companionSelection = null; companionRequestKey = null; } cancellation.Dispose(); }
    }
    internal void CompanionHidden(PetSkin hidden)
    {
        if (companionRequestKey == hidden.PickerKey || hidden.OfficialCharacterId != null && Settings.PendingOfficialCompanionId == hidden.OfficialCharacterId)
        {
            latestCompanionToken = officialCompanions.BeginSelection(); companionSelectionGeneration++;
            companionSelection?.Cancel(); companionRefreshProgress?.Dispose(); companionRefreshProgress=null; Settings.PendingOfficialCompanionId = ""; Save();
        }
    }
    private void ReplaceCompanionEntry(PetSkin next)
    {
        var index = Skins.FindIndex(s => s.PickerKey == next.PickerKey);
        if (index < 0) Skins.Add(next); else Skins[index] = next;
    }
    private bool CanPublishOfficial(PetSkin next) => Skins.Any(s => s.PickerKey == next.PickerKey)
        && (next.OfficialCharacterId == "e-bao" || !CompanionPackStorage.Hidden(DataDirectory).Contains(next.PickerKey));
    private async Task RefreshOfficialAsync(PetSkin previous, long token, OfficialCompanionCheckMode mode,CompanionSelectionOrigin origin=CompanionSelectionOrigin.StartupRestore)
    {
        long order = ++appearanceOperation;
        try
        {
            using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(companionLifetime.Token);
            using var progress=origin==CompanionSelectionOrigin.Settings?new OfficialCompanionProgressView(false,settingsWindow??(Window)Pet,Settings.Topmost,cancellation,lazy:true,isCurrent:()=>!exiting&&officialCompanions.OwnsSelection(token)&&ReferenceEquals(Skin,previous)):null;
            companionRefreshProgress=progress;
            var result = await officialCompanions.EnsureAsync(previous.OfficialCharacterId!, CompanionRouting(), mode, progress, cancellation.Token);
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
    private long companionSelectionGeneration;
    private OfficialCompanionPreparation? preparation;
    internal Func<IProgress<OfficialCompanionProgress>,CancellationToken,Task>? PreparationDownloadOverride;
    internal OfficialCompanionPreparation OfficialPreparation=>preparation??=new OfficialCompanionPreparation((progress,token)=>PreparationDownloadOverride?.Invoke(progress,token)??PrepareAllOfficialAsync(progress,token),companionLifetime.Token);
    internal Task DownloadAllOfficialAsync()
    {
        if(ExplainGuideCompanions()||exiting)return Task.CompletedTask;
        ShowOfficialPreparation();return OfficialPreparation.StartAsync();
    }
    internal void ShowOfficialPreparation()=>new CompanionPreparationWindow(this).Show();
    private async Task PrepareAllOfficialAsync(IProgress<OfficialCompanionProgress> progress,CancellationToken cancel)
    {
        var captured=Skin;long generation=companionSelectionGeneration;long order=++appearanceOperation;Exception? failure=null;
        try{var results=await officialCompanions.EnsureAllAsync(CompanionRouting(),progress,cancel);foreach(var result in results)await PublishBulkResultAsync(result.Installation,captured,generation,order,cancel);}
        catch(Exception error){failure=error;}
        if(failure!=null&&!exiting){
            foreach(var entry in Skins.Where(skin=>skin.OfficialCharacterId!=null).ToArray()){
                var installed=await Task.Run(()=>officialCompanions.Installed(entry.OfficialCharacterId!),companionLifetime.Token);
                if(installed!=null){await companionNative.ValidateAsync(installed,companionLifetime.Token);await PublishBulkResultAsync(installed,captured,generation,order,companionLifetime.Token);}
            }
        }
        if(failure!=null)throw failure;
    }
    private async Task PublishBulkResultAsync(OfficialCompanionInstallation installation, PetSkin captured, long generation, long order, CancellationToken cancel)
    {
        var entry = Skins.FirstOrDefault(s => s.OfficialCharacterId == installation.Asset.Id);
        if (entry == null) return;
        var next = await Task.Run(() => ReadPublished(installation, entry.Id), cancel);
        if (exiting || !CanPublishOfficial(next.Skin)) return;
        if (Skin.OfficialCharacterId != installation.Asset.Id) { if (AcceptAppearance(next.Skin, order)) { ReplaceCompanionEntry(next.Skin); MergeCompanionCatalog(installation.Asset.Id, next.Catalog); Notify(); } return; }
        if (next.Skin.SourceKey == Skin.SourceKey) return;
        // Use the latest real-selection token without advancing it for bulk.
        QueueAppearance(next.Skin, next.Catalog, Skin, latestCompanionToken, order);
    }
    private long latestCompanionToken;
    private async Task StartOfficialCompanionsAsync()
    {
        if (GuideLocked || companionSelection != null || showAutomaticGuide && Settings.OnboardingState is not ("completed" or "skipped")) return;
        if (Skin.OfficialCharacterId == null && Skin.PackageId != null && Skin.Folder != null && OfficialCompanionRules.Ids.Contains(Skin.PackageId))
        {
            await SelectSkinAsync(Skin, CompanionSelectionOrigin.StartupRestore); return;
        }
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
                await RefreshOfficialAsync(active, token, OfficialCompanionCheckMode.AfterApplicationUpdate);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { /* Embedded/legacy source remains usable offline. */ }
        }
    }
}
