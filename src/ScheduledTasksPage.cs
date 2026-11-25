#if NET10_0
#pragma warning disable 8600, 8601, 8602, 8603, 8604, 8618, 8622, 8625
#endif
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Dafeiyu;
#if NET10_0
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Element = Avalonia.Controls.Control;
namespace DeskPet.Desktop
#else
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Element = System.Windows.FrameworkElement;
namespace Dafeiyu
#endif
{
    internal sealed partial class ScheduledTasksPage : UserControl, IDisposable
    {
        readonly ScheduledTaskRuntime runtime;
        readonly ListBox tasks = new ListBox { Name = "ScheduledTaskList", BorderThickness = new Thickness(0) };
        readonly TextBox search;
        readonly StackPanel details = new StackPanel();
        readonly StackPanel composer = new StackPanel();
        readonly StackPanel pendingAccess = new StackPanel();
        readonly TextBlock error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        readonly ScrollViewer detailScroll;
        readonly List<ChatMessage> turns = new List<ChatMessage>();
        string selectedId, selectedRun;
        string draftInput = "";
        string draftError;
        bool rebuilding, drafting, sending, disposed;
        ScheduledTaskDefinition prepared;
        CancellationTokenSource draftCancellation;
        Window editor;
        internal event Action ShowMissedRequested;

        internal ScheduledTasksPage(ScheduledTaskRuntime runtime)
        {
            this.runtime = runtime;
            Name = "ScheduledTasksPage";
            search = Input("", () => TextResources.Get("scheduledTasks.search"), "ScheduledTaskSearch");
            search.TextChanged += delegate { RefreshList(); };
            tasks.SelectionChanged += delegate
            {
                if (rebuilding) return;
                var selection = tasks.SelectedItem as ListBoxItem;
                if (selection == null) return;
                selectedId = selection.Tag as string;
                selectedRun = null;
                drafting = false;
                RenderDetails(true);
            };
            var rail = new Grid { Margin = new Thickness(16) };
            Row(rail, Caption(() => TextResources.Get("scheduledTasks.title"), 21, true), GridLength.Auto);
            Row(rail, Actions(
                ButtonFor(() => TextResources.Get("scheduledTasks.new"), "NewScheduledTask", () => Edit(null), true),
                ButtonFor(() => TextResources.Get("scheduledTasks.draft"), "DraftScheduledTask", () => OpenDraft(null))), GridLength.Auto);
            Row(rail, Field(() => TextResources.Get("scheduledTasks.search"), search), GridLength.Auto);
            Row(rail, pendingAccess, GridLength.Auto);
            Row(rail, tasks, new GridLength(1, GridUnitType.Star));
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(244) });
            root.ColumnDefinitions.Add(new ColumnDefinition());
            root.Children.Add(new Border { Background = SubtleSurface(), Child = rail });
            var content = new Grid { MaxWidth = 920, Margin = new Thickness(26, 18, 26, 18) };
            Row(content, error, GridLength.Auto);
            detailScroll = Scroll(details);
            Row(content, detailScroll, new GridLength(1, GridUnitType.Star));
            Row(content, composer, GridLength.Auto);
            Grid.SetColumn(content, 1);
            root.Children.Add(content);
            Content = root;
            runtime.Changed += Refresh;
            Refresh();
        }

        internal void ShowTask(string id, string runId = null)
        {
            selectedId = id;
            selectedRun = runId;
            drafting = false;
            if (!String.IsNullOrEmpty(search.Text)) search.Text = "";
            Refresh();
            detailScroll.ScrollToHome();
        }

        void Refresh()
        {
            if (disposed) return;
            error.Text = runtime.Error;
            SetVisible(error, !String.IsNullOrEmpty(runtime.Error));
            pendingAccess.Children.Clear();
            var count = runtime.Pending().Count;
            if (count != 0)
                pendingAccess.Children.Add(ButtonFor(() => TextResources.Format("scheduledTasks.missedView", count), "ReviewMissedTasks", OpenMissed));
            RefreshList();
            if (!drafting) RenderDetails(false);
        }

        void RefreshList()
        {
            if (rebuilding || disposed) return;
            rebuilding = true;
            try
            {
                var snapshots = runtime.List().OrderBy(task => task.Definition.Title, StringComparer.CurrentCultureIgnoreCase).ToArray();
                var query = (search.Text ?? "").Trim();
                var items = new List<ListBoxItem>();
                foreach (var task in snapshots)
                {
                    if (query.Length != 0 && CultureInfo.CurrentCulture.CompareInfo.IndexOf(task.Definition.Title, query, CompareOptions.IgnoreCase) < 0
                        && CultureInfo.CurrentCulture.CompareInfo.IndexOf(task.Definition.Content, query, CompareOptions.IgnoreCase) < 0) continue;
                    var captions = new StackPanel();
                    captions.Children.Add(Caption(() => task.Definition.Title, 14, true));
                    captions.Children.Add(Caption(() => NextText(task.Next(DateTimeOffset.UtcNow, TimeZoneInfo.Local)), 11));
                    items.Add(new ListBoxItem { Tag = task.Definition.Id, Content = captions });
                }
                tasks.ItemsSource = items;
                tasks.SelectedItem = items.FirstOrDefault(item => (string)item.Tag == selectedId && !drafting);
            }
            finally { rebuilding = false; }
        }

        void RenderDetails(bool scrollTop)
        {
            var task = runtime.List().FirstOrDefault(item => item.Definition.Id == selectedId);
            details.Children.Clear();
            composer.Children.Clear();
            if (scrollTop) detailScroll.ScrollToHome();
            if (task == null)
            {
                var empty = runtime.List().Count == 0;
                details.Children.Add(Caption(empty ? (Func<string>)(() => TextResources.Get("scheduledTasks.empty"))
                    : () => TextResources.Get("scheduledTasks.noSelection"), 25, true));
                if (empty)
                {
                    details.Children.Add(Caption(() => TextResources.Get("scheduledTasks.emptyHint")));
                    AddTemplate(() => TextResources.Get("scheduledTasks.templateWater"), () => TextResources.Get("scheduledTasks.templateWaterPrompt"), "WaterTaskTemplate");
                    AddTemplate(() => TextResources.Get("scheduledTasks.templateReport"), () => TextResources.Get("scheduledTasks.templateReportPrompt"), "ReportTaskTemplate");
                    AddTemplate(() => TextResources.Get("scheduledTasks.templateWeekend"), () => TextResources.Get("scheduledTasks.templateWeekendPrompt"), "WeekendTaskTemplate");
                }
                return;
            }
            var definition = task.Definition;
            details.Children.Add(Caption(() => definition.Title, 25, true));
            details.Children.Add(Caption(() => definition.Content));
            details.Children.Add(Caption(() => NextText(task.Next(DateTimeOffset.UtcNow, TimeZoneInfo.Local))));
            var delete = ButtonFor(() => TextResources.Get("common.delete"), "DeleteScheduledTask", () => DeleteTask(definition));
            delete.IsEnabled = !task.Runs.Any(run => run.State == ScheduledRunState.Running);
            details.Children.Add(Actions(ButtonFor(() => TextResources.Get("scheduledTasks.edit"), "EditScheduledTask", () => Edit(definition)),
                ButtonFor(definition.Enabled ? (Func<string>)(() => TextResources.Get("scheduledTasks.pause")) : () => TextResources.Get("scheduledTasks.enable"),
                    "ToggleScheduledTask", () => TryAction(() => runtime.SetEnabled(definition.Id, !definition.Enabled))), delete));
            details.Children.Add(Caption(() => TextResources.Get("scheduledTasks.history"), 18, true));
            if (task.Runs.Count == 0) details.Children.Add(Caption(() => TextResources.Get("scheduledTasks.noHistory")));
            foreach (var run in task.Runs)
            {
                var row = new StackPanel();
                row.Children.Add(Caption(() => FormatLocal(run.DueUtc) + " · " + StateText(run.State), 13, true));
                if (run.State == ScheduledRunState.Pending)
                    row.Children.Add(DecisionButtons(run, runtime, error));
                if (!String.IsNullOrEmpty(run.Result))
                {
                    var result = new Expander { Header = TextResources.Get("scheduledTasks.openResult"), IsExpanded = run.Id == selectedRun || run.Id == task.Runs[0].Id };
                    LocalizedUi.Set(result, "Header", () => TextResources.Get("scheduledTasks.openResult"));
                    var body = new StackPanel();
                    body.Children.Add(ResultView(run.Result));
                    body.Children.Add(ButtonFor(() => TextResources.Get("scheduledTasks.copyResult"), null, () => CopyToClipboard(run.Result)));
                    result.Content = body;
                    row.Children.Add(result);
                }
                details.Children.Add(Card(row));
            }
        }

        void AddTemplate(Func<string> heading, Func<string> description, string name)
        {
            var title = Caption(heading, 15, true);
            var subtitle = Caption(description);
            var card = ButtonFor(() => "", name, () => OpenDraft(description()));
            LiveTextBindings.TakeRender(card, "Content", card.Content);
            card.Content = Column(title, subtitle);
            LocalizedUi.Automation(card, heading);
            card.HorizontalAlignment = HorizontalAlignment.Stretch;
            card.HorizontalContentAlignment = HorizontalAlignment.Left;
            details.Children.Add(card);
        }

        void OpenDraft(string seed)
        {
            if (seed != null && !sending)
            {
                turns.Clear();
                draftInput = seed;
                prepared = null;
                draftError = null;
            }
            drafting = true;
            RefreshList();
            RenderDraft(true);
        }

        void RenderDraft(bool focus)
        {
            details.Children.Clear();
            composer.Children.Clear();
            details.Children.Add(Caption(() => TextResources.Get("scheduledTasks.draft"), 25, true));
            if (turns.Count == 0) details.Children.Add(Caption(() => TextResources.Get("scheduledTasks.draftPrompt")));
            foreach (var turn in turns)
            {
                var bubble = Card(Caption(() => turn.Content));
                bubble.MaxWidth = 580;
                bubble.HorizontalAlignment = turn.Role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                details.Children.Add(bubble);
            }
            if (!String.IsNullOrEmpty(draftError)) details.Children.Add(Caption(() => draftError));
            if (prepared != null)
                details.Children.Add(ButtonFor(() => TextResources.Get("scheduledTasks.draftReady"), "ReviewScheduledDraft", () => Edit(prepared), true));
            var input = Input(draftInput, () => TextResources.Get("scheduledTasks.draftPrompt"), "ScheduledTaskDraftInput");
            input.MaxLength = 16000;
            input.AcceptsReturn = true;
            input.TextWrapping = TextWrapping.Wrap;
            input.MinHeight = 74;
            input.IsEnabled = !sending;
            input.TextChanged += delegate { draftInput = input.Text ?? ""; };
            composer.Children.Add(input);
            composer.Children.Add(Actions(
                sending ? ButtonFor(() => TextResources.Get("scheduledTasks.stop"), "StopScheduledDraft", () => draftCancellation.Cancel())
                    : ButtonFor(() => TextResources.Get("scheduledTasks.send"), "SendScheduledDraft", SendDraft, true),
                ButtonFor(() => TextResources.Get("scheduledTasks.manual"), "OpenScheduledTaskForm", () => Edit(prepared))));
            detailScroll.ScrollToEnd();
            if (focus && !sending) input.Focus();
        }

        async void SendDraft()
        {
            var text = draftInput.Trim();
            if (sending || text.Length == 0) return;
            draftError = null;
            prepared = null;
            turns.Add(new ChatMessage("user", text));
            draftInput = "";
            sending = true;
            draftCancellation = new CancellationTokenSource();
            RenderDraft(false);
            try
            {
                var response = await runtime.DraftAsync(turns.ToArray(), draftCancellation.Token);
                if (disposed) return;
                prepared = response.Definition;
                if (prepared == null) turns.Add(new ChatMessage("assistant", response.Question));
            }
            catch (OperationCanceledException) { }
            catch (ApiException exception) { draftError = exception.Message; }
            catch (Exception) { draftError = TextResources.Get("scheduledTasks.draftError"); }
            finally
            {
                draftCancellation.Dispose();
                draftCancellation = null;
                sending = false;
            }
            if (disposed || !drafting) return;
            RenderDraft(true);
            if (prepared != null && editor == null && IsVisible && FindWindow(this) != null) Edit(prepared);
        }

        void OpenMissed()
        {
            var handler = ShowMissedRequested; if (handler != null) handler();
        }

        internal static Window ShowMissed(Window owner, ScheduledTaskRuntime runtime)
        {
            var window = MakeWindow(() => TextResources.Get("scheduledTasks.missedTitle"), "MissedScheduledTasksDialog", 620, 420);
            var rows = new StackPanel();
            var failure = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var done = ButtonFor(() => TextResources.Get("common.close"), "CloseMissedTasks", window.Close);
            var body = new Grid();
            Row(body, Caption(() => TextResources.Get("scheduledTasks.missedHint")), GridLength.Auto);
            Row(body, Scroll(rows), new GridLength(1, GridUnitType.Star));
            Row(body, failure, GridLength.Auto);
            Row(body, Footer(done), GridLength.Auto);
            Action refresh = () =>
            {
                rows.Children.Clear();
                foreach (var run in runtime.Pending())
                {
                    var stack = Column(Caption(() => TextResources.Format("scheduledTasks.missedTask", run.Title, FormatLocal(run.DueUtc)), 14, true),
                        DecisionButtons(run, runtime, failure));
                    rows.Children.Add(Card(stack));
                }
                if (rows.Children.Count == 0 && window.IsVisible) window.Close();
            };
            refresh();
            runtime.Changed += refresh;
            window.Closed += delegate { runtime.Changed -= refresh; };
            DialogUi.ApplyTitled(window, body);
            ShowWindow(window, owner, false);
            done.Focus();
            return window;
        }

        static Element DecisionButtons(ScheduledTaskRun run, ScheduledTaskRuntime runtime, TextBlock failure)
        {
            return Actions(
                ButtonFor(() => TextResources.Get("scheduledTasks.skip"), "SkipMissedTask", () => TryAction(() => runtime.Resolve(run, false), failure)),
                ButtonFor(() => TextResources.Get("scheduledTasks.runNow"), "RunMissedTask", () => TryAction(() => runtime.Resolve(run, true), failure), true));
        }

        void DeleteTask(ScheduledTaskDefinition task)
        {
            var dialog = MakeWindow(() => TextResources.Get("scheduledTasks.deleteTitle"), "DeleteScheduledTaskDialog", 510, 235);
            var failure = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var cancel = ButtonFor(() => TextResources.Get("common.cancel"), "CancelScheduledDeletion", dialog.Close);
            var delete = ButtonFor(() => TextResources.Get("common.delete"), "ConfirmScheduledDeletion", () => TryAction(() =>
            {
                runtime.Delete(task.Id);
                dialog.Close();
            }, failure));
            DialogUi.ApplyTitled(dialog, Column(Caption(() => TextResources.Format("scheduledTasks.deleteDescription", task.Title)), failure, Footer(cancel, delete)));
            ShowWindow(dialog, FindWindow(this), true);
        }

        void TryAction(Action action) { TryAction(action, error); }

        static void TryAction(Action action, TextBlock failure)
        {
            failure.Text = "";
            try { action(); }
            catch (InvalidOperationException exception) { failure.Text = exception.Message; SetVisible(failure, true); }
            catch (System.IO.IOException) { failure.Text = TextResources.Get("scheduledTasks.storageError"); SetVisible(failure, true); }
            catch (UnauthorizedAccessException) { failure.Text = TextResources.Get("scheduledTasks.storageError"); SetVisible(failure, true); }
        }

        static string FormatLocal(DateTimeOffset utc) { return utc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture); }

        static string NextText(ScheduledOccurrence? next)
        {
            return next.HasValue ? TextResources.Format("scheduledTasks.next", FormatLocal(next.Value.Utc)) : TextResources.Get("scheduledTasks.noNext");
        }

        internal static string StateText(ScheduledRunState state)
        {
            switch (state)
            {
                case ScheduledRunState.Pending: return TextResources.Get("scheduledTasks.pending");
                case ScheduledRunState.Queued: return TextResources.Get("scheduledTasks.queued");
                case ScheduledRunState.Running: return TextResources.Get("scheduledTasks.running");
                case ScheduledRunState.Succeeded: return TextResources.Get("scheduledTasks.succeeded");
                case ScheduledRunState.Failed: return TextResources.Get("scheduledTasks.failed");
                case ScheduledRunState.Skipped: return TextResources.Get("scheduledTasks.skipped");
                default: return TextResources.Get("scheduledTasks.interrupted");
            }
        }

        public void Dispose()
        {
            disposed = true;
            runtime.Changed -= Refresh;
            if (draftCancellation != null) draftCancellation.Cancel();
        }
    }
}
