using System.IO;
namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        GeneratedImageStore imageStore;ImageGenerationClient imageClient;AgentQuestionController questionController;
        public GeneratedImageStore ImageStore {get{return imageStore??(imageStore=new GeneratedImageStore(Store.DirectoryPath));}}
        public ImageGenerationClient ImageClient {get{return imageClient??(imageClient=new ImageGenerationClient(ImageStore));}}
        public AgentQuestionController QuestionController {get{return questionController??(questionController=new AgentQuestionController());}}
        void ConfigureImageServices(ConversationSession session)
        {session.ImageStore=ImageStore;session.ImageClient=ImageClient;session.QuestionProvider=QuestionController;}
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
        void DisposeImageServices()
        {if(questionController!=null)questionController.Dispose();if(imageClient!=null)imageClient.Dispose();if(imageStore!=null)imageStore.Dispose();}
    }
}
