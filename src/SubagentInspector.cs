using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Dafeiyu
{
    public sealed partial class ChatWindow
    {
        void OpenSubagentInspector()
        {
            if (closed) return;
            CloseTransientSummary();
            EnsurePreviewWorkspace();
            var pane = filePreview.Pages<SubagentInspector>().FirstOrDefault();
            if (pane == null)
                filePreview.AddPage(() => TextResources.Get("workSummary.workers"), "target", new SubagentInspector(session));
            else
                filePreview.SelectPage(pane);
            RefreshWorkspaceLayout();
        }
    }

    internal sealed class SubagentInspector : DockPanel, IDisposable
    {
        readonly ConversationSession conversation;
        readonly StackPanel body = new StackPanel { Margin = new Thickness(20, 12, 20, 16) };
        readonly ScrollViewer scroll;
        readonly Border header;
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        readonly List<Action> updateTimes = new List<Action>();
        string selectedId;
        bool disposed;
        int refreshQueued;

        internal SubagentInspector(ConversationSession conversation)
        {
            this.conversation = conversation;
            Name = "SubagentInspector";
            Background = ThemeManager.Surface;
            header = new Border { BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
            SetDock(header, Dock.Top);
            Children.Add(header);
            scroll = Ui.Scroll(body);
            Children.Add(scroll);
            conversation.AgentTaskChanged += QueueRefresh;
            conversation.HistoryChanged += QueueRefresh;
            timer.Tick += UpdateTimes;
            IsVisibleChanged += delegate
            {
                if (IsVisible) Render();
                else timer.Stop();
            };
            Render();
        }

        internal static bool IsCompleted(AgentChildResult child)
        {
            return child.Status == "completed" || child.Status == "complete";
        }

        static bool IsActive(AgentChildResult child)
        {
            return child.Status == "running" || child.Status == "queued";
        }

        void QueueRefresh()
        {
            if (Interlocked.Exchange(ref refreshQueued, 1) != 0) return;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
            {
                Interlocked.Exchange(ref refreshQueued, 0);
                if (!disposed && IsVisible) Render();
            }));
        }

        void Render()
        {
            if (disposed) return;
            timer.Stop();
            updateTimes.Clear();
            body.Children.Clear();
            var agents = conversation.Subagents;
            var selected = selectedId == null ? null : agents.FirstOrDefault(agent => agent.Id == selectedId);
            if (selected != null) RenderDetails(selected);
            else
            {
                selectedId = null;
                header.Child = null;
                header.Visibility = Visibility.Collapsed;
                if (agents.Count == 0)
                    body.Children.Add(LocalizedUi.Text(() => TextResources.Get("workSummary.noWorkers"), 12, Ui.Muted));
                AddGroup(agents.Where(IsActive).ToArray(), count => TextResources.Format("workSummary.subagents.activeGroup", count));
                AddGroup(agents.Where(IsCompleted).ToArray(), count => TextResources.Format("workSummary.subagents.completedGroup", count));
                AddGroup(agents.Where(agent => !IsActive(agent) && !IsCompleted(agent)).ToArray(),
                    count => TextResources.Format("workSummary.subagents.stoppedGroup", count));
            }
            if (IsVisible && updateTimes.Count > 0) timer.Start();
        }

        void AddGroup(AgentChildResult[] agents, Func<int, string> title)
        {
            if (agents.Length == 0) return;
            var caption = LocalizedUi.Text(() => title(agents.Length), 12, Ui.Muted);
            caption.Margin = new Thickness(6, 10, 0, 7);
            body.Children.Add(caption);
            foreach (var agent in agents)
            {
                var row = NewRow();
                AddTime(row, agent, false);
                AddIcon(row, agent);
                var text = new StackPanel();
                text.Children.Add(LocalizedUi.Text(() => DisplayName(agent), 13, Ui.Ink));
                var status = LocalizedUi.Text(() => Status(agent), 12, Ui.Muted);
                status.Margin = new Thickness(0, 3, 0, 0);
                text.Children.Add(status);
                row.Children.Add(text);
                var button = AgentUi.Nav("", "SubagentInspectorRow");
                button.Content = row;
                button.Tag = agent.Id;
                button.Padding = new Thickness(6, 8, 6, 8);
                button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                LocalizedUi.Automation(button, () => DisplayName(agent) + " · " + Status(agent));
                button.Click += delegate
                {
                    selectedId = agent.Id;
                    Render();
                    scroll.ScrollToTop();
                    var panel = header.Child as DockPanel;
                    if (panel != null && panel.Children.Count > 0) ((Button)panel.Children[0]).Focus();
                };
                body.Children.Add(button);
            }
        }

        void RenderDetails(AgentChildResult agent)
        {
            var row = NewRow();
            row.Margin = new Thickness(14, 7, 14, 7);
            var back = UiIcons.Button("back", () => TextResources.Get("common.back"));
            back.Name = "SubagentInspectorBack";
            back.Width = back.Height = back.MinHeight = 28;
            back.Padding = new Thickness(6);
            back.Margin = new Thickness(0, 0, 8, 0);
            back.Click += delegate
            {
                selectedId = null;
                Render();
                var previous = body.Children.OfType<Button>().FirstOrDefault(button => Equals(button.Tag, agent.Id));
                if (previous != null) { previous.BringIntoView(); previous.Focus(); }
            };
            SetDock(back, Dock.Left);
            row.Children.Add(back);
            if (!String.IsNullOrWhiteSpace(agent.Model))
            {
                var model = Ui.Text(agent.Model, 11, Ui.Muted);
                model.Margin = new Thickness(10, 0, 0, 0);
                model.VerticalAlignment = VerticalAlignment.Center;
                SetDock(model, Dock.Right);
                row.Children.Add(model);
            }
            AddIcon(row, agent);
            var title = LocalizedUi.Text(() => DisplayName(agent), 13, Ui.Ink);
            title.FontWeight = FontWeights.SemiBold;
            title.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(title);
            header.Child = row;
            header.Visibility = Visibility.Visible;

            var metadata = NewRow();
            AddTime(metadata, agent, true);
            metadata.Children.Add(LocalizedUi.Text(() => Status(agent), 12, Ui.Muted));
            body.Children.Add(metadata);
            AddSection(() => TextResources.Get("workSummary.subagents.task"), agent.Input);
            if (agent.Process != null && agent.Process.Length > 0)
            {
                body.Children.Add(SectionTitle(() => TextResources.Get("workSummary.subagents.progress")));
                foreach (var entry in agent.Process)
                {
                    if (entry == null) continue;
                    var text = Ui.Text(String.IsNullOrWhiteSpace(entry.Summary) ? entry.Tool : entry.Summary, 12, Ui.Muted);
                    text.Margin = new Thickness(0, 0, 0, 5);
                    body.Children.Add(text);
                }
            }
            AddSection(() => TextResources.Get("workSummary.subagents.result"), agent.Result, "SubagentInspectorResult");
            if (agent.ToolCalls > 0 || agent.ModelRequests > 0)
            {
                var usage = LocalizedUi.Text(() => TextResources.Format("workSummary.subagents.usage", agent.ToolCalls, agent.ModelRequests), 11, Ui.Muted);
                usage.Margin = new Thickness(0, 12, 0, 0);
                body.Children.Add(usage);
            }
        }

        void AddSection(Func<string> heading, string content, string name = null)
        {
            if (String.IsNullOrWhiteSpace(content)) return;
            body.Children.Add(SectionTitle(heading));
            var text = Ui.Text(content, 13, Ui.Ink);
            if (name != null) text.Name = name;
            body.Children.Add(text);
        }

        static TextBlock SectionTitle(Func<string> heading)
        {
            var title = LocalizedUi.Text(heading, 12, Ui.Muted);
            title.Margin = new Thickness(0, 14, 0, 5);
            return title;
        }

        static DockPanel NewRow()
        {
            return new DockPanel { LastChildFill = true };
        }

        static void AddIcon(DockPanel row, AgentChildResult agent)
        {
            var icon = ShellIcons.Image("target", IsActive(agent) ? Ui.Blue : Ui.Muted, 15);
            icon.Margin = new Thickness(0, 0, 8, 0);
            icon.VerticalAlignment = VerticalAlignment.Top;
            SetDock(icon, Dock.Left);
            row.Children.Add(icon);
        }

        void AddTime(DockPanel row, AgentChildResult agent, bool detail)
        {
            if (!agent.StartedUtc.HasValue) return;
            if (!agent.FinishedUtc.HasValue && !IsActive(agent)) return;
            Func<string> elapsed = () =>
            {
                var duration = (agent.FinishedUtc ?? DateTime.UtcNow) - agent.StartedUtc.Value;
                if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
                string time = duration.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);
                if (duration.Days > 0) time = duration.Days.ToString(CultureInfo.CurrentCulture) + "." + time;
                return detail ? TextResources.Format("workSummary.subagents.duration", time) : time;
            };
            var label = LocalizedUi.Text(elapsed, 11, Ui.Muted);
            label.Margin = new Thickness(10, 0, 0, 0);
            SetDock(label, Dock.Right);
            row.Children.Add(label);
            if (!agent.FinishedUtc.HasValue) updateTimes.Add(() => label.Text = elapsed());
        }

        void UpdateTimes(object sender, EventArgs args)
        {
            foreach (var update in updateTimes) update();
        }

        static string DisplayName(AgentChildResult agent)
        {
            if (!String.IsNullOrWhiteSpace(agent.DisplayName)) return agent.DisplayName;
            if (!String.IsNullOrWhiteSpace(agent.Input))
            {
                string firstLine = agent.Input.Trim().Split(new[] { '\r', '\n' }, 2)[0];
                if (firstLine.Length <= 60) return firstLine;
                int length = Char.IsHighSurrogate(firstLine[59]) ? 59 : 60;
                return firstLine.Substring(0, length).TrimEnd() + "…";
            }
            string id = agent.Id ?? "";
            return TextResources.Format("workSummary.subagents.name", id.Length > 8 ? id.Substring(0, 8) : id);
        }

        static string Status(AgentChildResult agent)
        {
            if (IsCompleted(agent)) return TextResources.Get("workSummary.subagents.completed");
            switch (agent.Status)
            {
                case "running": return TextResources.Get("workSummary.subagents.running");
                case "queued": return TextResources.Get("workSummary.subagents.queued");
                case "failed": return TextResources.Get("workSummary.subagents.failed");
                case "cancelled": return TextResources.Get("workSummary.subagents.cancelled");
                default: return TextResources.Get("workSummary.subagents.interrupted");
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            timer.Stop();
            timer.Tick -= UpdateTimes;
            conversation.AgentTaskChanged -= QueueRefresh;
            conversation.HistoryChanged -= QueueRefresh;
        }
    }
}
