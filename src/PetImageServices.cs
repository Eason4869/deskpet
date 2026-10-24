using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        ImageAssetManagementService imageManagement;
        public ImageAssetManagementService ImageManagement {get{return imageManagement??(imageManagement=new ImageAssetManagementService(ImageStore,ImageUsage,new ImageAssetReferenceResolver(Store.DirectoryPath,ImageStore,ImageTasks)));}}
        ImageGenerationTaskManager imageTasks;
        Task applicationExitDrain=Task.FromResult(0);
        readonly object imageRepairAdmissionGate=new object();
        readonly System.Collections.Generic.HashSet<Task> pendingImageRepairs=new System.Collections.Generic.HashSet<Task>();
        bool imageRepairAdmissionClosed;
        Task imageRepairExitDrain=Task.FromResult(0);
        ConversationSession[] imageExitOwners=new ConversationSession[0];int conversationServicesFinalized;
        Task imageServicesShutdown=Task.FromResult(0);
        public Task ImageServicesShutdown {get{return imageServicesShutdown;}}
        ImageAssetUsageRegistry imageUsage;
        public ImageAssetUsageRegistry ImageUsage {get{return imageUsage??(imageUsage=new ImageAssetUsageRegistry());}}
        public ImageGenerationTaskManager ImageTasks {get{if(imageTasks==null){imageTasks=new ImageGenerationTaskManager(new ImageGenerationTaskStore(Store.DirectoryPath),ImageStore,ImageClient,ImageUsage);imageTasks.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();imageTasks.Concurrency=Settings.ImageGenerationConcurrency;}EnsureImageTaskNotifications();return imageTasks;}}
        ImageGenerationTaskManager notificationTasks;ImageTaskCompletionTracker completionTracker;string imageNotificationId;
        void EnsureImageTaskNotifications(){if(ReferenceEquals(notificationTasks,imageTasks))return;if(notificationTasks!=null)notificationTasks.Changed-=ImageTaskCompleted;notificationTasks=imageTasks;completionTracker=new ImageTaskCompletionTracker(imageTasks.List());imageTasks.Changed+=ImageTaskCompleted;}
        void ImageTaskCompleted(ImageGenerationTaskSnapshot task){if(completionTracker==null||!completionTracker.Observe(task))return;Dispatcher.BeginInvoke(new System.Action(delegate{if(closing||tray==null)return;imageNotificationId=task.Id;tray.BalloonTipTitle=task.State==ImageGenerationTaskState.Succeeded?TextResources.Get("petImageServices.imageTaskCompleted.stateImageGenerationTaskStateSucceeded"):TextResources.Get("petImageServices.imageTaskCompleted.stateImageGenerationTaskStateSucceeded.alternativeA");tray.BalloonTipText=TextResources.Format("petImageServices.imageTaskCompleted.trayBalloonTipTextImageGenerationMotion",ImageGenerationMotion.Label(task));tray.ShowBalloonTip(6000);}));}
        void OpenImageTaskNotification(object sender,System.EventArgs args){if(String.IsNullOrEmpty(imageNotificationId))return;ShowChat();if(chat!=null)chat.ShowImageTask(imageNotificationId);}
        GeneratedImageStore imageStore;ImageGenerationClient imageClient;AgentQuestionController questionController;
        public GeneratedImageStore ImageStore {get{return imageStore??(imageStore=new GeneratedImageStore(Store.DirectoryPath,ImageUsage));}}
        public ImageGenerationClient ImageClient {get{return imageClient??(imageClient=new ImageGenerationClient(ImageStore));}}
        public AgentQuestionController QuestionController {get{return questionController??(questionController=new AgentQuestionController());}}
        void ConfigureImageServices(ConversationSession session)
        {session.ImageStore=ImageStore;session.ImageClient=ImageClient;session.ImageTasks=ImageTasks;session.ImageUsage=ImageUsage;session.QuestionProvider=QuestionController;}
        internal Task RepairImageDeliveryAsync(string id)
        {
            var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock(imageRepairAdmissionGate)
            {
                if(imageRepairAdmissionClosed||closing)throw new OperationCanceledException(TextResources.Get("petImageServices.repairImageDeliveryAsync.text"));
                pendingImageRepairs.Add(completion.Task);
            }
            // Owner resolution may wait for a close, or perform synchronous work.
            // Publish ownership first, then invoke it outside the admission lock.
            CompleteImageRepairAsync(id,completion);
            return completion.Task;
        }
        async void CompleteImageRepairAsync(string id,TaskCompletionSource<bool> completion)
        {
            try
            {
                await ImageTaskCenterState.RepairExistingAsync(ImageTasks,ImageStore,ImageUsage,ConversationSessions,Projects,Store.DirectoryPath,id,Settings.SaveChatHistory,CancellationToken.None,Workspaces).ConfigureAwait(false);
                completion.TrySetResult(true);
            }
            catch(OperationCanceledException){completion.TrySetCanceled();}
            catch(Exception error){completion.TrySetException(error);}
            finally{lock(imageRepairAdmissionGate)pendingImageRepairs.Remove(completion.Task);}
        }
        Task FreezeImageRepairAdmission()
        {
            Task[] accepted;
            lock(imageRepairAdmissionGate){imageRepairAdmissionClosed=true;accepted=pendingImageRepairs.ToArray();}
            return Task.WhenAll(accepted.Select(ObserveImageRepairAsync));
        }
        static async Task ObserveImageRepairAsync(Task repair)
        {try{await repair.ConfigureAwait(false);}catch(Exception){/* The caller owns its result; exit observes completion. */}}
        internal void ShowImageConfiguration()
        {ShowSettings(true);if(preferences!=null)preferences.SelectImageConfiguration();}
        internal void OpenImageWorkspace(GeneratedImage previous){ShowChat();if(chat!=null)chat.ShowImagesPage(previous);}
        internal void CancelQuestionsWithoutHost(System.Windows.Window closingHost)
        {
            if(questionController==null)return;
            foreach(var request in questionController.Requests)
            {
                bool visible=false;
                if(System.Windows.Application.Current!=null)foreach(System.Windows.Window window in System.Windows.Application.Current.Windows)
                {
                    if(ReferenceEquals(window,closingHost)||!window.IsVisible)continue;var main=window as ChatWindow;var small=window as QuickChatWindow;
                    if((main!=null&&main.CanAnswerQuestion(request))||(small!=null&&small.CanAnswerQuestion(request))){visible=true;break;}
                }
                if(!visible)questionController.CancelSession(request.SessionId);
            }
        }
        async Task DrainApplicationForExitAsync(PetSettings exitSettings)
        {
            var owners=ConversationSessions.ToArray();
            imageExitOwners=owners;
            try
            {
                var pending=new System.Collections.Generic.List<Task>{imageRepairExitDrain};
                if(imageTasks!=null)pending.Add(imageTasks.StopAsync(CancellationToken.None));
                foreach(var session in owners)pending.Add(Task.Run(()=>session.StopAsync()));
                pending.Add(CloseCallForExitAsync());
                if(dictation!=null)pending.Add(dictation.CancelAsync());
                if(inputQueue!=null)pending.Add(inputQueue.PauseAsync());
                if(live!=null)pending.Add(live.StopAsync());
                await Task.WhenAll(pending).ConfigureAwait(false);
                owners=owners.Concat(ConversationSessions).Distinct().ToArray();
                imageExitOwners=owners;
                if(workspaces!=null)await workspaces.SaveAllAsync().ConfigureAwait(false);
                await Task.WhenAll(owners.Select(s=>s.FlushHistoryAsync())).ConfigureAwait(false);
                await Relationships.FlushAsync().ConfigureAwait(false);
                Store.Save(exitSettings);
            }
            finally
            {
                // Session/workspace Dispose can save, promote assets, or run callbacks.
                // Keep every such finalizer in the tracked worker, including after timeout.
                // This finally runs in the tracked worker; the .NET Framework C# 5
                // compiler cannot await in finally. Keep its close join off the UI.
                try
                {
                    imageRepairExitDrain.GetAwaiter().GetResult();
                    imageExitOwners=owners.Concat(ConversationSessions).Distinct().ToArray();
                    if(workspaces!=null)workspaces.WaitForClosingAsync().GetAwaiter().GetResult();
                }
                finally{FinalizeConversationServices();}
            }
        }
        void FinalizeConversationServices()
        {
            if(Interlocked.Exchange(ref conversationServicesFinalized,1)!=0)return;
            if(workspaces!=null)workspaces.Dispose();
            if(dictation!=null)dictation.Dispose();
            if(inputQueue!=null)inputQueue.Dispose();
            if(live!=null)live.Dispose();
            if(conversation!=null)conversation.Dispose();
            Relationships.Dispose();
            if(mcpConnections!=null)mcpConnections.Dispose();
            if(questionController!=null)questionController.Dispose();
        }
        Task CloseCallForExitAsync()
        {
            var completion=new TaskCompletionSource<bool>();Dispatcher.BeginInvoke(new Action(async delegate{try{if(call!=null)await call.CloseAsync();completion.TrySetResult(true);}catch(Exception error){completion.TrySetException(error);}}));return completion.Task;
        }
        void DisposeImageServices()
        {
            if(notificationTasks!=null)notificationTasks.Changed-=ImageTaskCompleted;
            imageServicesShutdown=Task.Run(async delegate
            {
                try{await applicationExitDrain.ConfigureAwait(false);}catch(Exception){}
                await imageRepairExitDrain.ConfigureAwait(false);
                if(imageTasks!=null)await imageTasks.StopAsync(CancellationToken.None).ConfigureAwait(false);
                FinalizeConversationServices();
                await Task.WhenAll(imageExitOwners.Select(s=>s.WaitForIdleAsync())).ConfigureAwait(false);
                await Task.WhenAll(imageExitOwners.Select(s=>s.FlushHistoryAsync())).ConfigureAwait(false);
                if(imageClient!=null)imageClient.Dispose();
                if(imageStore!=null)imageStore.Dispose();
            });
        }
    }
}
