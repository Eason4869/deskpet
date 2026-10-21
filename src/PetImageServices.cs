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
        Task imageServicesShutdown=Task.FromResult(0);
        public Task ImageServicesShutdown {get{return imageServicesShutdown;}}
        ImageAssetUsageRegistry imageUsage;
        public ImageAssetUsageRegistry ImageUsage {get{return imageUsage??(imageUsage=new ImageAssetUsageRegistry());}}
        public ImageGenerationTaskManager ImageTasks {get{if(imageTasks==null){imageTasks=new ImageGenerationTaskManager(new ImageGenerationTaskStore(Store.DirectoryPath),ImageStore,ImageClient,ImageUsage);imageTasks.RestoreAsync(CancellationToken.None).GetAwaiter().GetResult();}return imageTasks;}}
        GeneratedImageStore imageStore;ImageGenerationClient imageClient;AgentQuestionController questionController;
        public GeneratedImageStore ImageStore {get{return imageStore??(imageStore=new GeneratedImageStore(Store.DirectoryPath,ImageUsage));}}
        public ImageGenerationClient ImageClient {get{return imageClient??(imageClient=new ImageGenerationClient(ImageStore));}}
        public AgentQuestionController QuestionController {get{return questionController??(questionController=new AgentQuestionController());}}
        void ConfigureImageServices(ConversationSession session)
        {session.ImageStore=ImageStore;session.ImageClient=ImageClient;session.ImageTasks=ImageTasks;session.ImageUsage=ImageUsage;session.QuestionProvider=QuestionController;}
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
        void DisposeImageServices(ConversationSession[] owners)
        {if(questionController!=null)questionController.Dispose();imageServicesShutdown=Task.Run(async delegate{if(imageTasks!=null)await imageTasks.StopAsync(CancellationToken.None).ConfigureAwait(false);await Task.WhenAll(owners.Select(s=>s.WaitForIdleAsync())).ConfigureAwait(false);if(imageClient!=null)imageClient.Dispose();if(imageStore!=null)imageStore.Dispose();});}
    }
}
