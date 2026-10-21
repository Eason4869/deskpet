using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public interface IImageGenerationExecutor
    {Task<GeneratedImage> GenerateAsync(ImageGenerationRequest request,IProgress<ImageGenerationPhase> phase,CancellationToken token);}

    public sealed class ImageGenerationTaskManager
    {
        readonly object gate=new object();
        readonly ImageGenerationTaskStore log;
        readonly GeneratedImageStore assets;
        readonly IImageGenerationExecutor executor;
        readonly ImageAssetUsageRegistry usage;
        readonly List<Job> jobs=new List<Job>();
        readonly Queue<ImageGenerationTaskSnapshot> notifications=new Queue<ImageGenerationTaskSnapshot>();
        bool notifying,restored,stopping,recoveryRequired;int concurrency=1,running;
        public event Action<ImageGenerationTaskSnapshot> Changed;
        public string Warning {get;private set;}
        sealed class Job
        {
            public ImageGenerationTaskRecord Record;public ImageGenerationRequest Request;
            public readonly TaskCompletionSource<GeneratedImage> Completion=new TaskCompletionSource<GeneratedImage>();
            public readonly CancellationTokenSource Cancel=new CancellationTokenSource();
            public CancellationTokenRegistration Owner;public IDisposable Lease;
            public bool Ready,Running,Prepared;
            public readonly SemaphoreSlim Commit=new SemaphoreSlim(1,1);
            public readonly TaskCompletionSource<bool> AssetsReady=new TaskCompletionSource<bool>();
            public ImageGenerationTaskHandle Handle{get{return new ImageGenerationTaskHandle(Record.Id,Completion.Task);}}
        }
        sealed class Phase : IProgress<ImageGenerationPhase>
        {readonly Action<ImageGenerationPhase> report;public Phase(Action<ImageGenerationPhase> report){this.report=report;}public void Report(ImageGenerationPhase value){report(value);}}
        public ImageGenerationTaskManager(ImageGenerationTaskStore log,GeneratedImageStore assets,IImageGenerationExecutor executor,ImageAssetUsageRegistry usage)
        {if(log==null || assets==null || executor==null || usage==null)throw new ArgumentNullException();this.log=log;this.assets=assets;this.executor=executor;this.usage=usage;Warning="";}
        public int Concurrency{get{lock(gate)return concurrency;}set{lock(gate)concurrency=Math.Max(1,Math.Min(4,value));Schedule();}}
        public ImageGenerationTaskSnapshot Get(string id){lock(gate){var j=Find(id);return j==null?null:new ImageGenerationTaskSnapshot(j.Record);}}
        public IList<ImageGenerationTaskSnapshot> List(){lock(gate)return jobs.Select(j=>new ImageGenerationTaskSnapshot(j.Record)).ToList();}
        public int GetQueuePosition(string id){lock(gate){int position=0;foreach(var j in jobs){if(j.Record.State==ImageGenerationTaskState.Queued)position++;if(j.Record.Id==id)return j.Record.State==ImageGenerationTaskState.Queued?position:0;}return 0;}}
        Job Find(string id){return jobs.Find(j=>j.Record.Id==id);}
        static bool SameCall(ImageTaskOrigin a,ImageTaskOrigin b){return a!=null && b!=null && !String.IsNullOrEmpty(a.ConversationId) && !String.IsNullOrEmpty(a.AgentTaskId) && !String.IsNullOrEmpty(a.ToolCallId) && a.ConversationId==b.ConversationId && a.AgentTaskId==b.AgentTaskId && a.ToolCallId==b.ToolCallId;}
        public Task<ImageGenerationTaskHandle> EnqueueAsync(ImageGenerationTaskSubmission submission,CancellationToken ownerToken){return EnqueueCore(submission,ownerToken,null);}
        async Task<ImageGenerationTaskHandle> EnqueueCore(ImageGenerationTaskSubmission submission,CancellationToken ownerToken,string retryOf)
        {
            if(submission==null)throw new ArgumentNullException("submission");
            var request=submission.Request;var origin=submission.Origin;var beforeStart=submission.BeforeStart;Job job;
            lock(gate){
                if(!restored)throw new InvalidOperationException("请先恢复图片任务记录。");
                if(recoveryRequired)throw new IOException("图片任务记录需要恢复，未请求图片服务。");
                var existing=jobs.Find(j=>j.Record.RequestId==submission.RequestId || (retryOf==null && SameCall(j.Record.Origin,origin)));
                if(existing!=null)return existing.Handle;
                ownerToken.ThrowIfCancellationRequested();
                if(stopping)throw new InvalidOperationException("图片任务正在停止。");
                if(jobs.Count(j=>j.Record.State==ImageGenerationTaskState.Queued)>=50)throw new InvalidOperationException("图片任务队列已满（50 项）。");
                if(request.Provider==null || String.IsNullOrWhiteSpace(request.Provider.Id))throw new InvalidOperationException("请选择图片服务。");
                request.Provider.Normalize();
                var now=DateTime.UtcNow;string id=Guid.NewGuid().ToString("N");request.ImageJobId=id;request.Source=origin.Source;
                job=new Job{Request=request,Record=new ImageGenerationTaskRecord{Id=id,RequestId=submission.RequestId,RetryOfTaskId=retryOf,Persistent=request.Persistent,Origin=origin,State=ImageGenerationTaskState.Queued,QueuedUtc=now,UpdatedUtc=now,ProviderId=request.Provider.Id,ProviderName=request.Provider.Name,Protocol=request.Provider.Protocol,Model=request.Provider.Connection.Model,Prompt=request.Prompt,Size=request.Size??request.Provider.Size,Quality=request.Quality??request.Provider.Quality,ReferenceImageIds=new List<string>(),DeliveryState=origin.Source==ImageGenerationSource.Conversation?ImageTaskDeliveryState.Pending:ImageTaskDeliveryState.NotRequired}};
                jobs.Add(job);try{Save();}catch{jobs.Remove(job);throw new IOException("图片任务记录保存失败，未请求图片服务。");}Notify(job);
            }
            var inputNotifications=new List<Action>();
            job.Owner=ownerToken.Register(delegate{CancelAsync(job.Record.Id);});
            try{
                if(job.Cancel.IsCancellationRequested){job.AssetsReady.TrySetResult(false);Release(job);return job.Handle;}
                // Asset calls and acceptance callbacks can notify views: never hold gate.
                if(request.ReferenceImageBytes!=null){
                    var reference=assets.SaveReference(request.ReferenceImageBytes,new GeneratedImage{Source=origin.Source},request.Persistent);
                    request.ReferenceImageId=reference.Id;
                }
                if(!String.IsNullOrEmpty(request.ReferenceImageId)){
                    job.Lease=usage.Acquire(request.ReferenceImageId,job.Record.Id);
                    if(request.Persistent)assets.PromoteDeferred(request.ReferenceImageId,inputNotifications);
                    if(request.ReferenceImageBytes==null)request.ReferenceImageBytes=assets.Read(request.ReferenceImageId);
                    lock(gate){job.Record.ReferenceImageIds.Add(request.ReferenceImageId);Save();}
                }
                bool canceled;lock(gate){job.Prepared=true;canceled=job.Record.IsTerminal;}
                job.AssetsReady.TrySetResult(true);
                foreach(var notify in inputNotifications)notify();inputNotifications.Clear();
                if(canceled){Release(job);return job.Handle;}
                if(beforeStart!=null)await beforeStart(job.Record.Id).ConfigureAwait(false);
                lock(gate){if(!job.Record.IsTerminal){job.Ready=true;Notify(job);}}
                Schedule();return job.Handle;
            }catch(Exception failure){
                bool canceled=failure is OperationCanceledException || ownerToken.IsCancellationRequested;
                job.AssetsReady.TrySetResult(false);
                lock(gate){if(!job.Record.IsTerminal){Terminal(job,canceled?ImageGenerationTaskState.Canceled:ImageGenerationTaskState.Failed,canceled?"图片生成已取消。":"图片任务准备失败，请检查存储和会话状态。");TrySave();Notify(job);}}
                Release(job);if(canceled)job.Completion.TrySetCanceled();else job.Completion.TrySetException(new IOException("图片任务准备失败，请检查存储和会话状态。"));throw;
            }finally{foreach(var notify in inputNotifications)notify();}
        }
        void Save(){var records=jobs.Select(j=>j.Record).ToList();if(records.Any(r=>r.Persistent))log.Save(records);}
        void TrySave(){try{Save();}catch(Exception){Warning="图片任务记录保存失败；已提交的图片不会重新请求，请检查存储。";}}
        void Terminal(Job j,ImageGenerationTaskState state,string error){j.Record.State=state;j.Record.Error=error;j.Record.CompletedUtc=DateTime.UtcNow;j.Record.UpdatedUtc=j.Record.CompletedUtc;}
        void Schedule(){Task.Run((Action)Pump);}
        void Pump()
        {
            var start=new List<Job>();var failed=new List<Job>();
            lock(gate){
                if(stopping || !restored)return;
                foreach(var j in jobs){
                    if(running>=concurrency)break;
                    if(!j.Ready || j.Record.State!=ImageGenerationTaskState.Queued || jobs.Any(x=>x.Running && x.Record.ProviderId==j.Record.ProviderId))continue;
                    j.Record.State=ImageGenerationTaskState.Generating;j.Record.StartedUtc=DateTime.UtcNow;j.Record.UpdatedUtc=j.Record.StartedUtc;
                    try{Save();}catch(Exception){Terminal(j,ImageGenerationTaskState.Failed,"图片任务记录保存失败，未请求图片服务。");failed.Add(j);Notify(j);continue;}
                    j.Running=true;running++;Notify(j);start.Add(j);
                }
            }
            foreach(var j in failed){Release(j);j.Completion.TrySetException(new IOException(j.Record.Error));}
            foreach(var j in start){var current=j;Task.Run(()=>Run(current));}
        }
        async Task Run(Job j)
        {
            GeneratedImage result=null;Exception error=null;
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(j.Cancel.Token)){
                timeout.CancelAfter(TimeSpan.FromSeconds(j.Request.Provider.TimeoutSeconds));
                try{
                    timeout.Token.ThrowIfCancellationRequested();
                    ImageGenerationRequest frozen;lock(gate)frozen=j.Request.Copy();
                    result=await executor.GenerateAsync(frozen,new Phase(p=>Report(j,p)),timeout.Token).ConfigureAwait(false);
                    // Only indexed assets count as committed; custom executors cannot invent success.
                    result=assets.FindByJobId(j.Record.Id);
                    if(result==null)throw new IOException();
                }catch(Exception e){error=e;try{result=assets.FindByJobId(j.Record.Id);}catch(Exception){}}
                // Promotion may race execution. Read the durable policy at completion,
                // promote before recording a durable result, then recheck under gate.
                var assetNotifications=new List<Action>();
                await j.Commit.WaitAsync().ConfigureAwait(false);
                try{
                bool persistent;lock(gate)persistent=j.Record.Persistent;
                if(result!=null && persistent && result.Temporary)try{result=assets.PromoteDeferred(result.Id,assetNotifications);}catch(Exception){Warning="图片已生成，但永久保存失败；请恢复存储后重新保存，勿重复生成。";}
                lock(gate){
                    j.Running=false;running--;
                    if(result!=null){j.Record.ResultImageId=result.Id;Terminal(j,ImageGenerationTaskState.Succeeded,"");}
                    else if(j.Cancel.IsCancellationRequested)Terminal(j,ImageGenerationTaskState.Canceled,"图片生成已取消。");
                    else if(error is TimeoutException || timeout.IsCancellationRequested)Terminal(j,ImageGenerationTaskState.Failed,"图片生成超时，请手动重试。");
                    else Terminal(j,ImageGenerationTaskState.Failed,"图片生成失败，请检查服务配置、网络和存储后手动重试。");
                    TrySave();Notify(j);
                }
                }finally{j.Commit.Release();foreach(var notify in assetNotifications)notify();}
            }
            Release(j);
            if(result!=null)j.Completion.TrySetResult(result.Copy());else if(j.Record.State==ImageGenerationTaskState.Canceled)j.Completion.TrySetCanceled();else j.Completion.TrySetException(new IOException(j.Record.Error));
            Schedule();
        }
        void Report(Job j,ImageGenerationPhase phase)
        {
            lock(gate){if(!j.Running || j.Record.IsTerminal)return;var state=(ImageGenerationTaskState)((int)phase+1);if(state<=j.Record.State || state>ImageGenerationTaskState.Saving)return;j.Record.State=state;j.Record.UpdatedUtc=DateTime.UtcNow;TrySave();Notify(j);}
        }
        void Release(Job j){j.Owner.Dispose();var lease=Interlocked.Exchange(ref j.Lease,null);if(lease!=null)lease.Dispose();lock(gate){if(j.Record.IsTerminal)j.Request=null;}}
        public Task<bool> CancelAsync(string id)
        {
            Job j;bool queued;
            lock(gate){j=Find(id);if(j==null || j.Record.IsTerminal)return Task.FromResult(false);queued=!j.Running;if(queued){Terminal(j,ImageGenerationTaskState.Canceled,"图片生成已取消。");TrySave();Notify(j);}}
            // Cancellation callbacks may execute arbitrary executor code.
            try{j.Cancel.Cancel();}catch(AggregateException){}
            if(queued){if(j.Prepared)Release(j);j.Completion.TrySetCanceled();Schedule();}return Task.FromResult(true);
        }
        public async Task CancelProviderAsync(string providerId){List<string> ids;lock(gate)ids=jobs.Where(j=>j.Record.ProviderId==providerId && !j.Record.IsTerminal).Select(j=>j.Record.Id).ToList();foreach(var id in ids)await CancelAsync(id).ConfigureAwait(false);}
        public async Task<ImageGenerationTaskHandle> RetryAsync(string id,ImageGenerationProvider currentProvider,CancellationToken token)
        {
            ImageGenerationTaskRecord record;lock(gate){var j=Find(id);if(j==null || !j.Record.IsTerminal || j.Record.State==ImageGenerationTaskState.Succeeded)throw new InvalidOperationException("仅可重试未成功的已结束任务。");record=j.Record.Copy();}
            var request=new ImageGenerationRequest{Provider=currentProvider,Prompt=record.Prompt,Size=record.Size,Quality=record.Quality,Persistent=record.Persistent,SessionId=record.Origin==null?null:record.Origin.KernelSessionId,TaskId=record.Origin==null?null:record.Origin.AgentTaskId,MessageId=record.Origin==null?null:record.Origin.MessageId,ReferenceImageId=record.ReferenceImageIds==null?null:record.ReferenceImageIds.FirstOrDefault()};
            return await EnqueueCore(new ImageGenerationTaskSubmission(Guid.NewGuid().ToString("N"),request,record.Origin),token,id).ConfigureAwait(false);
        }
        public Task RestoreAsync(CancellationToken token)
        {
            lock(gate){if(restored)return Task.FromResult(0);token.ThrowIfCancellationRequested();var records=log.Load();Warning=log.Warning;recoveryRequired=!String.IsNullOrEmpty(Warning);
                foreach(var record in records){var j=new Job{Record=record};j.AssetsReady.TrySetResult(true);var image=assets.FindByJobId(record.Id);if(image!=null){record.ResultImageId=image.Id;if(record.State!=ImageGenerationTaskState.Succeeded)Terminal(j,ImageGenerationTaskState.Succeeded,"");j.Completion.TrySetResult(image);}else{if(!record.IsTerminal)Terminal(j,ImageGenerationTaskState.Interrupted,"上次图片任务已中断，未自动重新请求。");j.Completion.TrySetCanceled();}jobs.Add(j);Notify(j);}
                restored=true;if(String.IsNullOrEmpty(Warning))TrySave();
            }return Task.FromResult(0);
        }
        public async Task PromoteAsync(string id,CancellationToken token)
        {
            Job job;lock(gate){job=Find(id);if(job==null)throw new InvalidOperationException("图片任务不存在。");}
            await job.AssetsReady.Task.ConfigureAwait(false);
            var assetNotifications=new List<Action>();
            await job.Commit.WaitAsync(token).ConfigureAwait(false);
            try{
                List<string> references;lock(gate){references=new List<string>(job.Record.ReferenceImageIds??new List<string>());if(!String.IsNullOrEmpty(job.Record.ResultImageId))references.Add(job.Record.ResultImageId);}
                token.ThrowIfCancellationRequested();foreach(var asset in references)assets.PromoteDeferred(asset,assetNotifications);
                var image=assets.FindByJobId(id);if(image!=null && image.Temporary)assets.PromoteDeferred(image.Id,assetNotifications);
                lock(gate){job.Record.Persistent=true;if(job.Request!=null)job.Request.Persistent=true;Save();Notify(job);}
            }finally{job.Commit.Release();foreach(var notify in assetNotifications)notify();}
        }
        public Task SetDeliveryStateAsync(string id,ImageTaskDeliveryState state)
        {lock(gate){var j=Find(id);if(j==null)throw new InvalidOperationException("图片任务不存在。");j.Record.DeliveryState=state;j.Record.DeliveryError=state==ImageTaskDeliveryState.Failed?"图片已生成，但会话同步失败。":"";j.Record.UpdatedUtc=DateTime.UtcNow;Save();Notify(j);}return Task.FromResult(0);}
        public async Task StopAsync(CancellationToken token)
        {
            List<Job> pending;lock(gate){stopping=true;pending=jobs.Where(j=>!j.Record.IsTerminal).ToList();}
            foreach(var j in pending)await CancelAsync(j.Record.Id).ConfigureAwait(false);
            foreach(var j in pending){token.ThrowIfCancellationRequested();var wait=Task.Delay(Timeout.Infinite,token);await Task.WhenAny(j.Completion.Task,wait).ConfigureAwait(false);token.ThrowIfCancellationRequested();}
        }
        // Single FIFO dispatcher; no manager, store or asset lock is held during callbacks.
        void Notify(Job j){notifications.Enqueue(new ImageGenerationTaskSnapshot(j.Record));if(notifying)return;notifying=true;Task.Run((Action)Drain);}
        void Drain(){while(true){ImageGenerationTaskSnapshot snapshot;lock(gate){if(notifications.Count==0){notifying=false;return;}snapshot=notifications.Dequeue();}var handler=Changed;if(handler!=null)foreach(Action<ImageGenerationTaskSnapshot> listener in handler.GetInvocationList())try{listener(new ImageGenerationTaskSnapshot(snapshot));}catch(Exception){}}}
    }
}
