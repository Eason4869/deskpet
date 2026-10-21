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
        Task imageServicesShutdown=Task.FromResult(0);
        public Task ImageServicesShutdown {get{return imageServicesShutdown;}}
        ImageAssetUsageRegistry imageUsage;
        public ImageAssetUsageRegistry ImageUsage {get{return imageUsage??(imageUsage=new ImageAssetUsageRegistry());}}
        public ImageGenerationTaskManager ImageTasks {get{if(imageTasks==null){imageTasks=new ImageGenerationTaskManager(new ImageGenerationTaskStore(Store.DirectoryPath),ImageStore,ImageClient,ImageUsage);imageTasks.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();imageTasks.Concurrency=Settings.ImageGenerationConcurrency;}EnsureImageTaskNotifications();return imageTasks;}}
        ImageGenerationTaskManager notificationTasks;ImageTaskCompletionTracker completionTracker;string imageNotificationId;
        void EnsureImageTaskNotifications(){if(ReferenceEquals(notificationTasks,imageTasks))return;if(notificationTasks!=null)notificationTasks.Changed-=ImageTaskCompleted;notificationTasks=imageTasks;completionTracker=new ImageTaskCompletionTracker(imageTasks.List());imageTasks.Changed+=ImageTaskCompleted;}
        void ImageTaskCompleted(ImageGenerationTaskSnapshot task){if(completionTracker==null||!completionTracker.Observe(task))return;Dispatcher.BeginInvoke(new System.Action(delegate{if(closing||tray==null)return;imageNotificationId=task.Id;tray.BalloonTipTitle=task.State==ImageGenerationTaskState.Succeeded?"图片已生成":"图片任务已结束";tray.BalloonTipText=ImageGenerationMotion.Label(task)+"，点击查看任务。";tray.ShowBalloonTip(6000);}));}
        void OpenImageTaskNotification(object sender,System.EventArgs args){if(String.IsNullOrEmpty(imageNotificationId))return;ShowChat();if(chat!=null)chat.ShowImageTask(imageNotificationId);}
        GeneratedImageStore imageStore;ImageGenerationClient imageClient;AgentQuestionController questionController;
        public GeneratedImageStore ImageStore {get{return imageStore??(imageStore=new GeneratedImageStore(Store.DirectoryPath,ImageUsage));}}
        public ImageGenerationClient ImageClient {get{return imageClient??(imageClient=new ImageGenerationClient(ImageStore));}}
        public AgentQuestionController QuestionController {get{return questionController??(questionController=new AgentQuestionController());}}
        void ConfigureImageServices(ConversationSession session)
        {session.ImageStore=ImageStore;session.ImageClient=ImageClient;session.ImageTasks=ImageTasks;session.ImageUsage=ImageUsage;session.QuestionProvider=QuestionController;}
        internal Task RepairImageDeliveryAsync(string id) { return ImageTaskCenterState.RepairExistingAsync(ImageTasks,ImageStore,ImageUsage,ConversationSessions,Projects,Store.DirectoryPath,id,Settings.SaveChatHistory,CancellationToken.None); }
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
            var pending=new System.Collections.Generic.List<Task>();
            if(imageTasks!=null)pending.Add(Task.Run(()=>imageTasks.StopAsync(CancellationToken.None)));
            foreach(var session in ConversationSessions)pending.Add(Task.Run(()=>session.StopAsync()));
            pending.Add(CloseCallForExitAsync());if(dictation!=null)pending.Add(dictation.CancelAsync());if(inputQueue!=null)pending.Add(inputQueue.PauseAsync());if(live!=null)pending.Add(live.StopAsync());
            await Task.WhenAll(pending);if(workspaces!=null)await workspaces.SaveAllAsync();await Task.WhenAll(ConversationSessions.Select(s=>s.FlushHistoryAsync()));Store.Save(exitSettings);
        }
        Task CloseCallForExitAsync()
        {
            var completion=new TaskCompletionSource<bool>();Dispatcher.BeginInvoke(new Action(async delegate{try{if(call!=null)await call.CloseAsync();completion.TrySetResult(true);}catch(Exception error){completion.TrySetException(error);}}));return completion.Task;
        }
        void DisposeImageServices(ConversationSession[] owners)
        {if(notificationTasks!=null)notificationTasks.Changed-=ImageTaskCompleted;if(questionController!=null)questionController.Dispose();imageServicesShutdown=Task.Run(async delegate{try{await applicationExitDrain.ConfigureAwait(false);}catch(Exception){}if(imageTasks!=null)await imageTasks.StopAsync(CancellationToken.None).ConfigureAwait(false);await Task.WhenAll(owners.Select(s=>s.WaitForIdleAsync())).ConfigureAwait(false);if(imageClient!=null)imageClient.Dispose();if(imageStore!=null)imageStore.Dispose();});}
    }
}
