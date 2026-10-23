using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu
{
    public enum ConversationStage { Idle, Recording, Recognizing, Thinking, Speaking }
    public sealed partial class ConversationSession : IDisposable
    {
        string inputDraft="";
        internal string InputDraft {get{return inputDraft;}set{value=value??"";if(inputDraft==value)return;inputDraft=value;var changed=InputDraftChanged;if(changed!=null)changed();}}
        internal event Action InputDraftChanged;
        private readonly IConversationApi api;
        private readonly IReplySpeech speech;
        private readonly Func<IAudioRecorder> microphone;
        private readonly HistoryStore store;
        private readonly AgentTaskStore taskStore;
        private readonly Dictionary<string,AgentTaskState> agentTasks=new Dictionary<string,AgentTaskState>();
        internal Func<string,string,IList<AgentGuidance>> GuidanceReader;
        internal Action<string,IList<AgentGuidance>> GuidanceApplied;
        internal string ActiveAgentWorkspace{get{lock(gate)return active!=null && active.Snapshot.AgentEnabled?active.Snapshot.AgentWorkspace:null;}}
        public event Action AgentTaskChanged;
        public IAgentApprovalProvider ApprovalProvider {get;set;}
        internal AgentPermissionSelection PermissionOverride;
        public McpConnectionManager McpConnections {get;set;}
        public bool NeedsExecutionReconciliation {get{lock(gate){AgentTaskState state;return agentTasks.TryGetValue(personaId,out state)&&state.Status=="needs_reconciliation";}}}
        public string AgentTaskWarning {get{return taskStore==null?"":taskStore.Warning;}}
        public AgentTaskState CurrentAgentTask {get{lock(gate){AgentTaskState task;return agentTasks.TryGetValue(personaId,out task)?task.Copy():null;}}}
        private readonly Func<bool> saveEnabled;
        private readonly object gate=new object();
        internal object SynchronizationRoot { get { return gate; } }
        private Operation active;
        private bool disposed;private AgentSessionModel kernelSession;private AgentSessionStore kernelStore;private string kernelWarning="";private bool kernelReadFailed;
        private readonly Dictionary<string,ConversationLog> histories=new Dictionary<string,ConversationLog>();
        private string personaId="";
        public ConversationLog History { get; private set; }
        public ConversationStage Stage { get; private set; }
        string runtimeAction="idle";
        public string RuntimeAction {get{return runtimeAction;}private set{runtimeAction=value;}}
        public int TerminalRevision {get;private set;}
        public bool IsBusy { get { lock(gate) return active!=null; } }
        public Task WaitForIdleAsync(){lock(gate)return Task.WhenAll(active==null?Task.FromResult(true):active.Complete.Task,imageRepairTail);}
        public string PersonaId { get { lock(gate)return personaId; } }
        public string LastNotice { get; private set; }
        string imageHistoryWarning="";
        public string HistoryWarning { get { return String.Join(" ",new[]{store==null?"":store.Warning,kernelWarning,imageHistoryWarning}.Where(s=>!String.IsNullOrEmpty(s))); } }
        public event Action StateChanged;
        public event Action<ChatMessage> MessageAdded;
        public event Action<ChatMessage> MessageUpdated;
        public event Action HistoryChanged;
        private sealed class Operation
        {
            public readonly CancellationTokenSource Cancel=new CancellationTokenSource();
            public readonly TaskCompletionSource<bool> Complete=new TaskCompletionSource<bool>();
            public PetSettings Snapshot,Live; public IAudioRecorder Recorder; public bool Finishing,SuppressSpeech; public int Ended;public string Outcome="";
            public Action<string> RecordingFailure;public Action AgentStarted;
            public ConversationLog History;public string PersonaId,ReplyId,GuidanceId,Source="text";public IList<ChatMessage> SteeringContext;
            public readonly StringBuilder Partial=new StringBuilder();public readonly Stopwatch Checkpoint=Stopwatch.StartNew();
            public readonly StringBuilder Reasoning=new StringBuilder();public readonly Stopwatch ThinkingTime=Stopwatch.StartNew();public readonly Stopwatch ProcessingTime=new Stopwatch();public List<ChatAttachment> Attachments;
            public AgentWorkspace Workspace;public AgentTaskState ResumeTask;
            public readonly List<AgentToolEvent> ToolEvents=new List<AgentToolEvent>();public bool Regenerating;
            public readonly HashSet<string> PresentedGuidance=new HashSet<string>();
        }
        public ConversationSession(IConversationApi transport,IReplySpeech output,Func<IAudioRecorder> recorderFactory,HistoryStore historyStore=null,Func<bool> persistenceEnabled=null,string imageConversationId=null)
        { ImageConversationId=String.IsNullOrWhiteSpace(imageConversationId)?CreateImageConversationId(historyStore):imageConversationId;api=transport;speech=output;microphone=recorderFactory;store=historyStore;taskStore=store==null?null:new AgentTaskStore(System.IO.Path.Combine(store.DirectoryPath,"agent-tasks"));if(taskStore!=null)taskStore.WarningChanged+=OnWarningChanged;saveEnabled=persistenceEnabled??delegate{return true;};History=new ConversationLog();histories[""]=History;LastNotice="";if(store!=null)store.WarningChanged+=OnWarningChanged; }
        private void OnWarningChanged() { if(!disposed && StateChanged!=null)StateChanged(); }
        private ConversationLog GetHistory(string id)
        {
            ConversationLog history;
            lock(gate)if(histories.TryGetValue(id,out history))return history;
            // Storage completion can publish warnings back into the session/queue.
            // Never wait for that worker while holding the session gate.
            var loaded=new ConversationLog();loaded.ConfigureImageUsage(imageUsage);if(store!=null)loaded.Replace(store.Load(id));
            lock(gate){if(histories.TryGetValue(id,out history)){loaded.ReleaseImageUsage();return history;}histories[id]=loaded;return loaded;}
        }
        private void Save(string id,ConversationLog history)
        {
            if(store==null||!saveEnabled())return;
            try{PromoteImageTasksAsync(history,CancellationToken.None).GetAwaiter().GetResult();history.PersistGeneratedImages(ImageStore);imageHistoryWarning="";store.SaveAsync(id,history.Messages).ContinueWith(t=>{if(t.Status==TaskStatus.RanToCompletion&&String.IsNullOrEmpty(store.Warning))history.ReconcileImageUsage();});}
            catch(Exception error){if(!(error is System.IO.IOException||error is UnauthorizedAccessException||error is InvalidOperationException))throw;imageHistoryWarning=TextResources.Get("conversationSession.save.imageHistoryWarning");OnWarningChanged();}
        }
        public AgentSessionModel KernelSession{get{return kernelSession;}}
        internal void ConfigureKernelSession(AgentSessionModel model,AgentSessionStore store){kernelSession=model;kernelStore=store;}
        internal void ReportKernelWarning(string warning){kernelWarning=warning??"";kernelReadFailed=true;}
        private void FinishOperation(Operation op)
        {
            try
            {
                if(kernelStore==null || kernelSession==null)return;
                try{if(saveEnabled() && op.Snapshot.SaveChatHistory){if(kernelReadFailed)return;kernelStore.Save(kernelSession);}else{kernelStore.Clear();kernelReadFailed=false;}kernelWarning="";}
                catch(Exception e){if(!AgentSessionStore.StorageError(e))throw;kernelWarning=TextResources.Get("conversationSession.finishOperation.kernelWarning");}
            }
            finally{End(op);}
        }
        public void SelectPersona(string id)
        {
            id=id??"";
            lock(gate){if(active!=null)throw new InvalidOperationException(TextResources.Get("conversationSession.selectPersona.text"));if(disposed)throw new ObjectDisposedException("ConversationSession");}
            var selected=GetHistory(id);
            lock(gate)
            {
                if(active!=null) throw new InvalidOperationException(TextResources.Get("conversationSession.selectPersona.text"));
                if(disposed)throw new ObjectDisposedException("ConversationSession");History=selected;personaId=id;CancelPredictions();
            }
            if(HistoryChanged!=null)HistoryChanged();
        }
        public void AddLiveTurn(string id,string user,string reply,string status="complete")
        {
            id=id??"";var history=GetHistory(id);
            lock(gate) {
                if(disposed || (String.IsNullOrWhiteSpace(reply) && (status=="complete" || String.IsNullOrWhiteSpace(user))))return;
                if(status!="complete" && status!="interrupted" && status!="cancelled" && status!="error")status="interrupted";
                ChatMessage input;var output=history.BeginTurn(String.IsNullOrWhiteSpace(user)?TextResources.Get("conversationSession.addLiveTurn.historyBeginTurn"):user,"live",out input);output=history.Update(output.Id,reply,status);Save(id,history);
                if(id==personaId){Emit(input);if(output!=null)Emit(output);}
            }
        }
        private Operation Begin(PetSettings settings,ConversationStage stage)
        {
            Operation op;
            lock(gate)
            {
                if(disposed) throw new ObjectDisposedException("ConversationSession");
                if(active!=null) throw new InvalidOperationException(TextResources.Get("conversationSession.begin.text"));
                op=new Operation { Snapshot=settings.Copy(),Live=settings,History=History,PersonaId=personaId }; op.Snapshot.Text.RequestContextCapacity=ModelContextCapacity.Budget(op.Snapshot.Text,op.Snapshot.AgentContextTokenLimit);active=op; LastNotice="";CancelPredictions();
            }
            SetStage(stage); return op;
        }
        private void SetStage(ConversationStage stage)
        { Stage=stage;if(stage!=ConversationStage.Idle)RuntimeAction=stage==ConversationStage.Recording?"waiting":stage==ConversationStage.Recognizing?"review":stage==ConversationStage.Speaking?"waving":"running"; if(!disposed && StateChanged!=null) StateChanged(); }
        void SetRuntimeAction(Operation op,string action)
        {if(disposed || active!=op || op.Cancel.IsCancellationRequested || RuntimeAction==action)return;RuntimeAction=action;if(StateChanged!=null)StateChanged();}
        private void Emit(ChatMessage message)
        { if(!disposed && MessageAdded!=null) MessageAdded(message.Copy()); }
        internal void RegisterGuidance(string persona,string id,string text,IList<ChatAttachment> files)
        {
            var history=GetHistory(persona);lock(gate){if(disposed)return;history.RegisterGuidance(id,text,files);if(active!=null&&active.PersonaId==persona&&active.History==history&&active.ReplyId!=null&&Stage==ConversationStage.Thinking&&!active.Cancel.IsCancellationRequested)PresentGuidance(active,id,text,files);Save(persona,history);if(persona==personaId && HistoryChanged!=null)HistoryChanged();}
        }
        void PresentGuidance(Operation op,string id,string text,IList<ChatAttachment> files)
        {
            if(!op.PresentedGuidance.Add(id))return;
            var previous=op.History.Messages.FirstOrDefault(m=>m.Id==op.ReplyId);
            // Presentation follows the latest user input; the operation, timer,
            // cancellation token and in-flight tool continue unchanged.
            op.History.Update(op.ReplyId,op.Partial.ToString(),"complete",op.Reasoning.ToString(),op.ThinkingTime.ElapsedMilliseconds,op.ToolEvents.Where(e=>e.Status!="running"&&e.Status!="pending"),op.ProcessingTime.ElapsedMilliseconds);
            op.History.RemoveEmptyReply(op.ReplyId);
            op.Partial.Clear();op.Reasoning.Clear();op.ToolEvents.RemoveAll(e=>e.Status!="running"&&e.Status!="pending");
            ChatMessage user;var reply=op.History.BeginGuidance(id,text,out user);op.History.Attach(user.Id,files);op.ReplyId=reply.Id;
            op.History.StartProcessing(reply.Id,op.Snapshot.AgentEnabled,previous==null?null:previous.ProcessingStartedAt,op.Snapshot.SkinId);
            if(op.Workspace!=null)op.History.LinkAgentTask(reply.Id,op.Workspace.TaskState.Id);
            op.History.Update(reply.Id,"","pending",tools:op.ToolEvents,processingMilliseconds:op.ProcessingTime.ElapsedMilliseconds);
        }
        public async Task SendTextAsync(string text,PetSettings settings,string petName,IList<ChatMessage> steeringContext=null,IList<ChatAttachment> attachments=null,AgentTaskState resumeTask=null,string kernelInputId=null,string guidanceId=null,Action onAgentStarted=null)
        {
            var files=ChatAttachment.Snapshot(attachments);if(String.IsNullOrWhiteSpace(text) && files.Count>0)text=TextResources.Get("conversationSession.sendTextAsync.text");
            if(settings!=null&&settings.Text!=null&&AgentCapabilityResolver.Resolve(settings,null).Images==CapabilitySupport.Unsupported&&files.Any(f=>f.IsImage))throw new ApiException(TextResources.Get("conversationSession.sendTextAsync.text.alternativeA"));
            text=(text??"").Trim(); if(text.Length==0 || text.Length>8000) throw new ApiException(TextResources.Get("conversationSession.sendTextAsync.text.alternativeB"));
            ApiClient.ValidateConnection(settings.Text,ApiClient.ChatPath(settings.Text));
            Operation op=Begin(settings,ConversationStage.Thinking);
            op.AgentStarted=onAgentStarted;op.SteeringContext=steeringContext;op.GuidanceId=guidanceId;if(guidanceId!=null)op.Source="guide";
            op.Attachments=files;op.ResumeTask=resumeTask==null?null:resumeTask.Copy();
            try { if(kernelSession!=null){kernelSession.Enqueue(new AgentSessionInput{IdempotencyId=String.IsNullOrWhiteSpace(kernelInputId)?Guid.NewGuid().ToString("N"):kernelInputId,Kind=resumeTask==null?"prompt":"resume",Text=text,Resume=resumeTask!=null});kernelSession.DeliverNext();kernelSession.StartStep();}await RequestReply(op,text,petName);if(kernelSession!=null)kernelSession.CompleteStep(op.ReplyId); }
            catch(OperationCanceledException) { if(kernelSession!=null)kernelSession.FailStep("cancelled"); LastNotice=TextResources.Get("conversationSession.sendTextAsync.lastNotice"); throw; }
            finally { FinishOperation(op); }
        }
        public void StartRecording(PetSettings settings)
        {
            ValidateVoice(settings); Operation op=Begin(settings,ConversationStage.Recording);
            try {
                op.Recorder=microphone();var failures=op.Recorder as IPcmMicrophone;
                if(failures!=null){op.RecordingFailure=message=>RecordingFailed(op,message);failures.Failed+=op.RecordingFailure;}
                op.Recorder.Start();
            }
            catch { End(op); throw; }
        }
        private void RecordingFailed(Operation op,string message)
        {
            lock(gate) {
                if(disposed || active!=op || op.Finishing || op.Ended!=0)return;
                op.Finishing=true;LastNotice=String.IsNullOrWhiteSpace(message)?TextResources.Get("conversationSession.recordingFailed.lastNoticeStringIsNullOrWhiteSpace"):message;
                op.Cancel.Cancel();
            }
            // A device may raise failure on its capture worker; disposing there can
            // deadlock when the recorder waits for that same worker to finish.
            Task.Run(delegate { End(op); });
        }
        private static void ValidateVoice(PetSettings settings)
        { ApiClient.ValidateConnection(settings.Text,ApiClient.ChatPath(settings.Text)); var connection=settings.TranscriptionConnection(); ApiClient.ValidateConnection(connection,ApiClient.AsrPath(connection)); }
        public async Task FinishRecordingAsync(string petName)
        {
            Operation op;
            lock(gate)
            {
                op=active;
                if(op==null || op.Recorder==null || op.Finishing) throw new InvalidOperationException(TextResources.Get("conversationSession.finishRecordingAsync.text"));
                op.Finishing=true;
            }
            try
            {
                SetStage(ConversationStage.Recognizing); byte[] wav=await op.Recorder.FinishAsync();
                op.Cancel.Token.ThrowIfCancellationRequested();
                await RecognizeAndReply(op,wav,petName);
            }
            catch(Exception)
            { if(op.Cancel.IsCancellationRequested) { LastNotice=TextResources.Get("conversationSession.finishRecordingAsync.lastNotice"); throw new OperationCanceledException(op.Cancel.Token); } throw; }
            finally { FinishOperation(op); }
        }
        public async Task SendVoiceAsync(byte[] wav,PetSettings settings,string petName)
        {
            if(!WaveData.IsWave(wav)) throw new ApiException(TextResources.Get("conversationSession.sendVoiceAsync.text"));
            ValidateVoice(settings); Operation op=Begin(settings,ConversationStage.Recognizing);
            try { await RecognizeAndReply(op,wav,petName); }
            catch(OperationCanceledException) { LastNotice=TextResources.Get("conversationSession.sendVoiceAsync.lastNotice"); throw; }
            finally { FinishOperation(op); }
        }
        private async Task RecognizeAndReply(Operation op,byte[] wav,string petName)
        {
            string text;try{text=await api.TranscribeAsync(op.Snapshot.TranscriptionConnection(),wav,op.Cancel.Token);}catch(ApiException e){throw new ApiException(TextResources.Format("conversationSession.recognizeAndReply.template",e.Message));}
            op.Cancel.Token.ThrowIfCancellationRequested();op.Source="asr";await RequestReply(op,text,petName);
        }
        private async Task RequestReply(Operation op,string text,string petName)
        {
            using(UsageTracking.BeginScope(op.Snapshot.AgentEnabled?"work":"chat",kernelSession==null?null:kernelSession.ConversationId,kernelSession==null?null:kernelSession.ProjectId))
            {
            CancellationToken token=op.Cancel.Token; token.ThrowIfCancellationRequested();
            string persona=PersonaProfiles.LocalizeDefaultPrompt(op.Snapshot.AgentEnabled?op.Snapshot.AgentSystemPrompt??PetSettings.DefaultAgentSystemPrompt:op.Snapshot.SystemPrompt??"").Replace("{pet_name}",op.Snapshot.AgentEnabled?TextResources.Get("conversationSession.requestReply.text"):petName??TextResources.Get("app.displayName"));
            if(op.Snapshot.AgentEnabled)persona+="\n若最终交付本地文件，请在最终回复使用 [文件名](<文件完整绝对路径>) 链接，路径尽量使用正斜杠，便于用户打开交付卡片。只列明确交付的文件，不把所有改动或读取的文件当作附件，不虚构文件。";
            if(!op.Snapshot.AgentEnabled)persona+="\n当前为纯对话模式，只能聊天和分析已提供的内容，没有文件、目录或其他 Agent 工具；不要声称已经操作电脑。";
            if(op.Snapshot.AgentEnabled && !String.IsNullOrWhiteSpace(op.Snapshot.AgentGoal))persona+="\n用户设置的持续目标（在当前请求范围内推进，不能声称未完成的操作成功）：\n"+op.Snapshot.AgentGoal;
            if(op.Snapshot.AgentEnabled && op.Snapshot.AgentPlanMode)persona+="\n当前为计划模式：仅分析、阅读并提出计划，不执行写入或修改文件。";
            if(op.ResumeTask!=null)persona+="\n"+op.ResumeTask.CheckpointText();
            persona=ReplyLanguagePolicy.Apply(persona);
            var request=op.Regenerating?op.History.ForRegeneration(op.ReplyId,persona):op.History.ForRequest(persona,text);
            if(!op.Regenerating)request.Last().SetAttachments(op.Attachments);
            if(op.SteeringContext!=null)request.InsertRange(request.Count-1,op.SteeringContext.Select(m=>m.Copy()));
            ConversationLog.BudgetRequest(request);
            // An interrupted guide turn is excluded from ordinary conversation
            // history. Recover its accepted inputs from the task, including media.
            if(op.ResumeTask!=null)foreach(var guide in op.ResumeTask.Guidance??new List<AgentGuidance>()){
                bool present=request.Any(m=>m.Role=="user" && m.Content==guide.Text && SameAttachments(m.Attachments,guide.Attachments));
                if(!present){var message=new ChatMessage("user",guide.Text);message.SetAttachments(guide.Attachments);request.Insert(request.Count-1,message);}
            }
            lock(gate) {
                token.ThrowIfCancellationRequested();if(disposed)throw new OperationCanceledException(token);
                op.ProcessingTime.Restart();
                if(op.Regenerating){op.History.StartProcessing(op.ReplyId,op.Snapshot.AgentEnabled,skinId:op.Snapshot.SkinId);UpdateReply(op,"","pending",false);}
                else {ChatMessage user;var reply=op.GuidanceId==null?op.History.BeginTurn(text,op.Source,out user):op.History.BeginGuidance(op.GuidanceId,text,out user);op.History.Attach(user.Id,op.Attachments);user.SetAttachments(op.Attachments);op.ReplyId=reply.Id;reply=op.History.StartProcessing(reply.Id,op.Snapshot.AgentEnabled,skinId:op.Snapshot.SkinId);if(op.GuidanceId==null){Emit(user);Emit(reply);}else if(HistoryChanged!=null)HistoryChanged();}
                Save(op.PersonaId,op.History);op.Checkpoint.Restart();op.ThinkingTime.Restart();
            }
            SetStage(ConversationStage.Thinking);string answer;
            try {
                var streaming=api as IStreamingConversationApi;
                var reasoning=api as IReasoningConversationApi;
                var agent=api as IAgentConversationApi;
                if(op.Snapshot.AgentEnabled && Environment.OSVersion.Platform==PlatformID.Win32NT && agent!=null){
                    var approval=ApprovalProvider==null?null:new AgentApprovalTracking(ApprovalProvider,action=>{lock(gate)SetRuntimeAction(op,action);});
                    op.Workspace=OpenAgentWorkspace(op.Snapshot,approval,McpConnections,text);
                    if(kernelSession==null&&(QuestionProvider!=null||ImageClient!=null))kernelSession=new AgentSessionModel(Guid.NewGuid().ToString("N"),"","local-"+Guid.NewGuid().ToString("N"));
                    op.Workspace.AttachKernelSession(kernelSession);
                    var state=op.ResumeTask??new AgentTaskState{PersonaId=op.PersonaId,Workspace=op.Workspace.Root,Goal=text};
                    if(state.PersonaId!=op.PersonaId)throw new InvalidOperationException(TextResources.Get("conversationSession.requestReply.text.alternativeA"));
                    op.History.LinkAgentTask(op.ReplyId,state.Id);op.Workspace.TaskChanged=task=>RecordAgentTask(op,task);op.Workspace.BeginTask(state);await ConfigureJobToolsAsync(op,token).ConfigureAwait(false);ConfigureInteractiveTools(op);if(op.AgentStarted!=null)op.AgentStarted();op.Workspace.TakeGuidanceAsync=ct=>ApplyGuidance(op,ct);
                }
                if(op.Workspace==null)await PreparePlainContextAsync(op,request).ConfigureAwait(false);
                answer=op.Workspace!=null?await AgentTaskRunner.RunConversationAsync(agent,new AgentRunRequest{Model=op.Snapshot.Text,Messages=request,Workspace=op.Workspace,Text=delta=>AppendDelta(op,delta),Reasoning=delta=>AppendReasoning(op,delta),Tool=item=>AppendToolEvent(op,item),Stream=op.Snapshot.Text.StreamReplies},token):reasoning!=null?await reasoning.ChatWithReasoningAsync(op.Snapshot.Text,request,delta=>AppendDelta(op,delta),delta=>AppendReasoning(op,delta),op.Snapshot.Text.StreamReplies,token):streaming!=null && op.Snapshot.Text.StreamReplies ? await streaming.ChatStreamAsync(op.Snapshot.Text,request,delta=>AppendDelta(op,delta),token) : await api.ChatAsync(op.Snapshot.Text,request,token);
                token.ThrowIfCancellationRequested();UpdateReply(op,answer,"complete",true);if(op.Workspace!=null)op.Workspace.FinishTask("complete","");
            }
            catch(Exception e) {
                if(op.Workspace!=null)op.Workspace.FinishTask(op.Workspace.NeedsReconciliation?"needs_reconciliation":token.IsCancellationRequested || e is OperationCanceledException?"cancelled":"error",e is ApiException?e.Message:TextResources.Get("conversationSession.requestReply.opWorkspaceFinishTask"));
                UpdateReply(op,op.Partial.ToString(),token.IsCancellationRequested || e is OperationCanceledException?"cancelled":"error",true);
                if(e is ApiException)throw new ApiException(TextResources.Format("conversationSession.requestReply.template",e.Message));throw;
            }
            if(!op.SuppressSpeech && op.Live.ReadReplies && op.Live.Volume>0 && !op.Live.QuietMode)
            {
                SetStage(ConversationStage.Speaking);
                try { await speech.SpeakAsync(MarkdownContent.PlainText(answer),op.Live.Copy(),token); }
                catch(OperationCanceledException) { if(token.IsCancellationRequested) throw; LastNotice=TextResources.Get("conversationSession.requestReply.lastNotice"); }
                catch(Exception e)
                { if(!(e is ApiException || e is InvalidOperationException || e is System.IO.IOException || e is UnauthorizedAccessException)) throw; LastNotice=TextResources.Get("conversationSession.requestReply.lastNotice.alternativeA")+(e is ApiException || e is InvalidOperationException ? e.Message : TextResources.Get("conversationSession.requestReply.lastNotice.alternativeB")); }
            }

            }
        }
        async Task<IList<ChatMessage>> ApplyGuidance(Operation op,CancellationToken cancel)
        {
            var reader=GuidanceReader;var acknowledge=GuidanceApplied;var result=new List<ChatMessage>();if(reader==null || acknowledge==null)return result;cancel.ThrowIfCancellationRequested();var pending=reader(op.PersonaId,op.Workspace.Root);if(pending.Count==0)return result;
            var state=op.Workspace.TaskState;if(state.Guidance==null)state.Guidance=new List<AgentGuidance>();var fresh=new List<AgentGuidance>();var accepted=new List<AgentGuidance>();
            foreach(var guide in pending){
                if(state.Guidance.Any(g=>g.Id==guide.Id && g.Revision==guide.Revision)){accepted.Add(guide);continue;}
                var message=new ChatMessage("user",guide.Text);message.SetAttachments(guide.Attachments);var proposed=result.Concat(new[]{message}).ToList();
                var candidate=state.Copy();candidate.Guidance.Add(guide.Copy());
                string blocked=!AgentTaskStore.FitsCheckpoint(candidate)?TextResources.Get("conversationSession.applyGuidance.blockedAgentTaskStoreFitsCheckpoint"):op.Workspace.GuidanceFits!=null && !op.Workspace.GuidanceFits(proposed)?TextResources.Get("conversationSession.applyGuidance.blockedAgentTaskStoreFitsCheckpoint.alternativeA"):null;
                if(blocked!=null){if(fresh.Count==0)throw new ApiException(blocked);LastNotice=blocked;break;}
                state=candidate;fresh.Add(guide);accepted.Add(guide);result.Add(message);
            }
            lock(gate){cancel.ThrowIfCancellationRequested();if(disposed || active!=op)return new List<ChatMessage>();
                foreach(var guide in fresh)PresentGuidance(op,guide.Id,guide.Text,guide.Attachments);
                if(fresh.Count>0){if(HistoryChanged!=null)HistoryChanged();Save(op.PersonaId,op.History);}
            }
            if(fresh.Count>0)op.Workspace.RecordGuidance(fresh);
            if(store!=null)await store.FlushAsync().ConfigureAwait(false);if(taskStore!=null)await taskStore.FlushAsync().ConfigureAwait(false);cancel.ThrowIfCancellationRequested();
            if(String.IsNullOrEmpty(HistoryWarning) && String.IsNullOrEmpty(AgentTaskWarning))try{acknowledge(op.PersonaId,accepted);}catch(Exception e){if(!(e is System.IO.IOException || e is UnauthorizedAccessException))throw;LastNotice=TextResources.Get("conversationSession.applyGuidance.lastNotice");}
            return result;
        }
        static bool SameAttachments(IList<ChatAttachment> a,IList<ChatAttachment> b)
        {var first=a??new List<ChatAttachment>();var second=b??new List<ChatAttachment>();return first.Count==second.Count && first.Zip(second,(x,y)=>x.Name==y.Name && x.MediaType==y.MediaType && x.Data==y.Data && x.Text==y.Text).All(equal=>equal);}
        private void AppendDelta(Operation op,string delta)
        {
            if(String.IsNullOrEmpty(delta))return;
            lock(gate) {
                if(disposed || active!=op || op.Cancel.IsCancellationRequested)return;
                if(op.Partial.Length+delta.Length>50000)throw new ApiException(TextResources.Get("conversationSession.appendDelta.text"));
                op.ThinkingTime.Stop();
                SetRuntimeAction(op,"review");
                op.Partial.Append(delta);UpdateReply(op,op.Partial.ToString(),"pending",op.Checkpoint.ElapsedMilliseconds>=1000);
            }
        }
        private void UpdateReply(Operation op,string content,string status,bool persist)
        {
            lock(gate) {
                if(disposed || active!=op || op.ReplyId==null)return;
                if(status=="complete")op.Cancel.Token.ThrowIfCancellationRequested();
                if(status!="pending")
                {
                    op.Outcome=status;
                    op.ThinkingTime.Stop();op.ProcessingTime.Stop();
                    foreach(var item in op.ToolEvents.Where(e=>e.Status=="running")){item.Status="error";item.Summary+=(status=="cancelled"?TextResources.Get("conversationSession.updateReply.itemSummary"):TextResources.Get("conversationSession.updateReply.itemSummary.alternativeA"));}
                }
                var updated=op.History.Update(op.ReplyId,content,status,op.Reasoning.ToString(),op.ThinkingTime.ElapsedMilliseconds,op.ToolEvents,op.ProcessingTime.ElapsedMilliseconds);
                if(updated!=null && op.PersonaId==personaId && MessageUpdated!=null)MessageUpdated(updated);
                if(persist){Save(op.PersonaId,op.History);op.Checkpoint.Restart();}
            }
        }
        private void AppendReasoning(Operation op,string delta)
        {
            if(String.IsNullOrEmpty(delta))return;
            lock(gate){if(disposed || active!=op || op.Cancel.IsCancellationRequested)return;SetRuntimeAction(op,"running");if(op.Reasoning.Length>50000-delta.Length){if(!op.Snapshot.AgentEnabled)throw new ApiException(TextResources.Get("conversationSession.appendReasoning.text"));op.Reasoning.Remove(0,Math.Min(op.Reasoning.Length,op.Reasoning.Length+delta.Length-50000));}op.Reasoning.Append(delta.Length>50000?delta.Substring(delta.Length-50000):delta);UpdateReply(op,op.Partial.ToString(),"pending",op.Checkpoint.ElapsedMilliseconds>=1000);}
        }
        private void AppendToolEvent(Operation op,AgentToolEvent item)
        {
            lock(gate)
            {
                if(disposed || active!=op || op.Cancel.IsCancellationRequested)return;
                SetRuntimeAction(op,item.Status=="waiting_approval"?"waiting":item.Status=="error"?"failed":item.Status=="complete"?"review":"running");
                if(item.Status=="running" && op.Partial.Length>0){if(op.Reasoning.Length+op.Partial.Length<50000)op.Reasoning.AppendLine(op.Partial.ToString());op.Partial.Clear();op.ThinkingTime.Start();}
                int index=op.ToolEvents.FindIndex(e=>e.Id==item.Id);if(index>=0)op.ToolEvents[index]=item.Copy();else{op.ToolEvents.Add(item.Copy());if(op.ToolEvents.Count>100)op.ToolEvents.RemoveAt(0);}
                UpdateReply(op,op.Partial.ToString(),"pending",op.Checkpoint.ElapsedMilliseconds>=1000);
            }
        }
        public static AgentWorkspace OpenAgentWorkspace(PetSettings settings,IAgentApprovalProvider approval=null,McpConnectionManager mcp=null,string approvalIntent=null)
        {
            string path=System.IO.Path.GetFullPath(settings.AgentWorkspace??"");
            string defaultPath=System.IO.Path.GetFullPath(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","agent-workspace"));
            if(!System.IO.Directory.Exists(path))
            {
                if(!String.Equals(path,defaultPath,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(TextResources.Get("conversationSession.openAgentWorkspace.text"));
                for(var directory=new System.IO.DirectoryInfo(path);directory!=null;directory=directory.Parent)
                    if(directory.Exists && (directory.Attributes&System.IO.FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException(TextResources.Get("conversationSession.openAgentWorkspace.text.alternativeA"));
                System.IO.Directory.CreateDirectory(path);
            }
            try{var workspace=new AgentWorkspace(path,settings.AgentApprovalMode==AgentApprovalMode.Legacy&&!settings.AgentFullAccess){ReadOnly=settings.AgentPlanMode,BackupEnabled=settings.AgentBackupFiles,ContextTokenLimit=ModelContextCapacity.Budget(settings.Text,settings.AgentContextTokenLimit),CommandTimeoutSeconds=settings.AgentCommandTimeoutSeconds};workspace.ConfigureGitHub(settings.GitHubRepository,settings.GitHub);string kernelRoot=System.IO.Path.Combine(path,".deskpet-agent-kernel");workspace.ConfigureKernel(AgentPermissionRuntime.DefaultWorkspacePolicy(),new AgentAuditLog(kernelRoot),approval,new ExecutionClaimStore(kernelRoot),null);workspace.ConfigureAgentProfile(new AgentProfileRegistry().Get(settings.AgentPlanMode?"plan":"build"));workspace.ConfigureCapabilities(AgentCapabilityResolver.Resolve(settings,null));workspace.ConfigureInstructions(settings.AgentTrustProjectInstructions,settings.AgentGlobalInstructions,settings.AgentSkillsDirectory);workspace.ConfigureAccessPolicy(settings,approvalIntent);workspace.ConfigureWorkers(()=>new ApiClient(),settings.Text);workspace.ConfigureTerminal(settings.AgentTerminalPolicy);if(mcp!=null)foreach(var transport in mcp.ConnectedTransports())workspace.ConfigureMcp(transport);workspace.ConfigureDesktopSession();return workspace;}
            catch(Exception e){if(!(e is ArgumentException || e is UnauthorizedAccessException || e is System.IO.IOException || e is PlatformNotSupportedException))throw;throw new InvalidOperationException(TextResources.Get("conversationSession.openAgentWorkspace.text.alternativeB"));}
        }
        void RecordAgentTask(Operation op,AgentTaskState task)
        {
            PublishContextUsage(op,task.EstimatedContextTokens,op.Snapshot.Text.ContextWindowTokens,task.ContextInputBudget>0?task.ContextInputBudget:AgentContextPolicy.Resolve(ModelContextCapacity.Budget(op.Snapshot.Text,op.Snapshot.AgentContextTokenLimit)).InputBudget,task.ContextStatus,task.ContextCompactions);
            lock(gate){if(disposed || active!=op)return;agentTasks[op.PersonaId]=task.Copy();op.History.UpdateAgentTaskSummary(task);if(task.Status!="running")Save(op.PersonaId,op.History);}
            if(taskStore!=null)taskStore.SaveAsync(task);var changed=AgentTaskChanged;if(changed!=null)changed();
        }
        public void LoadAgentTask(PetSettings settings)
        {
            if(taskStore==null || IsBusy)return;string id=PersonaId;string path;
            try{path=System.IO.Path.GetFullPath(settings.AgentWorkspace??"");}catch(ArgumentException){return;}
            var loaded=taskStore.Load(id,path);lock(gate){if(active!=null || personaId!=id)return;if(loaded!=null)agentTasks[id]=loaded;else agentTasks.Remove(id);}
            var changed=AgentTaskChanged;if(changed!=null)changed();
        }
        public async Task<string> RestoreAgentFileAsync(string path,PetSettings settings)
        {
            var state=CurrentAgentTask;if(state==null)throw new InvalidOperationException(TextResources.Get("conversationSession.restoreAgentFileAsync.text"));ValidateTaskProject(state,settings);var op=Begin(settings,ConversationStage.Thinking);
            try{op.Workspace=OpenAgentWorkspace(op.Snapshot);op.Workspace.AttachKernelSession(kernelSession);op.History.LinkAgentTask(op.ReplyId,state.Id);op.Workspace.TaskChanged=task=>RecordAgentTask(op,task);op.Workspace.BeginTask(state);string result=await op.Workspace.ExecuteAsync("restore_file",new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{path=path}),op.Cancel.Token);op.Workspace.FinishTask(TextResources.IsToolError(result)?"error":"complete",result);LastNotice=result;return result;}finally{End(op);}
        }
        public Task<AgentChangePreview> PreviewAgentChangeAsync(string path,PetSettings settings)
        {
            var state=CurrentAgentTask;if(state==null)throw new InvalidOperationException(TextResources.Get("conversationSession.previewAgentChangeAsync.text"));ValidateTaskProject(state,settings);var snapshot=settings.Copy();
            return Task.Run(delegate{using(var workspace=OpenAgentWorkspace(snapshot)){workspace.BeginTask(state);return workspace.PreviewChange(path,CancellationToken.None);}});
        }
        static void ValidateTaskProject(AgentTaskState task,PetSettings settings)
        {if(!String.Equals(System.IO.Path.GetFullPath(settings.AgentWorkspace).TrimEnd('\\','/'),System.IO.Path.GetFullPath(task.Workspace).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(TextResources.Get("conversationSession.validateTaskProject.text"));}
        public async Task ClearAgentTaskAsync(PetSettings settings)
        {
            string id=PersonaId;var op=Begin(settings,ConversationStage.Thinking);try{if(taskStore!=null){await taskStore.ClearAsync(id,settings.AgentWorkspace);if(taskStore.HasWarning(id,settings.AgentWorkspace))throw new InvalidOperationException(TextResources.Get("conversationSession.clearAgentTaskAsync.text"));}lock(gate)agentTasks.Remove(id);var changed=AgentTaskChanged;if(changed!=null)changed();}finally{End(op);}
        }
        public async Task RetryAgentTaskSaveAsync(){if(taskStore==null)return;await taskStore.RetryPendingAsync();if(!disposed && StateChanged!=null)StateChanged();}
        public async Task<string> GenerateTitleAsync(PetSettings settings,CancellationToken cancel)
        {
            using(UsageTracking.BeginScope("title",kernelSession==null?null:kernelSession.ConversationId,kernelSession==null?null:kernelSession.ProjectId))
            {
            if(settings==null)throw new ArgumentNullException("settings");var source=History.Messages.Where(m=>m.Role=="user"||m.Role=="assistant").Take(2).ToList();if(source.Count==0)return TextResources.Get("conversationSession.generateTitleAsync.text");
            var request=new List<ChatMessage>{new ChatMessage("system",ReplyLanguagePolicy.Apply(""))};foreach(var message in source)request.Add(new ChatMessage(message.Role,message.Content));request.Add(new ChatMessage("user","请根据上面的对话生成一个简短会话标题。最多 24 个汉字或 12 个英文单词，不加引号、标点、前缀或解释，只输出标题。"));
            string raw=await api.ChatAsync(settings.Text.Copy(),request,cancel).ConfigureAwait(false);return NormalizeTitle(raw,source.FirstOrDefault(m=>m.Role=="user"));

            }
        }
        static string NormalizeTitle(string raw,ChatMessage fallback)
        {
            string value=(raw??"").Split(new[]{"\r","\n"},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??"";
            value=value.Trim().Trim('"','\'','“','”','「','」','【','】');value=System.Text.RegularExpressions.Regex.Replace(value,"^(标题|会话标题|对话标题)\\s*[:：]\\s*","",System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            value=value.TrimEnd('：',':','，',',','。','.','！','!','？','?');if(value.Length>40)value=value.Substring(0,40).Trim();if(value.Length>0)return value;
            string fallbackText=(fallback==null?null:fallback.Content??"").Trim();if(fallbackText.Length>24)fallbackText=fallbackText.Substring(0,24).Trim();return fallbackText.Length>0?fallbackText:TextResources.Get("conversationSession.normalizeTitle.text");
        }
        public Task ResumeAgentTaskAsync(PetSettings settings,string name)
        {
            if(!settings.AgentEnabled)throw new InvalidOperationException(TextResources.Get("conversationSession.resumeAgentTaskAsync.text"));
            var state=CurrentAgentTask;if(state==null)throw new InvalidOperationException(TextResources.Get("conversationSession.resumeAgentTaskAsync.text.alternativeA"));
            if(!String.Equals(System.IO.Path.GetFullPath(settings.AgentWorkspace).TrimEnd('\\','/'),System.IO.Path.GetFullPath(state.Workspace).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException(TextResources.Get("conversationSession.resumeAgentTaskAsync.text.alternativeB"));
            string text=TextResources.Format("conversationSession.resumeAgentTaskAsync.textStateGoal",state.Goal);if(text.Length>8000)text=text.Substring(0,8000);
            return SendTextAsync(text,settings,name,resumeTask:state);
        }
        public void ReportNotice(string message){LastNotice=message??"";if(!disposed && StateChanged!=null)StateChanged();}
        public async Task SpeakReplyAsync(string id,PetSettings settings)
        {
            ChatMessage reply;lock(gate)reply=History.Messages.FirstOrDefault(m=>m.Id==id && m.Role=="assistant");
            if(reply==null || String.IsNullOrWhiteSpace(reply.Content))throw new InvalidOperationException(TextResources.Get("conversationSession.speakReplyAsync.text"));
            Operation op=Begin(settings,ConversationStage.Speaking);
            try{await speech.SpeakAsync(MarkdownContent.PlainText(reply.Content),op.Snapshot,op.Cancel.Token);}finally{End(op);}
        }
        public async Task RegenerateAsync(string id,PetSettings settings,string petName)
        {
            ApiClient.ValidateConnection(settings.Text,ApiClient.ChatPath(settings.Text));
            lock(gate)History.ForRegeneration(id,"");
            Operation op=Begin(settings,ConversationStage.Thinking);op.Regenerating=true;op.ReplyId=id;
            try{await RequestReply(op,"",petName);}catch(OperationCanceledException){LastNotice=TextResources.Get("conversationSession.regenerateAsync.lastNotice");throw;}finally{End(op);}
        }
        public async Task<bool> PreviewSpeechAsync(PetSettings settings,string name)
        {
            Operation op=Begin(settings,ConversationStage.Speaking);
            try { var voices=settings.ActiveVoiceRecommendations;await speech.SpeakAsync(voices!=null && !String.IsNullOrWhiteSpace(voices.SampleText)?voices.SampleText:TextResources.Format("conversationSession.previewSpeechAsync.speechSpeakAsync",name),op.Snapshot,op.Cancel.Token); LastNotice=TextResources.Get("conversationSession.previewSpeechAsync.lastNotice"); return true; }
            finally { FinishOperation(op); }
        }
        public void SetVolume(int volume) { lock(gate)if(active!=null)active.Live.Volume=Math.Max(0,Math.Min(100,volume));speech.SetVolume(volume); }
        public void StopSpeech() { lock(gate)if(active!=null)active.SuppressSpeech=true;speech.Stop(); }
        public async Task StopAsync()
        {
            Operation op; lock(gate){CancelPredictions();op=active;} if(op==null) { speech.Stop();await FlushHistoryAsync();return; }
            try { op.Cancel.Cancel(); } catch(ObjectDisposedException) { return; }
            speech.Stop();
            if(op.Recorder!=null && !op.Finishing)
            { await Task.Run(delegate { op.Recorder.Dispose(); }); LastNotice=TextResources.Get("conversationSession.stopAsync.lastNotice"); End(op); }
            await op.Complete.Task;
            await FlushHistoryAsync();
        }
        private void End(Operation op)
        {
            if(Interlocked.Exchange(ref op.Ended,1)!=0) return;
            if(op.Recorder!=null) {
                var failures=op.Recorder as IPcmMicrophone;
                if(failures!=null && op.RecordingFailure!=null)failures.Failed-=op.RecordingFailure;
                op.Recorder.Dispose();
            }
            lock(gate) if(Object.ReferenceEquals(active,op)){active=null;RuntimeAction=op.Outcome=="complete"?"review":op.Outcome=="error"?"failed":"idle";TerminalRevision++;}
            if(op.Workspace!=null){op.Workspace.Dispose();op.Workspace=null;}op.Cancel.Dispose(); SetStage(ConversationStage.Idle); op.Complete.TrySetResult(true);
        }
        public void Clear() { lock(gate){if(active!=null)throw new InvalidOperationException(TextResources.Get("conversationSession.clear.text"));History.Clear();contextUsage.Remove(personaId);CancelPredictions();LastNotice="";if(store!=null){var cleared=History;store.ClearAsync(personaId).ContinueWith(t=>{if(t.Status==TaskStatus.RanToCompletion&&String.IsNullOrEmpty(store.Warning))cleared.ReconcileImageUsage();});}else History.ReconcileImageUsage();if(kernelSession!=null){kernelSession.Pending.Clear();kernelSession.Delivered.Clear();kernelSession.Events.Clear();kernelSession.Step=0;kernelSession.Sequence=0;kernelSession.RecoveryState="idle";}if(kernelStore!=null)try{kernelStore.Clear();kernelWarning="";kernelReadFailed=false;}catch(Exception e){if(!AgentSessionStore.StorageError(e))throw;kernelWarning=TextResources.Get("conversationSession.clear.kernelWarning");}}if(HistoryChanged!=null)HistoryChanged(); }
        internal async Task SaveHistoryForCloseAsync()
        {
            await WaitForIdleAsync().ConfigureAwait(false);
            lock(gate)foreach(var entry in histories)Save(entry.Key,entry.Value);
            await FlushHistoryAsync().ConfigureAwait(false);
        }
        public Task FlushHistoryAsync()
        {
            lock(gate){if(active!=null && active.ReplyId!=null)Save(active.PersonaId,active.History);return Task.WhenAll(imageRepairTail,store==null?Task.FromResult(0):store.FlushAsync(),taskStore==null?Task.FromResult(0):taskStore.FlushAsync());}
        }
        public string ExportHistory()
        {
            IList<ChatMessage> messages;string id;lock(gate){messages=History.Messages;id=personaId;}
            var result=new StringBuilder();result.Append(TextResources.Get("conversationSession.exportHistory.resultAppend")).Append(id).Append("\n\n");
            foreach(var message in messages)result.Append("## ").Append(message.Role=="user"?TextResources.Get("conversationSession.exportHistory.resultAppend.alternativeA"):TextResources.Get("conversationSession.exportHistory.resultAppend.alternativeB")).Append(" · ").Append(message.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")).Append(" · ").Append(message.Status).Append(" · ").Append(message.Source).Append("\n\n").Append(message.Content).Append("\n\n");
            return result.ToString();
        }
        public void Dispose()
        {
            Operation op;lock(gate){if(disposed)return;disposed=true;CancelPredictions();op=active;if(op!=null && op.ReplyId!=null){var current=op.History.Messages.FirstOrDefault(m=>m.Id==op.ReplyId);if(current!=null && current.Status=="pending")op.History.Update(op.ReplyId,current.Content,"interrupted",processingMilliseconds:op.ProcessingTime.ElapsedMilliseconds);Save(op.PersonaId,op.History);}}
            if(store!=null)store.WarningChanged-=OnWarningChanged;
            if(op!=null)
            { try {op.Cancel.Cancel();}catch(ObjectDisposedException){} if(op.Recorder!=null) op.Recorder.Dispose(); if(!op.Finishing && Stage==ConversationStage.Recording) End(op); }
            if(taskStore!=null)taskStore.WarningChanged-=OnWarningChanged;speech.Dispose(); api.Dispose();
            Task.WhenAll(store==null?Task.FromResult(0):store.FlushAsync(),op==null?Task.FromResult(true):op.Complete.Task).ContinueWith(t=>{lock(gate)foreach(var log in histories.Values)log.ReleaseImageUsage();});
        }
    }
}
