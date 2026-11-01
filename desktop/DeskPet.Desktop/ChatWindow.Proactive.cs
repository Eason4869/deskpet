using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed partial class ChatWindow
{
    private bool proactiveAdmissionPending;
    private PetSkin ChatCompanion=>ProactiveBinding?.CompanionSkin??owner.Skin;
    private void RefreshProactiveComposer(){if(closed)return;var text=ProactiveBinding!.Draft;if(draft.Text!=text)draft.Text=text;RefreshAttachments();RefreshAction();}
    private async Task SendProactiveAsync(string text)
    {
        if(proactiveAdmissionPending)return;proactiveAdmissionPending=true;
        var binding=ProactiveBinding!;var submitted=binding.CaptureComposer();
        try{await binding.SendAsync(text,submitted.Attachments,()=>Dispatcher.UIThread.Post(()=>
        {
            proactiveAdmissionPending=false;
            // Every quick/full view contributes to the same text/attachment revision.
            binding.ClearSubmittedComposer(submitted);
        }));}finally{proactiveAdmissionPending=false;}
    }
    private void EnqueueProactive(string text)
    {
        var binding=ProactiveBinding!;var submitted=binding.CaptureComposer();
        try{binding.Queue.Enqueue(text,submitted.Attachments);uploadEpoch++;binding.ClearSubmittedComposer(submitted);}
        catch(Exception error){owner.Error(error);}
    }
}
