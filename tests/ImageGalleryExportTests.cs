using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Dafeiyu.Tests
{
    public static class ImageGalleryExportTests
    {
        static int passed;
        static readonly byte[] Png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGP4DwQACfsD/fteaysAAAAASUVORK5CYII=");
        public static int Main()
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"deskpet-gallery-test-"+Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);string exports=Path.Combine(root,"exports");Directory.CreateDirectory(exports);
                using(var store=new GeneratedImageStore(root))
                {
                    var a=store.Save(Png,new GeneratedImage{Prompt="red cat",Model="alpha",ProviderId="one",Source=ImageGenerationSource.Workspace},true);
                    var b=store.Save(Png,new GeneratedImage{Prompt="blue cat",Model="beta",ProviderId="two",Source=ImageGenerationSource.Conversation},true);
                    var c=store.Save(Png,new GeneratedImage{Prompt="red dog",Model="beta",ProviderId="one",Source=ImageGenerationSource.Unknown},true);
                    store.SetFavorite(new[]{b.Id},true);store.MoveToTrash(new[]{c.Id});
                    var query=new ImageGalleryQueryService(store);
                    Check(query.Query(new ImageGalleryQuery{Keyword="cat",ProviderId="two",Model="beta",Source=ImageGenerationSource.Conversation,FavoritesOnly=true}).Items.Single().Id==b.Id,"FiltersAndOrderingCompose");
                    Check(query.Query(new ImageGalleryQuery{Trash=true}).Items.Single().Id==c.Id && query.Query(new ImageGalleryQuery{OldestFirst=true,Limit=1}).Items.Single().Id==a.Id,"TrashRestoreAndProtectedPurgeAreVisible");
                    Check(query.Query(new ImageGalleryQuery{FromUtc=a.CreatedUtc.AddSeconds(-1),ToUtc=b.CreatedUtc.AddSeconds(1),OldestFirst=true,Limit=1}).Total>=2 && query.Query(new ImageGalleryQuery{OldestFirst=true,Offset=1,Limit=1}).Items.Single().Id==b.Id,"TimeOrderingAndPagingCompose");
                    var selection=new ImageGallerySelection();selection.Toggle(a.Id);selection.SelectLoaded(new[]{a,b});Check(selection.Count==2 && selection.Contains(a.Id) && selection.Contains(b.Id),"SelectionUsesStableIds");
                    selection.Clear();selection.SelectLoaded(query.Query(new ImageGalleryQuery{Limit=1}).Items);Check(selection.Count==1,"SelectAllMeansLoadedItems");
                    var usage=new ImageAssetUsageRegistry();var service=new ImageGalleryExport(store,usage);var management=new ImageAssetManagementService(store,usage,new ImageAssetReferenceResolver(root,store,null));
                    var first=service.ExportAsync(new[]{a.Id},exports,null,CancellationToken.None).GetAwaiter().GetResult();Check(first.Completed==1 && File.ReadAllBytes(first.Items[0].Path).SequenceEqual(Png),"ExportPreservesOriginalBytes");
                    byte[] foreign={7,8,9};File.WriteAllBytes(first.Items[0].Path,foreign);var second=service.ExportAsync(new[]{a.Id},exports,null,CancellationToken.None).GetAwaiter().GetResult();Check(second.Completed==1 && second.Items[0].Path!=first.Items[0].Path && File.ReadAllBytes(first.Items[0].Path).SequenceEqual(foreign) && File.ReadAllBytes(second.Items[0].Path).SequenceEqual(Png),"CollisionNeverOverwrites");
                    using(var cancel=new CancellationTokenSource())
                    {var result=service.ExportAsync(new[]{a.Id,b.Id},exports,new SynchronousProgress<ImageExportItem>(item=>cancel.Cancel()),cancel.Token).GetAwaiter().GetResult();Check(result.Canceled && result.Completed==1 && File.Exists(result.Items[0].Path) && Directory.GetFiles(exports,".deskpet-*.tmp").Length==0,"CancelKeepsFinishedExports");}
                    Check(management.PurgeAsync(new[]{a.Id},CancellationToken.None).GetAwaiter().GetResult().Blocked.ContainsKey(a.Id),"VisiblePurgeRequiresTrash");
                    store.Save(Png,new GeneratedImage{Prompt="dependent",ReferenceImageId=c.Id},true);Check(management.PurgeAsync(new[]{c.Id},CancellationToken.None).GetAwaiter().GetResult().Blocked.ContainsKey(c.Id),"ProtectedPurgeKeepsReferencedTrash");
                    store.Restore(new[]{c.Id});Check(query.Query(new ImageGalleryQuery{Trash=true}).Total==0,"RestoreLeavesTrash");
                }
                Console.WriteLine("ImageGalleryExportTests: "+passed+" passed");return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
            finally{string intended=Path.GetFullPath(root);string parent=Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;if(intended.StartsWith(parent,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(intended).StartsWith("deskpet-gallery-test-",StringComparison.Ordinal)&&Directory.Exists(intended))Directory.Delete(intended,true);}
        }
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
        sealed class SynchronousProgress<T>:IProgress<T>{readonly Action<T> action;public SynchronousProgress(Action<T> action){this.action=action;}public void Report(T value){action(value);}}
    }
}
