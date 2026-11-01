using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed partial class ChatWindow
{
    private async Task SendProactiveAsync(string text)
    {
        var binding=ProactiveBinding!;var capturedText=draft.Text;int capturedEpoch=uploadEpoch;
        await binding.SendAsync(text,ChatAttachment.Snapshot(Files),()=>Dispatcher.UIThread.Post(()=>
        {
            // Admission may finish after the person has already edited the composer.
            if(closed||draft.Text!=capturedText||uploadEpoch!=capturedEpoch)return;
            Files.Clear();uploadEpoch++;draft.Text="";RefreshAttachments();RefreshAction();
        }));
    }
}
