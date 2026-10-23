using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dafeiyu;
namespace DeskPet.Desktop;

internal sealed class ImageTaskCenterPage : Grid, IDisposable
{
    readonly DesktopController owner;readonly ImageGenerationTaskManager tasks;readonly Window host;readonly ComboBox filter=new(){Name="ImageTaskFilter",ItemsSource=new[]{TextResources.Get("imageTaskCenterPage.filter.itemsSource"),TextResources.Get("imageTaskCenterPage.filter.itemsSource.alternativeA"),TextResources.Get("imageTaskCenterPage.filter.itemsSource.alternativeB"),TextResources.Get("imageTaskCenterPage.filter.itemsSource.alternativeC"),TextResources.Get("imageTaskCenterPage.filter.itemsSource.alternativeD")},SelectedIndex=0,MinWidth=140};
    readonly StackPanel rows=Ui.Column();readonly TextBlock summary=Ui.Text(""),notice=Ui.Text("");readonly Button more;readonly Button batchCancel;readonly Dictionary<string,Row> controls=new();readonly HashSet<string> selected=new();readonly DispatcherTimer clock=new(){Interval=TimeSpan.FromSeconds(1)};
    readonly List<Visual> ancestors=new();
    int limit=40;bool attached,subscribed,disposed,cancelingSelection;string? selectedId;
    sealed class Row { internal required Border Root;internal required CheckBox Select;internal required TextBlock Stage,Elapsed,Result;internal required WrapPanel Actions;internal bool ResultAvailable;internal ImageGenerationTaskSnapshot? Task; }
    internal bool ClockRunning=>clock.IsEnabled;
    internal ImageTaskCenterPage(DesktopController controller,Window window)
    {
        owner=controller;tasks=owner.ImageTasks;host=window;Name="ImageTaskCenterPage";RowDefinitions=new RowDefinitions("Auto,*");
        var cancel=new Button{Content=TextResources.Get("imageTaskCenterPage.imageTaskCenterPage.content"),Name="CancelSelectedImageTasks",IsEnabled=false};batchCancel=cancel;
        cancel.Click+=async(_,_)=>
        {
            if(cancelingSelection)return;cancelingSelection=true;cancel.IsEnabled=false;
            try{foreach(var id in selected.ToArray()){var task=tasks.Get(id);if(task!=null&&!task.IsTerminal)await tasks.CancelAsync(id);}selected.Clear();}
            catch(OperationCanceledException){}catch(Exception e){owner.Error(e);}
            finally{cancelingSelection=false;Refresh();}
        };
        var actions=new WrapPanel{Orientation=Orientation.Horizontal};actions.Children.Add(filter);actions.Children.Add(cancel);Children.Add(Ui.Column(summary,actions,notice));
        more=Ui.Button(TextResources.Get("imageTaskCenterPage.imageTaskCenterPage.text"),()=>{limit+=40;Refresh();});more.Name="LoadMoreImageTasks";var scroll=new ScrollViewer{Content=Ui.Column(rows,more),HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);Children.Add(scroll);
        filter.SelectionChanged+=(_,_)=>{limit=40;Refresh();};clock.Tick+=(_,_)=>{foreach(var row in controls.Values)if(row.Task is {IsTerminal:false} task)row.Elapsed.Text=ImageTaskCenterState.Elapsed(task);};
        AttachedToVisualTree+=(_,_)=>{attached=true;foreach(var parent in this.GetVisualAncestors()){ancestors.Add(parent);parent.PropertyChanged+=HostChanged;}VisibilityChanged();};DetachedFromVisualTree+=(_,_)=>{attached=false;foreach(var parent in ancestors)parent.PropertyChanged-=HostChanged;ancestors.Clear();Unsubscribe();};PropertyChanged+=(_,e)=>{if(e.Property==IsVisibleProperty){if(IsVisible)Subscribe();else Unsubscribe();}};
    }
    void HostChanged(object? sender,AvaloniaPropertyChangedEventArgs e){if(e.Property==Window.WindowStateProperty||e.Property==IsVisibleProperty)VisibilityChanged();}
    void VisibilityChanged(){if(IsEffectivelyVisible&&TopLevel.GetTopLevel(this) is Window {IsVisible:true,WindowState:not WindowState.Minimized})Subscribe();else Unsubscribe();}
    void Subscribe(){if(disposed||!attached||!IsEffectivelyVisible)return;if(!subscribed){tasks.Changed+=Changed;owner.ImageStore.Changed+=AssetsChanged;subscribed=true;}Refresh();}
    void Unsubscribe(){if(subscribed){tasks.Changed-=Changed;owner.ImageStore.Changed-=AssetsChanged;subscribed=false;}clock.Stop();}
    void AssetsChanged()=>Dispatcher.UIThread.Post(()=>{if(!disposed&&subscribed)Refresh();});
    void Changed(ImageGenerationTaskSnapshot task)=>Dispatcher.UIThread.Post(()=>{if(!disposed&&subscribed)Refresh(task);});
    void Refresh(ImageGenerationTaskSnapshot? changed=null)
    {
        if(disposed)return;var all=tasks.List();selected.RemoveWhere(id=>!all.Any(t=>t.Id==id&&!t.IsTerminal));batchCancel.IsEnabled=!cancelingSelection&&selected.Count>0;summary.Text=TextResources.Format("imageTaskCenterPage.refresh.summaryAllCount",all.Count(t=>!t.IsTerminal),selected.Count);notice.Text=tasks.Warning;
        var matches=all.Reverse().Where(t=>ImageTaskCenterState.Matches(t,filter.SelectedIndex)).ToList();var visible=matches.Take(limit).ToList();var ids=visible.Select(t=>t.Id).ToHashSet();
        foreach(var id in controls.Keys.Where(id=>!ids.Contains(id)).ToArray()){rows.Children.Remove(controls[id].Root);controls.Remove(id);}
        for(int index=0;index<visible.Count;index++){var task=visible[index];if(!controls.TryGetValue(task.Id,out var row)){row=CreateRow(task);controls.Add(task.Id,row);rows.Children.Insert(index,row.Root);}if(changed==null||changed.Id==task.Id||row.Task==null)Update(row,task);}
        more.IsVisible=matches.Count>visible.Count;if(subscribed&&IsEffectivelyVisible&&(TopLevel.GetTopLevel(this) is not Window window||window.WindowState!=WindowState.Minimized)&&visible.Any(t=>!t.IsTerminal))clock.Start();else clock.Stop();
    }
    Row CreateRow(ImageGenerationTaskSnapshot task)
    {
        var check=new CheckBox{Content=Ui.Text(task.Prompt),IsChecked=selected.Contains(task.Id),HorizontalAlignment=HorizontalAlignment.Stretch};check.IsCheckedChanged+=(_,_)=>{if(check.IsChecked==true)selected.Add(task.Id);else selected.Remove(task.Id);batchCancel.IsEnabled=!cancelingSelection&&selected.Count>0;summary.Text=TextResources.Format("imageTaskCenterPage.createRow.summaryTasksList",tasks.List().Count(t=>!t.IsTerminal),selected.Count);};
        var stage=Ui.Text("",15);var elapsed=Ui.Text("",12);var result=Ui.Text("",12);var actions=new WrapPanel{Orientation=Orientation.Horizontal};
        var body=Ui.Column(check,Ui.Text(ImageTaskCenterState.Metadata(task),12),stage,elapsed,result,actions);
        return new Row{Select=check,Root=new Border{Name="ImageTaskRow",Tag=task.Id,Child=body,Padding=new Thickness(14),Margin=new Thickness(0,0,0,10),CornerRadius=new CornerRadius(12),Background=DesktopTheme.Brush("#EDF3FF"),BorderBrush=DesktopTheme.Brush("#E0E4E9"),BorderThickness=new Thickness(1)},Stage=stage,Elapsed=elapsed,Result=result,Actions=actions};
    }
    void Update(Row row,ImageGenerationTaskSnapshot task)
    {
        bool available=ImageTaskCenterState.HasResult(task,owner.ImageStore);bool actionsChanged=row.Task==null||row.Task.State!=task.State||row.Task.DeliveryState!=task.DeliveryState||row.ResultAvailable!=available||row.Task.ResultImageId!=task.ResultImageId;row.ResultAvailable=available;row.Task=task;row.Select.IsChecked=selected.Contains(task.Id);row.Select.IsEnabled=!task.IsTerminal;row.Stage.Text=ImageGenerationMotion.Label(task);row.Elapsed.Text=ImageTaskCenterState.Elapsed(task);row.Result.Text=ImageTaskCenterState.ResultLabel(task,available);row.Root.BorderThickness=new Thickness(task.Id==selectedId?2:1);if(!actionsChanged)return;row.Actions.Children.Clear();
        if(!task.IsTerminal)Add(row,TextResources.Get("imageTaskCenterPage.update.text"),"CancelImageTask",async()=>{await tasks.CancelAsync(task.Id);});else if(task.State!=ImageGenerationTaskState.Succeeded)Add(row,TextResources.Get("imageTaskCenterPage.update.text.alternativeA"),"RetryImageTask",()=>Retry(task));
        if(available){Add(row,TextResources.Get("imageTaskCenterPage.update.text.alternativeB"),TextResources.Get("imageTaskCenterPage.update.text.alternativeC"),()=>{var image=owner.ImageStore.Get(task.ResultImageId)??throw new InvalidOperationException(TextResources.Get("imageTaskCenterPage.update.ownerImageStoreGet"));var card=new GeneratedImageCard(owner,host,image);var dialog=Ui.Window(TextResources.Get("imageTaskCenterPage.update.window"),new ScrollViewer{Content=card},600,650);dialog.Closed+=(_,_)=>card.Dispose();dialog.Show(host);return Task.CompletedTask;});if(ImageTaskCenterState.CanRepairDelivery(task))Add(row,TextResources.Get("imageTaskCenterPage.update.text.alternativeD"),"RepairImageDelivery",()=>owner.RepairImageDeliveryAsync(task.Id));}
        row.Root.BorderThickness=new Thickness(task.Id==selectedId?2:1);
    }
    void Add(Row row,string label,string name,Func<Task> action){var button=Ui.AsyncButton(label,async()=>{try{await action();}catch(Exception e){notice.Text=e is InvalidOperationException or IOException?e.Message:TextResources.Get("imageTaskCenterPage.add.iOExceptionEMessage");}},owner.Error);button.Name=name;row.Actions.Children.Add(button);}
    async Task Retry(ImageGenerationTaskSnapshot task)
    {
        var providers=new ComboBox{ItemsSource=owner.Settings.ImageProviders.Select(p=>p.Copy()).ToArray(),MinWidth=0,HorizontalAlignment=HorizontalAlignment.Stretch};providers.ItemTemplate=new Avalonia.Controls.Templates.FuncDataTemplate<ImageGenerationProvider>((p,_)=>Ui.Text(p?.Name??""));providers.SelectedItem=providers.ItemsSource.Cast<ImageGenerationProvider>().FirstOrDefault(p=>p.Id==owner.Settings.ActiveImageProviderId);
        var currentDetails=Ui.Text("");void ShowCurrent(){currentDetails.Text=providers.SelectedItem is ImageGenerationProvider p?TextResources.Format("imageTaskCenterPage.retry.pConnectionModel",p.Connection.Model):TextResources.Get("imageTaskCenterPage.retry.pConnectionModel.alternativeA");}providers.SelectionChanged+=(_,_)=>ShowCurrent();ShowCurrent();
        var prompt=Ui.Input(task.Prompt);prompt.IsReadOnly=true;prompt.AcceptsReturn=true;prompt.TextWrapping=Avalonia.Media.TextWrapping.Wrap;prompt.Height=140;
        var dialog=Ui.Window(TextResources.Get("imageTaskCenterPage.retry.window"),Ui.Text(""),490,560);dialog.Name="ImageTaskRetryDialog";dialog.WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var confirm=Ui.AsyncButton(TextResources.Get("imageTaskCenterPage.retry.asyncButton"),async()=>{var current=providers.SelectedItem as ImageGenerationProvider;var live=owner.Settings.ImageProviders.FirstOrDefault(p=>p.Id==current?.Id)??throw new InvalidOperationException(TextResources.Get("imageTaskCenterPage.retry.settingsImageProvidersFirstOrDefault"));var handle=await tasks.RetryAsync(task.Id,live.Copy(),CancellationToken.None);Observe(handle.Completion);dialog.Close(true);SelectTask(handle.Id);},owner.Error);confirm.Name="ConfirmImageTaskRetry";confirm.IsEnabled=providers.SelectedItem!=null;providers.SelectionChanged+=(_,_)=>confirm.IsEnabled=providers.SelectedItem!=null;
        dialog.Content=new Border{Padding=new Thickness(22),Child=new ScrollViewer{Content=Ui.Column(Ui.Text(TextResources.Get("imageTaskCenterPage.retry.text"),16),Ui.Field(TextResources.Get("imageTaskCenterPage.retry.field"),providers),currentDetails,Ui.Field(TextResources.Get("imageTaskCenterPage.retry.field.alternativeA"),prompt),Ui.Text(TextResources.Get("imageTaskCenterPage.retry.text.alternativeA")+task.Model+TextResources.Get("imageTaskCenterPage.retry.text.alternativeB")+(task.Size??TextResources.Get("imageTaskCenterPage.retry.text.alternativeC"))+TextResources.Get("imageTaskCenterPage.retry.text.alternativeD")+(task.Quality??TextResources.Get("imageTaskCenterPage.retry.text.alternativeC"))+TextResources.Get("imageTaskCenterPage.retry.text.alternativeE")+(task.ReferenceImageIds?.Count??0)+TextResources.Get("imageTaskCenterPage.retry.text.alternativeF"),12),Ui.Row(Ui.Button(TextResources.Get("common.back"),()=>dialog.Close(false)),confirm))}};
        await dialog.ShowDialog<bool>(VisualRoot as Window??host);
    }
    static async void Observe(Task task){try{await task;}catch(Exception){}}
    internal void SelectTask(string id){selectedId=id;filter.SelectedIndex=0;int index=tasks.List().Reverse().ToList().FindIndex(t=>t.Id==id);if(index>=0)limit=Math.Max(limit,index+1);Refresh();if(controls.TryGetValue(id,out var row))row.Root.BringIntoView();}
    public void Dispose(){if(disposed)return;disposed=true;foreach(var parent in ancestors)parent.PropertyChanged-=HostChanged;ancestors.Clear();Unsubscribe();rows.Children.Clear();controls.Clear();selected.Clear();}
}
