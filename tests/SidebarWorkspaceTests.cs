using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Dafeiyu.Tests
{
    public static class SidebarWorkspaceTests
    {
        static Application app;static int checks;static PetWindow pet;
        static void Check(bool ok,string text){if(!ok)throw new Exception(text);checks++;Console.WriteLine("PASS "+text);}
        static IEnumerable<T> All<T>(DependencyObject p)where T:DependencyObject{if(p is T)yield return(T)p;for(int i=0;i<VisualTreeHelper.GetChildrenCount(p);i++)foreach(var x in All<T>(VisualTreeHelper.GetChild(p,i)))yield return x;}
        static void CheckWidth(Window window,double requested){Check(Math.Abs(window.ActualWidth-requested)<1,"sidebar fixture actual width matches "+requested+"px (actual "+window.ActualWidth+")");}
        static object Field(ChatWindow window,string name){return typeof(ChatWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);}
        static void Capture(FrameworkElement host,string name){host.UpdateLayout();var bmp=new RenderTargetBitmap((int)host.ActualWidth,(int)host.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(host);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bmp));using(var stream=File.Create(Path.Combine("work","permissions",name)))png.Save(stream);}
        static void RunGit(string root,string arguments)
        {
            var start=new ProcessStartInfo("git.exe",arguments){WorkingDirectory=root,UseShellExecute=false,
                CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            using(var process=Process.Start(start))
            {
                if(!process.WaitForExit(8000)){process.Kill();throw new Exception("Git fixture timed out");}
                if(process.ExitCode!=0)throw new Exception("Git fixture failed: "+process.StandardError.ReadToEnd());
            }
        }
        static void WriteRepository(string root)
        {
            string git=Path.Combine(root,".git");
            Directory.CreateDirectory(Path.Combine(git,"objects"));
            Directory.CreateDirectory(Path.Combine(git,"refs","heads"));
            File.WriteAllText(Path.Combine(git,"HEAD"),"ref: refs/heads/summary-fixture\n");
            File.WriteAllText(Path.Combine(git,"config"),"[core]\nrepositoryformatversion = 0\n[remote \"origin\"]\nurl = https://github.com/example/repo.git\n");
        }
        sealed class BrokenView:Border,IDisposable{public void Dispose(){throw new IOException("cleanup fixture");}}
        sealed class ReleasedView:Border,IDisposable{internal bool Released;public void Dispose(){Released=true;}}
        static void CheckSubagentHeader(SubagentInspector pane)
        {
            pane.UpdateLayout();
            var icon=All<Viewbox>(pane).Single(image=>image.Name=="SubagentInspectorHeaderIcon");
            var title=All<TextBlock>(pane).Single(label=>label.Name=="SubagentInspectorTitle");
            var back=All<Button>(pane).Single(button=>button.Name=="SubagentInspectorBack");
            Func<FrameworkElement,double> centerY=element=>element.TranslatePoint(new Point(0,element.ActualHeight/2),pane).Y;
            var centers = new[] { centerY(icon), centerY(title), centerY(back) };
            // WPF rounds each element to device pixels independently.
            double spread = (centers.Max() - centers.Min()) * VisualTreeHelper.GetDpi(pane).DpiScaleY;
            Check(spread <= 1.000001, "subagent detail icon, title and back button share a visual center line (" +
                spread.ToString("F3") + " physical pixel spread)");
        }
        static async Task RunSubagentHeader()
        {
            Directory.CreateDirectory(Path.Combine("work","permissions"));
            var transport=new ApiClient();
            using(var session=new ConversationSession(transport,
                new SpeechOutput(transport,Path.Combine("work","permissions"),app.Dispatcher),null))
            {
                var task=new AgentTaskState{Workspace=Directory.GetCurrentDirectory(),Status="complete"};
                var started=DateTime.UtcNow.AddMinutes(-3);
                task.Children.Add(new AgentChildResult{Id="fixture-child",Status="completed",Input="检查项目",
                    Result="已读完项目中的说明",Model="fixture-model",StartedUtc=started,FinishedUtc=started.AddSeconds(24)});
                session.History.Commit("检查项目","任务已处理");
                session.History.LinkAgentTask(session.History.Messages.Last().Id,task.Id);
                session.History.UpdateAgentTaskSummary(task);
                using(var pane=new SubagentInspector(session))
                {
                    var host = new Window { Content = pane };
                    Ui.Window(host, "", 639, 790);
                    try
                    {
                        host.Show();
                        await Task.Delay(60);
                        All<Button>(pane).Single(button=>button.Name=="SubagentInspectorRow").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        CheckSubagentHeader(pane);
                        Capture(pane,"workspace-subagents-result.png");
                    }
                    finally{host.Close();}
                }
            }
            Console.WriteLine("Subagent header tests: "+checks+" passed");
        }
        static async Task Run()
        {
            string root=Path.Combine(Directory.GetCurrentDirectory(),"work","permissions","sidebar-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);WriteRepository(root);string file=Path.Combine(root,"README.md");File.WriteAllText(file,"# 测试预览\n\n侧栏支持拖动与独立标签。");var cfg=new PetSettings{AgentEnabled=true,AgentWorkspace=root,AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,ShowPredictedMessages=false,OnboardingState="completed"};foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(root,"data","settings.json")),null);
            // MinWidth establishes each wide viewport even when the CI desktop caps a requested Width.
            var project=new ProjectDefinition{Id="fixture",Name="项目名称",Workspace=root};var definition=new ConversationDefinition{Id=Guid.NewGuid().ToString("N"),Name="检查侧栏布局与交互",Mode="work"};var workspace=pet.Workspaces.CreateWorkspace(project,definition);var full=new ChatWindow(pet,workspace);double normalMinWidth=full.MinWidth;full.MinWidth=1780;full.Width=1780;full.Height=880;full.Show();await Task.Delay(150);CheckWidth(full,1780);var title=All<TextBlock>(full).Single(t=>t.Name=="ConversationTitle");Check(title.Text==definition.Name&&title.TextTrimming==TextTrimming.CharacterEllipsis,"header displays active conversation title with overflow trimming");
            var summary=All<Border>(full).Single(b=>b.Name=="AgentWorkspaceInspector");var toggle=All<Button>(full).Single(b=>b.Name=="ToggleWorkspaceSummary");var add=All<Button>(full).Single(b=>b.Name=="NewWorkspaceTab");Check(summary.IsVisible&&toggle.IsVisible&&add.IsVisible,"wide work window exposes summary and both aligned toolbar actions");Check(Math.Abs(toggle.TranslatePoint(new Point(),full).Y-add.TranslatePoint(new Point(),full).Y)<.5,"summary and new-tab toolbar buttons share vertical alignment");
            var overview=All<ChatWindow.WorkSummaryOverview>(summary).Single();
            Check(overview.Children.OfType<Border>().Where(card=>((Panel)card.Child).Name!="WorkSummaryGitChanges")
                .All(card=>card.Visibility==Visibility.Collapsed),"workspace hides all categories with no corresponding content");
            // A collapsed ContentControl need not create its visual presenter.
            // Inspect its existing content, then verify rendering after a task appears.
            var navigation=(AgentWorkspaceNavigation)Field(full,"navigation");var projects=All<ProjectSidebar>(navigation).Single();var taskHost=projects.Children.OfType<ContentControl>().Single(c=>c.Name=="CurrentProjectTaskHost");var taskShortcut=(Button)taskHost.Content;Check(taskShortcut.Name=="WorkspaceTaskDetails"&&!taskHost.IsVisible,"project task shortcut reserves no visible row when there is no task");
            var tasks=(Dictionary<string,AgentTaskState>)typeof(ConversationSession).GetField("agentTasks",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(workspace.Session);tasks[workspace.Session.PersonaId]=new AgentTaskState{PersonaId=workspace.Session.PersonaId,Workspace=root,Goal="检查当前项目任务入口",Status="complete"};navigation.Refresh();await Task.Delay(60);var projectSection=All<Button>(projects).Single(b=>b.Name=="ToggleSection_projects");var recentSection=All<Button>(projects).Single(b=>b.Name=="ToggleSection_recent");Check(taskShortcut.IsVisible&&ReferenceEquals(taskShortcut.Parent,taskHost)&&projects.Children.IndexOf(taskHost)>projects.Children.IndexOf((UIElement)projectSection.Parent)&&projects.Children.IndexOf(taskHost)<projects.Children.IndexOf(recentSection),"current task shortcut belongs under projects and before recent, not above the footer");
            var task=tasks[workspace.Session.PersonaId];
            task.Plan.Add(new AgentPlanStep{Text="核对项目文档",Status="complete"});
            definition.WorkOptions=new ConversationWorkOptions("goal","整理项目说明\n补充安装步骤");
            File.AppendAllText(file,"\n");
            RunGit(root,"add -- README.md");
            RunGit(root,"-c user.name=Fixture -c user.email=fixture@example.invalid commit --no-gpg-sign --no-verify -m fixture");
            File.AppendAllText(file,"Git workspace fixture.\n");
            task.Changes.Add(new AgentChange{Path=file,Status="modified",AddedLines=3,RemovedLines=2,AfterHash=new string('a',64)});
            task.Changes.Add(new AgentChange{Path=Path.Combine(root,"restored.txt"),Status="restored",AddedLines=20,RemovedLines=10,AfterHash=new string('b',64)});
            var started=DateTime.UtcNow.AddMinutes(-3);
            task.Children.Add(new AgentChildResult{Id="fixture-child",Status="complete",Input="检查项目",
                Result="已读完项目中的说明",Model="fixture-model",StartedUtc=started,FinishedUtc=started.AddSeconds(24)});
            workspace.Session.History.Commit("检查项目","任务已处理");
            workspace.Session.History.LinkAgentTask(workspace.Session.History.Messages.Last().Id,task.Id);
            workspace.Session.History.UpdateAgentTaskSummary(task);
            typeof(ChatWindow).GetMethod("RefreshSummary",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(full,null);
            full.UpdateLayout();
            Check(All<TextBlock>(summary).Single(t=>t.Name=="WorkSummaryAddedLines").Text=="+3"&&All<TextBlock>(summary).Single(t=>t.Name=="WorkSummaryRemovedLines").Text=="−2","work summary presents actual net line counts and excludes restored files");
            Check(All<Button>(summary).Single(b=>b.Name=="WorkSummaryWorkers").IsEnabled,"recorded subagents expose their task details");
            Check(All<TextBlock>(summary).Single(t=>t.Name=="WorkSummaryCompletedWorkers").Text=="1 个已完成",
                "completed workers remain visible without an active task");
            Check(!All<StackPanel>(summary).Any(panel=>panel.Name=="AgentTaskChildren"),
                "workspace summary does not repeat inline worker details");
            Check(!All<StackPanel>(summary).Any(panel=>panel.Name=="AgentTaskChanges"),
                "workspace summary keeps task file changes in the top card only");
            Check(All<Button>(summary).Any(button=>button.Name=="OpenWorkspacePlan"&&button.IsVisible)&&
                All<Button>(summary).Any(button=>button.Name=="OpenWorkspaceGoal"&&button.IsVisible)&&
                !All<StackPanel>(summary).Any(panel=>panel.Name=="AgentTaskPlan"),
                "populated plan and goal each have a category without repeating plan steps inline");
            var taskPanel=(AgentTaskPanel)Field(full,"taskPanel");taskPanel.IsExpanded=false;taskShortcut.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(taskPanel.IsExpanded,"relocated current task shortcut preserves task-focus action");
            project.Conversations.Add(definition);pet.Projects.Projects.Add(project);navigation.Refresh();await Task.Delay(60);Check(!taskHost.IsVisible&&All<Button>(projects).Any(b=>b.Name=="ProjectConversation"&&b.IsVisible),"rendered active conversation suppresses a duplicate current-task shortcut");pet.Projects.Projects.Remove(project);project.Conversations.Remove(definition);tasks.Remove(workspace.Session.PersonaId);full.RefreshAppearance();navigation.Refresh();await Task.Delay(60);Check(!taskHost.IsVisible&&taskHost.DesiredSize.Height<1,"clearing the task collapses the relocated shortcut height");
            full.MinWidth=normalMinWidth;full.Width=1000;await Task.Delay(90);CheckWidth(full,1000);All<Button>(full).Single(b=>b.Name=="ToggleAppSidebar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(50);Check(!((FrameworkElement)Field(full,"compactTaskHost")).IsVisible,"hiding project sidebar never exposes the legacy compact task status in the chat header");All<Button>(full).Single(b=>b.Name=="ToggleAppSidebar").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));full.MinWidth=2300;full.Width=2300;await Task.Delay(90);CheckWidth(full,2300);
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(70);Check(!summary.IsVisible,"explicit hide removes fixed summary");full.OpenFilePreview(file);await Task.Delay(100);full.MinWidth=2300;full.Width=2300;await Task.Delay(80);CheckWidth(full,2300);var fixtureShell=(Grid)Field(full,"workspaceShell");All<Thumb>(full).Single(t=>t.Name=="PreviewDivider").RaiseEvent(new DragDeltaEventArgs(fixtureShell.ColumnDefinitions[6].ActualWidth-600,0){RoutedEvent=Thumb.DragDeltaEvent});await Task.Delay(80);Check(!summary.IsVisible,"resize and preview do not override explicit hidden preference");toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);Check(summary.IsVisible,"explicit show restores summary when room allows");
            full.MinWidth=1380;full.Width=1380;await Task.Delay(100);CheckWidth(full,1380);Check(!summary.IsVisible,"preview automatically collapses summary when width is insufficient");toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);var popup=(Popup)Field(full,"transientSummary");Check(popup!=null&&popup.IsOpen&&!popup.StaysOpen,"summary action temporarily opens a dismissible overlay in narrow layout");var temporary=(SummaryPreview)popup.Child;All<Button>(temporary).Single(b=>b.Name=="OpenSummaryTab").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);var area=All<PreviewWorkspace>(full).Single();Check(!popup.IsOpen&&area.Selected is SummaryPreview,"summary overlay can open its real contents in a right-side tab");area.Close(area.Selected);
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(80);popup=(Popup)Field(full,"transientSummary");full.Activate();Keyboard.Focus(All<MarkdownEditor>(full).Single(t=>t.Name=="MessageInput"));await Task.Delay(120);Check(!popup.IsOpen,"moving keyboard focus back to main input also closes summary");toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(70);popup=(Popup)Field(full,"transientSummary");var other=new Window{Width=200,Height=100,Content=new TextBox()};other.Show();other.Activate();((TextBox)other.Content).Focus();await Task.Delay(120);Check(!popup.IsOpen,"losing focus to another window automatically closes temporary summary");other.Close();full.Activate();
            full.MinWidth=2300;full.Width=2300;await Task.Delay(100);CheckWidth(full,2300);Check(summary.IsVisible,"widening restores automatically collapsed summary");var shell=(Grid)Field(full,"workspaceShell");double before=shell.ColumnDefinitions[6].ActualWidth;var previewGrip=All<Thumb>(full).Single(t=>t.Name=="PreviewDivider");previewGrip.RaiseEvent(new DragDeltaEventArgs(100,0){RoutedEvent=Thumb.DragDeltaEvent});await Task.Delay(100);Check(shell.ColumnDefinitions[6].ActualWidth<before-80,"dragging divider resizes preview independently");double nav=shell.ColumnDefinitions[0].ActualWidth;All<Thumb>(full).Single(t=>t.Name=="NavigationDivider").RaiseEvent(new DragDeltaEventArgs(35,0){RoutedEvent=Thumb.DragDeltaEvent});await Task.Delay(60);Check(shell.ColumnDefinitions[0].ActualWidth>nav+25,"left navigation divider also resizes freely");
            previewGrip.RaiseEvent(new DragDeltaEventArgs(-(shell.ColumnDefinitions[2].ActualWidth-425),0){RoutedEvent=Thumb.DragDeltaEvent});await Task.Delay(80);Check(shell.ColumnDefinitions[2].ActualWidth>=419&&shell.ColumnDefinitions[6].ActualWidth>=320,"divider near its split limit preserves readable minimum widths");
            var navGrip=All<Thumb>(full).Single(t=>t.Name=="NavigationDivider");navGrip.RaiseEvent(new DragDeltaEventArgs(500,0){RoutedEvent=Thumb.DragDeltaEvent});full.MinWidth=normalMinWidth;full.Width=1000;await Task.Delay(100);CheckWidth(full,1000);All<Button>(full).Single(b=>b.Name=="BackToConversation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(80);double constrained=shell.ColumnDefinitions[0].ActualWidth;navGrip.RaiseEvent(new DragDeltaEventArgs(-10,0){RoutedEvent=Thumb.DragDeltaEvent});await Task.Delay(60);Check(shell.ColumnDefinitions[0].ActualWidth<constrained-8,"reverse drag responds immediately after sidebar is constrained by resize");full.MinWidth=2300;full.Width=2300;await Task.Delay(100);CheckWidth(full,2300);
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);Check(area.Selected is BlankPreviewPage&&All<Button>(area.Selected).Count(b=>b.Name.StartsWith("NewTab"))==5,"new tab opens five real tool entries");All<Button>(area.Selected).Single(b=>b.Name=="NewTabBrowser").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(80);Check(area.Selected is BrowserPane,"blank tool page turns into in-app browser without external launch");
            full.OpenSideChat();await Task.Delay(100);Check(area.Selected is SideChatPane&&((SideChatPane)area.Selected).Session.History.Messages.Count==0,"side chat opens independent empty project session in right tab");
            var existingSideChat=(SideChatPane)area.Selected;
            project.Conversations.Add(definition);pet.Projects.Projects.Add(project);pet.ProjectStore.Save(pet.Projects);
            var taskStore=(AgentTaskStore)typeof(ConversationSession).GetField("taskStore",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(workspace.Session);
            await taskStore.SaveAsync(task);
            Check(taskStore.Load(workspace.Session.PersonaId,root)!=null,"registered conversation loads its persisted task checkpoint");
            workspace.Session.LoadAgentTask(workspace.EffectiveSettings());
            workspace.Session.History.RegisterGuidance("source-fixture","核对来源",Enumerable.Range(1,4).Select(i=>ChatAttachment.FromText("来源"+i+".txt","fixture data")));
            workspace.Session.History.Commit("链接","[GitHub 示例](https://github.com/example/repo) [重复](https://github.com/example/repo) [被排除](https://user@github.com/example/repo)");
            full.ReloadHistory(false);
            var sources=WorkSummarySource.FromHistory(workspace.Session.History.Messages);
            Check(sources.Length==5&&sources.Count(item=>item.File!=null)==4&&sources.Single(item=>item.Link!=null).Title=="GitHub 示例","summary includes sent attachments and deduplicates reply links without user info");
            full.OpenBlankTab();full.MinWidth=1380;full.Width=1380;await Task.Delay(100);
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
            popup=(Popup)Field(full,"transientSummary");Check(popup!=null&&popup.IsOpen,"compact work summary opens with live session data");
            temporary=(SummaryPreview)popup.Child;
            var detected=await Task.Run(()=>WorkSummaryRepository.Read(root,System.Threading.CancellationToken.None));
            Check(detected.Repository=="example/repo"&&detected.Branch=="summary-fixture","local Git checkout supplies its GitHub origin and branch without a network request");
            var deadline=DateTime.UtcNow.AddSeconds(4);
            while(!All<Button>(temporary).Single(b=>b.Name=="OpenWorkSummaryGitHub").IsVisible&&DateTime.UtcNow<deadline)await Task.Delay(30);
            Check(All<Button>(temporary).Single(b=>b.Name=="OpenWorkSummaryGitHub").ToolTip.ToString()=="example/repo · summary-fixture"&&All<Button>(temporary).Single(b=>b.Name=="LinkWorkSummaryGitHub").IsVisible,"summary displays detected repository and offers an explicit per-project override");
            temporary.UpdateLayout();
            var gitCard=temporary.Overview.Children.OfType<Border>().Select(card=>card.Child).OfType<StackPanel>()
                .Single(panel=>panel.Name=="WorkSummaryGitChanges");
            var gitPlus=All<TextBlock>(gitCard).FirstOrDefault(t=>t.Name=="WorkSummaryGitAddedLines");
            while((gitPlus==null||gitPlus.Text=="")&&DateTime.UtcNow<deadline)
            {
                await Task.Delay(30);temporary.UpdateLayout();
                gitPlus=All<TextBlock>(gitCard).FirstOrDefault(t=>t.Name=="WorkSummaryGitAddedLines");
            }
            var fileChanges=All<TextBlock>(temporary).FirstOrDefault(t=>t.Name=="WorkSummaryAddedLines");
            Check(gitPlus!=null&&gitPlus.Text=="+1"&&fileChanges!=null&&fileChanges.Text=="+3",
                "Git line counts are shown independently of task file edits (Git "+(gitPlus==null?"missing":gitPlus.Text)+
                ", files "+(fileChanges==null?"missing":fileChanges.Text)+")");
            var gitActions=All<Button>(gitCard).Single(b=>b.Name=="WorkSummaryGitHubActions");
            gitActions.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(65);
            Check(popup.IsOpen&&gitActions.ContextMenu.IsOpen,"GitHub actions remain usable in the temporary workspace summary");
            gitActions.ContextMenu.IsOpen=false;
            if(!popup.IsOpen){toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(85);popup=(Popup)Field(full,"transientSummary");temporary=(SummaryPreview)popup.Child;}
            Check(All<Button>(temporary).Count(b=>b.Name=="OpenWorkSummarySource")==3&&All<Button>(temporary).Single(b=>b.Name=="ViewAllWorkSummarySources").IsVisible,"compact summary limits source rows and offers the full list");
            Capture(temporary,"work-summary.png");
            All<Button>(temporary).Single(b=>b.Name=="OpenWorkSummaryGitHub").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(70);
            var gitPane=area.Selected as GitChangesPane;
            Check(!popup.IsOpen&&gitPane!=null&&gitPane.IsVisible,"Git changes opens the right-side pane and closes the floating summary");
            var patch=All<RichTextBox>(gitPane).Single(t=>t.Name=="GitChangesPatch");
            deadline=DateTime.UtcNow.AddSeconds(8);
            while(!patch.IsVisible&&DateTime.UtcNow<deadline)await Task.Delay(30);
            Check(patch.IsVisible&&new TextRange(patch.Document.ContentStart,patch.Document.ContentEnd).Text.Contains("+Git workspace fixture."),
                "the Git sidebar renders the actual tracked worktree patch");
            Capture(gitPane,"workspace-git-changes.png");
            var gitFilter=All<TextBox>(gitPane).Single(t=>t.Name=="GitChangesFilter");
            gitFilter.Text="no-matching-path";await Task.Delay(60);
            Check(All<ListBox>(gitPane).Single().Items.Count==0&&!patch.IsVisible,"filtering clears the previous file's preview when no files match");
            gitFilter.Clear();
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(85);
            popup=(Popup)Field(full,"transientSummary");temporary=(SummaryPreview)popup.Child;
            All<Button>(temporary).Single(b=>b.Name=="WorkSummaryWorkers").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(80);
            var workerPane=area.Selected as SubagentInspector;
            Check(!popup.IsOpen&&workerPane!=null&&workerPane.IsVisible&&All<MarkdownEditor>(full).Single(t=>t.Name=="MessageInput").IsVisible,
                "the temporary workspace summary opens subagents beside the live composer");
            var worker=All<Button>(workerPane).Single(b=>b.Name=="SubagentInspectorRow");
            Check(All<TextBlock>(workerPane).Any(t=>t.Text=="已完成 · 1")&&Equals(worker.Tag,"fixture-child"),
                "the sidebar lists the retained worker under the completed group");
            Capture(workerPane,"workspace-subagents.png");
            worker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(60);
            Check(All<TextBlock>(workerPane).Single(t=>t.Name=="SubagentInspectorResult").Text=="已读完项目中的说明"&&
                All<TextBlock>(workerPane).Any(t=>t.Text=="fixture-model")&&All<TextBlock>(workerPane).Any(t=>t.Text=="用时 0:00:24"),
                "worker details show the actual result, model, and elapsed time");
            CheckSubagentHeader(workerPane);
            Capture(workerPane,"workspace-subagents-result.png");
            All<Button>(workerPane).Single(b=>b.Name=="SubagentInspectorBack").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(60);
            Check(All<Button>(workerPane).Any(b=>b.Name=="SubagentInspectorRow"),
                "the back button returns to the subagent list in the same tab");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(80);
            popup=(Popup)Field(full,"transientSummary");
            temporary=(SummaryPreview)popup.Child;
            All<Button>(temporary).Single(b=>b.Name=="WorkSummaryWorkers").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(60);
            Check(!popup.IsOpen&&area.Pages<SubagentInspector>().Length==1&&ReferenceEquals(workerPane,area.Selected),
                "opening subagents again selects the same right-side tab");
            foreach(var name in new[]{"OpenWorkspacePlan","OpenWorkspaceGoal"})
            {
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(85);
                popup=(Popup)Field(full,"transientSummary");temporary=(SummaryPreview)popup.Child;
                All<Button>(temporary).Single(button=>button.Name==name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(55);
                var page=area.Selected as WorkspacePlanGoalPane;
                Check(!popup.IsOpen&&page!=null,"plan and goal content open in a workspace tab");
                var doc=All<FlowDocumentScrollViewer>(page).Single().Document;
                string text=new TextRange(doc.ContentStart,doc.ContentEnd).Text;
                Check(page.IsPlan?text.Contains("✓ 核对项目文档"):text.Contains("补充安装步骤"),
                    "the plan or goal tab displays the actual complete content");
            }
            definition.WorkOptions=new ConversationWorkOptions("off","");full.RefreshAppearance();
            Check(All<FlowDocumentScrollViewer>(area.Selected).Single().Document==null,"clearing a goal clears its open detail");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(80);
            popup=(Popup)Field(full,"transientSummary");
            temporary=(SummaryPreview)popup.Child;
            Check(!All<Button>(temporary).Any(button=>button.Name=="OpenWorkspaceGoal"&&button.IsVisible),
                "clearing existing content hides that specific workspace category");
            All<Button>(temporary).Single(b=>b.Name=="OpenSummarySideChat").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!popup.IsOpen&&ReferenceEquals(existingSideChat,area.Selected)&&area.Pages<SideChatPane>().Length==1,"summary returns to the existing side chat without duplicating it");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(80);
            temporary=(SummaryPreview)((Popup)Field(full,"transientSummary")).Child;
            All<Button>(temporary).Single(b=>b.Name=="OpenWorkSummarySource"&&Object.Equals(b.ToolTip,"来源1.txt")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var textSource=area.Selected as TextBox;
            Check(textSource!=null&&textSource.Name=="WorkSummaryTextAttachment"&&textSource.IsReadOnly&&textSource.Text=="fixture data","legacy inline text sources open their original contents read-only");
            var git=new WorkSummaryRepository("git@github.com:example/repo.git","feature/summary");
            Check(git.Repository=="example/repo"&&git.Branch=="feature/summary"&&new WorkSummaryRepository("ssh://git@github.com/example/repo.git","main").Repository=="example/repo","standard GitHub checkout remotes and branch names are recognized");
            Check(WorkSummaryRepository.Normalize("https://github.com/example/repo.git")=="example/repo"&&WorkSummaryRepository.Normalize("https://github.com.evil.test/example/repo")==""&&WorkSummaryRepository.Normalize("https://user@github.com/example/repo")=="","manual repository links reject lookalike hosts and embedded credentials");
            var original=ProjectCatalogStore.ConfigurationSnapshot(project);var edited=ProjectCatalogStore.ConfigurationSnapshot(project);edited.GitHubRepositoryLink="example/override";
            using(var publication=await pet.ProjectStore.SaveProjectConfigurationAsync(original,edited))publication.Publish(project);
            Check(project.GitHubRepositoryLink=="example/override"&&pet.ProjectStore.Load().Projects.Single(p=>p.Id==project.Id).GitHubRepositoryLink=="example/override","a linked GitHub repository is stored with the named project");
            definition.Name=new string('长',70)+" 标题";full.RefreshAppearance();full.MinWidth=normalMinWidth;full.Width=950;await Task.Delay(100);CheckWidth(full,950);All<Button>(full).Single(b=>b.Name=="BackToConversation").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);Check(title.Text==definition.Name&&title.ActualWidth>0&&title.ActualWidth<600,"long conversation title remains bounded in compact header");definition.Name="检查侧栏布局与交互";full.RefreshAppearance();full.MinWidth=2300;full.Width=2300;await Task.Delay(100);CheckWidth(full,2300);All<Thumb>(full).Single(t=>t.Name=="PreviewDivider").RaiseEvent(new DragDeltaEventArgs(shell.ColumnDefinitions[6].ActualWidth-640,0){RoutedEvent=Thumb.DragDeltaEvent});await Task.Delay(100);Capture(full,"workspace-sidebar-final.png");full.OpenBlankTab();await Task.Delay(120);Capture(full,"workspace-new-tab.png");var broken=new BrokenView();area.AddPage("清理失败夹具","file",broken);area.Close(broken);Check(workspace.Session.LastNotice.Contains("清理"),"failed tab cleanup reports a notice without escaping into global exception");area.AddPage("再次失败","file",new BrokenView());var released=new ReleasedView();area.AddPage("释放夹具","file",released);full.MinWidth=normalMinWidth;full.Close();Check(released.Released,"window close continues releasing sibling tabs after one cleanup fails");Console.WriteLine("Sidebar workspace tests: "+checks+" passed");
        }
        [STAThread]public static int Main(string[] args){app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int result=1;app.DispatcherUnhandledException+=delegate(object sender,System.Windows.Threading.DispatcherUnhandledExceptionEventArgs error){Console.Error.WriteLine(error.Exception.GetType().FullName+" "+error.Exception.Message);Console.Error.WriteLine(error.Exception.StackTrace);error.Handled=true;app.Shutdown(1);};app.Dispatcher.BeginInvoke(new Action(async delegate{try{if(args.Contains("--subagent-header-only"))await RunSubagentHeader();else await Run();result=0;}catch(Exception e){Console.Error.WriteLine(e.GetType().FullName+" "+e.Message);Console.Error.WriteLine(e.StackTrace);}finally{foreach(var window in app.Windows.OfType<Window>().ToArray())window.Close();if(pet!=null)pet.Close();app.Shutdown();}}));return app.Run()==0?result:1;}
    }
}
