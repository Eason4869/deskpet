using Avalonia.Controls;

namespace DeskPet.Desktop;

internal sealed partial class ChatWindow
{
    CompanionHomePage? companionPage;
    internal void ShowCompanionPage()=>Navigate("companion");
    Control CompanionRouteContent()
    {
        if(CurrentPage=="companion"){
            companionPage??=new CompanionHomePage(owner);owner.RecordVisit();RefreshCompanion();return companionPage;
        }
        return CurrentPage=="images"?imageWorkspace:chatPage;
    }
    async void RefreshCompanion(){await companionPage!.RefreshAsync();}
    void DisposeCompanionRoutes(){companionPage?.Dispose();}
}
