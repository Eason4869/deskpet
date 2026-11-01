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
        var binding=ProactiveBinding!;var capturedText=draft.Text;int capturedEpoch=uploadEpoch;
        try{await binding.SendAsync(text,ChatAttachment.Snapshot(Files),()=>Dispatcher.UIThread.Post(()=>
        {
            proactiveAdmissionPending=false;
            // Admission may finish after the person has already edited the composer.
            if(closed||draft.Text!=capturedText||uploadEpoch!=capturedEpoch)return;
            Files.Clear();uploadEpoch++;draft.Text="";RefreshAttachments();RefreshAction();
        }));}finally{proactiveAdmissionPending=false;}
    }
}
