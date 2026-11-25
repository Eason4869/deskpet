using Avalonia.Controls;

namespace DeskPet.Desktop;

internal sealed partial class ChatWindow
{
    private const string ScheduledTasksRoute = "scheduledTasks";
    CompanionHomePage? companionPage;
    private ScheduledTasksPage? scheduledTasksPage;
    internal void ShowCompanionPage()=>Navigate("companion");
    private void ShowScheduledTemplates()
    {
        Navigate(ScheduledTasksRoute);
        scheduledTasksPage!.ShowTemplates();
    }
    internal void ShowScheduledTask(string taskId, string runId)
    {
        Navigate(ScheduledTasksRoute);
        scheduledTasksPage!.ShowTask(taskId, runId);
    }
    Control CompanionRouteContent()
    {
        if(CurrentPage=="companion"){
            companionPage??=new CompanionHomePage(owner);owner.RecordVisit();RefreshCompanion();return companionPage;
        }
        if (CurrentPage == ScheduledTasksRoute)
        {
            if (scheduledTasksPage == null)
            {
                scheduledTasksPage = new ScheduledTasksPage(owner.ScheduledTasks);
                scheduledTasksPage.ShowMissedRequested += owner.ShowScheduledMissed;
            }
            scheduledTasksPage.ShowTemplates();
            return scheduledTasksPage;
        }
        return CurrentPage=="images"?imageWorkspace:chatPage;
    }
    async void RefreshCompanion(){await companionPage!.RefreshAsync();}
    void DisposeCompanionRoutes()
    {
        companionPage?.Dispose();
        if (scheduledTasksPage == null) return;
        scheduledTasksPage.ShowMissedRequested -= owner.ShowScheduledMissed;
        scheduledTasksPage.Dispose();
    }
}
