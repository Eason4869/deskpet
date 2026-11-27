using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Dafeiyu
{
    public sealed partial class ChatWindow
    {
        readonly StackPanel inspectorSummaryBody = new StackPanel();
        ScrollViewer inspectorSummaryScroll;
        WorkSummaryOverview inspectorOverview;
        WorkSummarySource[] summarySources;

        void MountInspectorSummary()
        {
            inspectorOverview = new WorkSummaryOverview(this);
            inspectorSummaryBody.Children.Add(inspectorOverview);
            inspectorSummaryBody.Children.Add(taskPanel);
            if (inspectorSummaryScroll == null) inspectorSummaryScroll = Ui.Scroll(inspectorSummaryBody);
            inspectorSummaryScroll.ScrollToHome();
            inspectorHost.Content = inspectorSummaryScroll;
            RefreshWorkSummaryOverviews();
        }

        void UnmountInspectorSummary()
        {
            inspectorHost.Content = null;
            inspectorSummaryBody.Children.Clear();
            inspectorOverview = null;
        }

        void RefreshWorkSummaryOverviews()
        {
            if (closed || reloading) return;
            var overviews = new List<WorkSummaryOverview>();
            if (inspectorOverview != null) overviews.Add(inspectorOverview);
            if (transientSummaryView != null) overviews.Add(transientSummaryView.Overview);
            if (filePreview != null) overviews.AddRange(filePreview.Pages<SummaryPreview>().Select(view => view.Overview));
            var contentPages = filePreview == null ? new WorkspacePlanGoalPane[0] : filePreview.Pages<WorkspacePlanGoalPane>();
            if (overviews.Count == 0 && contentPages.Length == 0) return;
            var task = session.CurrentAgentTask;
            bool work = WorkEntry;
            bool ownsTask = task != null && session.History.Messages.Any(message => message.AgentTaskId == task.Id);
            var plan = work ? WorkspacePlanGoalPane.Plan(ownsTask ? task : null, session.LatestGeneratedPlan) : null;
            var goalOptions = work ? pet.ConversationGoalOptions(session) : null;
            string goal = goalOptions==null ? "" : goalOptions.Goal;
            foreach (var page in contentPages) page.Show(page.IsPlan ? plan : WorkspacePlanGoalPane.Goal(goalOptions));
            if (overviews.Count == 0) return;
            ReadWorkSummaryRepository();
            if (summarySources == null) summarySources = WorkSummarySource.FromHistory(session.History.Messages);
            ConversationPreviewState state;
            var chats = conversationPreviews.TryGetValue(session, out state) ? state.SideChats.ToArray() : new SideChatPane[0];
            var changes = AgentChangeSummaryData.FromTask(task);
            var workers = session.Subagents;
            foreach (var overview in overviews) overview.Refresh(changes, chats, summarySources, workers, plan, goal);
        }

        void OpenSummarySideChat(SideChatPane chat)
        {
            ConversationPreviewState state;
            if (closed || (workspace != null && workspace.IsDisposed) || !conversationPreviews.TryGetValue(session, out state) || !state.SideChats.Contains(chat)) return;
            CloseTransientSummary();
            EnsurePreviewWorkspace();
            ShowSideChat(chat);
        }

        void OpenWorkSummarySource(WorkSummarySource source)
        {
            CloseTransientSummary();
            if (source.Link != null) OpenBrowserLink(source.Link);
            else if (source.File.IsImage) ChatImagePreview.OpenAttachment(this, source.File);
            else if (source.File.ImportedFile != null) new AttachmentPreviewWindow(this, source.File, pet.Store.DirectoryPath).ShowDialog();
            else if (source.File.MediaType == "application/pdf")
            {
                try
                {
                    string path = ChatMessageView.WriteAttachmentCopy(source.File, "document.pdf");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch (Exception error)
                {
                    if (!(error is System.IO.IOException || error is UnauthorizedAccessException || error is FormatException ||
                        error is System.ComponentModel.Win32Exception || error is InvalidOperationException)) throw;
                    session.ReportNotice(TextResources.Get("chatMessageView.openAttachment.reportAttachmentError"));
                }
            }
            else
            {
                var preview = new TextBox { Name = "WorkSummaryTextAttachment", Text = source.File.Text ?? "", IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(16) };
                OpenDetailTab(source.Title, preview);
            }
        }

        internal sealed partial class WorkSummaryOverview : StackPanel
        {
            readonly ChatWindow owner;
            readonly ConversationSession conversation;
            readonly Button changes, workers, allSources, repository, linkGitHub;
            readonly TextBlock changeLabel, added, removed, workerLabel, workerCompleted, repositoryLabel, localBranch, gitAdded, gitRemoved, planLabel, goalLabel;
            readonly StackPanel sideChats, sourceRows, changeSection, gitSection, planSection, goalSection, sideChatSection, workerSection, sourceSection;
            string linkedRepository;
            WorkSummarySource[] sources;
            SideChatPane[] displayedChats;
            string[] chatLabels;
            bool[] chatActivity;

            internal WorkSummaryOverview(ChatWindow owner)
            {
                this.owner = owner;
                conversation = owner.session;
                Name = "WorkSummaryOverview";
                Margin = new Thickness(14, 0, 14, 8);

                changeSection = Section("WorkSummaryFileChanges", () => TextResources.Get("agentTaskPanel.refresh.section.alternativeA"), null);
                changes = Row("WorkSummaryChanges", () => {
                    owner.CloseTransientSummary();
                    owner.OpenTaskChanges(conversation.CurrentAgentTask);
                });
                changeLabel = Label("", 12, Ui.Ink);
                var lineCounts = new StackPanel { Orientation = Orientation.Horizontal };
                added = Label("", 11, ThemeManager.Brush("#167047", "#65D19A"));
                removed = Label("", 11, ThemeManager.Brush("#AD3E51", "#FF9B9E"));
                added.Name = "WorkSummaryAddedLines";
                removed.Name = "WorkSummaryRemovedLines";
                removed.Margin = new Thickness(6, 0, 0, 0);
                lineCounts.Children.Add(added);
                lineCounts.Children.Add(removed);
                SetRow(changes, "file", changeLabel, lineCounts);
                changeSection.Children.Add(changes);

                linkGitHub = IconAction("LinkWorkSummaryGitHub", () => TextResources.Get("workSummary.linkGitHub"), owner.EditWorkSummaryRepository);
                gitSection = Section("WorkSummaryGitChanges", () => TextResources.Get("workSummary.gitChanges.title"), linkGitHub);
                repository = Row("OpenWorkSummaryGitHub", () => {
                    if (owner.summaryRepository != null || owner.summaryGitChanges != null) owner.OpenGitChanges();
                    else OpenLinkedGitHubRepository();
                });
                repositoryLabel = Label("", 12, Ui.Ink);
                gitAdded = Label("", 11, ThemeManager.Brush("#167047", "#65D19A"));
                gitRemoved = Label("", 11, ThemeManager.Brush("#AD3E51", "#FF9B9E"));
                gitAdded.Name = "WorkSummaryGitAddedLines";
                gitRemoved.Name = "WorkSummaryGitRemovedLines";
                gitRemoved.Margin = new Thickness(6, 0, 0, 0);
                var gitCounts = new StackPanel { Orientation = Orientation.Horizontal };
                gitCounts.Children.Add(gitAdded);
                gitCounts.Children.Add(gitRemoved);
                SetRow(repository, "git-branch", repositoryLabel, gitCounts);
                gitSection.Children.Add(BuildGitHubRow());
                localBranch = Label("", 11, Ui.Muted);
                localBranch.Margin = new Thickness(6, 0, 6, 3);
                gitSection.Children.Add(localBranch);

                planSection = Section("WorkSummaryPlan", () => TextResources.Get("workSummary.plan"), null);
                planLabel = Label("", 12, Ui.Ink);
                var plan = Row("OpenWorkspacePlan", () => owner.OpenWorkspacePlanGoal(true));
                SetRow(plan, "list", planLabel, null);
                planSection.Children.Add(plan);

                goalSection = Section("WorkSummaryGoal", () => TextResources.Get("conversationComposer.openAddMenu.addMenuLabel"), null);
                goalLabel = Label("", 12, Ui.Ink);
                var goal = Row("OpenWorkspaceGoal", () => owner.OpenWorkspacePlanGoal(false));
                SetRow(goal, "target", goalLabel, null);
                goalSection.Children.Add(goal);

                var newChat = IconAction("NewSummarySideChat", () => TextResources.Get("workSummary.newSideChat"), () => {
                    owner.CloseTransientSummary();
                    owner.OpenSideChat();
                });
                sideChats = new StackPanel();
                sideChatSection = Section("WorkSummarySideChats", () => TextResources.Get("workSummary.sideChats"), newChat);
                sideChatSection.Children.Add(sideChats);

                workerSection = Section("WorkSummarySubagents", () => TextResources.Get("workSummary.workers"), null);
                workers = Row("WorkSummaryWorkers", owner.OpenSubagentInspector);
                workerLabel = Label("", 12, Ui.Ink);
                workerLabel.Name = "WorkSummaryActiveWorkers";
                workerCompleted = Label("", 11, Ui.Muted);
                workerCompleted.Name = "WorkSummaryCompletedWorkers";
                SetRow(workers, "target", workerLabel, workerCompleted);
                workerSection.Children.Add(workers);

                var addSource = IconAction("AddWorkSummarySource", () => TextResources.Get("conversationComposer.openAddMenu.header.alternativeA"), async () => {
                    owner.CloseTransientSummary();
                    if (owner.composer != null) await owner.composer.PickAttachments();
                });
                sourceSection = Section("WorkSummarySources", () => TextResources.Get("webSearch.sources"), addSource);
                sourceRows = new StackPanel();
                sourceSection.Children.Add(sourceRows);
                allSources = AgentUi.Nav(() => TextResources.Get("workSummary.allSources"), "ViewAllWorkSummarySources");
                allSources.Click += delegate {
                    if (!IsCurrent) return;
                    owner.CloseTransientSummary();
                    var content = new StackPanel { Margin = new Thickness(20) };
                    foreach (var source in sources) content.Children.Add(SourceRow(source));
                    owner.OpenDetailTab(() => TextResources.Get("webSearch.sources"), Ui.Scroll(content));
                };
                sourceSection.Children.Add(allSources);
            }

            bool IsCurrent { get { return !owner.closed && ReferenceEquals(conversation, owner.session); } }

            internal void Refresh(AgentChangeSummaryData summary, SideChatPane[] chats, WorkSummarySource[] nextSources,
                IReadOnlyList<AgentChildResult> children, WorkspacePlanGoalPane.Content plan, string goal)
            {
                if (!IsCurrent) return;
                var git = owner.summaryRepository;
                var gitChanges = owner.summaryGitChanges;
                var project = owner.SummaryProject;
                linkGitHub.Visibility = project == null ? Visibility.Collapsed : Visibility.Visible;
                linkedRepository = WorkSummaryRepository.Normalize(project == null ? null : project.GitHubRepositoryLink);
                if (linkedRepository.Length == 0 && git != null) linkedRepository = git.Repository;
                ShowSection(gitSection, linkedRepository.Length > 0 || git != null || gitChanges != null);
                var branch = gitChanges != null ? gitChanges.Branch : git == null ? "" : git.Branch;
                bool matchingOrigin = git != null && linkedRepository.Length > 0 && String.Equals(linkedRepository, git.Repository, StringComparison.OrdinalIgnoreCase);
                RefreshGitHubMenu(linkedRepository.Length > 0, matchingOrigin && branch.Length > 0);
                string repoName = linkedRepository.Length > 0 ? linkedRepository : gitChanges != null ? gitChanges.RepositoryName : "";
                repositoryLabel.Text = repoName + (branch.Length > 0 && (matchingOrigin || linkedRepository.Length == 0)
                    ? (repoName.Length > 0 ? " · " : "") + branch : "");
                repository.ToolTip = repositoryLabel.Text;
                localBranch.Visibility = branch.Length > 0 && !matchingOrigin && linkedRepository.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (localBranch.Visibility == Visibility.Visible) LocalizedUi.Set(localBranch, "Text", () => TextResources.Format("workSummary.localBranch", branch));
                gitAdded.Text = gitChanges == null ? "" : "+" + gitChanges.AddedLines.ToString("N0");
                gitRemoved.Text = gitChanges == null ? "" : "−" + gitChanges.RemovedLines.ToString("N0");
                ShowSection(changeSection, summary.FileCount > 0);
                LocalizedUi.Set(changeLabel, "Text", () => summary.FileCount > 0
                    ? TextResources.Format("agentChangeSummaryCard.refresh.template", summary.FileCount)
                    : TextResources.Get("chatFilePreview.openChangesTab.text"));
                if (summary.FileCount > 0)
                {
                    var activeProject = owner.workspace == null ? null : owner.workspace.Project;
                    var projectName = activeProject == null ? "" : activeProject.Name;
                    if (!String.IsNullOrWhiteSpace(projectName))
                        LocalizedUi.Set(changeLabel, "Text", () => projectName + " · " + TextResources.Format("agentChangeSummaryCard.refresh.template", summary.FileCount));
                }
                changes.ToolTip = changeLabel.Text;
                changes.IsEnabled = summary.RecordedFileCount > 0;
                added.Text = summary.FileCount > 0 && summary.AddedLines.HasValue ? "+" + summary.AddedLines.Value.ToString("N0") : "";
                removed.Text = summary.FileCount > 0 && summary.RemovedLines.HasValue ? "−" + summary.RemovedLines.Value.ToString("N0") : "";
                if (summary.FileCount > 0 && !summary.AddedLines.HasValue)
                    LocalizedUi.Set(changes, "ToolTip", () => TextResources.Get("liveChangeSummary.refresh.labelInlines.alternativeA"));

                planLabel.Text = plan == null ? "" : plan.Title;
                goalLabel.Text = WorkspacePlanGoalPane.SummaryLine(goal);
                ShowSection(planSection, plan != null);
                ShowSection(goalSection, !String.IsNullOrWhiteSpace(goal));
                ShowSection(sideChatSection, chats.Length > 0);
                ShowSection(sourceSection, nextSources.Length > 0);

                int active = children.Count(child => child.Status == "running");
                int queued = children.Count(child => child.Status == "queued");
                int completed = children.Count(SubagentInspector.IsCompleted);
                int total = children.Count;
                int stopped = total - active - queued - completed;
                ShowSection(workerSection, total > 0);
                LocalizedUi.Set(workerLabel, "Text", () => active > 0 || queued > 0
                    ? String.Join(" · ", new[]{
                        active > 0 ? TextResources.Format("workSummary.subagents.runningCount", active) : "",
                        queued > 0 ? TextResources.Format("workSummary.subagents.queuedCount", queued) : ""}.Where(text => text.Length > 0))
                    : stopped > 0 ? TextResources.Format("workSummary.subagents.stoppedCount", stopped)
                    : "");
                workerCompleted.Visibility = completed > 0 ? Visibility.Visible : Visibility.Collapsed;
                LocalizedUi.Set(workerCompleted, "Text", () => TextResources.Format("workSummary.subagents.completedCount", completed));
                LocalizedUi.HelpText(workers, () => TextResources.Format("workSummary.workerCounts", active, queued, total) +
                    " · " + TextResources.Format("workSummary.subagents.completedCount", completed));

                RenderSideChats(chats);
                if (!ReferenceEquals(sources, nextSources))
                {
                    sources = nextSources;
                    sourceRows.Children.Clear();
                    foreach (var source in sources.Take(3)) sourceRows.Children.Add(SourceRow(source));
                    allSources.Visibility = sources.Length > 3 ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            void RenderSideChats(SideChatPane[] chats)
            {
                var labels = chats.Select(chat => chat.SummaryTitle).ToArray();
                var busy = chats.Select(chat => chat.Session.IsBusy).ToArray();
                if (displayedChats != null && displayedChats.SequenceEqual(chats) && chatLabels.SequenceEqual(labels) && chatActivity.SequenceEqual(busy)) return;
                displayedChats = chats;
                chatLabels = labels;
                chatActivity = busy;
                sideChats.Children.Clear();
                for (int index = 0; index < chats.Length; index++)
                {
                    var chat = chats[index];
                    var label = labels[index];
                    int number = index + 1;
                    var title = Label(label, 12, Ui.Ink);
                    if (String.IsNullOrWhiteSpace(label)) LocalizedUi.Set(title, "Text", () => TextResources.Format("workSummary.sideChatNumber", number));
                    var activity = busy[index] ? LocalizedUi.Text(() => TextResources.Get("workSummary.replying"), 10, Ui.Blue) : null;
                    var button = Row("OpenSummarySideChat", () => owner.OpenSummarySideChat(chat));
                    button.ToolTip = title.Text;
                    SetRow(button, "chat", title, activity);
                    sideChats.Children.Add(button);
                }
            }

            Button SourceRow(WorkSummarySource source)
            {
                string icon = source.File != null ? "attachment" : source.Link.Host == "github.com" ? "github" : "link";
                var title = Label(source.Title, 12, Ui.Ink);
                var button = Row("OpenWorkSummarySource", () => owner.OpenWorkSummarySource(source));
                SetRow(button, icon, title, null);
                button.ToolTip = source.Link == null ? source.Title : source.Link.AbsoluteUri;
                return button;
            }

            static void ShowSection(Panel content, bool visible)
            {
                ((Border)content.Parent).Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }

            StackPanel Section(string name, Func<string> title, Button action)
            {
                var body = new StackPanel { Name = name };
                var heading = new DockPanel { Margin = new Thickness(6, 0, 3, 2) };
                if (action != null)
                {
                    DockPanel.SetDock(action, Dock.Right);
                    heading.Children.Add(action);
                }
                var label = LocalizedUi.Text(title, 11, Ui.Muted);
                label.FontWeight = FontWeights.SemiBold;
                label.VerticalAlignment = VerticalAlignment.Center;
                heading.Children.Add(label);
                body.Children.Add(heading);
                Children.Add(new Border { Child = body, Background = ThemeManager.CardSurface, BorderBrush = Ui.Line,
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(5), Margin = new Thickness(0, 0, 0, 7),
                    Visibility = Visibility.Collapsed });
                return body;
            }

            Button Row(string name, Action action)
            {
                var button = AgentUi.Nav("", name);
                button.MinHeight = 27;
                button.Padding = new Thickness(6, 2, 6, 2);
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                button.Click += delegate { if (IsCurrent) action(); };
                return button;
            }

            Button IconAction(string name, Func<string> title, Action action)
            {
                var button = UiIcons.Button("add", title);
                button.Name = name;
                button.Width = button.Height = button.MinHeight = 24;
                button.Padding = new Thickness(5);
                button.Margin = new Thickness(0);
                button.Click += delegate { if (IsCurrent) action(); };
                return button;
            }

            static void SetRow(Button button, string icon, TextBlock title, FrameworkElement detail)
            {
                var row = new DockPanel();
                var glyph = UiIcons.Image(icon, Ui.Blue);
                glyph.Width = glyph.Height = 14;
                glyph.Margin = new Thickness(0, 0, 8, 0);
                glyph.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(glyph, Dock.Left);
                row.Children.Add(glyph);
                if (detail != null)
                {
                    detail.Margin = new Thickness(5, 0, 0, 0);
                    detail.VerticalAlignment = VerticalAlignment.Center;
                    DockPanel.SetDock(detail, Dock.Right);
                    row.Children.Add(detail);
                }
                row.Children.Add(title);
                button.Content = row;
                button.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new System.Windows.Data.Binding("Text") { Source = title });
            }

            static TextBlock Label(string text, double size, Brush ink)
            {
                var label = Ui.InlineText(text, size, ink);
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                return label;
            }

        }
    }
}
