using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
namespace Dafeiyu.Tests
{
    public static class ImageGalleryManagementTests
    {
        static readonly byte[] Png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGP4DwQACfsD/fteaysAAAAASUVORK5CYII=");
        static int passed;
        static void Check(bool value,string name)
        {
            if(!value)throw new Exception(name);
            passed++;
            Console.WriteLine("PASS "+name);
        }
        public static int Main()
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"gallery-fixture-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                Run(root);
                OrphanReferenceCleanup(Path.Combine(root,"orphan-cleanup"));
                TaskPublishedBeforeAssetReservation(Path.Combine(root,"reservation-window"));
                Console.WriteLine("ImageGalleryManagementTests: "+passed+" passed");
                return 0;
            }
            catch(Exception e)
            {
                Console.WriteLine(e);
                return 1;
            }
            finally
            {
                string full=Path.GetFullPath(root);
                if(!full.StartsWith(Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("gallery-fixture-"))throw new Exception("unsafe fixture");
                Directory.Delete(full,true);
            }
        }
        sealed class HeldImageExecutor : IImageGenerationExecutor
        {
            public readonly System.Threading.Tasks.TaskCompletionSource<GeneratedImage> Completion=new System.Threading.Tasks.TaskCompletionSource<GeneratedImage>();
            public System.Threading.Tasks.Task<GeneratedImage> GenerateAsync(ImageGenerationRequest request,IProgress<ImageGenerationPhase> phase,CancellationToken token){return Completion.Task;}
        }
        static void TaskPublishedBeforeAssetReservation(string root)
        {
            string taskId=null;
            var usage=new ImageAssetUsageRegistry();
            using(var store=new GeneratedImageStore(root,usage))
            {
                var reference=store.SaveReference(Png,null,true);
                var executor=new HeldImageExecutor();
                var tasks=new ImageGenerationTaskManager(new ImageGenerationTaskStore(root),store,executor,usage);
                tasks.RestoreAsync(CancellationToken.None).Wait();
                var service=new ImageAssetManagementService(store,usage,new ImageAssetReferenceResolver(root,store,tasks));
                var preview=service.InspectOrphanReferencesAsync(CancellationToken.None).Result;
                Check(preview.Count==1,"reservation window starts with an unowned confirmed candidate");
                service.BeforeAssetReservations=()=>{
                    var handle=tasks.EnqueueAsync(new ImageGenerationTaskSubmission(Guid.NewGuid().ToString("N"),new ImageGenerationRequest{Provider=new ImageGenerationProvider{Id="window-fixture"},Prompt="offline",ReferenceImageId=reference.Id},new ImageTaskOrigin()),CancellationToken.None).Result;
                    taskId=handle.Id;
                    executor.Completion.TrySetException(new IOException("controlled window failure"));
                    try{handle.Completion.GetAwaiter().GetResult();}catch(IOException){}
                    Check(SpinWait.SpinUntil(()=>!usage.IsInUse(reference.Id),5000),"new task publishes input then ends before asset reservation");
                };
                Check(service.CleanupOrphanReferencesAsync(preview.Candidates.Select(i=>i.Id),CancellationToken.None).Result.DeletedIds.Contains(reference.Id),"unowned input is cleaned after window task ends");
                var log=new ConversationLog();log.ConfigureImageUsage(usage);ChatMessage user;var reply=log.BeginTurn("late","text",out user);
                bool blocked=false;try{log.AttachImageTask(reply.Id,taskId);}catch(InvalidOperationException){blocked=true;}
                Check(blocked,"task published in reservation window cannot attach after cleanup");
                tasks.StopAsync(CancellationToken.None).Wait();log.ReleaseImageUsage();
            }
            var restartedUsage=new ImageAssetUsageRegistry();
            using(var restarted=new GeneratedImageStore(root,restartedUsage))
            {
                bool blocked=false;try{using(restartedUsage.Acquire("job:"+taskId,"late-restart")){}}catch(InvalidOperationException){blocked=true;}
                Check(blocked,"reservation-window dependent job tombstone survives restart");
            }
        }
        static void OrphanReferenceCleanup(string root)
        {
            var usage=new ImageAssetUsageRegistry();
            var terminalId=Guid.NewGuid().ToString("N");
            using(var store=new GeneratedImageStore(root,usage))
            {
                var reference=store.SaveReference(Png,null,true);
                var taskStore=new ImageGenerationTaskStore(root);
                taskStore.Save(new[]{new ImageGenerationTaskRecord{Id=terminalId,RequestId=terminalId,Origin=new ImageTaskOrigin(),State=ImageGenerationTaskState.Failed,QueuedUtc=DateTime.UtcNow,CompletedUtc=DateTime.UtcNow,Persistent=true,ReferenceImageIds=new List<string>{reference.Id}}});
                var executor=new HeldImageExecutor();
                var tasks=new ImageGenerationTaskManager(taskStore,store,executor,usage);
                tasks.RestoreAsync(CancellationToken.None).Wait();
                var service=new ImageAssetManagementService(store,usage,new ImageAssetReferenceResolver(root,store,tasks));
                var preview=service.InspectOrphanReferencesAsync(CancellationToken.None).Result;
                Check(preview.Count==1&&preview.TotalBytes==reference.Bytes&&preview.Candidates.Single().Id==reference.Id,"retained terminal no-owner reference is discoverable with count and bytes");
                var log=new ConversationLog();log.ConfigureImageUsage(usage);ChatMessage user;var reply=log.BeginTurn("pending","text",out user);log.AttachImageTask(reply.Id,terminalId);
                Check(service.InspectOrphanReferencesAsync(CancellationToken.None).Result.Count==0,"memory pending terminal task protects private input");
                log.ReleaseImageUsage();
                var history=new HistoryStore(Path.Combine(root,"conversations","archived","history"));history.SaveAsync("persona",log.Messages).Wait();
                Check(service.CleanupOrphanReferencesAsync(new[]{reference.Id},CancellationToken.None).Result.DeletedIds.Count==0,"saved pending terminal task protects private input");history.ClearAsync("persona").Wait();
                var result=store.Save(Png,new GeneratedImage{ReferenceImageId=reference.Id},true);
                Check(service.InspectOrphanReferencesAsync(CancellationToken.None).Result.Count==0,"result protects private input");
                Check(service.CleanupOrphanReferencesAsync(new[]{result.Id},CancellationToken.None).Result.DeletedIds.Count==0&&store.Get(result.Id)!=null,"private cleanup cannot bypass result trash requirement");
                var other=store.Save(Png,new GeneratedImage{ReferenceImageId=reference.Id},true);store.MoveToTrash(new[]{result.Id});service.PurgeAsync(new[]{result.Id},CancellationToken.None).Wait();
                Check(service.InspectOrphanReferencesAsync(CancellationToken.None).Result.Count==0,"another result protects shared input after first removal");
                Check(service.CleanupOrphanReferencesAsync(preview.Candidates.Select(i=>i.Id),CancellationToken.None).Result.DeletedIds.Count==0,"stale confirmed preview cannot delete newly referenced input");
                store.MoveToTrash(new[]{other.Id});service.PurgeAsync(new[]{other.Id},CancellationToken.None).Wait();
                preview=service.InspectOrphanReferencesAsync(CancellationToken.None).Result;
                Check(preview.Count==1,"last result removal exposes private input without trash mutation");
                service.AfterReservations=()=>{bool blocked=false;try{log.AttachImageTask(reply.Id,terminalId);}catch(InvalidOperationException){blocked=true;}Check(blocked,"new pending task cannot race private input cleanup");};
                Check(service.CleanupOrphanReferencesAsync(preview.Candidates.Select(i=>i.Id),CancellationToken.None).Result.DeletedIds.Contains(reference.Id),"explicit cleanup deletes orphan private input despite retained terminal task");service.AfterReservations=null;
                bool lateRejected=false;try{log.AttachImageTask(reply.Id,terminalId);}catch(InvalidOperationException){lateRejected=true;}Check(lateRejected,"late pending task cannot reference purged private input");
                var activeReference=store.SaveReference(Png,null,true);
                var handle=tasks.EnqueueAsync(new ImageGenerationTaskSubmission(Guid.NewGuid().ToString("N"),new ImageGenerationRequest{Provider=new ImageGenerationProvider{Id="offline-fixture"},Prompt="fixture",ReferenceImageId=activeReference.Id},new ImageTaskOrigin()),CancellationToken.None).Result;
                Check(service.InspectOrphanReferencesAsync(CancellationToken.None).Result.Count==0,"active task protects private input");
                Check(service.CleanupOrphanReferencesAsync(new[]{activeReference.Id},CancellationToken.None).Result.DeletedIds.Count==0,"explicit cleanup rescan preserves active input");
                executor.Completion.TrySetException(new IOException("controlled offline failure"));try{handle.Completion.GetAwaiter().GetResult();}catch(IOException){}
                Check(SpinWait.SpinUntil(()=>!usage.IsInUse(activeReference.Id),5000),"terminal task releases execution ownership");
                Check(service.CleanupOrphanReferencesAsync(new[]{activeReference.Id},CancellationToken.None).Result.DeletedIds.Contains(activeReference.Id),"failed task without remaining owner permits cleanup");
                tasks.StopAsync(CancellationToken.None).Wait();
            }
            string discoveryRoot=Path.Combine(root,"restart-discovery");
            using(var saved=new GeneratedImageStore(discoveryRoot))saved.SaveReference(Png,null,true);
            using(var reopened=new GeneratedImageStore(discoveryRoot))
            {
                var cleanup=new ImageAssetManagementService(reopened,new ImageAssetUsageRegistry(),new ImageAssetReferenceResolver(discoveryRoot,reopened,null));
                var found=cleanup.InspectOrphanReferencesAsync(CancellationToken.None).Result;
                Check(found.Count==1&&reopened.List().Count==0&&reopened.ListIncludingTrash().Count==0,"hidden private inputs are discoverable after restart without task logs");
                Check(cleanup.CleanupOrphanReferencesAsync(found.Candidates.Select(i=>i.Id),CancellationToken.None).Result.DeletedIds.Count==1,"discovered restart candidate can be explicitly cleaned");
            }
            var restartedUsage=new ImageAssetUsageRegistry();
            using(var restarted=new GeneratedImageStore(root,restartedUsage))
            {
                Check(restarted.Recover()==0,"private cleanup tombstones survive restart");
                bool rejected=false;try{using(restartedUsage.Acquire("job:"+terminalId,"late")){}}catch(InvalidOperationException){rejected=true;}
                Check(rejected,"private input dependent job tombstones survive restart");
            }
        }
        static void Run(string root)
        {
            using(var store=new GeneratedImageStore(root))
            {
                var usage=new ImageAssetUsageRegistry();
                var manager=new ImageAssetManagementService(store,usage,new ImageAssetReferenceResolver(root,store,null));
                var a=store.Save(Png,new GeneratedImage
                {
                    ImageJobId="old-job"
                }
                ,true);
                Check(!a.IsFavorite&&!a.TrashedUtc.HasValue&&store.List().Count==1,"OldAssetsDefaultUnfavoritedAndVisible");
                store.SetFavorite(new[]
                {
                    a.Id
                }
                ,true);
                store.MoveToTrash(new[]
                {
                    a.Id
                }
                );
                Check(store.List().Count==0&&store.Read(a.Id).Length>0&&store.Get(a.Id).IsFavorite,"TrashKeepsChatReadable");
                store.Restore(new[]
                {
                    a.Id
                }
                );
                Check(store.List().Count==1,"restore");
                store.MoveToTrash(new[]
                {
                    a.Id
                }
                );
                var log=new ConversationLog();
                log.ConfigureImageUsage(usage);
                ChatMessage user;
                var reply=log.BeginTurn("x","text",out user);
                log.AttachGeneratedImage(reply.Id,a);
                Check(!manager.PurgeAsync(new[]
                {
                    a.Id
                }
                ,CancellationToken.None).Result.DeletedIds.Contains(a.Id),"ArchivedAndUnsavedMessagesProtectAssets");
                log.ReleaseImageUsage();
                var h=new HistoryStore(Path.Combine(root,"conversations","archived","history"));
                var pending=new ConversationLog();
                reply=pending.BeginTurn("x","text",out user);
                pending.AttachImageTask(reply.Id,"old-job");
                h.SaveAsync("p",pending.Messages).Wait();
                Check(!manager.PurgeAsync(new[]
                {
                    a.Id
                }
                ,CancellationToken.None).Result.DeletedIds.Contains(a.Id),"pending task IDs protect pruned task records");
                h.ClearAsync("p").Wait();
                var legacy=new HistoryStore(Path.Combine(root,"projects","archived-project","chats","unopened","history"));
                legacy.SaveAsync("p",pending.Messages).Wait();
                Check(manager.PurgeAsync(new[]
                {
                    a.Id
                }
                ,CancellationToken.None).Result.DeletedIds.Count==0,"legacy archived unopened history protects pending result");
                legacy.ClearAsync("p").Wait();
                var standalone=new HistoryStore(Path.Combine(root,"history"));
                standalone.SaveAsync("p",pending.Messages).Wait();
                Check(manager.PurgeAsync(new[]
                {
                    a.Id
                }
                ,CancellationToken.None).Result.DeletedIds.Count==0,"standalone history protects pending result");
                standalone.ClearAsync("p").Wait();
                using(var reservation=usage.TryAcquireDeletion(a.Id))
                {
                    bool rejected=false;
                    try
                    {
                        log.AttachGeneratedImage(log.BeginTurn("x","text",out user).Id,a);
                    }
                    catch(InvalidOperationException)
                    {
                        rejected=true;
                    }
                    Check(rejected,"NewReferenceCannotRacePurge");
                }
                File.WriteAllText(Path.Combine(h.DirectoryPath,"broken.history"),"broken");
                Check(manager.PurgeAsync(new[]
                {
                    a.Id
                }
                ,CancellationToken.None).Result.DeletedIds.Count==0,"UnreadableHistoryBlocksPurge");
                File.Delete(Path.Combine(h.DirectoryPath,"broken.history"));
                bool notifiedOutsideReservation=false;
                Action changed=()=>
                {
                    using(var reservation=usage.TryAcquireDeletion(a.Id))notifiedOutsideReservation=reservation!=null;
                    manager.InspectReferencesAsync(new[]
                    {
                        a.Id
                    }
                    ,CancellationToken.None).GetAwaiter().GetResult();
                };
                store.Changed+=changed;
                manager.AfterReservations=()=>
                {
                    bool blocked=false;
                    try
                    {
                        store.Save(Png,new GeneratedImage
                        {
                            ReferenceImageId=a.Id
                        }
                        ,true);
                    }
                    catch(InvalidOperationException)
                    {
                        blocked=true;
                    }
                    Check(blocked,"new asset reference cannot race exclusive purge");
                };
                Check(manager.PurgeAsync(new[]
                {
                    a.Id
                }
                ,CancellationToken.None).Result.DeletedIds.Contains(a.Id),"unreferenced trash purged");
                manager.AfterReservations=null;
                store.Changed-=changed;
                Check(notifiedOutsideReservation,"purge notification runs outside deletion reservation and store lock");
                bool stale=false;
                try
                {
                    using(usage.Acquire(a.Id,"late-reference"))
                    {
                    }
                }
                catch(InvalidOperationException)
                {
                    stale=true;
                }
                Check(stale,"deleted ID rejects stale new references");
                stale=false;
                try
                {
                    log.AttachImageTask(log.BeginTurn("late","text",out user).Id,"old-job");
                }
                catch(InvalidOperationException)
                {
                    stale=true;
                }
                Check(stale,"deleted result rejects stale pending-task references");
                string blocker=Path.Combine(root,"blocked-save");
                File.WriteAllText(blocker,"blocked");
                var failedHistory=new HistoryStore(Path.Combine(blocker,"history"))
                {
                    ImageUsage=usage
                };
                var failedLog=new ConversationLog();
                reply=failedLog.BeginTurn("x","text",out user);
                var held=store.Save(Png,null,true);
                failedLog.AttachGeneratedImage(reply.Id,held);
                failedHistory.SaveAsync("p",failedLog.Messages).Wait();
                Check(!String.IsNullOrEmpty(failedHistory.Warning)&&usage.IsInUse(held.Id),"failed history write keeps pending-save lease");
                File.Delete(blocker);
                failedHistory.SaveAsync("p",new List<ChatMessage>()).Wait();
                Check(!usage.IsInUse(held.Id),"successful replacement releases failed-save lease");
                var reference=store.SaveReference(Png,null,true);
                var b=store.Save(Png,new GeneratedImage
                {
                    ReferenceImageId=reference.Id
                }
                ,true);
                var c=store.Save(Png,new GeneratedImage
                {
                    ReferenceImageId=reference.Id
                }
                ,true);
                store.MoveToTrash(new[]
                {
                    b.Id,reference.Id
                }
                );
                var result=manager.PurgeAsync(new[]
                {
                    b.Id,reference.Id
                }
                ,CancellationToken.None).Result;
                Check(result.DeletedIds.Contains(b.Id)&&!result.DeletedIds.Contains(reference.Id)&&store.Read(reference.Id).Length>0,"SharedReferenceSurvivesOtherResultPurge");
            }
            string lockedRoot=Path.Combine(root,"locked-quarantine");
            using(var originalStore=new GeneratedImageStore(lockedRoot))
            {
                var lockedAsset=originalStore.Save(Png,null,true);
                originalStore.MoveToTrash(new[]{lockedAsset.Id});
                originalStore.ManagementFault=stage=>{if(stage=="Cleanup")throw new IOException("cleanup crash");};
                var cleanupManager=new ImageAssetManagementService(originalStore,new ImageAssetUsageRegistry(),new ImageAssetReferenceResolver(lockedRoot,originalStore,null));
                Check(cleanupManager.PurgeAsync(new[]{lockedAsset.Id},CancellationToken.None).Result.CleanupPendingIds.Contains(lockedAsset.Id),"committed purge reports cleanup pending");
                string held=Directory.GetFiles(Directory.GetDirectories(Path.Combine(lockedRoot,"generated-images"),"purge-*").Single(),"*.png").Single();
                using(var heldOpen=new FileStream(held,FileMode.Open,FileAccess.Read,FileShare.None))
                using(var restarted=new GeneratedImageStore(lockedRoot))
                {
                    Check(restarted.Get(lockedAsset.Id)==null&&heldOpen.ReadByte()==Png[0],"open committed quarantine does not prevent startup or resurrect a purged asset");
                    // A deny-delete share blocks Windows cleanup; Unix unlinks the open file immediately.
                    bool deletionBlocked=Environment.OSVersion.Platform==PlatformID.Win32NT;
                    Check(File.Exists(held)==deletionBlocked&&!String.IsNullOrEmpty(restarted.Warning)==deletionBlocked,"quarantine cleanup warning follows native open-file deletion semantics");
                }
                using(var restarted=new GeneratedImageStore(lockedRoot))Check(!File.Exists(held)&&restarted.Get(lockedAsset.Id)==null,"later restart completes locked quarantine cleanup");
            }
            foreach(bool isReference in new[]
            {
                false,true
            }
            ) foreach(bool temporary in new[]
            {
                false,true
            }
            ) foreach(bool crash in new[]
            {
                false,true
            }
            ) foreach(string point in new[]
            {
                "Prepared","Quarantined","IndexCommitted","Cleanup"
            }
            )
            {
                string folder=Path.Combine(root,point+temporary+crash+isReference);
                string id,original;
                var first=new GeneratedImageStore(folder);
                var asset=isReference?first.SaveReference(Png,null,!temporary):first.Save(Png,null,!temporary);
                id=asset.Id;
                original=first.PathFor(id);
                first.MoveToTrash(new[]
                {
                    id
                }
                );
                first.ManagementSimulateCrash=crash;
                first.ManagementFault=stage=>
                {
                    if(stage==point)throw new IOException("fault");
                };
                var manager=new ImageAssetManagementService(first,new ImageAssetUsageRegistry(),new ImageAssetReferenceResolver(folder,first,null));
                manager.PurgeAsync(new[]
                {
                    id
                }
                ,CancellationToken.None).Wait();
                if(!crash)first.Dispose();
                using(var store=new GeneratedImageStore(folder))
                {
                    bool committed=point=="IndexCommitted"||point=="Cleanup";
                    if(committed)File.WriteAllBytes(Path.Combine(folder,"generated-images",id+".png"),Png);
                    store.Recover();
                    Check(temporary?store.Get(id)==null:(store.Get(id)==null)==committed,"DeleteRollbackAndRestartNeverResurrectPurgedFiles "+point+" temporary="+temporary+" crash="+crash);
                    if(!temporary&&!committed)Check(store.Read(id).Length>0,"reference/result rollback remains readable");
                    if(temporary)Check(File.Exists(original)==(!committed&&crash),"temporary bytes rollback/commit "+point+" crash="+crash);
                    Check(!Directory.GetDirectories(Path.Combine(folder,"generated-images"),"purge-*").Any(d=>Directory.GetFiles(d,"*.png").Length>0),"restart finishes quarantine "+point);
                }
                first.Dispose();
            }
        }
    }
}
