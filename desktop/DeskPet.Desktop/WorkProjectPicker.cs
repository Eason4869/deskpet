using Avalonia;
using Avalonia.Controls;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class WorkProjectPicker:Window
{
    private readonly DesktopController owner;
    private readonly Func<string?,string?,Task> select;
    private readonly StackPanel rows=Ui.Column();
    internal WorkProjectPicker(DesktopController owner,Window host,Func<string?,string?,Task> select)
    {
        this.owner=owner;this.select=select;Title=TextResources.Get("agent.work.choose");Width=600;Height=630;MinWidth=390;MinHeight=400;Background=DesktopTheme.Surface;
        var actions=Ui.Row(Ui.AsyncButton(TextResources.Get("agent.work.normal"),()=>Choose(null,null),owner.Error),Ui.AsyncButton(TextResources.Get("agent.work.newProject"),NewProjectAsync,owner.Error),Ui.Button(TextResources.Get("common.close"),Close));
        var root=new DockPanel{Margin=new Thickness(16)};DockPanel.SetDock(actions,Dock.Top);root.Children.Add(actions);root.Children.Add(new ScrollViewer{Content=rows});Content=root;Refresh();
    }
    private async Task Choose(string? project,string? conversation){await select(project,conversation);Close();}
    private void Refresh()
    {
        rows.Children.Clear();
        foreach(var project in owner.WorkBindings.Snapshot.Projects.Where(project=>!project.Archived))
        {
            rows.Children.Add(Ui.Text(project.Name,17));rows.Children.Add(Ui.Text(project.Workspace,12));
            rows.Children.Add(Ui.Button(TextResources.Get("agent.jobs.ui.title"),()=>owner.ShowAgentJobs(project.Id)));
            rows.Children.Add(Ui.AsyncButton(TextResources.Get("agent.work.newConversation"),async()=>{var conversation=await owner.WorkBindings.CreateConversationAsync(project.Id,TextResources.Get("projectCatalog.createConversation.nameStringIsNullOrWhiteSpace"));await Choose(project.Id,conversation.Id);},owner.Error));
            foreach(var conversation in project.Conversations.Where(value=>value.Mode=="work"))
            {
                var open=Ui.AsyncButton(conversation.Name,()=>Choose(project.Id,conversation.Id),owner.Error);open.Name="OpenWorkConversation";open.Tag=conversation.Id;
                var rename=Ui.AsyncButton(TextResources.Get("agent.work.rename"),async()=>{var input=Ui.Input(conversation.Name);var dialog=Ui.Window(TextResources.Get("agent.work.rename"),new Border(),420,240);dialog.Content=Ui.Column(input,Ui.AsyncButton(TextResources.Get("common.save"),async()=>{await owner.WorkBindings.RenameArchiveAsync(project.Id,conversation.Id,input.Text??"",conversation.Archived);dialog.Close();Refresh();},owner.Error));await dialog.ShowDialog(this);},owner.Error);
                var archive=Ui.AsyncButton(conversation.Archived?TextResources.Get("agent.work.restore"):TextResources.Get("agent.work.archive"),async()=>{await owner.WorkBindings.RenameArchiveAsync(project.Id,conversation.Id,conversation.Name,!conversation.Archived);Refresh();},owner.Error);
                var actions=new WrapPanel();actions.Children.Add(rename);actions.Children.Add(archive);actions.Children.Add(Ui.Button(TextResources.Get("agent.jobs.ui.title"),()=>owner.ShowAgentJobs(project.Id,conversation.Id)));rows.Children.Add(Ui.Column(open,actions));
            }
        }
    }
    private async Task NewProjectAsync()
    {
        var name=Ui.Input("",TextResources.Get("agent.work.projectName"));var directory=Ui.Input(owner.Settings.AgentWorkspace,TextResources.Get("agent.work.directory"));
        var dialog=Ui.Window(TextResources.Get("agent.work.newProject"),new Border(),500,320);dialog.Content=Ui.Column(name,directory,Ui.AsyncButton(TextResources.Get("common.save"),async()=>{var project=await owner.WorkBindings.CreateProjectAsync(name.Text??"",directory.Text??"");dialog.Close();await Choose(project.Id,project.Conversations.Single().Id);},owner.Error),Ui.Button(TextResources.Get("common.cancel"),dialog.Close));await dialog.ShowDialog(this);
    }
}
