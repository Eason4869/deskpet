using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Dafeiyu
{
    public sealed partial class ChatWindow
    {
        WorkSummaryRepository summaryRepository;
        GitWorkspaceChangeSnapshot summaryGitChanges;
        string summaryRepositoryWorkspace;
        ConversationSession summaryRepositorySession;
        int summaryRepositoryRequest;

        async void ReadWorkSummaryRepository(bool refresh = false)
        {
            var sourceSession = session;
            var path = SummaryWorkspacePath;
            bool same = ReferenceEquals(sourceSession, summaryRepositorySession) &&
                String.Equals(path, summaryRepositoryWorkspace, StringComparison.OrdinalIgnoreCase);
            if (!refresh && same) return;
            if (!same) { summaryRepository = null; summaryGitChanges = null; }
            summaryRepositorySession = sourceSession;
            summaryRepositoryWorkspace = path;
            if (String.IsNullOrWhiteSpace(path)) return;
            int request = ++summaryRepositoryRequest;
            WorkSummaryRepository result = null;
            GitWorkspaceChangeSnapshot changes = null;
            using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
            {
                try { result = await Task.Run(() => WorkSummaryRepository.Read(path, cancel.Token)).ConfigureAwait(false); }
                catch (Exception error) { if (!CanReportGitReadError(error)) throw; }
                if (result != null)
                {
                    try { changes = await Task.Run(() => GitWorkspaceChanges.Read(path, false, cancel.Token)).ConfigureAwait(false); }
                    catch (Exception error) { if (!CanReportGitReadError(error)) throw; }
                }
            }
            var publication = Dispatcher.InvokeAsync(() =>
            {
                if (closed || request != summaryRepositoryRequest || !ReferenceEquals(sourceSession, session) ||
                    !String.Equals(path, summaryRepositoryWorkspace, StringComparison.OrdinalIgnoreCase)) return;
                summaryRepository = result;
                summaryGitChanges = changes;
                RefreshWorkSummaryOverviews();
            });
            try { await publication.Task.ConfigureAwait(false); }
            catch (TaskCanceledException) { if (publication.Status != DispatcherOperationStatus.Aborted) throw; }
        }

        internal static bool CanReportGitReadError(Exception error)
        {
            return error is IOException || error is UnauthorizedAccessException || error is ArgumentException ||
                error is OperationCanceledException || error is InvalidOperationException || error is System.ComponentModel.Win32Exception ||
                error is NotSupportedException || error is System.Security.SecurityException;
        }

        string SummaryWorkspacePath
        {
            get
            {
                var project = SummaryProject;
                return project != null ? project.Workspace : WorkEntry && workspace != null ? workspace.ProjectWorkspace : null;
            }
        }

        ProjectDefinition SummaryProject
        {
            get { return workspace == null ? null : pet.Projects.Projects.FirstOrDefault(project => project.Id == workspace.ProjectId && !project.Archived); }
        }

        void EditWorkSummaryRepository()
        {
            CloseTransientSummary();
            var project = SummaryProject;
            if (project == null) return;

            var window = new Window { Owner = this };
            LocalizedUi.Window(window, () => TextResources.Get("workSummary.linkGitHub"), 500, 270);
            var body = new StackPanel();
            body.Children.Add(LocalizedUi.Text(() => TextResources.Get("workSummary.gitHubHint"), 12, Ui.Muted));
            string detected = summaryRepository == null ? "" : summaryRepository.Repository;
            var input = Ui.Field(project.GitHubRepositoryLink ?? detected);
            input.MaxLength = 300;
            input.Name = "WorkSummaryGitHubRepository";
            input.Margin = new Thickness(0, 11, 0, 7);
            body.Children.Add(input);
            var notice = Ui.Text("", 12, Ui.Muted);
            notice.Name = "WorkSummaryGitHubStatus";
            body.Children.Add(notice);

            var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 9, 0, 0) };
            var cancel = LocalizedUi.Button(() => TextResources.Get("common.cancel"));
            cancel.Click += delegate { window.Close(); };
            var save = LocalizedUi.Button(() => TextResources.Get("common.save"), true);
            save.Name = "SaveWorkSummaryGitHub";
            save.IsDefault = true;
            bool saving = false;
            window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs args) { if (saving) args.Cancel = true; };
            save.Click += async delegate {
                if (saving) return;
                string repo = WorkSummaryRepository.Normalize(input.Text);
                if (!String.IsNullOrWhiteSpace(input.Text) && repo.Length == 0)
                {
                    LocalizedUi.Set(notice, "Text", () => TextResources.Get("gitHubUpdates.repository.text"));
                    return;
                }

                saving = true;
                save.IsEnabled = cancel.IsEnabled = false;
                try
                {
                    using (pet.DataMaintenance.EnterActivity())
                    using (await pet.Workspaces.BeginProjectConfigurationAsync(project.Id))
                    {
                        var original = ProjectCatalogStore.ConfigurationSnapshot(project);
                        var edited = ProjectCatalogStore.ConfigurationSnapshot(project);
                        edited.GitHubRepositoryLink = repo.Length == 0 ? null : repo;
                        using (var publication = await pet.ProjectStore.SaveProjectConfigurationAsync(original, edited))
                        {
                            var current = pet.Projects.Projects.FirstOrDefault(item => item.Id == project.Id);
                            if (current != null) publication.Publish(current);
                        }
                    }
                    saving = false;
                    window.Close();
                    RefreshWorkSummaryOverviews();
                }
                catch (Exception error)
                {
                    if (!(error is IOException || error is UnauthorizedAccessException || error is InvalidOperationException ||
                        error is System.Runtime.Serialization.SerializationException || error is ArgumentException)) throw;
                    LocalizedUi.Set(notice, "Text", () => TextResources.Get("projectCatalog.save.text"));
                }
                finally { saving = false; save.IsEnabled = cancel.IsEnabled = true; }
            };
            actions.Children.Add(cancel);
            actions.Children.Add(save);
            body.Children.Add(actions);
            DialogUi.ApplyTitled(window, body);
            window.Loaded += delegate { input.Focus(); };
            window.ShowDialog();
        }
    }
}
