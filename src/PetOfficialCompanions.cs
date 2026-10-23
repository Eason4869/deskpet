using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Dafeiyu
{
    internal enum CompanionSelectionOrigin { PassiveMenu, Settings, MainWindow, StartupRestore }
    public sealed partial class PetWindow
    {
        readonly WpfOfficialNativeValidator officialNative=new WpfOfficialNativeValidator();
        readonly CancellationTokenSource officialLifetime=new CancellationTokenSource();
        OfficialCompanionDownloads officialDownloads;
        GitHubUpdates officialTransport;
        readonly Dictionary<string,HolidayCatalog> officialCatalogs=new Dictionary<string,HolidayCatalog>(StringComparer.Ordinal);
        readonly Dictionary<string,SkinDefinition> officialSources=new Dictionary<string,SkinDefinition>(StringComparer.Ordinal);
        HolidayCatalog embeddedHolidayCatalog;
        PendingOfficialSource deferredOfficial;
        bool officialStartupStarted;
        bool checkingDeferredOfficial;
        long officialRequest;
        sealed class PendingOfficialSource
        {internal SkinDefinition Skin;internal long Request;internal bool Select;internal string ExpectedKey;internal SkinDefinition ExpectedSource;internal string Pointer;}
        OfficialCompanionDownloads OfficialDownloads
        {
            get {
                if(officialDownloads==null){officialTransport=new GitHubUpdates();officialDownloads=new OfficialCompanionDownloads(Store.DirectoryPath,ReleaseInfo.DisplayVersion,ReleaseVersion.Parse(ReleaseInfo.DisplayVersion).IsPreview?UpdateChannel.Preview:UpdateChannel.Stable,officialTransport,officialNative.ValidateAsync);}
                return officialDownloads;
            }
        }
        OfficialDownloadRouting OfficialRouting(){return new OfficialDownloadRouting(Settings.UpdateUseMirror,Settings.UpdateMirrorAddress,Settings.UpdateUseProxy,Settings.UpdateProxyAddress);}
        bool HasOfficialPointer(SkinDefinition skin){return skin.IsOfficial&&File.Exists(Path.Combine(Store.DirectoryPath,"official-companions",skin.BuiltinCharacterId,"current.txt"));}
        bool ShouldRefreshOfficial(SkinDefinition skin){return skin.IsOfficial&&!skin.FixtureEmbedded&&(!skin.Embedded||HasOfficialPointer(skin));}
        internal bool NoticeOnlyCompanionGuide()
        {
            if(!GuideActive)return false;
            Notice=TextResources.Get("petOfficialCompanions.noticeOnlyCompanionGuide.notice");
            if(preferences!=null)preferences.ShowNotice(Notice);return true;
        }
        bool CanRequestCompanion()
        {
            if(NoticeOnlyCompanionGuide())return false;
            if(closing||IsUpdating)return false;
            if(preferences!=null&&preferences.HasUnsavedEdits){Notice=TextResources.Get("petOfficialCompanions.canRequestCompanion.notice");preferences.ShowNotice(Notice);return false;}
            if(DialogueBusy||DictationActive||LiveActive){Notice=TextResources.Get("petOfficialCompanions.canRequestCompanion.notice.alternativeA");return false;}
            return true;
        }
        long BeginCompanionSelection(SkinDefinition target)
        {
            officialRequest=OfficialDownloads.BeginSelection();deferredOfficial=null;
            Settings.PendingOfficialCompanionId=target.IsOfficial&&target.IsPlaceholder?target.BuiltinCharacterId:"";
            SaveSettings();return officialRequest;
        }
        public bool SelectSkin(SkinDefinition skin)
        {
            if(skin==null||!CanRequestCompanion())return false;
            if(!Catalog.Skins.Any(s=>s.PickerKey==skin.PickerKey)){Notice=TextResources.Get("petOfficialCompanions.selectSkin.notice");return false;}
            if(skin.IsPlaceholder||skin.Embedded&&ShouldRefreshOfficial(skin)){var ignored=RequestSkinAsync(skin,CompanionSelectionOrigin.PassiveMenu,null);return false;}
            BeginCompanionSelection(skin);
            bool changed=ActivateSkin(skin);if(changed){Catalog.PreserveActive(skin);if(skin.IsOfficial)MergeOfficialHoliday(skin);}if(changed&&ShouldRefreshOfficial(skin)){var ignored=RefreshOfficialAsync(skin.BuiltinCharacterId,officialRequest,false,CompanionSelectionOrigin.StartupRestore,null,OfficialCompanionCheckMode.Cached);}
            return changed;
        }
        internal async Task<bool> RequestSkinAsync(SkinDefinition skin,CompanionSelectionOrigin origin,Window owner)
        {
            if(skin==null||!CanRequestCompanion())return false;
            if(!Catalog.Skins.Any(s=>s.PickerKey==skin.PickerKey)){Notice=TextResources.Get("petOfficialCompanions.requestSkinAsync.notice");return false;}
            RecordPetInteraction();long token=BeginCompanionSelection(skin);
            if(skin.Embedded&&ShouldRefreshOfficial(skin))return await RefreshOfficialAsync(skin.BuiltinCharacterId,token,true,origin,owner,OfficialCompanionCheckMode.Cached);
            if(!skin.IsPlaceholder) {
                bool selected=ActivateSkin(skin);
                if(selected){Catalog.PreserveActive(skin);if(skin.IsOfficial)MergeOfficialHoliday(skin);}if(selected&&ShouldRefreshOfficial(skin)){var ignored=RefreshOfficialAsync(skin.BuiltinCharacterId,token,false,CompanionSelectionOrigin.StartupRestore,null,OfficialCompanionCheckMode.Cached);}
                return selected;
            }
            return await RefreshOfficialAsync(skin.BuiltinCharacterId,token,true,origin,owner,OfficialCompanionCheckMode.Cached);
        }
        bool OwnsOfficial(long token){return !closing&&officialDownloads!=null&&officialDownloads.OwnsSelection(token);}
        async Task<bool> RefreshOfficialAsync(string id,long token,bool selecting,CompanionSelectionOrigin origin,Window owner,OfficialCompanionCheckMode mode)
        {
            OfficialCompanionProgressWindow dialog=null;var requestedSource=Skin;
            using(var cancel=CancellationTokenSource.CreateLinkedTokenSource(officialLifetime.Token)) {
                try {
                    if(selecting||Skin.IsOfficial&&Skin.BuiltinCharacterId==id) {
                        var installed=await Task.Run(()=>OfficialDownloads.Installed(id),cancel.Token);
                        if(installed!=null){try{await officialNative.ValidateAsync(installed,cancel.Token);}catch(OperationCanceledException){throw;}catch(Exception error){if(!OfficialError(error))throw;installed=null;}}
                        if(installed!=null){if(OwnsOfficial(token)){var ready=await PrepareOfficialAsync(installed,false);if(OwnsOfficial(token)){QueueOfficial(ready,token,selecting);TryApplyDeferredOfficial();if(ReferenceEquals(Skin,ready))requestedSource=Skin;}else PublishInactiveOfficial(ready);}}
                        else if(selecting&&id=="e-bao"&&OwnsOfficial(token)){QueueOfficial(Catalog.EmbeddedFallback(),token,true);TryApplyDeferredOfficial();requestedSource=Skin;}
                    }
                    if(!OwnsOfficial(token))return false;
                    if(selecting&&Skin.BuiltinCharacterId!=id) {
                        if(origin==CompanionSelectionOrigin.PassiveMenu){Notice=TextResources.Get("petOfficialCompanions.refreshOfficialAsync.notice");ShowHolidayNotice(Notice);}
                        else if(origin!=CompanionSelectionOrigin.StartupRestore){dialog=new OfficialCompanionProgressWindow(owner,false);dialog.Show();}
                    }
                    using(var lease=dialog==null?CancellationTokenSource.CreateLinkedTokenSource(cancel.Token):CancellationTokenSource.CreateLinkedTokenSource(cancel.Token,dialog.Token)) {
                        var progress=new Progress<OfficialCompanionProgress>(p=>{if(dialog!=null)dialog.Report(p);});
                        var result=await OfficialDownloads.EnsureAsync(id,OfficialRouting(),mode,progress,lease.Token);
                        if(closing)return false;
                        var ready=await PrepareOfficialAsync(result.Installation,result.InstalledNow);
                        if(OwnsOfficial(token)) {
                            if(selecting||Skin.PickerKey==ready.PickerKey&&ReferenceEquals(Skin,requestedSource)){QueueOfficial(ready,token,selecting);TryApplyDeferredOfficial();}
                            else PublishInactiveOfficial(ready);
                            if(result.UsedOfflineFallback){Notice=TextResources.Get("petOfficialCompanions.refreshOfficialAsync.notice.alternativeA");if(preferences!=null)preferences.ShowNotice(Notice);}
                        }else PublishInactiveOfficial(ready);
                        if(dialog!=null)dialog.Complete();return OwnsOfficial(token);
                    }
                }catch(OperationCanceledException){if(dialog!=null&&dialog.Token.IsCancellationRequested&&OwnsOfficial(token)){officialRequest=OfficialDownloads.BeginSelection();deferredOfficial=null;Settings.PendingOfficialCompanionId="";SaveSettings();}if(dialog!=null)dialog.Failure(TextResources.Get("petOfficialCompanions.refreshOfficialAsync.dialogFailure"),()=>{var ignored=RequestSkinAsync(Catalog.FindOfficial(id),origin,owner);});return false;}
                catch(Exception error) {
                    if(!OfficialError(error))throw;
                    if(OwnsOfficial(token)){Notice=TextResources.Get("petOfficialCompanions.refreshOfficialAsync.notice.alternativeB");if(preferences!=null)preferences.ShowNotice(Notice);if(origin==CompanionSelectionOrigin.PassiveMenu)ShowHolidayNotice(Notice);}
                    if(dialog!=null)dialog.Failure(TextResources.Get("petOfficialCompanions.refreshOfficialAsync.dialogFailure.alternativeA"),()=>{var ignored=RequestSkinAsync(Catalog.FindOfficial(id),origin,owner);});return false;
                }
            }
        }
        static bool OfficialError(Exception error){return error is IOException||error is InvalidDataException||error is UnauthorizedAccessException||error is InvalidOperationException||error is ArgumentException||error is NotSupportedException||error is ApiException||error is AggregateException||error is System.Runtime.Serialization.SerializationException;}
        async Task<SkinDefinition> PrepareOfficialAsync(OfficialCompanionInstallation installation,bool installedNow)
        {
            // A bulk refresh does not advance selection ownership. An older
            // completion must also agree with the latest verified disk receipt.
            for(int attempt=0;attempt<3;attempt++) {
                var latest=await Task.Run(()=>OfficialDownloads.Installed(installation.Asset.Id),officialLifetime.Token);
                if(latest==null)throw new InvalidDataException(TextResources.Get("petOfficialCompanions.prepareOfficialAsync.text"));
                if(latest.RootDirectory!=installation.RootDirectory){installation=latest;installedNow=false;}
                await officialNative.ValidateAsync(installation,officialLifetime.Token);
                latest=await Task.Run(()=>OfficialDownloads.Installed(installation.Asset.Id),officialLifetime.Token);
                if(latest==null)throw new InvalidDataException(TextResources.Get("petOfficialCompanions.prepareOfficialAsync.text"));
                if(latest.RootDirectory!=installation.RootDirectory){installation=latest;installedNow=false;continue;}
                SkinDefinition existing;
                if(!installedNow&&officialSources.TryGetValue(installation.Asset.Id,out existing)&&existing.Source==installation.RootDirectory)return existing;
                string persona=Catalog.FindOfficial(installation.Asset.Id).Id;
                var selected=installation;
                var prepared=await Task.Run(()=>{
                    SkinManifest manifest;using(var input=File.OpenRead(selected.PetManifestPath))manifest=SkinCatalog.ReadManifest(input);
                    var source=new SkinDefinition(persona,selected.RootDirectory,manifest){BuiltinCharacterId=selected.Asset.Id,OfficialInstallation=selected};
                    source.PreparedOfficialAtlas=source.Load();
                    return Tuple.Create(source,new HolidayCatalog(selected.HolidayRootDirectory));
                },officialLifetime.Token);
                latest=await Task.Run(()=>OfficialDownloads.Installed(selected.Asset.Id),officialLifetime.Token);
                if(latest==null)throw new InvalidDataException(TextResources.Get("petOfficialCompanions.prepareOfficialAsync.text"));
                if(latest.RootDirectory!=selected.RootDirectory){installation=latest;installedNow=false;continue;}
                var skin=prepared.Item1;officialSources[selected.Asset.Id]=skin;
                if(installedNow)officialCatalogs.Remove(selected.RootDirectory);
                if(!officialCatalogs.ContainsKey(selected.RootDirectory))officialCatalogs[selected.RootDirectory]=prepared.Item2;
                foreach(string retired in officialCatalogs.Keys.Where(root=>!officialSources.Values.Any(s=>s.Source==root)&&Skin.Source!=root).ToArray())officialCatalogs.Remove(retired);
                return skin;
            }
            throw new IOException(TextResources.Get("petOfficialCompanions.prepareOfficialAsync.text.alternativeA"));
        }
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
        bool OfficialSafe {get{return !closing&&!IsUpdating&&!GuideActive&&!HolidayBusinessBusy&&!interaction.Active&&!dragging&&!dropMotion.Active&&!walking&&!holidayPlayback.Active&&!holidayTransition.Active&&Animation!=null&&Animation.State=="idle"&&!clickTimer.IsEnabled&&(preferences==null||!preferences.HasUnsavedEdits);}}
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
                    pending.Skin=ready;pending.Pointer=Path.GetFileName(ready.Source);
                }
            }catch(OperationCanceledException){}catch(Exception error){if(!OfficialError(error))throw;if(ReferenceEquals(deferredOfficial,pending))deferredOfficial=null;}
            finally{checkingDeferredOfficial=false;}
            TryApplyDeferredOfficial();
        }
        void RefreshOfficialAppearance(SkinDefinition skin)
        {
            var replacement=skin.Load();var interactions=InteractionAtlas.LoadForSkin(skin);
            CancelInteraction();atlas=replacement;interactionAtlas=interactions;Skin=skin;interaction.Configure(skin);
            motion=null;motionSkin=null;shownMotion=null;shownSample=-1;shownRow=shownCol=-1;shownOffset=new Point();shownMirrored=false;gazeActive=false;
            Animation=skin.CreateAnimation(atlas.FrameMultiplier);RefreshHoliday();UpdateFrame();RefreshChatAppearance();
        }
        void MergeOfficialHoliday(SkinDefinition skin)
        {
            if(Holidays==null||!skin.IsOfficial)return;
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
            Holidays.ReplaceCatalog(merged);
        }
        internal async Task DownloadAllOfficialAsync(Window owner)
        {
            if(NoticeOnlyCompanionGuide()||closing||IsUpdating)return;
            var captured=Skin;long token=officialRequest;var dialog=new OfficialCompanionProgressWindow(owner,true);dialog.Show();
            IList<OfficialCompanionResult> results=null;Exception failure=null;
            try {results=await OfficialDownloads.EnsureAllAsync(OfficialRouting(),new Progress<OfficialCompanionProgress>(dialog.Report),dialog.Token);}
            catch(OperationCanceledException error){failure=error;}
            catch(Exception error){if(!OfficialError(error))throw;failure=error;}
            if(!closing)await RefreshBulkInstallations(captured,token,results);
            if(failure==null){dialog.Complete();return;}
            if(failure is OperationCanceledException){dialog.Failure(TextResources.Get("petOfficialCompanions.downloadAllOfficialAsync.dialogFailure.alternativeA"),()=>{var ignored=DownloadAllOfficialAsync(owner);});return;}
            var aggregate=failure as AggregateException;
            var failures=aggregate==null?new string[0]:OfficialCompanionProfiles.All.Where(p=>aggregate.Flatten().InnerExceptions.Any(e=>e.Message.StartsWith(p.Id+"：",StringComparison.Ordinal)||e.Message.StartsWith(p.Id+":",StringComparison.Ordinal))).Select(p=>p.Name).ToArray();
            dialog.Failure((failures.Length==0?TextResources.Get("petOfficialCompanions.downloadAllOfficialAsync.dialogFailure.alternativeB"):TextResources.Format("petOfficialCompanions.downloadAllOfficialAsync.dialogFailure",String.Join("、",failures)))+TextResources.Get("petOfficialCompanions.downloadAllOfficialAsync.dialogFailure.alternativeC"),()=>{var ignored=DownloadAllOfficialAsync(owner);});
        }
        async Task RefreshBulkInstallations(SkinDefinition captured,long token,IList<OfficialCompanionResult> results)
        {
            foreach(var profile in OfficialCompanionProfiles.All) {
                if(closing)return;
                try {
                    var installed=await Task.Run(()=>OfficialDownloads.Installed(profile.Id));if(installed==null)continue;
                    await officialNative.ValidateAsync(installed,officialLifetime.Token);
                    var result=results==null?null:results.FirstOrDefault(r=>r.Installation.Asset.Id==profile.Id);
                    var ready=await PrepareOfficialAsync(installed,result==null||result.InstalledNow);
                    if(captured.PickerKey==ready.PickerKey&&ReferenceEquals(captured,Skin)&&OwnsOfficial(token)&&String.IsNullOrEmpty(Settings.PendingOfficialCompanionId)&&(deferredOfficial==null||!deferredOfficial.Select)){QueueOfficial(ready,token,false);TryApplyDeferredOfficial();}else PublishInactiveOfficial(ready);
                }catch(OperationCanceledException){return;}catch(Exception error){if(!OfficialError(error))throw;}
            }
        }
        void TryStartOfficialStartup()
        {
            if(officialStartupStarted||GuideActive||closing)return;officialStartupStarted=true;
            string id=Settings.PendingOfficialCompanionId;
            if(!String.IsNullOrEmpty(id)&&!Catalog.Skins.Any(s=>s.BuiltinCharacterId==id)){Settings.PendingOfficialCompanionId="";SaveSettings();id="";}
            if(String.IsNullOrEmpty(id)&&Skin.IsOfficial&&!Skin.FixtureEmbedded&&(!Skin.Embedded||File.Exists(Path.Combine(Store.DirectoryPath,"official-companions",Skin.BuiltinCharacterId,"current.txt"))))id=Skin.BuiltinCharacterId;
            if(String.IsNullOrEmpty(id))return;
            officialRequest=OfficialDownloads.BeginSelection();
            var ignored=RefreshOfficialAsync(id,officialRequest,!String.IsNullOrEmpty(Settings.PendingOfficialCompanionId),CompanionSelectionOrigin.StartupRestore,null,OfficialCompanionCheckMode.AfterApplicationUpdate);
        }
        void OfficialCompanionHidden(SkinDefinition skin)
        {
            if(!skin.IsOfficial||Settings.PendingOfficialCompanionId!=skin.BuiltinCharacterId&&(deferredOfficial==null||!deferredOfficial.Select||deferredOfficial.Skin.PickerKey!=skin.PickerKey))return;
            officialRequest=OfficialDownloads.BeginSelection();deferredOfficial=null;Settings.PendingOfficialCompanionId="";SaveSettings();
        }
        void DisposeOfficialCompanions(){officialLifetime.Cancel();if(officialDownloads!=null)officialDownloads.Dispose();if(officialTransport!=null)officialTransport.Dispose();officialCatalogs.Clear();officialSources.Clear();deferredOfficial=null;}
    }
}
