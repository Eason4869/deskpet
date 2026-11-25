using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Dafeiyu
{
    public sealed partial class ChatWindow
    {
        void OpenWorkspacePlanGoal(bool plan)
        {
            CloseTransientSummary();
            EnsurePreviewWorkspace();
            var page = filePreview.Pages<WorkspacePlanGoalPane>().FirstOrDefault(item => item.IsPlan == plan);
            if (page == null)
            {
                page = new WorkspacePlanGoalPane(plan);
                filePreview.AddPage(plan ? (Func<string>)(() => TextResources.Get("workSummary.plan")) :
                    () => TextResources.Get("conversationComposer.openAddMenu.addMenuLabel"), plan ? "list" : "target", page);
            }
            else filePreview.SelectPage(page);
            RefreshWorkSummaryOverviews();
            RefreshWorkspaceLayout();
        }
    }

    internal sealed class WorkspacePlanGoalPane : Border
    {
        internal sealed class Content
        {
            internal string Title, Text;
            internal AgentPlanStep[] Steps;
        }

        readonly FlowDocumentScrollViewer viewer = new FlowDocumentScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsToolBarVisible = false, IsSelectionEnabled = true
        };
        Content current;
        internal bool IsPlan { get; private set; }

        internal WorkspacePlanGoalPane(bool plan)
        {
            IsPlan = plan;
            Name = plan ? "WorkspacePlanPane" : "WorkspaceGoalPane";
            Background = ThemeManager.Surface;
            Child = viewer;
        }

        internal static Content Plan(AgentTaskState task, ChatMessage generated)
        {
            var steps = task == null ? new AgentPlanStep[0] :
                task.Plan.Where(step => !String.IsNullOrWhiteSpace(step.Text)).ToArray();
            string text = generated != null && (steps.Length == 0 || generated.AgentTaskId == task.Id)
                ? generated.Content : "";
            if (steps.Length == 0 && String.IsNullOrWhiteSpace(text)) return null;
            string title = SummaryLine(steps.Length > 0 ? task.Goal : text);
            if (title.Length == 0 && steps.Length > 0) title = SummaryLine(steps[0].Text);
            return new Content { Title = title, Text = text ?? "", Steps = steps };
        }

        internal static Content Goal(string goal)
        {
            if (String.IsNullOrWhiteSpace(goal)) return null;
            return new Content { Title = SummaryLine(goal), Text = goal, Steps = new AgentPlanStep[0] };
        }

        internal static string SummaryLine(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return "";
            string headline = text.Trim().Split(new[] { '\r', '\n' }, 2)[0].TrimStart('#', ' ', '\t').TrimEnd();
            if (headline.Length <= 130) return headline;
            int length = Char.IsHighSurrogate(headline[129]) ? 129 : 130;
            return headline.Substring(0, length).TrimEnd() + "…";
        }

        internal void Show(Content next)
        {
            if (current == null && next == null) return;
            if (current != null && next != null && current.Text == next.Text &&
                current.Steps.Length == next.Steps.Length &&
                current.Steps.Zip(next.Steps, (a, b) => a.Status == b.Status && a.Text == b.Text).All(same => same)) return;
            current = next;
            if (next == null) { viewer.Document = null; return; }

            bool formatted;
            var family = Ui.Text("").FontFamily;
            var doc = IsPlan ? MarkdownDocument.Create(next.Text, family, Ui.Ink, false, out formatted) :
                new FlowDocument(new Paragraph(new Run(next.Text))) { FontFamily = family, FontSize = 14, Foreground = Ui.Ink };
            doc.PagePadding = new Thickness(18, 12, 18, 18);
            if (next.Steps.Length > 0)
            {
                var steps = new Section { Margin = new Thickness(0, 0, 0, 12) };
                foreach (var step in next.Steps)
                {
                    string mark = step.Status == "complete" ? "✓ " : step.Status == "in_progress" ? "• " : "○ ";
                    var item = new Paragraph(new Run(mark + step.Text)) { Margin = new Thickness(0, 0, 0, 6) };
                    item.Foreground = step.Status == "in_progress" ? Ui.Blue : Ui.Ink;
                    steps.Blocks.Add(item);
                }
                doc.Blocks.InsertBefore(doc.Blocks.FirstBlock, steps);
            }
            viewer.Document = doc;
        }
    }
}
