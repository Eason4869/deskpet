using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Dafeiyu
{
    internal enum CompanionSelectionOrigin { PassiveMenu, Settings, MainWindow, StartupRestore, Onboarding }
    public sealed partial class PetWindow
    {
        readonly WpfOfficialNativeValidator officialNative=new WpfOfficialNativeValidator();
        readonly CancellationTokenSource officialLifetime=new CancellationTokenSource();
        OfficialCompanionDownloads officialDownloads;
        GitHubUpdates officialTransport;
        readonly Dictionary<string,HolidayCatalog> officialCatalogs=new Dictionary<string,HolidayCatalog>(StringComparer.Ordinal);
        readonly Dictionary<string,SkinDefinition> officialSources=new Dictionary<string,SkinDefinition>(StringComparer.Ordinal);
        readonly List<OfficialCompanionSnapshot> retainedSnapshots=new List<OfficialCompanionSnapshot>();
        HolidayCatalog embeddedHolidayCatalog;
        PendingOfficialSource deferredOfficial;
        bool officialStartupStarted,officialFirstFrameRendered;
        bool checkingDeferredOfficial;
        int officialPreparationReaders;
        long officialRequest;
        CancellationTokenSource officialSelection;
        internal event Action<CompanionSelectionUpdate> CompanionSelectionChanged;
        internal CompanionSelectionUpdate CurrentCompanionSelection {get;private set;}
        CompanionSelectionUpdate renderedSelection;
        readonly Dictionary<long,System.Diagnostics.Stopwatch> selectionClocks=new Dictionary<long,System.Diagnostics.Stopwatch>();
        void SelectionStage(long token,SkinDefinition skin,CompanionSelectionOrigin origin,string stage,bool cached=false,OfficialCompanionProgress progress=null)
        {
            System.Diagnostics.Stopwatch clock;selectionClocks.TryGetValue(token,out clock);
            var update=new CompanionSelectionUpdate(token,skin.PickerKey,origin.ToString(),stage,clock==null?0:clock.Elapsed.TotalMilliseconds,cached,progress);
            CompanionSelectionUpdate.WriteTrace("Companion request="+token+" stage="+stage+" cache="+cached+" ms="+update.Milliseconds.ToString("F1",System.Globalization.CultureInfo.InvariantCulture));
            CurrentCompanionSelection=update;PresentCompanionInstallation(update);ObserveCompanionOperation(update);if(CompanionSelectionChanged!=null)CompanionSelectionChanged(update);
        }
        internal void CancelCompanionSelection(long token){if(OwnsOfficial(token)&&officialSelection!=null)officialSelection.Cancel();}
        void CompanionFirstFrameRendered()
        {
            ObserveCompanionAppearance();
            if(IsPetVisible&&!officialFirstFrameRendered){officialFirstFrameRendered=true;Dispatcher.BeginInvoke(new Action(()=>{var maintenance=MaintainCompanionCachesAsync();}));}
            var update=renderedSelection;if(update==null||Skin.PickerKey!=update.PickerKey)return;renderedSelection=null;
            if(OwnsOfficial(update.Request))SelectionStage(update.Request,Skin,(CompanionSelectionOrigin)Enum.Parse(typeof(CompanionSelectionOrigin),update.Origin),"completed");selectionClocks.Remove(update.Request);
        }

        async Task MaintainCompanionCachesAsync()
        {
            try{
                // Foreground restore/render has completed. Preparing the pinned fallback
                // does not select it or touch identity/business state.
                var downloads=OfficialDownloads;var retry=downloads.MaintainAfterStartupAsync(officialLifetime.Token);
                try{
                    var bundle=OfficialCompanionBundle.Ebao;
                    if(bundle!=null){
                        var fallback=Catalog.EmbeddedFallback();
                        await Task.Run(()=>{PrepareDefault(fallback);fallback.PackageHolidayCatalog.WithCharacter("e-bao",fallback.PackageHolidayCatalog);},officialLifetime.Token);
                        await downloads.ConfirmBundleReadyAsync(bundle,officialLifetime.Token);
                    }
                }catch(OperationCanceledException){}catch(Exception error){if(!OfficialError(error))throw;System.Diagnostics.Trace.TraceWarning("Bundled companion cache handoff will retry on a later launch.");}
                await retry;
            }catch(OperationCanceledException){}catch(Exception error){if(!OfficialError(error))throw;System.Diagnostics.Trace.TraceWarning("Automatic cache maintenance will retry on a later launch.");}
        }
        sealed class PendingOfficialSource
        {internal SkinDefinition Skin;internal long Request;internal bool Select;internal string ExpectedKey;internal SkinDefinition ExpectedSource;internal string Pointer;}
        OfficialCompanionDownloads OfficialDownloads
        {
            get {
                if(officialDownloads==null){officialTransport=new GitHubUpdates();officialDownloads=new OfficialCompanionDownloads(Store.DirectoryPath,ReleaseInfo.DisplayVersion,ReleaseVersion.Parse(ReleaseInfo.DisplayVersion).IsPreview?UpdateChannel.Preview:UpdateChannel.Stable,officialTransport,officialNative.ValidateAsync,OfficialCompanionReadMode.Snapshots);}
                return officialDownloads;
            }
        }
        OfficialDownloadRouting OfficialRouting(){return new OfficialDownloadRouting(Settings.UpdateUseMirror,Settings.UpdateMirrorAddress,Settings.UpdateUseProxy,Settings.UpdateProxyAddress);}
        bool HasOfficialPointer(SkinDefinition skin){return skin.IsOfficial&&File.Exists(Path.Combine(Store.DirectoryPath,"official-companions",skin.BuiltinCharacterId,"current.txt"));}
        bool ShouldRefreshOfficial(SkinDefinition skin){return skin.IsOfficial&&!skin.FixtureEmbedded&&(!skin.Embedded||HasOfficialPointer(skin));}
        // A downloaded role is a placeholder in the initial catalog. Rehydrate its
        // verified local installation before choosing the visible startup role.
        // This path is deliberately offline; refresh remains a separate operation.
        SkinDefinition RestoreCachedStartupCompanion(SkinDefinition requested)
        {
            if(!requested.IsOfficial||!HasOfficialPointer(requested))return requested;
            try {
                var restored=Task.Run(()=>PrepareOfficialAsync(requested.BuiltinCharacterId,officialLifetime.Token)).GetAwaiter().GetResult();
                if(restored==null)return requested;
                ReleaseVersion bundled,installed;if(requested.BuiltinCharacterId=="e-bao"&&requested.Manifest!=null&&ReleaseVersion.TryParse(requested.Manifest.PackVersion,out bundled)&&ReleaseVersion.TryParse(restored.Manifest.PackVersion,out installed)&&installed.CompareTo(bundled)<0)return requested;
                Catalog.ReplaceOfficial(restored);officialSources[restored.BuiltinCharacterId]=restored;

                if(Settings.PendingOfficialCompanionId==restored.BuiltinCharacterId)Settings.PendingOfficialCompanionId="";
                return restored;
            }catch(Exception error){if(!OfficialError(error))throw;return requested;}
        }
        internal bool NoticeOnlyCompanionGuide()
        {
            if(!GuideActive)return false;
            Notice=TextResources.Get("petOfficialCompanions.noticeOnlyCompanionGuide.notice");
            if(preferences!=null)preferences.ShowNotice(Notice);return true;
        }
        internal bool GuideCompanionSelection {get{return GuideActive&&guide.TargetName=="SkinPicker";}}
        bool CanRequestCompanion(bool onboarding=false)
        {
            if(onboarding&&GuideCompanionSelection)guide.AllowCompanionPreparation();else if(NoticeOnlyCompanionGuide())return false;
            if(closing||IsUpdating)return false;
            if(preferences!=null&&preferences.HasUnsavedEdits){Notice=TextResources.Get("petOfficialCompanions.canRequestCompanion.notice");preferences.ShowNotice(Notice);return false;}
            if(CompanionBusinessBusy){Notice=TextResources.Get("petOfficialCompanions.canRequestCompanion.notice.alternativeA");return false;}
            return true;
        }
        long BeginCompanionSelection(SkinDefinition target)
        {
            if(officialSelection!=null)officialSelection.Cancel();
            officialRequest=OfficialDownloads.BeginSelection();deferredOfficial=null;renderedSelection=null;
            bool hadPending=!String.IsNullOrEmpty(Settings.PendingOfficialCompanionId);Settings.PendingOfficialCompanionId="";
            if(hadPending)SaveSettings();return officialRequest;
        }
        // Compatibility for local callers of the original synchronous API.
        // Application pickers use RequestSkinAsync so all disk preparation yields.
        public bool SelectSkin(SkinDefinition skin)
        {
            if(skin==null||!CanRequestCompanion()||!Catalog.Skins.Any(s=>s.PickerKey==skin.PickerKey))return false;
            if(ReferenceEquals(skin,Skin)){BeginCompanionSelection(skin);return true;}
            if(skin.IsPlaceholder||skin.Snapshot!=null)return false;
            long token=BeginCompanionSelection(skin);selectionClocks.Clear();selectionClocks[token]=System.Diagnostics.Stopwatch.StartNew();SelectionStage(token,skin,CompanionSelectionOrigin.PassiveMenu,"requested");
            try{
                using(WpfPreparedCompanion.Hold(skin)){
                    Task.Run(async()=>{await PrepareLocalOutfitsAsync(skin);if(skin.Illustrated)PrepareDefault(skin);else if(skin.PreparedOfficialAtlas==null)skin.PreparedOfficialAtlas=skin.Load();}).GetAwaiter().GetResult();
                    if(!ActivateSkin(skin))return false;WpfPreparedCompanion.Activate(skin);Catalog.PreserveActive(skin);
                }
                SelectionStage(token,skin,CompanionSelectionOrigin.PassiveMenu,"committed");renderedSelection=new CompanionSelectionUpdate(token,skin.PickerKey,CompanionSelectionOrigin.PassiveMenu.ToString(),"render-pending",0);return true;
            }catch(Exception error){if(!OfficialError(error))throw;SelectionStage(token,skin,CompanionSelectionOrigin.PassiveMenu,"failed");return false;}
            finally{if(renderedSelection==null||renderedSelection.Request!=token)selectionClocks.Remove(token);}
        }
        internal async Task<bool> RequestSkinAsync(SkinDefinition skin,CompanionSelectionOrigin origin,Window owner)
        {
            if(skin==null||!CanRequestCompanion(origin==CompanionSelectionOrigin.Onboarding))return false;
            if(!Catalog.Skins.Any(s=>s.PickerKey==skin.PickerKey))return false;
            RecordPetInteraction();long token=BeginCompanionSelection(skin);selectionClocks.Clear();selectionClocks[token]=System.Diagnostics.Stopwatch.StartNew();
            using(var cancel=CancellationTokenSource.CreateLinkedTokenSource(officialLifetime.Token)) {
                IDisposable heldPreparation=null;officialSelection=cancel;SelectionStage(token,skin,origin,"requested");
                try {
                    SkinDefinition ready=skin;bool cached=false,repair=false;
                    if(skin.IsOfficial) {
                        SkinDefinition warm;cached=officialSources.TryGetValue(skin.BuiltinCharacterId,out warm)&&warm.PreparedOfficialAtlas!=null&&WpfPreparedCompanion.HasIdle(warm);
                        try{var local=await PrepareOfficialAsync(skin.BuiltinCharacterId,cancel.Token);if(local!=null)ready=local;}
                        catch(Exception error){if(!OfficialError(error))throw;repair=true;ready=Catalog.FindOfficial(skin.BuiltinCharacterId);}
                        if(!OwnsOfficial(token)||cancel.IsCancellationRequested)return false;
                        if(repair||ready.IsPlaceholder) {
                            SelectionStage(token,skin,origin,"installing");
                            var progress=new Progress<OfficialCompanionProgress>(p=>{if(OwnsOfficial(token)&&!cancel.IsCancellationRequested)SelectionStage(token,skin,origin,"progress",false,p);});
                            var result=await OfficialDownloads.EnsureAsync(skin.BuiltinCharacterId,OfficialRouting(),OfficialCompanionCheckMode.Cached,progress,cancel.Token,repair,false);
                            ready=await PrepareOfficialAsync(result.Installation,true);
                        }
                    } else await PrepareLocalOutfitsAsync(ready);
                    if(!OwnsOfficial(token)||cancel.IsCancellationRequested)return false;
                    heldPreparation=WpfPreparedCompanion.Hold(ready);
                    await Task.Run(()=>{if(ready.Illustrated)PrepareDefault(ready,false);else if(ready.PreparedOfficialAtlas==null)ready.PreparedOfficialAtlas=ready.Load();},cancel.Token);
                    if(!OwnsOfficial(token)||cancel.IsCancellationRequested)return false;
                    HolidayCatalog wardrobe;
                    if((ready.Bundle!=null&&(wardrobe=ready.PackageHolidayCatalog)!=null)||(ready.OfficialInstallation!=null&&officialCatalogs.TryGetValue(ready.Source,out wardrobe))){
                        var outfit=Holidays.PreviewOutfit(ready.OutfitCharacterId,ready.PickerKey,HolidayLocalTime(),wardrobe);
                        if(outfit!=null)try{await Task.Run(()=>{using(var idleBytes=outfit.FindClip("idle","default").OpenResource()){}using(var gazeBytes=outfit.Gaze.OpenResource()){}var visuals=new HolidayVisualAssets(null);visuals.Get(outfit.FindClip("idle","default"));visuals.Get(outfit.Gaze);},cancel.Token);}
                        catch(Exception error){if(!OfficialError(error))throw;wardrobe.ReportUnavailable(outfit);}
                    }
                    if(!OwnsOfficial(token)||cancel.IsCancellationRequested)return false;
                    SelectionStage(token,ready,origin,"prepared",cached);
                    if(!CanRequestCompanion(origin==CompanionSelectionOrigin.Onboarding)){SelectionStage(token,ready,origin,"reselect-required");return false;}
                    bool changed=Skin.PickerKey!=ready.PickerKey;
                    if(!ActivateSkin(ready))return false;
                    WpfPreparedCompanion.Activate(ready);Catalog.PreserveActive(ready);MergeOfficialHoliday(ready);RefreshHoliday();UpdateFrame();
                    SelectionStage(token,ready,origin,"committed",cached);
                    if(changed&&origin!=CompanionSelectionOrigin.StartupRestore)renderedSelection=new CompanionSelectionUpdate(token,ready.PickerKey,origin.ToString(),"render-pending",0);
                    if(ready.IsOfficial&&origin!=CompanionSelectionOrigin.StartupRestore){var background=RefreshOfficialAsync(ready.BuiltinCharacterId,token,false,origin,null,OfficialCompanionCheckMode.Cached);}
                    return true;
                }catch(OperationCanceledException){if(OwnsOfficial(token))SelectionStage(token,skin,origin,"cancelled");return false;}
                catch(Exception error){if(!OfficialError(error))throw;if(OwnsOfficial(token))SelectionStage(token,skin,origin,"failed");return false;}
                finally{if(cancel.IsCancellationRequested&&OwnsOfficial(token)&&CurrentCompanionSelection!=null&&CurrentCompanionSelection.Request==token&&CurrentCompanionSelection.Stage!="cancelled")SelectionStage(token,skin,origin,"cancelled");if(heldPreparation!=null)heldPreparation.Dispose();if(ReferenceEquals(officialSelection,cancel))officialSelection=null;if(renderedSelection==null||renderedSelection.Request!=token)selectionClocks.Remove(token);}
            }
        }
        bool OwnsOfficial(long token){return !closing&&officialDownloads!=null&&officialDownloads.OwnsSelection(token);}
        async Task<bool> RefreshOfficialAsync(string id,long token,bool selecting,CompanionSelectionOrigin origin,Window owner,OfficialCompanionCheckMode mode)
        {
            if(selecting)return await RequestSkinAsync(Catalog.FindOfficial(id),origin,owner);
            var previous=Skin;
            try {
                var bundle=id=="e-bao"?OfficialCompanionBundle.Ebao:null;
                var result=bundle!=null?await OfficialDownloads.UpdateBeyondVersionAsync(id,bundle.Version,OfficialRouting(),mode,officialLifetime.Token):await OfficialDownloads.EnsureAsync(id,OfficialRouting(),mode,null,officialLifetime.Token,false,true);
                if(closing)return false;
                if(result==null){
                    if(mode==OfficialCompanionCheckMode.AfterApplicationUpdate&&Settings.SelectedCompanionKey=="official:"+id){Settings.LastCompanionUpgradeVersion=ReleaseInfo.DisplayVersion;SaveSettings();}
                    return true;
                }
                var ready=await PrepareOfficialAsync(result.Installation,result.InstalledNow);
                if(OwnsOfficial(token)&&ReferenceEquals(Skin,previous)&&Skin.BuiltinCharacterId==id){QueueOfficial(ready,token,false);TryApplyDeferredOfficial();}
                else {
                    PublishInactiveOfficial(ready);
                    if(mode==OfficialCompanionCheckMode.AfterApplicationUpdate&&OwnsOfficial(token)&&ReferenceEquals(Skin,previous)&&Settings.SelectedCompanionKey==ready.PickerKey&&!HolidayBusinessBusy&&(preferences==null||!preferences.HasUnsavedEdits))await RequestSkinAsync(ready,CompanionSelectionOrigin.StartupRestore,null);
                }
                if(mode==OfficialCompanionCheckMode.AfterApplicationUpdate&&!result.UsedOfflineFallback&&Settings.SelectedCompanionKey==ready.PickerKey){Settings.LastCompanionUpgradeVersion=ReleaseInfo.DisplayVersion;SaveSettings();}
                return !result.UsedOfflineFallback;
            }catch(OperationCanceledException){return false;}catch(Exception error){if(!OfficialError(error))throw;CompanionSelectionUpdate.WriteTrace("Companion background failed: "+error.GetType().Name);return false;}
        }
        static bool OfficialError(Exception error){return error is IOException||error is InvalidDataException||error is UnauthorizedAccessException||error is InvalidOperationException||error is ArgumentException||error is NotSupportedException||error is ApiException||error is AggregateException||error is System.Runtime.Serialization.SerializationException;}
        async Task<SkinDefinition> PrepareOfficialAsync(string id,CancellationToken cancel)
        {
            Interlocked.Increment(ref officialPreparationReaders);try{
            SkinDefinition existing;
            if(officialSources.TryGetValue(id,out existing)&&existing.Snapshot!=null) {
                if(id=="e-bao"&&OfficialCompanionBundle.Ebao!=null&&OfficialCompanionBundle.Ebao.PrefersBundle(existing.Manifest.PackVersion))return Catalog.EmbeddedFallback();
                // A retained generation never waits behind background full validation.
                await Task.Run(()=>PrepareDefault(existing),cancel);return existing;
            }
            var snapshot=await Task.Run(()=>OfficialDownloads.CaptureLocalSnapshot(id,cancel),cancel);
            if(snapshot==null)return null;
            return await PrepareSnapshotAsync(snapshot,cancel);
        }finally{Interlocked.Decrement(ref officialPreparationReaders);}
        }
        async Task<SkinDefinition> PrepareOfficialAsync(OfficialCompanionInstallation installation,bool installedNow)
        {
            Interlocked.Increment(ref officialPreparationReaders);try{
            var snapshot=await Task.Run(()=>OfficialDownloads.CaptureLocalSnapshot(installation.Asset.Id,officialLifetime.Token),officialLifetime.Token);
            if(snapshot==null)throw new InvalidDataException("Installed source has no usable receipt.");
            SkinDefinition existing;
            if(!installedNow&&officialSources.TryGetValue(installation.Asset.Id,out existing)&&existing.Snapshot!=null&&existing.Snapshot.Generation==snapshot.Generation){snapshot.Dispose();await Task.Run(()=>PrepareDefault(existing),officialLifetime.Token);return existing;}
            return await PrepareSnapshotAsync(snapshot,officialLifetime.Token);
        }finally{Interlocked.Decrement(ref officialPreparationReaders);}
        }
        static void PrepareDefault(SkinDefinition skin,bool verifyCache=true)
        {
            // Always reverify bytes used by a warm source before trusting cached pixels.
            if(verifyCache&&skin.Snapshot!=null){if(skin.PreparedOfficialAtlas!=null)skin.Snapshot.ReadVerifiedBytes(skin.Manifest.Gaze.File);if(WpfPreparedCompanion.HasIdle(skin))skin.Snapshot.ReadVerifiedBytes(skin.Manifest.Animations["idle"].File);}
            if(!skin.Illustrated)return;
            if(skin.PreparedOfficialAtlas==null)skin.PreparedOfficialAtlas=skin.Load();
            WpfPreparedCompanion.Load(skin,skin.Manifest.Animations["idle"]);
        }
        async Task<SkinDefinition> PrepareSnapshotAsync(OfficialCompanionSnapshot snapshot,CancellationToken cancel)
        {
            try {
                // Apply the immutable application floor before caching any installed generation.
                if(snapshot.Installation.Asset.Id=="e-bao"&&OfficialCompanionBundle.Ebao!=null&&OfficialCompanionBundle.Ebao.PrefersBundle(snapshot.Installation.Asset.Version)){snapshot.Dispose();return Catalog.EmbeddedFallback();}
                string persona=Catalog.FindOfficial(snapshot.Installation.Asset.Id).Id;
                var prepared=await Task.Run(()=>{
                    SkinManifest manifest;using(var input=new MemoryStream(snapshot.ReadVerifiedBytes("pet.json",cancel),false))manifest=SkinCatalog.ReadManifest(input);
                    var source=new SkinDefinition(persona,snapshot.Installation.RootDirectory,manifest){BuiltinCharacterId=snapshot.Installation.Asset.Id,OfficialInstallation=snapshot.Installation,Snapshot=snapshot};
                    PrepareDefault(source);var wardrobe=new HolidayCatalog(snapshot);wardrobe.WithCharacter(snapshot.Installation.Asset.Id,wardrobe);return Tuple.Create(source,wardrobe);
                },cancel);
                retainedSnapshots.Add(snapshot);officialSources[snapshot.Installation.Asset.Id]=prepared.Item1;officialCatalogs[snapshot.Installation.RootDirectory]=prepared.Item2;
                var audit=AuditSnapshotAsync(snapshot);return prepared.Item1;
            }catch{snapshot.Dispose();throw;}
        }
        async Task AuditSnapshotAsync(OfficialCompanionSnapshot snapshot)
        {try{await OfficialDownloads.ValidateSnapshotInBackgroundAsync(snapshot,officialLifetime.Token);}catch(OperationCanceledException){}catch(Exception error){if(!OfficialError(error))throw;}}
        void PublishInactiveOfficial(SkinDefinition skin)
        {if(Skin.PickerKey==skin.PickerKey)return;Catalog.ReplaceOfficial(skin);MergeOfficialHoliday(skin);if(preferences!=null)preferences.RefreshSkinChoices();}
        void QueueOfficial(SkinDefinition skin,long token,bool select)
        {if(!OwnsOfficial(token)){PublishInactiveOfficial(skin);return;}deferredOfficial=new PendingOfficialSource{Skin=skin,Request=token,Select=select,ExpectedKey=Skin.PickerKey,ExpectedSource=Skin,Pointer=skin.OfficialInstallation==null?CurrentOfficialPointer(skin.BuiltinCharacterId):Path.GetFileName(skin.Source)};}
        string CurrentOfficialPointer(string id)
        {
            try {
                string path=CompanionPackStorage.AssetPath(Store.DirectoryPath,"official-companions/"+id+"/current.txt");
                if(!File.Exists(path))return "";
                using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
                    if(input.Length>160)return "!unavailable";
                    var bytes=new byte[161];int count=0,n;
                    while(count<bytes.Length&&(n=input.Read(bytes,count,bytes.Length-count))>0)count+=n;
                    return count>160?"!unavailable":new System.Text.UTF8Encoding(false,true).GetString(bytes,0,count).Trim();
                }
            }catch(Exception error){if(!OfficialError(error))throw;return "!unavailable";}
        }
        bool OfficialSafe {get{return !closing&&!IsUpdating&&(!GuideActive||guide.AllowsCompanionPreparation)&&!HolidayBusinessBusy&&!interaction.Active&&!dragging&&!dropMotion.Active&&!walking&&!holidayPlayback.Active&&!holidayTransition.Active&&Animation!=null&&Animation.State=="idle"&&!clickTimer.IsEnabled&&(preferences==null||!preferences.HasUnsavedEdits);}}
        void TryApplyDeferredOfficial()
        {
            var pending=deferredOfficial;if(pending==null||!OfficialSafe)return;
            if(pending.Select&&!Catalog.Skins.Any(s=>s.PickerKey==pending.Skin.PickerKey)){OfficialCompanionHidden(pending.Skin);return;}
            if(!OwnsOfficial(pending.Request)||(!pending.Select&&(Skin.PickerKey!=pending.ExpectedKey||!ReferenceEquals(Skin,pending.ExpectedSource)))){deferredOfficial=null;PublishInactiveOfficial(pending.Skin);return;}
            if(CurrentOfficialPointer(pending.Skin.BuiltinCharacterId)!=pending.Pointer){if(!checkingDeferredOfficial){var ignored=RefreshDeferredReceiptAsync(pending);}return;}
            deferredOfficial=null;
            if(pending.Select&&Skin.PickerKey!=pending.Skin.PickerKey) {
                if(!ActivateSkin(pending.Skin))return;
            }else if(!ReferenceEquals(Skin,pending.Skin))RefreshOfficialAppearance(pending.Skin);
            Catalog.ReplaceOfficial(Skin);MergeOfficialHoliday(Skin);
            if(Settings.PendingOfficialCompanionId==Skin.BuiltinCharacterId){Settings.PendingOfficialCompanionId="";SaveSettings();}
            if(preferences!=null)preferences.RefreshSkinChoices();
        }
        async Task RefreshDeferredReceiptAsync(PendingOfficialSource pending)
        {
            checkingDeferredOfficial=true;
            try {
                var latest=await Task.Run(()=>OfficialDownloads.Installed(pending.Skin.BuiltinCharacterId),officialLifetime.Token);
                if(!ReferenceEquals(deferredOfficial,pending)||!OwnsOfficial(pending.Request))return;
                if(latest==null) {
                    if(pending.Skin.Embedded){pending.Pointer=CurrentOfficialPointer(pending.Skin.BuiltinCharacterId);}
                    else{deferredOfficial=null;Notice=TextResources.Get("petOfficialCompanions.refreshDeferredReceiptAsync.notice");return;}
                }else {
                    var ready=await PrepareOfficialAsync(latest,false);
                    if(!ReferenceEquals(deferredOfficial,pending)||!OwnsOfficial(pending.Request))return;
                    pending.Skin=ready;pending.Pointer=ready.Bundle!=null?CurrentOfficialPointer(ready.BuiltinCharacterId):Path.GetFileName(ready.Source);
                }
            }catch(OperationCanceledException){}catch(Exception error){if(!OfficialError(error))throw;if(ReferenceEquals(deferredOfficial,pending))deferredOfficial=null;}
            finally{checkingDeferredOfficial=false;}
            TryApplyDeferredOfficial();
        }
        void RefreshOfficialAppearance(SkinDefinition skin)
        {
            var replacement=skin.Load();var interactions=InteractionAtlas.LoadForSkin(skin);
            CancelInteraction();atlas=replacement;interactionAtlas=interactions;Skin=skin;interaction.Configure(skin);
            WpfPreparedCompanion.Activate(skin);motion=null;motionSkin=null;shownMotion=null;shownSample=-1;shownRow=shownCol=-1;shownOffset=new Point();shownMirrored=false;gazeActive=false;
            Animation=skin.CreateAnimation(atlas.FrameMultiplier);MergeOfficialHoliday(skin);RefreshHoliday();UpdateFrame();RefreshChatAppearance();
        }
        async Task PrepareLocalOutfitsAsync(SkinDefinition skin)
        {
            if(skin.IsOfficial||skin.PackageHolidaysChecked)return;
            if(skin.Illustrated&&skin.Manifest!=null&&OfficialCompanionRules.Ids.Contains(skin.Manifest.PackageId)) {
                var catalog=await Task.Run(()=>{
                    string root=Path.Combine(skin.Source,"holidays");if(!Directory.Exists(root))return null;
                    var result=new HolidayCatalog(root);if(!String.IsNullOrEmpty(result.Warning)||result.Outfits.Any(o=>!o.Ready||o.CharacterId!=skin.Manifest.PackageId))throw new InvalidDataException(TextResources.Get("officialCompanionNativeValidator.validateRoot.text"));return result;
                });
                skin.PackageHolidayCatalog=catalog;
            }
            skin.PackageHolidaysChecked=true;
        }
        async Task RestoreLocalOutfitsAsync()
        {
            var captured=Skin;
            try {await PrepareLocalOutfitsAsync(captured);if(!closing&&Object.ReferenceEquals(Skin,captured)){MergeOfficialHoliday(captured);RefreshHoliday();UpdateFrame();}}
            catch(Exception error){if(!OfficialError(error))throw;Notice=error.Message;}
        }
        void MergeOfficialHoliday(SkinDefinition skin)
        {
            if(Holidays==null)return;
            if(embeddedHolidayCatalog==null)embeddedHolidayCatalog=Holidays.Catalog;
            var merged=embeddedHolidayCatalog;
            foreach(var pair in officialSources) {
                // Keep active old source until its deferred native source commit.
                var active=Skin.BuiltinCharacterId==pair.Key?Skin:null;
                var source=active??pair.Value;
                if(source.OfficialInstallation==null)continue;
                HolidayCatalog catalog;if(!officialCatalogs.TryGetValue(source.Source,out catalog))continue;
                merged=merged.WithCharacter(pair.Key,catalog);
            }
            if(skin.PackageHolidayCatalog!=null)merged=merged.WithCharacter(skin.OutfitCharacterId,skin.PackageHolidayCatalog);
            Holidays.ReplaceCatalog(merged);
        }
        OfficialCompanionPreparation preparation;
        internal Func<IProgress<OfficialCompanionProgress>,CancellationToken,Task> PreparationDownloadOverride=null;
        internal OfficialCompanionPreparation OfficialPreparation {get{return preparation??(preparation=TrackPreparationNotifications(new OfficialCompanionPreparation((progress,token)=>PreparationDownloadOverride==null?PrepareAllOfficialAsync(progress,token):PreparationDownloadOverride(progress,token),officialLifetime.Token)));}}
        async Task PrepareAllOfficialAsync(IProgress<OfficialCompanionProgress> progress,CancellationToken cancel)
        {
            var captured=Skin;long token=officialRequest;IList<OfficialCompanionResult> results=null;Exception failure=null;
            try{results=await OfficialDownloads.EnsureAllAsync(OfficialRouting(),progress,cancel);}
            catch(Exception error){failure=error;}
            if(!closing)await RefreshBulkInstallations(captured,token,results);
            if(failure!=null)throw failure;
        }
        internal Task DownloadAllOfficialAsync(Window owner)
        {
            if(NoticeOnlyCompanionGuide()||closing||IsUpdating)return Task.FromResult(0);
            ShowOfficialPreparation();return OfficialPreparation.StartAsync();
        }
        internal void ShowOfficialPreparation()
        {
            new CompanionPreparationWindow(OfficialPreparation).Show();
        }
        async Task RefreshBulkInstallations(SkinDefinition captured,long token,IList<OfficialCompanionResult> results)
        {
            foreach(var profile in OfficialCompanionProfiles.All) {
                if(closing)return;
                try {
                    using(var snapshot=await Task.Run(()=>OfficialDownloads.CaptureLocalSnapshot(profile.Id,officialLifetime.Token))){
                        if(snapshot==null)continue;
                        // Retain this exact generation through full verification, native
                        // semaphore waiting/decoding, and recovery publication.
                        var installed=await Task.Run(()=>OfficialDownloads.Storage.ReadVersionCancelable(snapshot.Installation.RootDirectory,profile.Id,snapshot.Installation.Asset,officialLifetime.Token));if(installed==null)continue;
                        await officialNative.ValidateAsync(installed,officialLifetime.Token);
                        var result=results==null?null:results.FirstOrDefault(r=>r.Installation.Asset.Id==profile.Id);
                        var ready=await PrepareOfficialAsync(installed,result==null||result.InstalledNow);
                        if(Skin.PickerKey==ready.PickerKey&&String.IsNullOrEmpty(Settings.PendingOfficialCompanionId)&&(deferredOfficial==null||!deferredOfficial.Select)){QueueOfficial(ready,officialRequest,false);TryApplyDeferredOfficial();}else PublishInactiveOfficial(ready);
                    }
                }catch(OperationCanceledException){return;}catch(Exception error){if(!OfficialError(error))throw;}
            }
        }
        void TryStartOfficialStartup()
        {
            if(officialStartupStarted||GuideActive||closing||!officialFirstFrameRendered||!IsPetVisible)return;
            officialStartupStarted=true;
            if(Settings.PendingOfficialCompanionId.Length!=0){Settings.PendingOfficialCompanionId="";SaveSettings();}
            var desired=Catalog.FindPicker(Settings.SelectedCompanionKey)??Skin;
            if(Settings.LastCompanionUpgradeVersion==ReleaseInfo.DisplayVersion||!desired.IsOfficial)return;
            // A new application version checks only the already visible selected
            // role. Failure retains the old version marker so restart can retry.
            var work=RefreshOfficialAsync(desired.BuiltinCharacterId,officialRequest,false,CompanionSelectionOrigin.StartupRestore,null,OfficialCompanionCheckMode.AfterApplicationUpdate);
        }
        void OfficialCompanionHidden(SkinDefinition skin)
        {
            if(!skin.IsOfficial||(officialSelection==null||CurrentCompanionSelection==null||CurrentCompanionSelection.PickerKey!=skin.PickerKey)&&Settings.PendingOfficialCompanionId!=skin.BuiltinCharacterId&&(deferredOfficial==null||!deferredOfficial.Select||deferredOfficial.Skin.PickerKey!=skin.PickerKey))return;
            if(officialSelection!=null)officialSelection.Cancel();
            officialRequest=OfficialDownloads.BeginSelection();deferredOfficial=null;renderedSelection=null;Settings.PendingOfficialCompanionId="";SaveSettings();
        }
        readonly Dictionary<string,bool> officialRepairs=new Dictionary<string,bool>(StringComparer.Ordinal);
        readonly Dictionary<string,DateTime> officialRepairAfter=new Dictionary<string,DateTime>(StringComparer.Ordinal);
        void DefaultCompanionFailed(SkinDefinition source)
        {
            if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(()=>DefaultCompanionFailed(source)));return;}
            if(closing||source.Snapshot==null||!ReferenceEquals(Skin,source))return;
            string generation=source.Snapshot.Generation;DateTime retry;
            var pending=deferredOfficial;
            // The prepared replacement already owns the repair while waiting for
            // safe idle. The existing pending slot is cleared on apply/invalidation.
            if(pending!=null&&!pending.Select&&ReferenceEquals(pending.ExpectedSource,source)&&OwnsOfficial(pending.Request)
                &&pending.Skin.Snapshot!=null&&pending.Skin.Snapshot.Generation!=generation)return;
            if(officialRepairs.ContainsKey(generation)||officialRepairAfter.TryGetValue(generation,out retry)&&DateTime.UtcNow<retry)return;
            officialRepairs[generation]=true;
            var work=RepairDefaultCompanionAsync(source,officialRequest);
        }
        async Task RepairDefaultCompanionAsync(SkinDefinition source,long token)
        {
            string generation=source.Snapshot.Generation;
            try{
                var result=await OfficialDownloads.EnsureAsync(source.BuiltinCharacterId,OfficialRouting(),OfficialCompanionCheckMode.Cached,null,officialLifetime.Token,true,true);
                if(closing||result.UsedOfflineFallback)return;
                var ready=await PrepareOfficialAsync(result.Installation,true);
                if(OwnsOfficial(token)&&ReferenceEquals(Skin,source)){QueueOfficial(ready,token,false);TryApplyDeferredOfficial();}
                else PublishInactiveOfficial(ready);
            }catch(OperationCanceledException){}
            catch(Exception error){if(!OfficialError(error))throw;CompanionSelectionUpdate.WriteTrace("Companion silent repair failed: "+error.GetType().Name);}
            finally{officialRepairs.Remove(generation);officialRepairAfter[generation]=DateTime.UtcNow.AddSeconds(30);}
        }
        void PreparedCompanionFrame(string key)
        {
            if(closing)return;
            string generation=Skin.Snapshot==null?Skin.NativeSourceKey:Skin.Snapshot.Generation;
            if(key.StartsWith(generation+"/",StringComparison.Ordinal)||key.StartsWith("holiday/"+generation+"/",StringComparison.Ordinal))UpdateFrame();
        }
        void RetireOfficialSources()
        {
            if(officialSelection!=null||officialPreparationReaders!=0||checkingDeferredOfficial)return;
            foreach(var snapshot in retainedSnapshots.ToArray()){
                if(ReferenceEquals(Skin.Snapshot,snapshot)||officialSources.Values.Any(s=>ReferenceEquals(s.Snapshot,snapshot))||deferredOfficial!=null&&ReferenceEquals(deferredOfficial.Skin.Snapshot,snapshot)||WpfPreparedCompanion.HasPending(snapshot.Generation))continue;
                if(!retainedSnapshots.Any(other=>!ReferenceEquals(other,snapshot)&&other.Generation==snapshot.Generation))WpfPreparedCompanion.Retire(snapshot.Generation);
                if(!retainedSnapshots.Any(other=>!ReferenceEquals(other,snapshot)&&other.Installation.RootDirectory==snapshot.Installation.RootDirectory))officialCatalogs.Remove(snapshot.Installation.RootDirectory);
                retainedSnapshots.Remove(snapshot);snapshot.Dispose();
            }
        }
        void DisposeOfficialCompanions(){if(companionInstallationBubble!=null)companionInstallationBubble.Shutdown();WpfPreparedCompanion.Prepared-=PreparedCompanionFrame;WpfPreparedCompanion.DefaultFailed-=DefaultCompanionFailed;officialLifetime.Cancel();if(preparation!=null)preparation.Dispose();if(officialDownloads!=null)officialDownloads.Dispose();if(officialTransport!=null)officialTransport.Dispose();foreach(var snapshot in retainedSnapshots)snapshot.Dispose();retainedSnapshots.Clear();WpfPreparedCompanion.Deactivate();officialCatalogs.Clear();officialSources.Clear();deferredOfficial=null;}
    }
}
