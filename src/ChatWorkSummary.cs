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
            inspectorOverview = new WorkSummaryOverview(this, taskPanel);
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
            if (overviews.Count == 0) return;
            ReadWorkSummaryRepository();
            if (summarySources == null) summarySources = WorkSummarySource.FromHistory(session.History.Messages);
            var chats = filePreview == null ? new SideChatPane[0] : filePreview.Pages<SideChatPane>();
            var task = session.CurrentAgentTask;
            var changes = AgentChangeSummaryData.FromTask(task);
            foreach (var overview in overviews) overview.Refresh(task, changes, chats, summarySources);
        }

        void OpenSummarySideChat(SideChatPane chat)
        {
            CloseTransientSummary();
            if (filePreview == null || !filePreview.SelectPage(chat)) return;
            previewVisible = true;
            tabsHidden = resumeConversation = false;
            RefreshWorkspaceLayout();
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

        internal sealed class WorkSummaryOverview : StackPanel
        {
            readonly ChatWindow owner;
            readonly ConversationSession conversation;
            readonly Button changes, workers, allSources, repository, linkGitHub;
            readonly TextBlock changeLabel, added, removed, workerLabel, repositoryLabel, localBranch, noTask;
            readonly StackPanel sideChats, sourceRows;
            string linkedRepository;
            WorkSummarySource[] sources;
            SideChatPane[] displayedChats;
            string[] chatLabels;
            bool[] chatActivity;

            internal WorkSummaryOverview(ChatWindow owner, AgentTaskPanel details)
            {
                this.owner = owner;
                conversation = owner.session;
                Name = "WorkSummaryOverview";
                Margin = new Thickness(14, 0, 14, 8);

                linkGitHub = IconAction("LinkWorkSummaryGitHub", () => TextResources.Get("workSummary.linkGitHub"), owner.EditWorkSummaryRepository);
                var changeSection = Section(() => TextResources.Get("agentTaskPanel.refresh.section.alternativeA"), linkGitHub);
                repository = Row("OpenWorkSummaryGitHub", () => {
                    owner.CloseTransientSummary();
                    if (!String.IsNullOrEmpty(linkedRepository)) MarkdownLinkMenu.OpenExternally(WorkSummaryRepository.GitHubPage(linkedRepository));
                });
                repositoryLabel = Label("", 12, Ui.Ink);
                SetRow(repository, "github", repositoryLabel, null);
                changeSection.Children.Add(repository);
                localBranch = Label("", 11, Ui.Muted);
                localBranch.Margin = new Thickness(6, 0, 6, 3);
                changeSection.Children.Add(localBranch);
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

                var newChat = IconAction("NewSummarySideChat", () => TextResources.Get("workSummary.newSideChat"), () => {
                    owner.CloseTransientSummary();
                    owner.OpenSideChat();
                });
                sideChats = new StackPanel();
                Section(() => TextResources.Get("workSummary.sideChats"), newChat).Children.Add(sideChats);

                var workerSection = Section(() => TextResources.Get("workSummary.workers"), null);
                workers = Row("WorkSummaryWorkers", details.ShowChildren);
                workerLabel = Label("", 12, Ui.Ink);
                SetRow(workers, "target", workerLabel, null);
                workerSection.Children.Add(workers);

                var addSource = IconAction("AddWorkSummarySource", () => TextResources.Get("conversationComposer.openAddMenu.header.alternativeA"), async () => {
                    owner.CloseTransientSummary();
                    if (owner.composer != null) await owner.composer.PickAttachments();
                });
                var sourceSection = Section(() => TextResources.Get("webSearch.sources"), addSource);
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
                noTask = LocalizedUi.Text(() => TextResources.Get("chatWorkspaceLayout.summaryPreview.text.alternativeA"), 11, Ui.Muted);
                noTask.Margin = new Thickness(6, 2, 6, 4);
                LocalizedUi.Tooltip(noTask, () => TextResources.Get("chatWorkspaceLayout.summaryEmpty.text"));
                Children.Add(noTask);
            }

            bool IsCurrent { get { return !owner.closed && ReferenceEquals(conversation, owner.session); } }

            internal void Refresh(AgentTaskState task, AgentChangeSummaryData summary, SideChatPane[] chats, WorkSummarySource[] nextSources)
            {
                if (!IsCurrent) return;
                noTask.Visibility = task == null ? Visibility.Visible : Visibility.Collapsed;
                var git = owner.summaryRepository;
                var project = owner.SummaryProject;
                linkGitHub.Visibility = project == null ? Visibility.Collapsed : Visibility.Visible;
                linkedRepository = WorkSummaryRepository.Normalize(project == null ? null : project.GitHubRepositoryLink);
                if (linkedRepository.Length == 0 && git != null) linkedRepository = git.Repository;
                repository.Visibility = linkedRepository.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                var branch = git == null ? "" : git.Branch;
                bool matchingOrigin = git != null && linkedRepository.Length > 0 && String.Equals(linkedRepository, git.Repository, StringComparison.OrdinalIgnoreCase);
                repositoryLabel.Text = linkedRepository + (matchingOrigin && branch.Length > 0 ? " · " + branch : "");
                repository.ToolTip = repositoryLabel.Text;
                localBranch.Visibility = branch.Length > 0 && !matchingOrigin ? Visibility.Visible : Visibility.Collapsed;
                if (localBranch.Visibility == Visibility.Visible) LocalizedUi.Set(localBranch, "Text", () => TextResources.Format("workSummary.localBranch", branch));
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

                var children = task == null ? null : task.Children;
                workers.IsEnabled = children != null && children.Count > 0;
                bool running = children != null && task.Status == "running" && conversation.IsBusy;
                int active = running ? children.Count(child => child.Status == "running") : 0;
                int queued = running ? children.Count(child => child.Status == "queued") : 0;
                int total = children == null ? 0 : children.Count;
                LocalizedUi.Set(workerLabel, "Text", () => total == 0 ? TextResources.Get("workSummary.noWorkers") :
                    TextResources.Format("workSummary.workerCounts", active, queued, total));

                RenderSideChats(chats);
                if (!ReferenceEquals(sources, nextSources))
                {
                    sources = nextSources;
                    sourceRows.Children.Clear();
                    foreach (var source in sources.Take(3)) sourceRows.Children.Add(SourceRow(source));
                    if (sources.Length == 0) sourceRows.Children.Add(Empty(() => TextResources.Get("workSummary.noSources")));
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
                if (chats.Length == 0) sideChats.Children.Add(Empty(() => TextResources.Get("workSummary.noSideChats")));
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

            StackPanel Section(Func<string> title, Button action)
            {
                var body = new StackPanel();
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
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(5), Margin = new Thickness(0, 0, 0, 7) });
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

            static FrameworkElement Empty(Func<string> text)
            {
                var label = LocalizedUi.Text(text, 11, Ui.Muted);
                label.Margin = new Thickness(6, 0, 5, 3);
                return label;
            }
        }
    }
}
