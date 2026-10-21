using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class ImageGenerationTaskManagerTests
    {
        static int passed;
        static readonly byte[] Png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;}
        static void Until(Func<bool> ready){if(!SpinWait.SpinUntil(ready,5000))throw new Exception("timed out waiting for controlled executor");}
        sealed class Controlled : IImageGenerationExecutor
        {
            public readonly List<Call> Calls=new List<Call>();public GeneratedImageStore Store;
            public Task<GeneratedImage> GenerateAsync(ImageGenerationRequest request,IProgress<ImageGenerationPhase> phase,CancellationToken token)
            {var call=new Call{Request=request,Phase=phase,Token=token};lock(Calls)Calls.Add(call);return call.Result.Task;}
            public int Count{get{lock(Calls)return Calls.Count;}}
            public Call At(int i){lock(Calls)return Calls[i];}
            public void Finish(int i){var c=At(i);c.Result.TrySetResult(Store.Save(Png,new GeneratedImage{ImageJobId=c.Request.ImageJobId,Source=c.Request.Source,ReferenceImageId=c.Request.ReferenceImageId},c.Request.Persistent));}
        }
        sealed class Call{public ImageGenerationRequest Request;public IProgress<ImageGenerationPhase> Phase;public CancellationToken Token;public TaskCompletionSource<GeneratedImage> Result=new TaskCompletionSource<GeneratedImage>();}
        sealed class Fixture : IDisposable
        {
            public string Root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"image-manager-"+Guid.NewGuid().ToString("N"));
            public GeneratedImageStore Assets;public ImageGenerationTaskStore Log;public Controlled Executor;public ImageGenerationTaskManager Manager;
            public Fixture(){Assets=new GeneratedImageStore(Root);Log=new ImageGenerationTaskStore(Root);Executor=new Controlled{Store=Assets};Manager=new ImageGenerationTaskManager(Log,Assets,Executor,new ImageAssetUsageRegistry());Manager.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();}
            public ImageGenerationTaskHandle Add(string id,string provider="a",bool persistent=true){return Manager.EnqueueAsync(Submission(id,provider,persistent),CancellationToken.None).GetAwaiter().GetResult();}
            public void Dispose(){var stop=Manager.StopAsync(CancellationToken.None);Until(delegate{for(int i=0;i<Executor.Count;i++)Executor.At(i).Result.TrySetCanceled();return stop.IsCompleted;});stop.GetAwaiter().GetResult();Assets.Dispose();Directory.Delete(Root,true);}
        }
        static ImageGenerationTaskSubmission Submission(string id,string provider,bool persistent){return new ImageGenerationTaskSubmission(id,new ImageGenerationRequest{Provider=new ImageGenerationProvider{Id=provider,TimeoutSeconds=1},Prompt="test",Persistent=persistent},new ImageTaskOrigin{Source=ImageGenerationSource.Workspace});}
        public static int Main(){try{passed=0;foreach(var test in new Action[]{DefaultSerialAndPerProviderLimit,QueueFullNeverCallsHttp,SameCallIsIdempotent,QueuedCancelNeverSendsHttp,TimeoutExcludesQueueWait,SavedResultWinsLateCancel,RestartFindsCommittedAssetWithoutHttp,RestartNeverResendsInterruptedJob,HistoryOffCreatesNoDurableJobPayload,WriteFailureAndEvents,AcceptanceAndLeases,CorruptStartupAndQueuedWriteFailure,ReferencesRetryAndPhases,OwnerAndAcceptanceCancellation,CanceledAcceptanceAndPromotionReentrancy,ClientPhasesAndIdentity,RecoveryKeepsCompletionTime,DurableTaskPromotesExistingReference}){Console.WriteLine("RUN "+test.Method.Name);test();}Console.WriteLine("ImageGenerationTaskManagerTests: "+passed+" passed");return 0;}catch(Exception e){Console.WriteLine("FAIL ImageGenerationTaskManagerTests: "+e);return 1;}}
        static void DefaultSerialAndPerProviderLimit(){using(var f=new Fixture()){var a=f.Add("1");Until(()=>f.Executor.Count==1);var b=f.Add("2");var c=f.Add("3","b");Check(f.Executor.Count==1,"default serial");f.Manager.Concurrency=4;Until(()=>f.Executor.Count==2);Check(f.Executor.At(1).Request.Provider.Id=="b","skip blocked provider fairly");f.Executor.Finish(0);a.Completion.GetAwaiter().GetResult();Until(()=>f.Executor.Count==3);f.Executor.Finish(1);f.Executor.Finish(2);Task.WaitAll(b.Completion,c.Completion);Check(f.Manager.List().All(x=>x.State==ImageGenerationTaskState.Succeeded),"all completed");f.Manager.Concurrency=9;Check(f.Manager.Concurrency==4,"clamp concurrency");}}
        static void QueueFullNeverCallsHttp(){using(var f=new Fixture()){f.Add("run");Until(()=>f.Executor.Count==1);for(int i=0;i<50;i++)f.Add("q"+i);bool refused=false;try{f.Add("overflow");}catch(InvalidOperationException){refused=true;}Check(refused && f.Executor.Count==1,"queue cap rejects before executor");}}
        static void SameCallIsIdempotent(){using(var f=new Fixture()){var a=f.Add("same");var b=f.Add("same");Check(a.Id==b.Id && Object.ReferenceEquals(a.Completion,b.Completion),"same request handle");var origin=new ImageTaskOrigin{ConversationId="c",AgentTaskId="a",ToolCallId="t"};var s=Submission("x","a",true);var x=f.Manager.EnqueueAsync(new ImageGenerationTaskSubmission("x",s.Request,origin),CancellationToken.None).Result;var y=f.Manager.EnqueueAsync(new ImageGenerationTaskSubmission("y",s.Request,origin),CancellationToken.None).Result;Check(x.Id==y.Id,"tool call deduplication");Check(f.Add("new").Id!=a.Id,"explicit new submit distinct");}}
        static void QueuedCancelNeverSendsHttp(){using(var f=new Fixture()){f.Add("run");Until(()=>f.Executor.Count==1);var q=f.Add("q");Check(f.Manager.GetQueuePosition(q.Id)==1,"queue position");f.Manager.CancelAsync(q.Id).Wait();Check(q.Completion.IsCanceled && f.Executor.Count==1,"queued cancel no executor");}}
        static void TimeoutExcludesQueueWait(){using(var f=new Fixture()){var a=f.Add("run");Until(()=>f.Executor.Count==1);var q=f.Add("q");Thread.Sleep(1200);f.Executor.Finish(0);a.Completion.Wait();Until(()=>f.Executor.Count==2);Check(!f.Executor.At(1).Token.IsCancellationRequested,"timeout starts at execution");f.Executor.Finish(1);q.Completion.Wait();}}
        static void SavedResultWinsLateCancel(){using(var f=new Fixture()){var a=f.Add("run");Until(()=>f.Executor.Count==1);var c=f.Executor.At(0);var image=f.Assets.Save(Png,new GeneratedImage{ImageJobId=a.Id},true);f.Manager.CancelAsync(a.Id).Wait();c.Result.TrySetException(new OperationCanceledException());a.Completion.GetAwaiter().GetResult();c.Phase.Report(ImageGenerationPhase.Downloading);c.Phase.Report(ImageGenerationPhase.Saving);Check(a.Completion.Result.Id==image.Id && f.Manager.Get(a.Id).State==ImageGenerationTaskState.Succeeded,"committed asset wins canceled exception");}}
        static ImageGenerationTaskRecord Record(string id){return new ImageGenerationTaskRecord{Id=id,RequestId=id,Origin=new ImageTaskOrigin(),QueuedUtc=DateTime.UtcNow,State=ImageGenerationTaskState.Generating,ProviderId="a"};}
        static void RestartFindsCommittedAssetWithoutHttp(){using(var f=new Fixture()){f.Log.Save(new[]{Record("saved")});var image=f.Assets.Save(Png,new GeneratedImage{ImageJobId="saved",TrashedUtc=DateTime.UtcNow},true);var m=new ImageGenerationTaskManager(f.Log,f.Assets,f.Executor,new ImageAssetUsageRegistry());m.RestoreAsync(CancellationToken.None).Wait();Check(m.Get("saved").ResultImageId==image.Id && m.Get("saved").State==ImageGenerationTaskState.Succeeded && f.Executor.Count==0,"recovery checks committed trash asset");}}
        static void RestartNeverResendsInterruptedJob(){using(var f=new Fixture()){f.Log.Save(new[]{Record("lost")});var m=new ImageGenerationTaskManager(f.Log,f.Assets,f.Executor,new ImageAssetUsageRegistry());m.RestoreAsync(CancellationToken.None).Wait();Check(m.Get("lost").State==ImageGenerationTaskState.Interrupted && f.Executor.Count==0,"restart never replays");}}
        static void HistoryOffCreatesNoDurableJobPayload(){using(var f=new Fixture()){var a=f.Add("private","a",false);Until(()=>f.Executor.Count==1);Check(!File.Exists(Path.Combine(f.Log.DirectoryPath,"tasks.dat")),"temporary task writes no file");f.Executor.Finish(0);a.Completion.Wait();Check(f.Log.Load().Count==0,"temporary terminal not durable");f.Manager.PromoteAsync(a.Id,CancellationToken.None).Wait();Check(f.Log.Load().Single().Persistent && !f.Assets.Get(a.Completion.Result.Id).Temporary,"explicit promotion");}}
        static void WriteFailureAndEvents(){using(var f=new Fixture()){f.Manager.Changed+=delegate{throw new Exception("view error");};var a=f.Add("ok");Until(()=>f.Executor.Count==1);f.Executor.Finish(0);a.Completion.Wait();Check(f.Executor.Count==1,"subscriber cannot replay");File.WriteAllText(Path.Combine(f.Log.DirectoryPath,"tasks.dat"),"corrupt");bool failed=false;try{f.Add("blocked");}catch(IOException){failed=true;}Check(failed && f.Executor.Count==1,"log failure prevents executor");}}
        static void AcceptanceAndLeases(){using(var f=new Fixture()){var s=Submission("accept","a",true);s.BeforeStart=delegate(string id){Check(f.Manager.Get(id)!=null && f.Executor.Count==0,"accepted callback before HTTP outside lock");throw new IOException("secret-key");};bool failed=false;try{f.Manager.EnqueueAsync(s,CancellationToken.None).GetAwaiter().GetResult();}catch(IOException){failed=true;}Check(failed && f.Executor.Count==0,"failed acceptance zero calls");var registry=new ImageAssetUsageRegistry();using(var lease=registry.Acquire("asset","owner")){Check(registry.TryAcquireDeletion("asset")==null,"lease blocks deletion");}using(var deletion=registry.TryAcquireDeletion("asset")){bool blocked=false;try{registry.Acquire("asset","owner");}catch(InvalidOperationException){blocked=true;}Check(blocked,"deletion blocks new lease");}}}

        static void CorruptStartupAndQueuedWriteFailure()
        {
            using(var f=new Fixture()){
                string path=Path.Combine(f.Log.DirectoryPath,"tasks.dat");File.WriteAllText(path,"protected corrupt");
                var m=new ImageGenerationTaskManager(f.Log,f.Assets,f.Executor,new ImageAssetUsageRegistry());m.RestoreAsync(CancellationToken.None).Wait();
                bool rejected=false;try{m.EnqueueAsync(Submission("private","a",false),CancellationToken.None).GetAwaiter().GetResult();}catch(IOException){rejected=true;}
                Check(rejected && f.Executor.Count==0 && File.ReadAllText(path)=="protected corrupt","corrupt startup blocks even temporary HTTP");
            }
            using(var f=new Fixture()){
                var a=f.Add("running");Until(()=>f.Executor.Count==1);var q=f.Add("queued");
                File.WriteAllText(Path.Combine(f.Log.DirectoryPath,"tasks.dat"),"corrupt");f.Executor.Finish(0);a.Completion.Wait();Until(()=>q.Completion.IsCompleted);
                Check(q.Completion.IsFaulted && f.Executor.Count==1 && f.Manager.Get(a.Id).State==ImageGenerationTaskState.Succeeded,"pre HTTP write failure refuses queue, saved success survives journal failure");
            }
        }
        static void ReferencesRetryAndPhases()
        {
            using(var f=new Fixture()){
                var request=Submission("ref","a",false).Request;request.ReferenceImageBytes=Png;
                var h=f.Manager.EnqueueAsync(new ImageGenerationTaskSubmission("ref",request,new ImageTaskOrigin()),CancellationToken.None).Result;Until(()=>f.Executor.Count==1);
                var call=f.Executor.At(0);var reference=f.Manager.Get(h.Id).ReferenceImageIds.Single();
                Check(f.Assets.Get(reference).IsReference && f.Assets.Get(reference).Temporary,"private reference stored before executor");
                call.Phase.Report(ImageGenerationPhase.Downloading);call.Phase.Report(ImageGenerationPhase.Generating);Check(f.Manager.Get(h.Id).State==ImageGenerationTaskState.Downloading,"phase monotonic");
                call.Result.TrySetException(new Exception("secret-key URL?key=private"));Until(()=>h.Completion.IsCompleted);
                Check(!f.Manager.Get(h.Id).Error.Contains("secret") && !f.Manager.Get(h.Id).Error.Contains("private"),"raw exceptions never persisted");
                var retry=f.Manager.RetryAsync(h.Id,new ImageGenerationProvider{Id="b",TimeoutSeconds=2},CancellationToken.None).Result;Until(()=>f.Executor.Count==2);
                Check(retry.Id!=h.Id && f.Manager.Get(retry.Id).RetryOfTaskId==h.Id && f.Executor.At(1).Request.Provider.Id=="b","manual retry new ID current provider");
                f.Manager.PromoteAsync(retry.Id,CancellationToken.None).Wait();f.Executor.Finish(1);retry.Completion.Wait();
                Check(!f.Assets.Get(reference).Temporary && !f.Assets.Get(retry.Completion.Result.Id).Temporary,"running promotion includes reference and final result");
            }
        }
        static void OwnerAndAcceptanceCancellation()
        {
            using(var f=new Fixture())using(var owner=new CancellationTokenSource()){
                var ready=new TaskCompletionSource<bool>();string id=null;var s=Submission("accept-wait","a",true);
                s.BeforeStart=delegate(string taskId){id=taskId;return ready.Task;};var accepting=f.Manager.EnqueueAsync(s,owner.Token);
                Check(id!=null && f.Executor.Count==0 && !accepting.IsCompleted,"async acceptance blocks HTTP");
                owner.Cancel();Check(f.Manager.Get(id).State==ImageGenerationTaskState.Canceled,"owner cancellation is immediate while acceptance awaits");ready.SetResult(true);var h=accepting.Result;Until(()=>h.Completion.IsCompleted);
                Check(h.Completion.IsCanceled && f.Executor.Count==0,"owner cancellation during acceptance no HTTP");
            }
        }

        static void CanceledAcceptanceAndPromotionReentrancy()
        {
            using(var f=new Fixture()){
                string id=null;var s=Submission("cancel-callback","a",true);s.BeforeStart=delegate(string accepted){id=accepted;throw new OperationCanceledException();};
                try{f.Manager.EnqueueAsync(s,CancellationToken.None).GetAwaiter().GetResult();}catch(OperationCanceledException){}
                Check(f.Manager.Get(id).State==ImageGenerationTaskState.Canceled && f.Executor.Count==0,"canceled acceptance is canceled");
                var h=f.Add("promote","a",false);Until(()=>f.Executor.Count==1);f.Executor.Finish(0);h.Completion.Wait();
                bool entered=false,returned=false;f.Assets.Changed+=delegate{entered=true;returned=f.Manager.PromoteAsync(h.Id,CancellationToken.None).Wait(2000);};
                f.Manager.PromoteAsync(h.Id,CancellationToken.None).Wait();Until(()=>entered);Until(()=>returned);Check(returned,"asset observer can reenter promotion without mutation lock deadlock");
            }
        }

        sealed class ImageHandler : HttpMessageHandler
        {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message,CancellationToken token){return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"data\":[{\"b64_json\":\""+Convert.ToBase64String(Png)+"\"}]}")});}}
        sealed class InlineProgress : IProgress<ImageGenerationPhase>
        {public readonly List<ImageGenerationPhase> Values=new List<ImageGenerationPhase>();public void Report(ImageGenerationPhase phase){Values.Add(phase);throw new Exception("observer error");}}
        static void ClientPhasesAndIdentity()
        {
            using(var f=new Fixture())using(var client=new ImageGenerationClient(f.Assets,new ImageHandler(),null)){
                var request=Submission("client","a",true).Request;request.Provider.Connection.BaseUrl="https://example.test/v1";request.Provider.Connection.Model="image-model";request.ImageJobId="client-job";request.Source=ImageGenerationSource.SettingsTest;
                var progress=new InlineProgress();var image=client.GenerateAsync(request,progress,CancellationToken.None).GetAwaiter().GetResult();
                Check(progress.Values.SequenceEqual(new[]{ImageGenerationPhase.Generating,ImageGenerationPhase.Saving}),"client reports actual base64 phases despite observer exception");
                Check(image.ImageJobId=="client-job" && image.Source==ImageGenerationSource.SettingsTest && f.Assets.FindByJobId("client-job").Id==image.Id,"client persists job identity and source");
            }
        }

        static void RecoveryKeepsCompletionTime()
        {
            using(var f=new Fixture()){
                var record=Record("old-success");record.State=ImageGenerationTaskState.Succeeded;record.CompletedUtc=DateTime.UtcNow.Date.AddDays(-5);f.Log.Save(new[]{record});f.Assets.Save(Png,new GeneratedImage{ImageJobId=record.Id},true);
                var m=new ImageGenerationTaskManager(f.Log,f.Assets,f.Executor,new ImageAssetUsageRegistry());m.RestoreAsync(CancellationToken.None).Wait();
                Check(m.Get(record.Id).CompletedUtc==record.CompletedUtc,"recovery preserves successful completion order for retention");
            }
        }

        static void DurableTaskPromotesExistingReference()
        {
            using(var f=new Fixture()){
                var reference=f.Assets.SaveReference(Png,new GeneratedImage(),false);var request=Submission("existing-ref","a",true).Request;request.ReferenceImageId=reference.Id;
                var h=f.Manager.EnqueueAsync(new ImageGenerationTaskSubmission("existing-ref",request,null),CancellationToken.None).Result;Until(()=>f.Executor.Count==1);
                Check(!f.Assets.Get(reference.Id).Temporary && f.Manager.Get(h.Id).ReferenceImageIds.Single()==reference.Id,"durable task promotes existing temporary reference before HTTP");
                f.Executor.Finish(0);h.Completion.Wait();
            }
        }
    }
}
