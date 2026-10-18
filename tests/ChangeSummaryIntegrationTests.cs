using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class ChangeSummaryIntegrationTests
    {
        static int checks;static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        static string Call(AgentWorkspace workspace,string tool,object args){string result=workspace.ExecuteAsync(tool,new JavaScriptSerializer().Serialize(args),CancellationToken.None).GetAwaiter().GetResult();workspace.PublishTask();return result;}
        static void Success(string result){if(result.StartsWith("错误："))throw new Exception(result);}
        [STAThread]public static int Main(string[] args)
        {
            try
            {
                string root=Path.Combine(Directory.GetCurrentDirectory(),"work","permissions","change-summary-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string edited=Path.Combine(root,"原文件.txt"),removed=Path.Combine(root,"删行.txt"),unchanged=Path.Combine(root,"内容不变.txt");
                const string original="same\r\nold\r\nend\r\n";File.WriteAllText(edited,original);File.WriteAllText(removed,"one\ntwo\n");File.WriteAllText(unchanged,"unchanged");
                AgentTaskState live,completed;
                using(var workspace=new AgentWorkspace(root))
                {
                    workspace.BeginTask(new AgentTaskState{Workspace=root,Goal="真实文件摘要回归"});
                    Check(AgentChangeSummaryData.FromTask(workspace.TaskState).FileCount==0,"empty tasks produce no changed-file count");
                    Success(Call(workspace,"write_file",new{path="原文件.txt",content="same\nnew\nextra\nend\n"}));Success(Call(workspace,"write_file",new{path="原文件.txt",content="same\nnew\nextra\nthird\nend\n"}));
                    var one=AgentChangeSummaryData.FromTask(workspace.TaskState);Check(one.FileCount==1&&one.AddedLines==3&&one.RemovedLines==1,"repeated edits count one file and the diff against the actual original backup");
                    Success(Call(workspace,"write_file",new{path="新建.txt",content="first"}));Success(Call(workspace,"write_file",new{path="新建.txt",content="one\ntwo\nthree"}));var created=workspace.Changes.Single(c=>Path.GetFileName(c.Path)=="新建.txt");Check(created.BeforeHash==null&&created.BackupPath==null&&created.AddedLines==3&&created.RemovedLines==0,"repeated creation writes keep absent-file provenance and count all current added lines");
                    var createdPreview=workspace.PreviewChange("新建.txt",CancellationToken.None);Check(createdPreview.Before==""&&createdPreview.Current=="one\ntwo\nthree"&&createdPreview.AddedLines==3&&createdPreview.RemovedLines==0,"created-file diff preview agrees with the shared summary baseline");
                    Success(Call(workspace,"write_file",new{path="删行.txt",content=""}));Success(Call(workspace,"write_file",new{path="内容不变.txt",content="unchanged"}));live=workspace.TaskState;var total=AgentChangeSummaryData.FromTask(live);Check(total.FileCount==3&&total.RecordedFileCount==4&&total.AddedLines==6&&total.RemovedLines==3,"shared net totals combine edits creations and removed lines while excluding byte-identical writes");
                    int recorded=workspace.Changes.Count;Check(Call(workspace,"delete_file",new{path="新建.txt"}).StartsWith("错误：")&&File.Exists(Path.Combine(root,"新建.txt"))&&workspace.Changes.Count==recorded,"unsupported file deletion neither deletes a file nor invents a change record");
                    Success(Call(workspace,"restore_file",new{path="删行.txt"}));var restored=AgentChangeSummaryData.FromTask(workspace.TaskState);Check(restored.FileCount==2&&restored.RecordedFileCount==4&&restored.AddedLines==6&&restored.RemovedLines==1&&restored.Files.Any(c=>c.Status=="restored"),"restored entries remain auditable but no longer contribute files or line totals");
                    Success(Call(workspace,"restore_file",new{path="原文件.txt"}));Check(File.ReadAllText(edited)==original,"undo restores the original backup including its line endings");
                    Success(Call(workspace,"write_file",new{path="原文件.txt",content="different"}));var editedAgain=AgentChangeSummaryData.FromTask(workspace.TaskState);Check(editedAgain.FileCount==2&&editedAgain.RecordedFileCount==4&&editedAgain.AddedLines==4&&editedAgain.RemovedLines==3,"restore then re-edit deduplicates the path and uses its latest active baseline");
                    workspace.FinishTask("complete","");completed=workspace.TaskState;
                }
                var summary=AgentChangeSummaryData.FromTask(completed);var copied=AgentChangeSummaryData.CopyTask(completed);completed.Outputs["private"]="sensitive tool output";completed.Goal="private goal";Check(copied.Outputs.Count==0&&copied.Goal==""&&copied.Id==completed.Id&&copied.Changes.Count==completed.Changes.Count,"navigation snapshots contain task identity and changes without unrelated goals or tool outputs");copied.Changes[0].Path="mutated-copy";Check(completed.Changes[0].Path!="mutated-copy"&&summary.Files.All(c=>c.Path!="mutated-copy"),"summary data and navigation copies do not alias mutable task changes");
                var message=new ChatMessage("assistant","任务完成"){AgentTaskId=completed.Id,ChangeSummary=AgentReplyChangeSummary.FromTask(completed)};var store=new HistoryStore(Path.Combine(root,"history"));store.SaveAsync("fixture",new[]{message}).GetAwaiter().GetResult();var reopened=new HistoryStore(store.DirectoryPath).Load("fixture").Single();var persisted=AgentChangeSummaryData.FromTask(reopened.ChangeSummary.ToTask());Check(persisted.FileCount==2&&persisted.RecordedFileCount==4&&persisted.AddedLines==4&&persisted.RemovedLines==3,"encrypted reply history reopens with its own correct file and line summary");
                using(var next=new AgentWorkspace(root)){next.BeginTask(new AgentTaskState{Workspace=root});Success(Call(next,"write_file",new{path="下一任务.txt",content="next"}));Check(AgentChangeSummaryData.FromTask(next.TaskState).FileCount==1&&persisted.FileCount==2&&reopened.ChangeSummary.TaskId==completed.Id,"a subsequent task cannot replace an earlier reply's persisted summary");}
                using(var resumed=new AgentWorkspace(root)){resumed.BeginTask(reopened.ChangeSummary.ToTask());Success(Call(resumed,"restore_file",new{path="原文件.txt"}));Check(File.ReadAllText(edited)==original&&AgentChangeSummaryData.FromTask(resumed.TaskState).FileCount==1,"reopening the matching task preserves safe real-backup undo and net file counts");Check(Call(resumed,"restore_file",new{path="新建.txt"}).StartsWith("错误：")&&File.ReadAllText(Path.Combine(root,"新建.txt"))=="one\ntwo\nthree","created-file undo remains safely unavailable instead of restoring generated intermediate output");}
                var unknown=AgentChangeSummaryData.CopyTask(completed);unknown.Changes.Last().AddedLines=null;unknown.Changes.Last().RemovedLines=null;Check(!AgentChangeSummaryData.FromTask(unknown).AddedLines.HasValue&&!AgentChangeSummaryData.FromTask(unknown).RemovedLines.HasValue,"unknown line counts remain unknown rather than reporting a partial or invented total");
                var allRestored=AgentChangeSummaryData.CopyTask(completed);foreach(var change in allRestored.Changes)change.Status="restored";var noNet=AgentChangeSummaryData.FromTask(allRestored);Check(noNet.FileCount==0&&noNet.RecordedFileCount==4&&noNet.AddedLines==0&&noNet.RemovedLines==0,"fully restored tasks keep their records with zero net totals");
                Check(AgentChangeSummaryData.FromTask(null).FileCount==0&&AgentChangeSummaryData.FromTask(new AgentTaskState()).RecordedFileCount==0,"missing and empty task summaries contain no stale files");
                if(args.Contains("--ui"))UiChecks(live,completed,allRestored);Console.WriteLine("Change summary integration: "+checks+" passed"+(args.Contains("--ui")?" (with WPF layout)":" (core only; no UI started)"));return 0;
            }
            catch(Exception e){Console.Error.WriteLine(e);return 1;}
        }
        static IEnumerable<T> All<T>(DependencyObject root)where T:DependencyObject{if(root is T)yield return(T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in All<T>(VisualTreeHelper.GetChild(root,i)))yield return item;}
        static void Capture(Window host,string name){host.UpdateLayout();var bitmap=new RenderTargetBitmap((int)host.ActualWidth,(int)host.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(host);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(Directory.GetCurrentDirectory(),"work","permissions",name)))encoder.Save(output);}
        static void UiChecks(AgentTaskState live,AgentTaskState completed,AgentTaskState restored)
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Window host=null;
            try
            {
                AgentTaskState opened=null;var badge=new LiveChangeSummary(task=>opened=task);badge.Refresh(live,true);var final=new AgentChangeSummaryCard(path=>{});final.Refresh(completed);var body=new StackPanel{Margin=new Thickness(24)};body.Children.Add(Ui.Text("运行中的文件变更",15));body.Children.Add(badge);body.Children.Add(Ui.Text("最终回复 · 已完成",15));body.Children.Add(final);host=new Window{Content=body,Width=700,Height=650};host.Show();host.UpdateLayout();
                var label=All<TextBlock>(badge).Single(t=>t.Name=="AgentLiveChangeStatus");string text=String.Join("",label.Inlines.OfType<Run>().Select(r=>r.Text));Check(badge.IsVisible&&text.Contains("3 个文件已更改")&&text.Contains("+6")&&text.Contains("−3"),"live badge visibly displays real shared net file and line totals");badge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(opened!=null&&opened.Id==live.Id&&opened.Changes.Count==live.Changes.Count&&opened.Outputs.Count==0,"clicking the live badge opens the exact corresponding minimal task snapshot");
                Check(All<TextBlock>(final).Any(t=>t.Text=="已编辑 2 个文件")&&All<TextBlock>(final).Any(t=>t.Text=="+4")&&All<TextBlock>(final).Any(t=>t.Text=="−3"),"completed reply card displays matching net summary totals");Capture(host,"change-summary-live-badge.png");Capture(host,"change-summary-final-card.png");
                badge.Refresh(live,false);Check(badge.Visibility==Visibility.Collapsed&&badge.Summary.FileCount==0&&!label.Inlines.Any(),"non-work contexts hide and clear the live badge without stale text");badge.Refresh(completed,true);Check(badge.Visibility==Visibility.Collapsed,"completed work never leaves a stale live badge");restored.Status="running";badge.Refresh(restored,true);Check(badge.Visibility==Visibility.Collapsed,"fully restored running work never shows a stale changed-file badge");
                final.Refresh(live);Check(final.Visibility==Visibility.Collapsed,"reply card retains its default completion gate");final.Refresh(live,true);Check(final.Visibility==Visibility.Visible,"an explicitly opened live changes tab can display the current real file summary");
                var editable=new AgentChangeSummaryCard(path=>{},change=>{});editable.Refresh(completed);body.Children.Add(editable);host.UpdateLayout();Check(All<Button>(editable).Count(b=>b.Name=="RestoreSummaryFile"&&b.IsEnabled)==1&&!All<Button>(final).Any(b=>b.Name=="RestoreSummaryFile"),"explicit restore callbacks enable only backed-up net edits while historical cards stay read-only");
                host.Close();host=null;HostChecks();
            }
            finally{if(host!=null)host.Close();app.Shutdown();}
        }
        static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(40)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static void Wait(Func<bool> predicate){var deadline=DateTime.UtcNow.AddSeconds(12);while(!predicate()&&DateTime.UtcNow<deadline)Pump();if(!predicate())throw new Exception("Full host change-summary fixture timed out");}
        sealed class Silent:IReplySpeech{public Task SpeakAsync(string text,PetSettings settings,CancellationToken cancel){return Task.FromResult(0);}public void SetVolume(int volume){}public void Stop(){}public void Dispose(){}}
        sealed class Model:IConversationApi,IAgentConversationApi
        {
            internal readonly TaskCompletionSource<bool> Release=new TaskCompletionSource<bool>();int calls;
            public async Task<string> ChatAgentAsync(ConnectionSettings connection,IList<ChatMessage> messages,AgentWorkspace workspace,Action<string> delta,Action<string> reasoning,Action<AgentToolEvent> tool,bool stream,CancellationToken cancel)
            {
                int call=++calls;Success(Call(workspace,"write_file",new{path=call==1?"任务一.txt":"任务二.txt",content=call==1?"after-one":"after-two\nmore"}));if(call==1)Success(Call(workspace,"write_file",new{path="任务一新建.txt",content="created-one"}));workspace.RecordTool(new AgentToolEvent{Id="fixture-"+call,Name="write_file",Status="complete",Summary="本地测试文件已写入"});if(call==1)await Release.Task;string result="第 "+call+" 个任务已完成，文件变更保留在本条回复。";delta(result);return result;
            }
            public Task<string> ChatAsync(ConnectionSettings connection,IList<ChatMessage> messages,CancellationToken cancel){throw new InvalidOperationException("Expected local agent fixture");}public Task<string> TranscribeAsync(ConnectionSettings connection,byte[] wave,CancellationToken cancel){throw new InvalidOperationException();}public Task<byte[]> SpeechAsync(ConnectionSettings connection,string voice,string text,CancellationToken cancel){throw new InvalidOperationException();}public void Dispose(){}
        }
        static AgentChangeSummaryCard Card(ChatWindow chat,string reply){var view=All<ChatMessageView>(chat).Single(v=>(string)v.Tag==reply);return All<AgentChangeSummaryCard>(view).Single();}
        static void HostChecks()
        {
            PetWindow pet=null;ChatWindow chat=null;
            try
            {
                string root=Path.Combine(Directory.GetCurrentDirectory(),"work","permissions","summary-host-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"任务一.txt"),"before-one");File.WriteAllText(Path.Combine(root,"任务二.txt"),"before-two");var cfg=new PetSettings{AgentEnabled=true,AgentWorkspace=root,AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,SaveChatHistory=true,ShowPredictedMessages=false,OnboardingState="completed"};foreach(var key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";cfg.Text.BaseUrl="http://127.0.0.1:1";cfg.Text.Model="local-fixture";cfg.Text.ToolSupport=CapabilitySupport.Supported;
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"data","settings.json")),null);pet.Conversation.Dispose();var model=new Model();var history=new HistoryStore(Path.Combine(root,"history"));var session=new ConversationSession(model,new Silent(),()=>null,history);session.SelectPersona(pet.Skin.Id);typeof(PetWindow).GetField("conversation",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(pet,session);chat=new ChatWindow(pet){Width=1150,Height=900};chat.Show();Pump();
                var firstSend=session.SendTextAsync("完成第一个真实本地文件任务",cfg,pet.Skin.Name);Wait(()=>All<LiveChangeSummary>(chat).Any(b=>b.IsVisible&&b.Summary.FileCount==2));chat.UpdateLayout();Pump();var badge=All<LiveChangeSummary>(chat).Single();Check(All<TextBlock>(chat).Count(t=>t.Name=="AgentLiveChangeStatus")==1&&badge.IsVisible,"full ChatWindow mounts one live floating badge without duplicating the legacy composer status");var composer=All<ConversationComposer>(chat).Single();Check(badge.TranslatePoint(new Point(0,badge.ActualHeight),chat).Y<=composer.TranslatePoint(new Point(),chat).Y,"real live badge floats above the main composer");Capture(chat,"change-summary-live-badge.png");
                badge.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump();var area=All<PreviewWorkspace>(chat).Single();Check(All<AgentChangeSummaryCard>(area.Selected).Single().Visibility==Visibility.Visible&&All<Button>(area.Selected).Count(b=>b.Name=="SummaryChangedFile")==2,"real live badge click opens its corresponding running task summary in a same-window tab");area.Close(area.Selected);chat.ToggleFullTabView();Pump();
                model.Release.TrySetResult(true);Wait(()=>firstSend.IsCompleted);firstSend.GetAwaiter().GetResult();Pump();string firstReply=session.History.Messages.Last(m=>m.Role=="assistant").Id;Check(!badge.IsVisible&&All<TextBlock>(Card(chat,firstReply)).Any(t=>t.Text=="已编辑 2 个文件"),"completion hides the real floating badge and attaches its two-file final card to that reply");
                var secondSend=session.SendTextAsync("完成第二个独立文件任务",cfg,pet.Skin.Name);Wait(()=>secondSend.IsCompleted);secondSend.GetAwaiter().GetResult();Pump();string secondReply=session.History.Messages.Last(m=>m.Role=="assistant").Id;var firstCard=Card(chat,firstReply);var secondCard=Card(chat,secondReply);Check(!ReferenceEquals(firstCard,secondCard)&&All<Button>(firstCard).Where(b=>b.Name=="SummaryChangedFile").Any(b=>(string)b.Content=="任务一.txt")&&All<Button>(secondCard).Where(b=>b.Name=="SummaryChangedFile").Any(b=>(string)b.Content=="任务二.txt"),"two actual completed replies retain distinct correctly owned file-summary cards");Check(!All<Button>(firstCard).Any(b=>b.Name=="RestoreSummaryFile")&&All<Button>(secondCard).Any(b=>b.Name=="RestoreSummaryFile"&&b.IsEnabled),"the actual older task is read-only while the matching current task exposes guarded undo");Capture(chat,"change-summary-final-card.png");
                chat.ReloadHistory();Pump();Check(All<TextBlock>(Card(chat,firstReply)).Any(t=>t.Text=="已编辑 2 个文件")&&All<TextBlock>(Card(chat,secondReply)).Any(t=>t.Text=="已编辑 1 个文件"),"rebuilding the actual message controls preserves both replies' individual summaries");
                All<Button>(Card(chat,secondReply)).Single(b=>b.Name=="RestoreSummaryFile"&&b.IsEnabled).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Wait(()=>!session.IsBusy&&File.ReadAllText(Path.Combine(root,"任务二.txt"))=="before-two");Pump();Check(All<TextBlock>(Card(chat,secondReply)).Any(t=>t.Text=="所有文件均已恢复")&&All<TextBlock>(Card(chat,firstReply)).Any(t=>t.Text=="已编辑 2 个文件"),"actual guarded undo restores the original file and updates only its matching reply summary");
                session.FlushHistoryAsync().GetAwaiter().GetResult();var reopened=new HistoryStore(history.DirectoryPath).Load(session.PersonaId);Check(reopened.Single(m=>m.Id==firstReply).ChangeSummary.Changes.Count==2&&AgentChangeSummaryData.FromTask(reopened.Single(m=>m.Id==secondReply).ChangeSummary.ToTask()).FileCount==0,"actual host history persists the older summary and the current task's restored state independently");
                RecordedPreviewChecks(chat,session,root);
            }
            finally{if(chat!=null)chat.Close();if(pet!=null)pet.Close();}
        }
        static FrameworkElement RecordedPreview(ChatWindow chat,AgentTaskState snapshot,string path)
        {
            var operation=(Task)typeof(ChatWindow).GetMethod("ShowRecordedChangeAsync",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(chat,new object[]{snapshot,path});Wait(()=>operation.IsCompleted);operation.GetAwaiter().GetResult();chat.UpdateLayout();Pump();var area=All<PreviewWorkspace>(chat).SingleOrDefault();if(area==null)throw new Exception("Recorded preview was not created: "+((ConversationSession)typeof(ChatWindow).GetField("session",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(chat)).LastNotice);return area.Selected;
        }
        static bool HasOriginals(ChatWindow chat,FrameworkElement detail,string before,string current)
        {
            if(!All<AgentDiffView>(detail).Any())return false;var raw=All<Expander>(detail).Single(e=>e.Header as string=="原文对照");raw.IsExpanded=true;chat.UpdateLayout();Pump();var boxes=All<TextBox>(raw).ToArray();return boxes.Any(b=>b.Text==before)&&boxes.Any(b=>b.Text==current)&&boxes.All(b=>b.IsReadOnly)&&!All<Button>(detail).Any(b=>b.Name=="RestoreSummaryFile");
        }
        static void RecordedPreviewChecks(ChatWindow chat,ConversationSession session,string root)
        {
            string nested=Path.Combine(root,"nested");Directory.CreateDirectory(nested);string local=Path.Combine(nested,"per-file.txt"),restricted=Path.Combine(nested,"root-backup.txt");File.WriteAllText(local,"local-before");File.WriteAllText(restricted,"root-before");AgentTaskState localTask,rootTask;
            using(var files=new AgentWorkspace(root,false)){files.BeginTask(new AgentTaskState{Workspace=root});Success(Call(files,"write_file",new{path=local,content="local-after"}));files.FinishTask("complete","");localTask=files.TaskState;}
            var localDetail=RecordedPreview(chat,localTask,local);Check(Path.GetDirectoryName(localTask.Changes.Single().BackupPath)==Path.Combine(nested,".deskpet-agent-backups")&&HasOriginals(chat,localDetail,"local-before","local-after"),"actual historical nested-file preview supports per-directory backups from unrestricted workspaces");
            using(var files=new AgentWorkspace(root,true)){files.BeginTask(new AgentTaskState{Workspace=root});Success(Call(files,"write_file",new{path=restricted,content="root-after"}));files.FinishTask("complete","");rootTask=files.TaskState;}
            var rootDetail=RecordedPreview(chat,rootTask,restricted);Check(Path.GetDirectoryName(rootTask.Changes.Single().BackupPath)==Path.Combine(root,".deskpet-agent-backups")&&HasOriginals(chat,rootDetail,"root-before","root-after"),"actual historical nested-file preview supports root-confined backups with the original and current text");
            string outsideDirectory=root+"-approved-sibling";Directory.CreateDirectory(outsideDirectory);string outside=Path.Combine(outsideDirectory,"approved.txt");File.WriteAllText(outside,"outside-before");using(var files=new AgentWorkspace(root,false)){files.BeginTask(new AgentTaskState{Workspace=root});Success(Call(files,"write_file",new{path=outside,content="outside-after"}));rootTask.Changes.Add(files.TaskState.Changes.Single().Copy());}
            string currentTask=session.CurrentAgentTask.Id;var scopedDetail=RecordedPreview(chat,rootTask,restricted);Check(HasOriginals(chat,scopedDetail,"root-before","root-after")&&session.CurrentAgentTask.Id==currentTask&&File.ReadAllText(outside)=="outside-after","an unrelated approved outside-workspace change cannot block the selected file's read-only historical preview");
            var area=All<PreviewWorkspace>(chat).Single();int beforeTabs=area.ToolCount;var previous=area.Selected;string unknown=Path.Combine(nested,"not-recorded.txt");File.WriteAllText(unknown,"untouched");RecordedPreview(chat,rootTask,unknown);Check(area.ToolCount==beforeTabs&&ReferenceEquals(area.Selected,previous)&&session.LastNotice.Contains("没有该文件的变更记录")&&File.ReadAllText(unknown)=="untouched","unknown paths are refused without creating a tab or changing an unrecorded file");
        }
    }
}
