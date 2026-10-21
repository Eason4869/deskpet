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
                    Check(restarted.Get(lockedAsset.Id)==null&&!String.IsNullOrEmpty(restarted.Warning),"locked committed quarantine does not prevent startup");
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
