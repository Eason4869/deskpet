namespace Dafeiyu {
 public sealed partial class ConversationSession {
  private ImageInputRouter imageInputRouter;
  private ImageInputRouter ImageInput {get{if(imageInputRouter==null)imageInputRouter=new ImageInputRouter(new ImageDescriptionService(api));return imageInputRouter;}}
 }
}
