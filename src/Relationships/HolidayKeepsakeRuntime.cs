using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Dafeiyu.Maintenance;
namespace Dafeiyu
{
    public sealed class HolidayKeepsakeRuntime
    {
        readonly object sync=new object();readonly HolidayKeepsakeStore store;readonly DataMaintenanceGate maintenance;
        readonly HolidayCalendar calendar;readonly HolidayKeepsakeRegistry registry;readonly IRelationshipSceneProvider provider;
        readonly IHolidayKeepsakeSceneValidator validator;readonly Action<Action> dispatch;
        Task worker=Task.FromResult(0),shutdown;bool closed;
        public event Action<string> Changed;
        public HolidayKeepsakeRuntime(string dataDirectory,DataMaintenanceGate maintenance,HolidayCalendar calendar,
            HolidayKeepsakeRegistry registry,IRelationshipSceneProvider provider,IHolidayKeepsakeSceneValidator validator,Action<Action> uiDispatch=null)
        {
            if(maintenance==null||calendar==null||registry==null||provider==null||validator==null)throw new ArgumentNullException("A holiday dependency is missing.");
            store=new HolidayKeepsakeStore(dataDirectory);this.maintenance=maintenance;this.calendar=calendar;this.registry=registry;
            this.provider=provider;this.validator=validator;dispatch=uiDispatch??(action=>action());
        }
        sealed class Candidate
        {internal HolidayKeepsakeDefinition Definition;internal HolidayOccurrence Occurrence;internal RelationshipSceneCapture Source;internal bool Supported;}
        static bool Trusted(string companion,string character)
        {return companion=="official:"+character&&new[]{"e-bao","dafeiyu","marfu","xiaobei","gugugaga"}.Contains(character,StringComparer.Ordinal);}
        Candidate Capture(string companion,string character,HolidayKeepsakeDefinition definition,HolidayOccurrence occurrence,HolidayCatalog catalog)
        {
            var candidate=new Candidate{Definition=definition,Occurrence=occurrence,Supported=Trusted(companion,character)};
            if(!candidate.Supported||catalog==null)return candidate;
            // Stable ordinal selection among exact mappings supported by the reviewed provider.
            foreach(var outfit in catalog.Outfits.Where(x=>x.Ready&&x.CharacterId==character&&x.HolidayId==definition.HolidayId)
                .OrderBy(x=>x.OutfitId,StringComparer.Ordinal).ThenBy(x=>x.Version,StringComparer.Ordinal)){
                try{
                    var source=provider.Capture(companion,character,definition.SceneSetId,outfit.OutfitId,outfit.Version);var d=source==null?null:source.Descriptor;
                    if(d!=null&&d.CompanionId==companion&&d.CharacterId==character&&d.HolidayId==definition.HolidayId&&d.OutfitId==outfit.OutfitId&&d.OutfitVersion==outfit.Version){candidate.Source=source;break;}
                }catch(Exception error){if(!ResourceError(error))throw;}
            }return candidate;
        }
        public Task<HolidayKeepsakeObservationResult> ObserveAsync(HolidayKeepsakeEncounter encounter,HolidayCatalog capturedCatalog)
        {
            if(encounter==null)throw new ArgumentNullException("encounter");
            lock(sync){
                if(closed)return Task.FromResult(new HolidayKeepsakeObservationResult(HolidayKeepsakeObservationStatus.Closed));
                IDisposable activity;if(!maintenance.TryEnterActivity(out activity))return Task.FromResult(new HolidayKeepsakeObservationResult(HolidayKeepsakeObservationStatus.MaintenanceBusy));
                try{
                    var occurrences=calendar.Resolve(encounter.Time.UtcNow.Add(encounter.Time.LocalOffset).Date);var candidates=new List<Candidate>();
                    foreach(var definition in registry.Definitions.Where(d=>d.EnabledForAcquisition)){
                        var occurrence=occurrences.FirstOrDefault(o=>o.Id==definition.CalendarId&&o.StartDate.Year>=definition.FirstYear);
                        if(occurrence!=null)candidates.Add(Capture(encounter.CompanionId,encounter.VerifiedCharacterId,definition,occurrence,capturedCatalog));
                    }
                    var task=worker.ContinueWith(delegate{using(activity)return Observe(encounter,candidates);},TaskScheduler.Default);worker=task;return task;
                }catch{activity.Dispose();throw;}
            }
        }
        HolidayKeepsakeObservationResult Observe(HolidayKeepsakeEncounter encounter,List<Candidate> candidates)
        {
            try{
                HolidayKeepsakeObservationResult result;bool saved=false;
                using(store.Acquire()){
                    var data=store.Load();string day=encounter.Time.LocalDate;
                    if(String.CompareOrdinal(day,data.DateHighWater)<0)return new HolidayKeepsakeObservationResult(HolidayKeepsakeObservationStatus.ClockRollback);
                    bool changed=day!=data.DateHighWater;data.DateHighWater=day;var results=new List<HolidayKeepsakeResult>();
                    foreach(var candidate in candidates){
                        var d=candidate.Definition;var o=candidate.Occurrence;
                        var owned=data.Records.FirstOrDefault(r=>r.CompanionId==encounter.CompanionId&&r.HolidayId==d.HolidayId&&r.OccurrenceYear==o.StartDate.Year);
                        if(owned!=null){results.Add(new HolidayKeepsakeResult(d.HolidayId,o.InstanceKey,HolidayKeepsakeStatus.AlreadyOwned,owned));continue;}
                        if(!candidate.Supported){results.Add(new HolidayKeepsakeResult(d.HolidayId,o.InstanceKey,HolidayKeepsakeStatus.UnsupportedCompanion));continue;}
                        try{
                            if(candidate.Source==null)throw new InvalidDataException("No exact scene mapping.");
                            var asset=candidate.Source.Descriptor;byte[] bytes=Read(candidate.Source);
                            var record=new HolidayKeepsakeRecord{CompanionId=encounter.CompanionId,CharacterId=encounter.VerifiedCharacterId,CompanionName=encounter.CompanionName,
                                HolidayId=d.HolidayId,CalendarId=d.CalendarId,InstanceKey=o.InstanceKey,OccurrenceYear=o.StartDate.Year,
                                OccurrenceStartLocalDate=HolidayKeepsakeStore.Date(o.StartDate),OccurrenceEndExclusiveLocalDate=HolidayKeepsakeStore.Date(o.EndExclusive),
                                EarnedUtcTicks=encounter.Time.UtcNow.Ticks,OffsetMinutes=(int)encounter.Time.LocalOffset.TotalMinutes,EarnedLocalDate=day,
                                OutfitId=asset.OutfitId,OutfitVersion=asset.OutfitVersion,SceneId=asset.SceneId,SceneVersion=asset.SceneVersion,
                                PixelWidth=asset.PixelWidth,PixelHeight=asset.PixelHeight,ByteLength=asset.ByteLength,SceneSha256=asset.Sha256};
                            store.PublishScene(record,bytes);data.Records.Add(record);changed=saved=true;
                            results.Add(new HolidayKeepsakeResult(d.HolidayId,o.InstanceKey,HolidayKeepsakeStatus.Saved,record));
                        }catch(Exception error){if(!ResourceError(error))throw;results.Add(new HolidayKeepsakeResult(d.HolidayId,o.InstanceKey,HolidayKeepsakeStatus.ResourceUnavailable));}
                    }
                    if(changed)store.Save(data);result=new HolidayKeepsakeObservationResult(HolidayKeepsakeObservationStatus.Completed,results);
                }
                if(saved)Notify(encounter.CompanionId);return result;
            }catch(HolidayKeepsakeIncompatibleException){return new HolidayKeepsakeObservationResult(HolidayKeepsakeObservationStatus.StoreIncompatible);}
            catch(Exception error){if(!ResourceError(error))throw;return new HolidayKeepsakeObservationResult(HolidayKeepsakeObservationStatus.StorageUnavailable);}
        }
        byte[] Read(RelationshipSceneCapture source)
        {var d=source.Descriptor;using(var stream=source.OpenRead())return HolidayKeepsakePng.Read(stream,d.ByteLength,d.Sha256,d.PixelWidth,d.PixelHeight,validator);}
        void Notify(string companion)
        {
            try{dispatch(delegate{var handlers=Changed;if(handlers==null)return;foreach(Action<string> handler in handlers.GetInvocationList())try{handler(companion);}catch(Exception){}});}catch(Exception){}
        }
        Task<T> EnqueueRead<T>(Func<T> action,T closedResult)
        {lock(sync){if(closed)return Task.FromResult(closedResult);var task=worker.ContinueWith(_=>action(),TaskScheduler.Default);worker=task;return task;}}
        public Task<HolidayKeepsakeAlbum> LoadAsync(string companionId)
        {
            return EnqueueRead(delegate{
                try{return new HolidayKeepsakeAlbum(HolidayKeepsakeReadStatus.Available,store.Load().Records.Where(r=>r.CompanionId==companionId));}
                catch(HolidayKeepsakeIncompatibleException){return new HolidayKeepsakeAlbum(HolidayKeepsakeReadStatus.StoreIncompatible);}
                catch(Exception error){if(!ResourceError(error))throw;return new HolidayKeepsakeAlbum(HolidayKeepsakeReadStatus.StorageUnavailable);}
            },new HolidayKeepsakeAlbum(HolidayKeepsakeReadStatus.Closed));
        }
        public Task<HolidayKeepsakeSceneResult> ReadSceneAsync(string companionId,string holidayId,string instanceKey)
        {
            return EnqueueRead(delegate{
                HolidayKeepsakeRecord record;
                try{record=store.Load().Records.FirstOrDefault(r=>r.CompanionId==companionId&&r.HolidayId==holidayId&&r.InstanceKey==instanceKey);}
                catch(HolidayKeepsakeIncompatibleException){return new HolidayKeepsakeSceneResult(HolidayKeepsakeReadStatus.StoreIncompatible);}
                catch(Exception error){if(!ResourceError(error))throw;return new HolidayKeepsakeSceneResult(HolidayKeepsakeReadStatus.StorageUnavailable);}
                if(record==null)return new HolidayKeepsakeSceneResult(HolidayKeepsakeReadStatus.NotFound);
                try{return new HolidayKeepsakeSceneResult(HolidayKeepsakeReadStatus.Available,record,store.ReadScene(record,validator));}
                catch(Exception error){if(!ResourceError(error))throw;return new HolidayKeepsakeSceneResult(HolidayKeepsakeReadStatus.ResourceUnavailable,record);}
            },new HolidayKeepsakeSceneResult(HolidayKeepsakeReadStatus.Closed));
        }
        public Task<HolidayKeepsakePreviewResult> PreviewAsync(string companionId,string verifiedCharacterId,string companionName,string holidayId,int year,HolidayCatalog capturedCatalog)
        {
            lock(sync){
                var definition=registry.Find(holidayId);var occurrence=registry.FindOccurrence(calendar,definition,year);
                if(closed||occurrence==null)return Task.FromResult(new HolidayKeepsakePreviewResult(HolidayKeepsakeAvailability.NotEnabled,holidayId));
                var candidate=Capture(companionId,verifiedCharacterId,definition,occurrence,capturedCatalog);
                return EnqueueRead(delegate{
                    if(!candidate.Supported)return new HolidayKeepsakePreviewResult(HolidayKeepsakeAvailability.UnsupportedCompanion,holidayId,occurrence.InstanceKey);
                    try{if(candidate.Source==null)throw new InvalidDataException();return new HolidayKeepsakePreviewResult(HolidayKeepsakeAvailability.Available,holidayId,occurrence.InstanceKey,candidate.Source.Descriptor,Read(candidate.Source));}
                    catch(Exception error){if(!ResourceError(error))throw;return new HolidayKeepsakePreviewResult(HolidayKeepsakeAvailability.ResourceUnavailable,holidayId,occurrence.InstanceKey);}
                },new HolidayKeepsakePreviewResult(HolidayKeepsakeAvailability.NotEnabled,holidayId));
            }
        }
        public Task FlushAsync(){lock(sync)return worker;}
        public Task ShutdownAsync(){lock(sync){if(shutdown!=null)return shutdown;closed=true;shutdown=worker.ContinueWith(delegate{Changed=null;},TaskScheduler.Default);return shutdown;}}
        static bool ResourceError(Exception error)
        {return error is InvalidDataException||error is IOException||error is UnauthorizedAccessException||error is ArgumentException||error is InvalidOperationException||error is NotSupportedException||error is System.Security.SecurityException;}
    }
}
