using Avalonia.Controls;
using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal enum CompanionSelectionOrigin { Settings, PetMenu, StartupRestore, Onboarding }

internal sealed partial class DesktopController
{
    private readonly Dictionary<string,(PetSkin Skin,HolidayCatalog Catalog)> preparedCompanions=new(StringComparer.Ordinal);
    private readonly List<OfficialCompanionSnapshot> companionSnapshots=new();
    private SpriteAssets companionAssets=null!;
    private IDisposable? activeCompanionPins;
    internal event Action<CompanionSelectionUpdate>? CompanionSelectionChanged;
    internal CompanionSelectionUpdate? CurrentCompanionSelection {get;private set;}
    private CompanionSelectionUpdate? awaitingCompanionFrame;
    private bool companionFirstVisibleFrame,companionUpgradeStarted;
    private readonly Dictionary<long,System.Diagnostics.Stopwatch> companionClocks=new();
    internal string CompanionSelectionNotice {get;private set;}="";
    private void SelectionStage(long token,PetSkin skin,CompanionSelectionOrigin origin,string stage,bool cached=false,OfficialCompanionProgress? progress=null)
    {
        var ms=companionClocks.TryGetValue(token,out var clock)?clock.Elapsed.TotalMilliseconds:0;
        CompanionSelectionUpdate.WriteTrace($"Companion request={token} stage={stage} cache={cached} ms={ms:F1}");
        CurrentCompanionSelection=new CompanionSelectionUpdate(token,skin.PickerKey,origin.ToString(),stage,ms,cached,progress!);
        PresentCompanionInstallation(CurrentCompanionSelection);CompanionSelectionChanged?.Invoke(CurrentCompanionSelection);
    }
    internal void CancelCompanionSelection(long token){if(officialCompanions.OwnsSelection(token))companionSelection?.Cancel();}
    internal void CompanionFirstFrameRendered()
    {
        if(!companionFirstVisibleFrame&&Pet.IsPetVisible){companionFirstVisibleFrame=true;Dispatcher.UIThread.Post(()=>{Run(StartOfficialCompanionsAsync);Run(MaintainCompanionCachesAsync);});}
        var pending=awaitingCompanionFrame;if(pending==null||pending.PickerKey!=Skin.PickerKey)return;
        awaitingCompanionFrame=null;
        if(officialCompanions.OwnsSelection(pending.Request))SelectionStage(pending.Request,Skin,Enum.Parse<CompanionSelectionOrigin>(pending.Origin),"completed");
        companionClocks.Remove(pending.Request);RetireCompanionSources();
    }
    private async Task MaintainCompanionCachesAsync()
    {
        try{
            var retry=officialCompanions.MaintainAfterStartupAsync(companionLifetime.Token);
            try{
                if(SpriteAssets.BundledEbao is { } bundle){
                    await Task.Run(()=>{var skin=SpriteAssets.ReadBundledEbao("e-bao");using(companionAssets.Prepare(skin)){}skin.PackageHolidayCatalog!.WithCharacter("e-bao",skin.PackageHolidayCatalog);},companionLifetime.Token);
                    await officialCompanions.ConfirmBundleReadyAsync(bundle,companionLifetime.Token);
                }
            }catch(OperationCanceledException){}catch(Exception){System.Diagnostics.Trace.TraceWarning("Bundled companion cache handoff will retry on a later launch.");}
            await retry;
        }catch(OperationCanceledException){}catch(Exception){System.Diagnostics.Trace.TraceWarning("Automatic cache maintenance will retry on a later launch.");}
    }
    private bool CanSelectCompanion()=>!exiting&&!HolidayBusinessBusy&&settingsWindow?.HasUnsavedChanges!=true;
    private readonly CancellationTokenSource companionLifetime = new();
    private readonly OfficialCompanionNativeValidator companionNative = new();
    private IOfficialCompanionSource officialCompanions = null!;
    private CancellationTokenSource? companionSelection;
    private OfficialCompanionProgressView? companionRefreshProgress;
    private string? companionRequestKey;
    private PetSkin embeddedEbao = null!;
    private readonly Dictionary<string, (PetSkin Skin, HolidayCatalog Catalog, PetSkin Previous, long Token)> pendingAppearances = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string? Version, long Order)> acceptedAppearances = new(StringComparer.Ordinal);
    private long appearanceOperation;
    private bool applyingAppearance;
    internal bool CompanionAppearanceSafe => (!GuideLocked||guide?.AllowsCompanionPreparation==true) && !HolidayBusinessBusy && settingsWindow?.HasUnsavedChanges!=true && Pet.CompanionSourceRefreshSafe;
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
        if(requested.OfficialCharacterId == null)return requested;
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
        var snapshot=officialCompanions.CaptureLocalSnapshot(installation.Asset.Id,companionLifetime.Token)
            ?? throw new InvalidDataException("Installed companion has no usable receipt.");
        return ReadSnapshot(snapshot,persona);
    }
    private (PetSkin Skin,HolidayCatalog Catalog) ReadSnapshot(OfficialCompanionSnapshot snapshot,string persona)
    {
        if(snapshot.Installation.Asset.Id=="e-bao"&&SpriteAssets.BundledEbao is { } bundle&&bundle.PrefersBundle(snapshot.Installation.Asset.Version)){
            snapshot.Dispose();var bundledSkin=SpriteAssets.ReadBundledEbao(persona);return (bundledSkin,bundledSkin.PackageHolidayCatalog!);
        }
        lock(preparedCompanions){
            if(preparedCompanions.TryGetValue(snapshot.Installation.Asset.Id,out var existing)&&existing.Skin.Snapshot?.Generation==snapshot.Generation){snapshot.Dispose();return existing;}
        }
        try {
            var skin=SpriteAssets.ReadOfficial(snapshot,persona);var catalog=new HolidayCatalog(snapshot);
            catalog.WithCharacter(snapshot.Installation.Asset.Id,catalog);
            using(var prepared=companionAssets.Prepare(skin)){}
            lock(preparedCompanions){companionLifetime.Token.ThrowIfCancellationRequested();preparedCompanions[skin.OfficialCharacterId!]=(skin,catalog);companionSnapshots.Add(snapshot);}
            _=AuditCompanionAsync(snapshot);return (skin,catalog);
        }catch {snapshot.Dispose();throw;}
    }
    private async Task AuditCompanionAsync(OfficialCompanionSnapshot snapshot)
    {try{await officialCompanions.AuditAsync(snapshot,companionLifetime.Token);}catch(OperationCanceledException){}catch(Exception){/* Per-read verification remains authoritative. */}}
    private async Task<(PetSkin Skin, HolidayCatalog Catalog)?> InstalledCompanionAsync(PetSkin entry, CancellationToken cancel)
    {
        (PetSkin Skin,HolidayCatalog Catalog) cached;
        lock(preparedCompanions){if(preparedCompanions.TryGetValue(entry.OfficialCharacterId!,out cached))return cached;}
        var snapshot=await Task.Run(()=>officialCompanions.CaptureLocalSnapshot(entry.OfficialCharacterId!,cancel),cancel);
        if(snapshot==null)return null;
        // Application-pinned E must never be downgraded by an older installed copy.
        if(entry.OfficialCharacterId=="e-bao"&&SpriteAssets.BundledEbao is { } bundled&&bundled.PrefersBundle(snapshot.Installation.Asset.Version)){snapshot.Dispose();return null;}
        return await Task.Run(()=>ReadSnapshot(snapshot,entry.Id));
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
    public Task SelectSkinAsync(PetSkin skin) => SelectSkinAsync(skin, CompanionSelectionOrigin.Settings);
    internal async Task SelectSkinAsync(PetSkin requested, CompanionSelectionOrigin origin)
    {
        if((origin!=CompanionSelectionOrigin.Onboarding&&ExplainGuideCompanions())||exiting)return;
        if(!CanSelectCompanion()){
            CompanionSelectionNotice=TextResources.Get(settingsWindow?.HasUnsavedChanges==true?"petOfficialCompanions.canRequestCompanion.notice":"petOfficialCompanions.canRequestCompanion.notice.alternativeA");
            SelectionStage(latestCompanionToken,requested,origin,"refused");Notify();return;
        }
        if(!Skins.Any(s=>s.PickerKey==requested.PickerKey))return;
        Observation.Invalidate();long token=officialCompanions.BeginSelection();latestCompanionToken=token;companionSelectionGeneration++;
        companionSelection?.Cancel();awaitingCompanionFrame=null;companionClocks.Clear();companionClocks[token]=System.Diagnostics.Stopwatch.StartNew();
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(companionLifetime.Token);companionSelection=cancellation;companionRequestKey=requested.PickerKey;
        Settings.PendingOfficialCompanionId="";CompanionSelectionNotice="";
        bool Owns()=>!exiting&&!cancellation.IsCancellationRequested&&officialCompanions.OwnsSelection(token);
        SelectionStage(token,requested,origin,"requested");
        IDisposable? preparedPins=null;
        try {
            var next=requested;HolidayCatalog? nextCatalog=null;bool cached=false,repair=false;
            if(requested.OfficialCharacterId!=null){
                try {
                    lock(preparedCompanions)cached=preparedCompanions.TryGetValue(requested.OfficialCharacterId,out var warm)&&companionAssets.IsPrepared(warm.Skin);
                    var local=await InstalledCompanionAsync(requested,cancellation.Token);
                    if(local!=null){next=local.Value.Skin;nextCatalog=local.Value.Catalog;}
                    if(!next.IsPlaceholder)preparedPins=await Task.Run(()=>companionAssets.Prepare(next),cancellation.Token);
                }catch(Exception error) when(error is IOException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException){repair=true;}
                if(!Owns())return;
                if(repair||next.IsPlaceholder){
                    SelectionStage(token,requested,origin,"installing");
                    var progress=new Progress<OfficialCompanionProgress>(p=>{if(Owns())SelectionStage(token,requested,origin,"progress",false,p);});
                    var result=repair?await officialCompanions.RepairAsync(requested.OfficialCharacterId,CompanionRouting(),progress,cancellation.Token):await officialCompanions.EnsureAsync(requested.OfficialCharacterId,CompanionRouting(),OfficialCompanionCheckMode.Cached,progress,cancellation.Token);
                    if(!Owns())return;
                    var published=await Task.Run(()=>ReadPublished(result.Installation,requested.Id),cancellation.Token);next=published.Skin;nextCatalog=published.Catalog;
                }
            }
            if(requested.OfficialCharacterId==null&&requested.PackageId!=null&&requested.Folder!=null&&OfficialCompanionRules.Ids.Contains(requested.PackageId)){
                nextCatalog=await Task.Run(()=>{var root=Path.Combine(requested.Folder,"holidays");if(!Directory.Exists(root))return null;var catalog=new HolidayCatalog(root);if(!string.IsNullOrEmpty(catalog.Warning)||catalog.Outfits.Any(o=>!o.Ready||o.CharacterId!=requested.PackageId))throw new InvalidDataException("Invalid companion wardrobe.");return catalog;},cancellation.Token);
            }
            if(next.Bundle!=null)nextCatalog=next.PackageHolidayCatalog;
            preparedPins??=await Task.Run(()=>companionAssets.Prepare(next),cancellation.Token);
            if(nextCatalog!=null){
                var outfit=Holidays.PreviewOutfit(next.OutfitCharacterId!,next.PickerKey,Pet.HolidayLocalNow(),nextCatalog);
                if(outfit!=null){try{var withOutfit=await Task.Run(()=>companionAssets.Prepare(next,outfit),cancellation.Token);preparedPins.Dispose();preparedPins=withOutfit;}
                    catch(Exception error) when(error is IOException or InvalidDataException or InvalidOperationException or ArgumentException){nextCatalog.ReportUnavailable(outfit);}}
            }
            if(!Owns())return;SelectionStage(token,next,origin,"prepared",cached);
            if(!CanSelectCompanion()||(GuideLocked&&guide?.AllowsCompanionPreparation!=true)){
                CompanionSelectionNotice=TextResources.Get("petOfficialCompanions.canRequestCompanion.notice.alternativeA");SelectionStage(token,next,origin,"reselect-required");Notify();return;
            }
            using var admission=DataMaintenance.EnterActivity();
            if(next.OfficialCharacterId!=null&&!CanPublishOfficial(next))return;
            bool changed=Skin.PickerKey!=next.PickerKey;
            Pet.CancelHolidayForSkinChange();if(next.OfficialCharacterId==null)next.PackageHolidayCatalog=nextCatalog;
            PersonaProfiles.SaveVoices(Settings);Skin=next;PersonaProfiles.Activate(Settings,next.Id,next.Prompt,next.Voices,next.OfficialCharacterId??next.BuiltinCharacterId);Session.SelectPersona(next.Id);
            Settings.SelectedCompanionKey=next.PickerKey;Settings.PendingOfficialCompanionId="";ReplaceCompanionEntry(next);
            if(nextCatalog!=null&&next.OfficialCharacterId!=null)MergeCompanionCatalog(next.OfficialCharacterId,nextCatalog);else RebuildCompanionCatalog();
            var oldPins=activeCompanionPins;activeCompanionPins=preparedPins;preparedPins=null;
            if(changed&&origin!=CompanionSelectionOrigin.StartupRestore)awaitingCompanionFrame=new CompanionSelectionUpdate(token,next.PickerKey,origin.ToString(),"render-pending",0);
            Pet.SelectSkin();oldPins?.Dispose();Save();if(tray!=null)tray.ToolTipText="DeskPet · "+next.Name;
            SelectionStage(token,next,origin,"committed",cached);
            if(next.OfficialCharacterId!=null&&origin!=CompanionSelectionOrigin.StartupRestore)Run(()=>RefreshOfficialAsync(next,token,OfficialCompanionCheckMode.Cached,origin));
        }catch(OperationCanceledException){if(officialCompanions.OwnsSelection(token))SelectionStage(token,requested,origin,"cancelled");}
        catch(Exception error) when(error is IOException or InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException or ApiException){if(Owns())SelectionStage(token,requested,origin,"failed");}
        finally{if(cancellation.IsCancellationRequested&&officialCompanions.OwnsSelection(token)&&CurrentCompanionSelection?.Request==token&&CurrentCompanionSelection.Stage!="cancelled")SelectionStage(token,requested,origin,"cancelled");preparedPins?.Dispose();if(ReferenceEquals(companionSelection,cancellation)){companionSelection=null;companionRequestKey=null;}if(awaitingCompanionFrame?.Request!=token)companionClocks.Remove(token);}
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
    private async Task RefreshOfficialAsync(PetSkin previous, long token, OfficialCompanionCheckMode mode,CompanionSelectionOrigin origin=CompanionSelectionOrigin.StartupRestore,string? requestedId=null)
    {
        string id=requestedId??previous.OfficialCharacterId!;
        long order = ++appearanceOperation;
        try
        {
            var result=await officialCompanions.UpdateAsync(id,CompanionRouting(),companionLifetime.Token,mode,id=="e-bao"?SpriteAssets.BundledEbao?.Version:null);
            if(exiting)return;
            if(result==null){
                if(mode==OfficialCompanionCheckMode.AfterApplicationUpdate&&officialCompanions.OwnsSelection(token)&&Settings.SelectedCompanionKey=="official:"+id){Settings.LastCompanionUpgradeVersion=ReleaseInfo.DisplayVersion;Save();}
                return;
            }
            var next=await Task.Run(()=>ReadPublished(result.Installation,Skins.First(s=>s.OfficialCharacterId==id).Id),companionLifetime.Token);
            if(!officialCompanions.OwnsSelection(token)||!ReferenceEquals(Skin,previous))return;
            if(mode==OfficialCompanionCheckMode.AfterApplicationUpdate&&!result.UsedOfflineFallback&&Settings.SelectedCompanionKey==next.Skin.PickerKey){Settings.LastCompanionUpgradeVersion=ReleaseInfo.DisplayVersion;Save();}
            if(previous.PickerKey!=next.Skin.PickerKey){
                ReplaceCompanionEntry(next.Skin);
                if(mode==OfficialCompanionCheckMode.AfterApplicationUpdate&&Settings.SelectedCompanionKey==next.Skin.PickerKey&&CanSelectCompanion())await SelectSkinAsync(next.Skin,CompanionSelectionOrigin.StartupRestore);
                return;
            }
            if (next.Skin.SourceKey == previous.SourceKey) return;
            QueueAppearance(next.Skin, next.Catalog, previous, token, order);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Background refresh preserves the usable source. */ }
    }
    private readonly Dictionary<string,bool> companionRepairs=new(StringComparer.Ordinal);
    private readonly Dictionary<string,DateTime> companionRepairAfter=new(StringComparer.Ordinal);
    private void DefaultCompanionFailed(PetSkin source)
    {
        if(exiting||source.Snapshot==null||!ReferenceEquals(Skin,source))return;
        string generation=source.Snapshot.Generation;
        // Successful preparation remains authoritative until the pending source
        // is applied, invalidated, or loses selection ownership.
        if(pendingAppearances.TryGetValue(source.PickerKey,out var pending)&&ReferenceEquals(pending.Previous,source)
            &&officialCompanions.OwnsSelection(pending.Token)&&pending.Skin.Snapshot!=null&&pending.Skin.Snapshot.Generation!=generation)return;
        if(companionRepairs.ContainsKey(generation)||companionRepairAfter.TryGetValue(generation,out var retry)&&DateTime.UtcNow<retry)return;
        companionRepairs[generation]=true;
        long token=latestCompanionToken,order=++appearanceOperation;
        Run(()=>RepairDefaultCompanionAsync(source,token,order));
    }
    private async Task RepairDefaultCompanionAsync(PetSkin source,long token,long order)
    {
        string generation=source.Snapshot!.Generation;
        try{
            var result=await officialCompanions.RepairAsync(source.OfficialCharacterId!,CompanionRouting(),null,companionLifetime.Token,true);
            if(exiting||result.UsedOfflineFallback)return;
            var next=await Task.Run(()=>ReadPublished(result.Installation,source.Id),companionLifetime.Token);
            if(officialCompanions.OwnsSelection(token)&&ReferenceEquals(Skin,source))QueueAppearance(next.Skin,next.Catalog,source,token,order);
        }catch(OperationCanceledException){}
        catch(Exception){/* Silent repair keeps the already prepared working frame. */}
        finally{companionRepairs.Remove(generation);companionRepairAfter[generation]=DateTime.UtcNow.AddSeconds(30);}
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
        RetireCompanionSources();
        if(applyingAppearance||exiting||!CompanionAppearanceSafe||pendingAppearances.Count==0)return;
        applyingAppearance=true;Run(ApplyPendingCompanionAppearanceAsync);
    }
    private async Task ApplyPendingCompanionAppearanceAsync()
    {
        try {
            foreach(var item in pendingAppearances.ToArray()){
                var next=item.Value;IDisposable? hold=null;
                try{
                    var outfit=Holidays.PreviewOutfit(next.Skin.OutfitCharacterId!,next.Skin.PickerKey,Pet.HolidayLocalNow(),next.Catalog);
                    try{hold=await Task.Run(()=>companionAssets.Prepare(next.Skin,outfit),companionLifetime.Token);}
                    catch(Exception error) when(outfit!=null&&(error is IOException or InvalidDataException or ArgumentException or InvalidOperationException)){next.Catalog.ReportUnavailable(outfit);hold=await Task.Run(()=>companionAssets.Prepare(next.Skin),companionLifetime.Token);}
                    if(!CompanionAppearanceSafe)return;
                    if(!pendingAppearances.TryGetValue(item.Key,out var current)||!ReferenceEquals(current.Skin,next.Skin))continue;
                    pendingAppearances.Remove(item.Key);
                    if(!officialCompanions.OwnsSelection(next.Token)||!ReferenceEquals(Skin,next.Previous))continue;
                    Pet.CancelHolidayForSkinChange();Skin=next.Skin;ReplaceCompanionEntry(next.Skin);
                    var old=activeCompanionPins;activeCompanionPins=hold;hold=null;
                    MergeCompanionCatalog(next.Skin.OfficialCharacterId!,next.Catalog);Pet.SelectSkin();old?.Dispose();Notify();
                }catch{
                    // A replacement that failed preparation must no longer block
                    // a later repair attempt for the still-visible source.
                    if(pendingAppearances.TryGetValue(item.Key,out var failed)&&ReferenceEquals(failed.Skin,next.Skin))pendingAppearances.Remove(item.Key);
                    throw;
                }finally{hold?.Dispose();}
            }
        }catch(OperationCanceledException){}catch(Exception){/* A failed refresh keeps the current source. */}
        finally{applyingAppearance=false;RetireCompanionSources();}
    }
    private void RetireCompanionSources()
    {
        if(companionSelection!=null||applyingAppearance||exiting)return;
        lock(preparedCompanions)foreach(var snapshot in companionSnapshots.ToArray()){
            if(ReferenceEquals(Skin.Snapshot,snapshot)||preparedCompanions.Values.Any(x=>ReferenceEquals(x.Skin.Snapshot,snapshot))||pendingAppearances.Values.Any(x=>ReferenceEquals(x.Skin.Snapshot,snapshot))||companionAssets.HasPendingSource(snapshot.Generation))continue;
            companionAssets.RetireSource(snapshot.Generation);companionSnapshots.Remove(snapshot);snapshot.Dispose();
        }
    }
    private long companionSelectionGeneration;
    private OfficialCompanionPreparation? preparation;
    internal Func<IProgress<OfficialCompanionProgress>,CancellationToken,Task>? PreparationDownloadOverride;
    internal OfficialCompanionPreparation OfficialPreparation=>preparation??=TrackPreparationNotifications(new OfficialCompanionPreparation((progress,token)=>PreparationDownloadOverride?.Invoke(progress,token)??PrepareAllOfficialAsync(progress,token),companionLifetime.Token));
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
            var captured=Skin;
            var catalog=await Task.Run(()=>{
                string root=Path.Combine(captured.Folder,"holidays");if(!Directory.Exists(root))return null;
                var loaded=new HolidayCatalog(root);
                if(!string.IsNullOrEmpty(loaded.Warning)||loaded.Outfits.Any(o=>!o.Ready||o.CharacterId!=captured.PackageId))throw new InvalidDataException(TextResources.Get("officialCompanionNativeValidator.validateRoot.text"));
                return loaded;
            },companionLifetime.Token);
            if(!exiting&&ReferenceEquals(Skin,captured)){captured.PackageHolidayCatalog=catalog;RebuildCompanionCatalog();Notify();}
            return;
        }
        if(Settings.PendingOfficialCompanionId.Length!=0){Settings.PendingOfficialCompanionId="";Save();}
        var desired=ResolveSavedCompanion()??Skin;
        if(!companionFirstVisibleFrame||!Pet.IsPetVisible||companionUpgradeStarted||Settings.LastCompanionUpgradeVersion==ReleaseInfo.DisplayVersion||desired.OfficialCharacterId==null)return;
        companionUpgradeStarted=true;
        await RefreshOfficialAsync(Skin,latestCompanionToken,OfficialCompanionCheckMode.AfterApplicationUpdate,requestedId:desired.OfficialCharacterId);
    }
}
