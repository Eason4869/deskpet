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
using Avalonia.Media.Imaging;
using Element = Avalonia.Controls.Control;
namespace DeskPet.Desktop
#else
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Element = System.Windows.FrameworkElement;
namespace Dafeiyu
#endif
{
    internal sealed partial class ScheduledTasksPage : UserControl, IDisposable
    {
        const double RailWidth = 244;
        const double TemplateInset = 6;
        const double DraftGutter = 20;
        readonly ScheduledTaskRuntime runtime;
        readonly ListBox tasks = new ListBox { Name = "ScheduledTaskList", BorderThickness = new Thickness(0) };
        readonly TextBox search;
        readonly StackPanel details = new StackPanel();
        readonly StackPanel composer = new StackPanel();
        readonly StackPanel pendingAccess = new StackPanel();
        readonly TextBlock error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        readonly Grid rail = new Grid { Margin = new Thickness(16) };
        readonly Grid content = new Grid { MaxWidth = 920, Margin = new Thickness(26, 18, 26, 18) };
        readonly ScrollViewer detailScroll;
        readonly ColumnDefinition railColumn = new ColumnDefinition { Width = new GridLength(RailWidth) };
        readonly List<ChatMessage> turns = new List<ChatMessage>();
#if NET10_0
        readonly List<Bitmap> templateImages = new List<Bitmap>();
#endif
        UniformGrid starterTemplates;
        double pageWidth;
        bool showingTemplates, templatesForEmptyList;
        string selectedId, selectedRun, draftTemplate;
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
            Row(rail, Caption(() => TextResources.Get("scheduledTasks.title"), 21, true), GridLength.Auto);
            Row(rail, Actions(
                RailAction(() => TextResources.Get("scheduledTasks.new"), "NewScheduledTask", () => Edit(null), true),
                RailAction(() => TextResources.Get("scheduledTasks.draft"), "DraftScheduledTask", () => OpenDraft(null))), GridLength.Auto);
            Row(rail, Field(() => TextResources.Get("scheduledTasks.search"), search), GridLength.Auto);
            Row(rail, pendingAccess, GridLength.Auto);
            Row(rail, tasks, new GridLength(1, GridUnitType.Star));
            var root = new Grid();
            root.ColumnDefinitions.Add(railColumn);
            root.ColumnDefinitions.Add(new ColumnDefinition());
            root.SizeChanged += delegate(object sender, SizeChangedEventArgs args)
            {
                pageWidth = args.NewSize.Width;
                ResizeRail();
            };
            root.Children.Add(new Border { Background = SubtleSurface(), Child = rail });
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

        static Button RailAction(Func<string> label, string name, Action open, bool primary = false)
        {
            var button = ButtonFor(label, name, open, primary);
#if !NET10_0
            LiveTextBindings.TakeRender(button, "Content", button.Content);
            button.Content = LocalizedUi.Text(label, button.FontSize, button.Foreground);
#endif
            LocalizedUi.Tooltip(button, label);
            return button;
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

        void RenderDetails(bool scrollTop, bool openTemplates = false)
        {
            var snapshots = runtime.List();
            var task = snapshots.FirstOrDefault(item => item.Definition.Id == selectedId);
            bool empty = snapshots.Count == 0;
            bool templates = task == null && (empty || openTemplates || (showingTemplates && !scrollTop));
            if (templates && showingTemplates && templatesForEmptyList == empty && !scrollTop) return;
            details.Children.Clear();
            composer.Children.Clear();
            templatesForEmptyList = empty;
            SetStarterLayout(templates);
            if (scrollTop) detailScroll.ScrollToHome();
            if (task == null)
            {
                if (templates)
                {
                    if (empty) details.Children.Add(Caption(() => TextResources.Get("scheduledTasks.empty"), 25, true));
                    details.Children.Add(Caption(() => TextResources.Get("scheduledTasks.emptyHint")));
                    if (starterTemplates == null) starterTemplates = CreateStarterTemplates();
                    details.Children.Add(starterTemplates);
                }
                else details.Children.Add(Caption(() => TextResources.Get("scheduledTasks.noSelection"), 25, true));
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

        void SetStarterLayout(bool show)
        {
            showingTemplates = show;
            details.MaxWidth = show ? 760 : Double.PositiveInfinity;
            ResizeRail();
        }

        void ResizeRail()
        {
            double width = showingTemplates && pageWidth > 0 ? Math.Max(120, Math.Min(RailWidth, pageWidth * .29)) : RailWidth;
            if (Math.Abs(railColumn.Width.Value - width) >= .5) railColumn.Width = new GridLength(width);
            var railMargin = new Thickness(width < 180 ? 10 : 16, 16, width < 180 ? 10 : 16, 16);
            if (rail.Margin != railMargin) rail.Margin = railMargin;
            double inset = showingTemplates && pageWidth < 540 ? 16 : 26;
            double gutter = drafting ? DraftGutter : 0;
            var contentMargin = new Thickness(inset - gutter, 18, inset, 18);
            if (content.Margin != contentMargin) content.Margin = contentMargin;
            composer.Margin = new Thickness(gutter, 0, 0, 0);
        }

        UniformGrid CreateStarterTemplates()
        {
            var grid = new UniformGrid { Name = "ScheduledTaskTemplates", Columns = 3, Margin = new Thickness(0, 7, 0, 0) };
            grid.Children.Add(TemplateTile(() => TextResources.Get("scheduledTasks.templateWater"), () => TextResources.Get("scheduledTasks.templateWaterPrompt"), "WaterTaskTemplate", "water"));
            grid.Children.Add(TemplateTile(() => TextResources.Get("scheduledTasks.templateReport"), () => TextResources.Get("scheduledTasks.templateReportPrompt"), "ReportTaskTemplate", "report"));
            grid.Children.Add(TemplateTile(() => TextResources.Get("scheduledTasks.templateWeekend"), () => TextResources.Get("scheduledTasks.templateWeekendPrompt"), "WeekendTaskTemplate", "weekend"));
            grid.Children.Add(TemplateTile(() => TextResources.Get("scheduledTasks.templatePlan"), () => TextResources.Get("scheduledTasks.templatePlanPrompt"), "PlanningTaskTemplate", "planning"));
            grid.Children.Add(TemplateTile(() => TextResources.Get("scheduledTasks.templateStretch"), () => TextResources.Get("scheduledTasks.templateStretchPrompt"), "StretchTaskTemplate", "stretch"));
            grid.Children.Add(TemplateTile(() => TextResources.Get("scheduledTasks.templateReading"), () => TextResources.Get("scheduledTasks.templateReadingPrompt"), "ReadingTaskTemplate", "reading"));
            grid.SizeChanged += delegate(object sender, SizeChangedEventArgs args)
            {
                double width = args.NewSize.Width;
                if (width <= 0) return;
                int columns = width >= 552 ? 3 : 2;
                if (grid.Columns != columns) grid.Columns = columns;
                double side = Math.Max(1, width / columns - TemplateInset * 2);
                foreach (Button tile in grid.Children)
                    if (Math.Abs(tile.Height - side) >= .5 || Double.IsNaN(tile.Height)) tile.Height = side;
            };
            return grid;
        }

        Button TemplateTile(Func<string> heading, Func<string> prompt, string name, string icon)
        {
            var title = Caption(heading, 14, true);
            title.Margin = new Thickness(0, 0, 0, 4);
            title.LineHeight = 17;
            title.MaxHeight = 34;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            var description = Caption(prompt, 12);
            description.Margin = new Thickness(0);
            description.LineHeight = 15;
            description.MaxHeight = 45;
            description.TextTrimming = TextTrimming.CharacterEllipsis;
            var face = new Grid();
            var artwork = TemplateImage(icon);
            if (artwork != null) Row(face, artwork, GridLength.Auto);
            Row(face, title, GridLength.Auto);
            Row(face, description, new GridLength(1, GridUnitType.Star));
            var card = ButtonFor(heading, name, () => OpenDraft(prompt(), name));
            LiveTextBindings.TakeRender(card, "Content", card.Content);
            card.Content = face;
            card.MinHeight = 0;
            card.Height = 140;
            card.Margin = new Thickness(TemplateInset);
            card.Padding = new Thickness(11);
            card.HorizontalAlignment = HorizontalAlignment.Stretch;
            card.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            card.VerticalContentAlignment = VerticalAlignment.Center;
#if NET10_0
            card.Background = DesktopTheme.CardSurface;
            card.BorderBrush = DesktopTheme.CardBorder;
            description.Foreground = SettingsVisual.Muted;
            title.MaxLines = 2;
            description.MaxLines = 3;
            LiveTextBindings.Bind(card, "AutomationHelpText", prompt,
                value => Avalonia.Automation.AutomationProperties.GetHelpText((Button)value),
                (value, text) => Avalonia.Automation.AutomationProperties.SetHelpText((Button)value, (string)text));
#else
            card.Background = ThemeManager.CardSurface;
            card.BorderBrush = ThemeManager.CardBorder;
            description.Foreground = Ui.Muted;
            title.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            description.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            LocalizedUi.HelpText(card, prompt);
#endif
            LocalizedUi.Tooltip(card, prompt);
            card.SizeChanged += delegate(object sender, SizeChangedEventArgs args)
            {
                bool compact = args.NewSize.Width < 150;
                double padding = compact ? 8 : 14;
                card.Padding = new Thickness(padding);
                description.MaxHeight = compact ? 30 : 45;
#if NET10_0
                description.MaxLines = compact ? 2 : 3;
#else
                face.Width = Math.Max(0, args.NewSize.Width - 2 * (padding + 1));
#endif
                if (artwork != null)
                    artwork.Width = artwork.Height = Math.Max(24, Math.Min(62, args.NewSize.Width * .25));
            };
            return card;
        }

        Image TemplateImage(string icon)
        {
            using (var stream = typeof(ScheduledTasksPage).Assembly.GetManifestResourceStream("Dafeiyu.ScheduledTasks." + icon + ".png"))
            {
                if (stream == null) return null;
#if NET10_0
                var bitmap = new Bitmap(stream);
                templateImages.Add(bitmap);
#else
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
#endif
                return new Image { Source = bitmap, Width = 40, Height = 40, Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 7), IsHitTestVisible = false };
            }
        }

        void OpenDraft(string seed, string template = null)
        {
            if (seed != null && !sending)
            {
                turns.Clear();
                draftInput = seed;
                prepared = null;
                draftError = null;
            }
            draftTemplate = template;
            drafting = true;
            RefreshList();
            RenderDraft(true);
        }

        void ReturnToTemplates()
        {
            drafting = false;
            var pending = draftCancellation;
            draftCancellation = null;
            sending = false;
            if (pending != null) pending.Cancel();
            selectedId = selectedRun = null;
            RefreshList();
            RenderDetails(true, true);
            var tiles = starterTemplates.Children.OfType<Button>();
            var target = tiles.FirstOrDefault(tile => tile.Name == draftTemplate) ?? tiles.First();
            target.Focus();
        }

        Grid DraftHeading()
        {
            Func<string> label = () => TextResources.Get("common.back");
#if NET10_0
            var back = Ui.Button("‹", ReturnToTemplates);
            back.FontSize = 22;
            back.Foreground = SettingsVisual.Ink;
            back.Background = Brushes.Transparent;
            back.BorderBrush = Brushes.Transparent;
            LocalizedUi.Automation(back, label);
#else
            var back = UiIcons.Button("back", label);
            back.Click += delegate { ReturnToTemplates(); };
#endif
            back.Name = "BackToScheduledTaskTemplates";
            back.Width = back.Height = back.MinHeight = 26;
            back.Padding = new Thickness(4, 0, 4, 0);
            back.Margin = new Thickness(0, 0, 6, 0);
            back.VerticalAlignment = VerticalAlignment.Center;
            back.HorizontalContentAlignment = HorizontalAlignment.Center;
            back.VerticalContentAlignment = VerticalAlignment.Center;
            LocalizedUi.Tooltip(back, label);
            var title = Caption(() => TextResources.Get("scheduledTasks.draft"), 25, true);
            title.Margin = new Thickness(0);
            title.VerticalAlignment = VerticalAlignment.Center;
            var heading = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            heading.ColumnDefinitions.Add(new ColumnDefinition());
            heading.Children.Add(back);
            Grid.SetColumn(title, 1);
            heading.Children.Add(title);
            return heading;
        }

        void RenderDraft(bool focus)
        {
            SetStarterLayout(false);
            details.Children.Clear();
            composer.Children.Clear();
            details.Children.Add(DraftHeading());
            var body = new StackPanel { Margin = new Thickness(DraftGutter, 0, 0, 0) };
            details.Children.Add(body);
            if (turns.Count == 0) body.Children.Add(Caption(() => TextResources.Get("scheduledTasks.draftPrompt")));
            foreach (var turn in turns)
            {
                var bubble = Card(Caption(() => turn.Content));
                bubble.MaxWidth = 580;
                bubble.HorizontalAlignment = turn.Role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                body.Children.Add(bubble);
            }
            if (!String.IsNullOrEmpty(draftError)) body.Children.Add(Caption(() => draftError));
            if (prepared != null)
                body.Children.Add(ButtonFor(() => TextResources.Get("scheduledTasks.draftReady"), "ReviewScheduledDraft", () => Edit(prepared), true));
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
            var request = new CancellationTokenSource();
            draftCancellation = request;
            RenderDraft(false);
            bool current;
            try
            {
                var response = await runtime.DraftAsync(turns.ToArray(), request.Token);
                if (disposed || draftCancellation != request) return;
                prepared = response.Definition;
                if (prepared == null) turns.Add(new ChatMessage("assistant", response.Question));
            }
            catch (OperationCanceledException) { }
            catch (ApiException exception) { if (draftCancellation == request) draftError = exception.Message; }
            catch (Exception) { if (draftCancellation == request) draftError = TextResources.Get("scheduledTasks.draftError"); }
            finally
            {
                current = draftCancellation == request;
                if (current)
                {
                    draftCancellation = null;
                    sending = false;
                }
                request.Dispose();
            }
            if (!current || disposed || !drafting) return;
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
#if NET10_0
            foreach (var image in templateImages) image.Dispose();
            templateImages.Clear();
#endif
        }
    }
}
