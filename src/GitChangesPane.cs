using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Dafeiyu
{
    public sealed partial class ChatWindow
    {
        void OpenGitChanges()
        {
            string path = SummaryWorkspacePath;
            if (closed || String.IsNullOrWhiteSpace(path)) return;
            CloseTransientSummary();
            EnsurePreviewWorkspace();
            var pane = filePreview.Pages<GitChangesPane>().FirstOrDefault(item =>
                String.Equals(item.Workspace, path, StringComparison.OrdinalIgnoreCase));
            if (pane == null)
                filePreview.AddPage(() => TextResources.Get("workSummary.gitChanges.title"), "git-branch",
                    new GitChangesPane(path, () => ReadWorkSummaryRepository(true)));
            else
                filePreview.SelectPage(pane);
            RefreshWorkspaceLayout();
        }
    }

    internal sealed class GitChangesPane : DockPanel, IDisposable
    {
        readonly Action worktreeRefreshed;
        readonly TextBox filter = Ui.Field();
        readonly TextBlock branch = Ui.InlineText("", 12), totals = Ui.InlineText("", 12), notice = Ui.Text("", 12, Ui.Muted);
        readonly TextBlock diffNotice = Ui.Text("", 12, Ui.Muted);
        readonly ListBox files = new ListBox { Name = "GitChangesFiles", HorizontalContentAlignment = HorizontalAlignment.Stretch };
        readonly RichTextBox patch = new RichTextBox { Name = "GitChangesPatch", IsReadOnly = true, IsUndoEnabled = false,
            BorderThickness = new Thickness(0), FontFamily = new FontFamily("Consolas, Cascadia Mono, Microsoft YaHei UI"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        readonly Grid columns = new Grid();
        readonly Grid detail = new Grid();
        readonly Button refresh;
        readonly CheckBox upstream;
        readonly Bold addedTotal = new Bold(), removedTotal = new Bold();
        CancellationTokenSource loading, opening;
        GitWorkspaceChangeSnapshot snapshot;
        bool started, disposed, updatingFiles;

        internal string Workspace { get; private set; }

        internal GitChangesPane(string workspace, Action worktreeRefreshed)
        {
            Workspace = workspace;
            this.worktreeRefreshed = worktreeRefreshed;
            Name = "GitChangesPane";
            Background = ThemeManager.Surface;
            var header = new StackPanel { Margin = new Thickness(14, 9, 14, 9) };
            var actions = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            refresh = LocalizedUi.Button(() => TextResources.Get("workSummary.gitChanges.refresh"));
            refresh.Name = "RefreshGitChanges";
            refresh.Click += async delegate { await RefreshAsync(true); };
            actions.Children.Add(refresh);
            upstream = new CheckBox { Name = "GitChangesCompareUpstream", VerticalAlignment = VerticalAlignment.Center };
            LocalizedUi.Set(upstream, "Content", () => TextResources.Get("workSummary.gitChanges.upstream"));
            upstream.IsEnabled = false;
            upstream.Checked += async delegate { await RefreshAsync(false); };
            upstream.Unchecked += async delegate { if (loading == null) await RefreshAsync(false); };
            actions.Children.Add(upstream);
            header.Children.Add(actions);
            var summary = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            branch.Name = "GitChangesBranch";
            branch.Margin = new Thickness(0, 0, 10, 0);
            summary.Children.Add(branch);
            addedTotal.Foreground = ThemeManager.Brush("#167047", "#65D19A");
            removedTotal.Foreground = ThemeManager.Brush("#AD3E51", "#FF9B9E");
            totals.Inlines.Add(addedTotal);
            totals.Inlines.Add(new Run("  "));
            totals.Inlines.Add(removedTotal);
            summary.Children.Add(totals);
            header.Children.Add(summary);
            notice.Name = "GitChangesNotice";
            header.Children.Add(notice);
            SetDock(header, Dock.Top);
            Children.Add(header);

            var filePanel = new DockPanel { Margin = new Thickness(0, 0, 8, 0) };
            filter.Name = "GitChangesFilter";
            filter.Margin = new Thickness(0, 0, 0, 8);
            LocalizedUi.Automation(filter, () => TextResources.Get("workSummary.gitChanges.filter"));
            LocalizedUi.Tooltip(filter, () => TextResources.Get("workSummary.gitChanges.filter"));
            filter.TextChanged += delegate { if (loading == null) RebuildFiles(); };
            SetDock(filter, Dock.Top);
            filePanel.Children.Add(filter);
            ScrollViewer.SetHorizontalScrollBarVisibility(files, ScrollBarVisibility.Disabled);
            files.Background = ThemeManager.CardSurface;
            files.BorderBrush = Ui.Line;
            files.SelectionChanged += async delegate { if (!updatingFiles) await LoadDiffAsync(); };
            filePanel.Children.Add(files);

            patch.Background = ThemeManager.Surface;
            patch.Foreground = Ui.Ink;
            patch.ContextMenu = ChatUi.EditorMenu(patch, true);
            detail.Children.Add(patch);
            diffNotice.Margin = new Thickness(12);
            diffNotice.VerticalAlignment = VerticalAlignment.Top;
            detail.Children.Add(diffNotice);
            columns.Margin = new Thickness(14, 0, 14, 12);
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.ColumnDefinitions.Add(new ColumnDefinition());
            columns.RowDefinitions.Add(new RowDefinition());
            columns.RowDefinitions.Add(new RowDefinition());
            columns.Children.Add(filePanel);
            columns.Children.Add(detail);
            Children.Add(columns);
            SizeChanged += delegate
            {
                bool stacked = ActualWidth < 535;
                columns.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                columns.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(205);
                columns.RowDefinitions[0].Height = stacked ? new GridLength(146) : new GridLength(1, GridUnitType.Star);
                columns.RowDefinitions[1].Height = stacked ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
                Grid.SetColumn(filePanel, stacked ? 0 : 1);
                Grid.SetRow(detail, stacked ? 1 : 0);
                filePanel.Margin = stacked ? new Thickness(0, 0, 0, 9) : new Thickness(8, 0, 0, 0);
            };
            Loaded += async delegate
            {
                if (started) return;
                started = true;
                await RefreshAsync(false);
            };
            SetPatchNotice(null);
        }

        async Task RefreshAsync(bool refreshSummary)
        {
            if (disposed || loading != null) return;
            if (opening != null) opening.Cancel();
            var request = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            loading = request;
            bool compare = upstream.IsChecked == true;
            refresh.IsEnabled = upstream.IsEnabled = files.IsEnabled = false;
            SetNotice(() => TextResources.Get("workSummary.gitChanges.loading"));
            try
            {
                var value = await Task.Run(() => GitWorkspaceChanges.Read(Workspace, compare, request.Token), request.Token);
                if (disposed) return;
                snapshot = value;
                if (snapshot == null)
                {
                    LocalizedUi.Set(branch, "Text", () => "");
                    SetNotice(() => TextResources.Get("workSummary.gitChanges.noRepository"));
                }
                else
                {
                    LocalizedUi.Set(branch, "Text", () =>
                    {
                        string name = value.Branch == "HEAD" ? TextResources.Get("workSummary.gitChanges.detached") : value.Branch;
                        return value.RepositoryName + " · " + name + " → " + (value.UsesUpstream
                            ? value.Upstream : TextResources.Get("workSummary.gitChanges.workingTree"));
                    });
                    SetNotice(() => String.Join("\n", new[] {
                        value.SkippedUntrackedCount > 0 ? TextResources.Format("workSummary.gitChanges.untracked", value.SkippedUntrackedCount) : "",
                        value.IsTruncated ? TextResources.Get("workSummary.gitChanges.truncated") : "" }.Where(text => text.Length > 0)));
                }
                if (snapshot == null || !snapshot.UsesUpstream) upstream.IsChecked = false;
                SetTotals();
                RebuildFiles();
                await LoadDiffAsync();
                if (refreshSummary && !disposed) worktreeRefreshed();
            }
            catch (Exception error)
            {
                if (!ChatWindow.CanReportGitReadError(error)) throw;
                if (!disposed) SetNotice(() => TextResources.Get("workSummary.gitChanges.failed"));
            }
            finally
            {
                loading = null;
                request.Dispose();
                if (!disposed)
                {
                    refresh.IsEnabled = files.IsEnabled = true;
                    upstream.IsEnabled = snapshot != null && !String.IsNullOrEmpty(snapshot.Upstream);
                }
            }
        }

        void SetTotals()
        {
            addedTotal.Inlines.Clear();
            removedTotal.Inlines.Clear();
            if (snapshot == null) return;
            addedTotal.Inlines.Add(new Run("+" + snapshot.AddedLines.ToString("N0")));
            removedTotal.Inlines.Add(new Run("−" + snapshot.RemovedLines.ToString("N0")));
        }

        void SetNotice(Func<string> message)
        {
            LocalizedUi.Set(notice, "Text", message);
            notice.Visibility = notice.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        void RebuildFiles()
        {
            var previous = files.SelectedItem as ListBoxItem;
            string previousPath = previous == null ? null : ((GitWorkspaceChangeEntry)previous.Tag).Path;
            string query = (filter.Text ?? "").Trim();
            updatingFiles = true;
            files.Items.Clear();
            ListBoxItem selected = null;
            if (snapshot != null)
            {
                foreach (var file in snapshot.Files.Where(entry => query.Length == 0 ||
                    entry.Path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    entry.PreviousPath.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    var row = FileRow(file);
                    files.Items.Add(row);
                    if (file.Path == previousPath) selected = row;
                }
            }
            if (files.Items.Count > 0) files.SelectedItem = selected ?? files.Items[0];
            updatingFiles = false;
            if (loading == null) ObserveSelection();
        }

        async void ObserveSelection() { await LoadDiffAsync(); }

        static ListBoxItem FileRow(GitWorkspaceChangeEntry file)
        {
            var row = new DockPanel { Margin = new Thickness(3, 5, 3, 5) };
            var badge = Ui.InlineText(file.Status.ToString().Substring(0, 1), 11, Ui.Muted);
            badge.Width = 18;
            DockPanel.SetDock(badge, Dock.Left);
            row.Children.Add(badge);
            if (!file.IsBinary)
            {
                var counts = new TextBlock { FontSize = 11, Margin = new Thickness(5, 0, 0, 0) };
                if (file.AddedLines > 0) counts.Inlines.Add(new Run("+" + file.AddedLines.ToString("N0") + " ") {
                    Foreground = ThemeManager.Brush("#167047", "#65D19A") });
                if (file.RemovedLines > 0) counts.Inlines.Add(new Run("−" + file.RemovedLines.ToString("N0")) {
                    Foreground = ThemeManager.Brush("#AD3E51", "#FF9B9E") });
                DockPanel.SetDock(counts, Dock.Right);
                row.Children.Add(counts);
            }
            var name = Ui.InlineText(file.Path, 12);
            row.Children.Add(name);
            string changes = file.IsBinary ? "" : " +" + file.AddedLines.ToString("N0") + " −" + file.RemovedLines.ToString("N0");
            return new ListBoxItem { Tag = file, Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ToolTip = (String.IsNullOrEmpty(file.PreviousPath) ? "" : file.PreviousPath + " → ") + file.Path + changes };
        }

        async Task LoadDiffAsync()
        {
            if (opening != null) opening.Cancel();
            if (disposed) return;
            var item = files.SelectedItem as ListBoxItem;
            var entry = item == null ? null : (GitWorkspaceChangeEntry)item.Tag;
            if (snapshot == null || entry == null)
            {
                if (snapshot == null) SetPatchNotice(null);
                else if (snapshot.Files.Count == 0) SetPatchNotice(() => TextResources.Get("workSummary.gitChanges.noChanges"));
                else SetPatchNotice(() => TextResources.Get("workSummary.gitChanges.noMatches"));
                return;
            }
            if (entry.IsBinary)
            {
                SetPatchNotice(() => TextResources.Get("workSummary.gitChanges.binary"));
                return;
            }

            var current = snapshot;
            var request = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            opening = request;
            SetPatchNotice(() => TextResources.Get("workSummary.gitChanges.loadingDiff"));
            try
            {
                string diff = await Task.Run(() => GitWorkspaceChanges.GetDiff(current, entry, request.Token), request.Token);
                if (!disposed && ReferenceEquals(files.SelectedItem, item) && ReferenceEquals(current, snapshot) && !request.IsCancellationRequested)
                {
                    if (String.IsNullOrWhiteSpace(diff)) SetPatchNotice(() => TextResources.Get("workSummary.gitChanges.noDiff"));
                    else ShowPatch(diff);
                }
            }
            catch (Exception error)
            {
                if (!ChatWindow.CanReportGitReadError(error)) throw;
                if (!disposed && ReferenceEquals(opening, request) && ReferenceEquals(files.SelectedItem, item) &&
                    ReferenceEquals(current, snapshot)) SetPatchNotice(() => TextResources.Get("workSummary.gitChanges.failed"));
            }
            finally
            {
                if (ReferenceEquals(opening, request)) opening = null;
                request.Dispose();
            }
        }

        void SetPatchNotice(Func<string> message)
        {
            patch.Visibility = Visibility.Collapsed;
            patch.Document.Blocks.Clear();
            diffNotice.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
            if (message != null) LocalizedUi.Set(diffNotice, "Text", message);
        }

        void ShowPatch(string diff)
        {
            diffNotice.Visibility = Visibility.Collapsed;
            patch.Visibility = Visibility.Visible;
            var plus = ThemeManager.Brush("#167047", "#65D19A");
            var minus = ThemeManager.Brush("#AD3E51", "#FF9B9E");
            var paragraph = new Paragraph { Margin = new Thickness(0) };
            foreach (string line in diff.Replace("\r\n", "\n").Split('\n'))
            {
                var run = new Run(line + "\n");
                if (line.StartsWith("+", StringComparison.Ordinal)) run.Foreground = plus;
                else if (line.StartsWith("-", StringComparison.Ordinal)) run.Foreground = minus;
                paragraph.Inlines.Add(run);
            }
            patch.Document = new FlowDocument(paragraph) { PagePadding = new Thickness(10), FontSize = 12 };
            patch.ScrollToHome();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (loading != null) loading.Cancel();
            if (opening != null) opening.Cancel();
        }
    }
}
