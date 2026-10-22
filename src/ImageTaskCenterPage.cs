using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace Dafeiyu
{
    internal sealed class ImageTaskCenterPage:Grid,IDisposable
    {
        readonly PetWindow pet;readonly ImageGenerationTaskManager tasks;readonly ComboBox filter;readonly StackPanel rows=new StackPanel();readonly TextBlock summary,notice;readonly Button more;Button batchCancel;readonly Dictionary<string,Row> controls=new Dictionary<string,Row>();readonly HashSet<string> selected=new HashSet<string>();readonly DispatcherTimer clock;int limit=40;bool subscribed,disposed;Window host;string selectedId;
        sealed class Row{internal Border Root;internal CheckBox select;internal TextBlock stage,elapsed,result;internal WrapPanel actions;internal bool resultAvailable;internal ImageGenerationTaskSnapshot task;}
        internal bool ClockRunning{get{return clock.IsEnabled;}}
        internal ImageTaskCenterPage(PetWindow owner)
        {
            pet=owner;tasks=owner.ImageTasks;Name="ImageTaskCenterPage";RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});RowDefinitions.Add(new RowDefinition());
            var top=new StackPanel();summary=Ui.Text("",14);summary.Margin=new Thickness(0,0,0,8);top.Children.Add(summary);var actions=new WrapPanel();filter=new ComboBox{Foreground=Ui.Ink,Background=ThemeManager.FieldSurface,Name="ImageTaskFilter",ItemsSource=new[]{"全部","排队中","进行中","已完成","失败及取消"},SelectedIndex=0,MinWidth=140,Margin=new Thickness(0,0,8,8)};actions.Children.Add(filter);var cancel=Ui.Button("取消所选任务");cancel.Name="CancelSelectedImageTasks";batchCancel=cancel;cancel.IsEnabled=false;cancel.Click+=async delegate{var ids=selected.ToArray();foreach(var id in ids){var task=tasks.Get(id);if(task!=null&&!task.IsTerminal)await tasks.CancelAsync(id);}selected.Clear();Refresh();};actions.Children.Add(cancel);top.Children.Add(actions);notice=Ui.Text("",12,Ui.Muted);top.Children.Add(notice);Children.Add(top);
            var body=new StackPanel();body.Children.Add(rows);more=Ui.Button("加载更多任务");more.Name="LoadMoreImageTasks";more.Click+=delegate{limit+=40;Refresh();};body.Children.Add(more);var scroll=Ui.Scroll(body);Grid.SetRow(scroll,1);Children.Add(scroll);
            filter.SelectionChanged+=delegate{limit=40;Refresh();};clock=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};clock.Tick+=delegate{foreach(var row in controls.Values)if(!row.task.IsTerminal)row.elapsed.Text=ImageTaskCenterState.Elapsed(row.task);};Loaded+=delegate{host=Window.GetWindow(this);if(host!=null)host.StateChanged+=HostChanged;Subscribe();};Unloaded+=delegate{if(host!=null)host.StateChanged-=HostChanged;host=null;Unsubscribe();};IsVisibleChanged+=delegate{if(IsVisible)Subscribe();else Unsubscribe();};
        }
        void HostChanged(object sender,EventArgs args){Refresh();}
        void Subscribe(){if(disposed||!IsVisible)return;if(!subscribed){tasks.Changed+=Changed;pet.ImageStore.Changed+=AssetsChanged;subscribed=true;}Refresh();}
        void Unsubscribe(){if(subscribed){tasks.Changed-=Changed;pet.ImageStore.Changed-=AssetsChanged;subscribed=false;}clock.Stop();}
        void AssetsChanged(){if(disposed||!subscribed||Dispatcher.HasShutdownStarted)return;Dispatcher.BeginInvoke(new Action(delegate{if(!disposed&&subscribed)Refresh();}));}
        void Changed(ImageGenerationTaskSnapshot task){if(disposed||!subscribed||Dispatcher.HasShutdownStarted)return;Dispatcher.BeginInvoke(new Action(delegate{if(!disposed&&subscribed)Refresh(task);}));}
        void Refresh(ImageGenerationTaskSnapshot changed=null)
        {
            if(disposed)return;var all=tasks.List();int active=all.Count(t=>!t.IsTerminal);selected.RemoveWhere(id=>!all.Any(t=>t.Id==id&&!t.IsTerminal));batchCancel.IsEnabled=selected.Count>0;summary.Text=active+" 项未完成 · 已选择 "+selected.Count+" 项";notice.Text=tasks.Warning;
            var matches=all.Reverse().Where(t=>ImageTaskCenterState.Matches(t,filter.SelectedIndex)).ToList();var visible=matches.Take(limit).ToList();var ids=new HashSet<string>(visible.Select(t=>t.Id));foreach(var id in controls.Keys.Where(id=>!ids.Contains(id)).ToArray()){rows.Children.Remove(controls[id].Root);controls.Remove(id);}
            for(int index=0;index<visible.Count;index++){var task=visible[index];Row row;if(!controls.TryGetValue(task.Id,out row)){row=CreateRow(task);controls.Add(task.Id,row);rows.Children.Insert(index,row.Root);}if(changed==null||changed.Id==task.Id||row.task==null)Update(row,task);}
            more.Visibility=matches.Count>visible.Count?Visibility.Visible:Visibility.Collapsed;if(subscribed&&IsVisible&&(host==null||host.WindowState!=WindowState.Minimized)&&visible.Any(t=>!t.IsTerminal))clock.Start();else clock.Stop();
        }
        Row CreateRow(ImageGenerationTaskSnapshot task)
        {
            var row=new Row();var body=new StackPanel();var select=new CheckBox{Content=Ui.Text(task.Prompt),Foreground=Ui.Ink,IsChecked=selected.Contains(task.Id),Tag=task.Id,Margin=new Thickness(0,0,0,6)};select.Checked+=delegate{selected.Add(task.Id);batchCancel.IsEnabled=true;summary.Text=tasks.List().Count(t=>!t.IsTerminal)+" 项未完成 · 已选择 "+selected.Count+" 项";};select.Unchecked+=delegate{selected.Remove(task.Id);batchCancel.IsEnabled=selected.Count>0;summary.Text=tasks.List().Count(t=>!t.IsTerminal)+" 项未完成 · 已选择 "+selected.Count+" 项";};body.Children.Add(select);body.Children.Add(Ui.Text(ImageTaskCenterState.Metadata(task),12,Ui.Muted));row.stage=Ui.Text("",14);row.elapsed=Ui.Text("",12,Ui.Muted);row.result=Ui.Text("",12,Ui.Muted);body.Children.Add(row.stage);body.Children.Add(row.elapsed);body.Children.Add(row.result);row.actions=new WrapPanel{Margin=new Thickness(0,6,0,0)};body.Children.Add(row.actions);row.select=select;row.Root=new Border{Name="ImageTaskRow",Tag=task.Id,Child=body,Padding=new Thickness(14),Margin=new Thickness(0,0,0,10),CornerRadius=new CornerRadius(12),Background=Ui.Pale,BorderBrush=Ui.Line,BorderThickness=new Thickness(1)};return row;
        }
        void Update(Row row,ImageGenerationTaskSnapshot task)
        {
            bool available=ImageTaskCenterState.HasResult(task,pet.ImageStore);bool actionsChanged=row.task==null||row.task.State!=task.State||row.task.DeliveryState!=task.DeliveryState||row.resultAvailable!=available||row.task.ResultImageId!=task.ResultImageId;row.resultAvailable=available;row.task=task;row.select.IsChecked=selected.Contains(task.Id);row.select.IsEnabled=!task.IsTerminal;row.stage.Text=ImageGenerationMotion.Label(task);row.elapsed.Text=ImageTaskCenterState.Elapsed(task);row.result.Text=ImageTaskCenterState.ResultLabel(task,available);row.Root.BorderThickness=new Thickness(task.Id==selectedId?2:1);if(!actionsChanged)return;row.actions.Children.Clear();
            if(!task.IsTerminal)Add(row,"取消此任务","CancelImageTask",async()=>{await tasks.CancelAsync(task.Id);});
            else if(task.State!=ImageGenerationTaskState.Succeeded)Add(row,"重试…","RetryImageTask",()=>Retry(task));
            if(available){Add(row,"查看结果","ViewImageTaskResult",delegate{var image=pet.ImageStore.Get(task.ResultImageId);if(image==null)throw new InvalidOperationException("图片结果不可用。");var card=new GeneratedImageCard(pet,Window.GetWindow(this),image);var window=new Window{Title="图片结果",Content=Ui.Scroll(card),Width=600,Height=650,Owner=Window.GetWindow(this),Background=ThemeManager.Surface};window.Show();return Task.FromResult(0);});if(ImageTaskCenterState.CanRepairDelivery(task))Add(row,"重试同步","RepairImageDelivery",()=>pet.RepairImageDeliveryAsync(task.Id));}
            row.Root.BorderThickness=new Thickness(task.Id==selectedId?2:1);
        }
        void Add(Row row,string label,string name,Func<Task> action){var button=Ui.Button(label);button.Name=name;button.Click+=async delegate{button.IsEnabled=false;try{await action();}catch(Exception e){notice.Text=e is InvalidOperationException||e is System.IO.IOException?e.Message:"任务操作失败，请检查配置和存储。";}finally{button.IsEnabled=true;}};row.actions.Children.Add(button);}
        Task Retry(ImageGenerationTaskSnapshot task)
        {
            var dialog=new Window{Name="ImageTaskRetryDialog",Title="确认重试图片任务",Width=490,Height=560,Owner=Window.GetWindow(this),WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=ThemeManager.Surface};var body=new StackPanel{Margin=new Thickness(22)};body.Children.Add(Ui.Text("重试将创建新的图片请求，可能产生费用。",14));var providers=new ComboBox{ItemsSource=pet.Settings.ImageProviders,DisplayMemberPath="Name",SelectedItem=pet.Settings.ImageProviders.FirstOrDefault(p=>p.Id==pet.Settings.ActiveImageProviderId),MinHeight=36};Ui.Label(body,"确认当前服务",providers);var currentDetails=Ui.Text("",12,Ui.Muted);Action showCurrent=delegate{var chosen=providers.SelectedItem as ImageGenerationProvider;currentDetails.Text=chosen==null?"请选择当前服务。":"当前模型："+chosen.Connection.Model;};providers.SelectionChanged+=delegate{showCurrent();};showCurrent();body.Children.Add(currentDetails);var preview=Ui.Field(task.Prompt,true);preview.IsReadOnly=true;preview.Height=140;Ui.Label(body,"请求预览",preview);body.Children.Add(Ui.Text("原模型："+task.Model+"\n尺寸："+(task.Size??"服务默认")+"\n质量："+(task.Quality??"服务默认")+"\n参考图："+(task.ReferenceImageIds==null?0:task.ReferenceImageIds.Count)+" 张",12,Ui.Muted));var actions=new WrapPanel();var cancel=Ui.Button("返回");cancel.Click+=delegate{dialog.Close();};actions.Children.Add(cancel);var confirm=Ui.Button("确认服务并重试",true);confirm.Name="ConfirmImageTaskRetry";confirm.IsEnabled=providers.SelectedItem!=null;providers.SelectionChanged+=delegate{confirm.IsEnabled=providers.SelectedItem!=null;};confirm.Click+=async delegate{confirm.IsEnabled=false;try{var chosen=providers.SelectedItem as ImageGenerationProvider;var current=chosen==null?null:pet.Settings.ImageProviders.FirstOrDefault(p=>p.Id==chosen.Id);if(current==null)throw new InvalidOperationException("所选服务已删除，请重新选择。");var handle=await tasks.RetryAsync(task.Id,current.Copy(),CancellationToken.None);Observe(handle.Completion);dialog.Close();SelectTask(handle.Id);}catch(Exception e){notice.Text=e.Message;dialog.Close();}};actions.Children.Add(confirm);body.Children.Add(actions);dialog.Content=Ui.Scroll(body);dialog.ShowDialog();return Task.FromResult(0);
        }
        static async void Observe(Task task){try{await task;}catch(Exception){}}
        internal void SelectTask(string id){selectedId=id;filter.SelectedIndex=0;var all=tasks.List().Reverse().ToList();int index=all.FindIndex(t=>t.Id==id);if(index>=0)limit=Math.Max(limit,index+1);Refresh();Row row;if(controls.TryGetValue(id,out row))row.Root.BringIntoView();}
        public void Dispose(){if(disposed)return;disposed=true;if(host!=null)host.StateChanged-=HostChanged;host=null;Unsubscribe();rows.Children.Clear();controls.Clear();selected.Clear();}
    }
}
