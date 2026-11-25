using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Dafeiyu
{
    public sealed partial class ChatWindow
    {
        bool workSummaryGitHubActionPending;

        internal sealed partial class WorkSummaryOverview
        {
            Button gitHubMenuButton;
            MenuItem gitHubCommit, gitHubPush, gitHubPullRequest;
            bool localGitHubBranch;

            internal bool GitHubMenuIsOpen
            {
                get { return gitHubMenuButton != null && gitHubMenuButton.ContextMenu.IsOpen; }
            }

            DockPanel BuildGitHubRow()
            {
                var row = new DockPanel();
                gitHubMenuButton = UiIcons.Button("more", () => TextResources.Get("workSummary.gitHub.menu"));
                gitHubMenuButton.Name = "WorkSummaryGitHubActions";
                gitHubMenuButton.Width = gitHubMenuButton.Height = gitHubMenuButton.MinHeight = 25;
                gitHubMenuButton.Margin = new Thickness(0);
                gitHubMenuButton.Padding = new Thickness(5);
                LocalizedUi.Tooltip(gitHubMenuButton, () => TextResources.Get("workSummary.gitHub.menu"));
                DockPanel.SetDock(gitHubMenuButton, Dock.Right);
                row.Children.Add(gitHubMenuButton);
                row.Children.Add(repository);

                var menu = ChatUi.Menu();
                menu.Placement = PlacementMode.Bottom;
                menu.PlacementTarget = gitHubMenuButton;
                menu.Items.Add(GitHubItem("OpenLinkedGitHubRepository", () => TextResources.Get("workSummary.gitHub.open"), "expand", OpenLinkedGitHubRepository));
                gitHubCommit = GitHubItem("CommitWorkSummaryStagedFiles", () => TextResources.Get("workSummary.gitHub.commit"), "file", async () => await owner.RunWorkSummaryGitHubAsync(linkedRepository, GitHubWorkspaceAction.Commit));
                gitHubPush = GitHubItem("PushWorkSummaryGitHubBranch", () => TextResources.Get("workSummary.gitHub.push"), "send", async () => await owner.RunWorkSummaryGitHubAsync(linkedRepository, GitHubWorkspaceAction.Push));
                gitHubPullRequest = GitHubItem("CreateWorkSummaryGitHubPullRequest", () => TextResources.Get("workSummary.gitHub.pullRequest"), "github", async () => await owner.RunWorkSummaryGitHubAsync(linkedRepository, GitHubWorkspaceAction.PullRequest));
                menu.Items.Add(new Separator());
                menu.Items.Add(gitHubCommit);
                menu.Items.Add(gitHubPush);
                menu.Items.Add(gitHubPullRequest);
                menu.Opened += delegate {
                    bool available = IsCurrent && localGitHubBranch && !conversation.IsBusy && !owner.workSummaryGitHubActionPending;
                    gitHubCommit.IsEnabled = gitHubPush.IsEnabled = gitHubPullRequest.IsEnabled = available;
                };
                gitHubMenuButton.ContextMenu = menu;
                gitHubMenuButton.Click += delegate { if (IsCurrent) menu.IsOpen = true; };
                return row;
            }

            void RefreshGitHubMenu(bool linked, bool localBranch)
            {
                localGitHubBranch = localBranch;
                gitHubMenuButton.Visibility = linked ? Visibility.Visible : Visibility.Collapsed;
            }

            void OpenLinkedGitHubRepository()
            {
                if (String.IsNullOrEmpty(linkedRepository)) return;
                owner.CloseTransientSummary();
                MarkdownLinkMenu.OpenExternally(WorkSummaryRepository.GitHubPage(linkedRepository));
            }

            MenuItem GitHubItem(string name, Func<string> label, string icon, Action click)
            {
                var item = new MenuItem { Name = name, Icon = UiIcons.Image(icon, Ui.Ink) };
                LocalizedUi.Set(item, "Header", label);
                item.Click += delegate { if (IsCurrent) click(); };
                return item;
            }
        }

        async Task RunWorkSummaryGitHubAsync(string repository, GitHubWorkspaceAction action)
        {
            var project = SummaryProject;
            if (project == null || workSummaryGitHubActionPending || closed) return;
            var sourceSession = session;
            string workspacePath = project.Workspace;
            string projectId = project.Id;
            string configuredLink = project.GitHubRepositoryLink;
            bool requiresGitHub = action == GitHubWorkspaceAction.PullRequest;
            var connection = requiresGitHub ? pet.Settings.Copy() : null;
            if (requiresGitHub && !GitHubAppConnection.IsConnected(connection))
            {
                CloseTransientSummary();
                ReportGitHubNotice(sourceSession, TextResources.Get("workSummary.gitHub.noToken"));
                return;
            }

            CloseTransientSummary();
            workSummaryGitHubActionPending = true;
            bool attemptedMutation = false;
            using (var cancel = new CancellationTokenSource())
            {
                EventHandler stop = delegate { cancel.Cancel(); };
                Closed += stop;
                try
                {
                    if (sourceSession.IsBusy) { ReportGitHubNotice(sourceSession, TextResources.Get("workSummary.gitHub.busy")); return; }
                    cancel.CancelAfter(TimeSpan.FromSeconds(20));
                    var git = new GitHubAppGit();
                    var snapshot = await Task.Run(() => git.Inspect(workspacePath, repository, action, cancel.Token), cancel.Token);
                    using (var client = requiresGitHub ? GitHubClient.ForConnection(snapshot.Repository, connection) : null)
                    {
                        string baseBranch = client == null ? null : await client.GetDefaultBranchAsync(cancel.Token);
                        cancel.CancelAfter(Timeout.InfiniteTimeSpan);
                        cancel.Token.ThrowIfCancellationRequested();
                        if (closed || !ReferenceEquals(sourceSession, session)) return;
                        var confirmation = ConfirmGitHubAction(snapshot, action, baseBranch);
                        if (confirmation == null || closed || !ReferenceEquals(sourceSession, session)) return;
                        using (pet.DataMaintenance.EnterActivity())
                        using (await pet.Workspaces.BeginProjectConfigurationAsync(projectId))
                        {
                            var current = SummaryProject;
                            if (closed || !ReferenceEquals(sourceSession, session) || current == null || current.Id != projectId ||
                                current.Workspace != workspacePath || current.GitHubRepositoryLink != configuredLink ||
                                (client != null && !GitHubAppConnection.SameConnection(connection, pet.Settings)))
                            {
                                ReportGitHubNotice(sourceSession, TextResources.Get("workSummary.gitHub.changed"));
                                return;
                            }

                            attemptedMutation = true;
                            cancel.CancelAfter(TimeSpan.FromSeconds(action == GitHubWorkspaceAction.Push ? 95 : 25));
                            if (action == GitHubWorkspaceAction.Commit)
                            {
                                await Task.Run(() => git.Commit(snapshot, confirmation.Title, cancel.Token), cancel.Token);
                                ReportGitHubNotice(sourceSession, TextResources.Get("workSummary.gitHub.committed"));
                            }
                            else if (action == GitHubWorkspaceAction.Push)
                            {
                                await Task.Run(() => git.Push(snapshot, cancel.Token), cancel.Token);
                                ReportGitHubNotice(sourceSession, TextResources.Get("workSummary.gitHub.pushed"));
                            }
                            else
                            {
                                await Task.Run(() => git.Verify(snapshot, action, cancel.Token), cancel.Token);
                                Uri page = await client.CreateAppPullRequestAsync(confirmation.Title, confirmation.Body, snapshot.Branch, confirmation.Base, cancel.Token);
                                ReportGitHubNotice(sourceSession, TextResources.Get("workSummary.gitHub.pullCreated"));
                                if (!closed) MarkdownLinkMenu.OpenExternally(page);
                            }
                        }
                    }
                }
                catch (GitHubAppGitException error) { ReportGitHubNotice(sourceSession, error.LocalizedMessage()); }
                catch (OperationCanceledException) { ReportGitHubNotice(sourceSession, MutationFailure(action, attemptedMutation)); }
                catch (Exception error)
                {
                    if (!GitHubAppConnection.IsOperationError(error)) throw;
                    ReportGitHubNotice(sourceSession, MutationFailure(action, attemptedMutation));
                }
                finally
                {
                    Closed -= stop;
                    workSummaryGitHubActionPending = false;
                    if (!closed) ReadWorkSummaryRepository(true);
                }
            }
        }

        static string MutationFailure(GitHubWorkspaceAction action, bool attempted)
        {
            if (attempted && action == GitHubWorkspaceAction.PullRequest) return TextResources.Get("workSummary.gitHub.pullFailed");
            if (attempted && action == GitHubWorkspaceAction.Push) return TextResources.Get("workSummary.gitHub.pushFailed");
            return TextResources.Get("workSummary.gitHub.operationFailed");
        }

        void ReportGitHubNotice(ConversationSession source, string message)
        {
            if (!closed && ReferenceEquals(source, session)) source.ReportNotice(message);
        }

        sealed class GitHubConfirmation { internal string Title, Base, Body; }

        GitHubConfirmation ConfirmGitHubAction(GitHubAppGit.Snapshot snapshot, GitHubWorkspaceAction action, string baseBranch)
        {
            bool commit = action == GitHubWorkspaceAction.Commit, push = action == GitHubWorkspaceAction.Push;
            Func<string> actionLabel = () => commit ? TextResources.Get("workSummary.gitHub.commit") :
                push ? TextResources.Get("workSummary.gitHub.push") : TextResources.Get("workSummary.gitHub.pullRequest");
            var window = new Window { Owner = this };
            LocalizedUi.Window(window, actionLabel, 520, commit ? 275 : push ? 225 : 435);
            var body = new StackPanel();
            var details = LocalizedUi.Text(() => commit ? TextResources.Format("workSummary.gitHub.commitInfo", snapshot.StagedCount, snapshot.Directory, snapshot.Branch) :
                push ? TextResources.Format("workSummary.gitHub.pushInfo", snapshot.Remote, snapshot.Branch, snapshot.Revision.Substring(0, 12)) :
                TextResources.Format("workSummary.gitHub.pullInfo", snapshot.Repository, snapshot.Branch), 12, Ui.Muted);
            details.Margin = new Thickness(0, 0, 0, 12);
            body.Children.Add(details);

            TextBox title = null, target = null, description = null;
            if (!push) title = AddGitHubDialogField(body, "WorkSummaryGitHubActionTitle", () => commit ? TextResources.Get("workSummary.gitHub.commitMessage") : TextResources.Get("workSummary.gitHub.title"), "", 200);
            if (!commit && !push)
            {
                target = AddGitHubDialogField(body, "WorkSummaryGitHubPullRequestBase", () => TextResources.Get("workSummary.gitHub.base"), baseBranch, 240);
                description = AddGitHubDialogField(body, "WorkSummaryGitHubPullRequestBody", () => TextResources.Get("workSummary.gitHub.body"), "", 12000, true);
            }
            var notice = Ui.Text("", 11, Ui.Muted);
            body.Children.Add(notice);
            var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 9, 0, 0) };
            var cancel = LocalizedUi.Button(() => TextResources.Get("common.cancel"));
            cancel.IsCancel = true;
            var apply = LocalizedUi.Button(actionLabel, true);
            apply.Name = "ConfirmWorkSummaryGitHubAction";
            actions.Children.Add(cancel);
            actions.Children.Add(apply);
            body.Children.Add(actions);

            GitHubConfirmation confirmed = null;
            apply.Click += delegate {
                var value = new GitHubConfirmation { Title = title == null ? "" : title.Text.Trim(), Base = target == null ? "" : target.Text.Trim(), Body = description == null ? "" : description.Text };
                if ((!push && (value.Title.Length == 0 || value.Title.Any(Char.IsControl))) ||
                    (target != null && (value.Base.Length == 0 || value.Base == snapshot.Branch || value.Base.Any(Char.IsControl))))
                {
                    LocalizedUi.Set(notice, "Text", () => TextResources.Get("workSummary.gitHub.invalidInput"));
                    return;
                }
                confirmed = value;
                window.DialogResult = true;
            };
            DialogUi.ApplyTitled(window, body);
            if (title != null) window.Loaded += delegate { title.Focus(); };
            return window.ShowDialog() == true ? confirmed : null;
        }

        static TextBox AddGitHubDialogField(Panel body, string name, Func<string> caption, string initial, int length, bool multiline = false)
        {
            var hint = LocalizedUi.Text(caption, 12, Ui.Ink);
            hint.Margin = new Thickness(0, 0, 0, 4);
            body.Children.Add(hint);
            var input = Ui.Field(initial, multiline);
            input.Name = name;
            input.MaxLength = length;
            input.Margin = new Thickness(0, 0, 0, 9);
            if (multiline) input.Height = 72;
            LocalizedUi.Automation(input, caption);
            body.Children.Add(input);
            return input;
        }
    }
}
