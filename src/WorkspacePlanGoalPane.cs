using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Dafeiyu
{
    public sealed partial class ChatWindow
    {
        internal void OpenWorkspacePlanGoal(bool plan)
        {
            CloseTransientSummary();
            EnsurePreviewWorkspace();
            var page = filePreview.Pages<WorkspacePlanGoalPane>().FirstOrDefault(item => item.IsPlan == plan);
            if (page == null)
            {
                var boundSession = session;
                page = new WorkspacePlanGoalPane(plan, async (expected,next) =>
                {
                    if (closed || !ReferenceEquals(session,boundSession) || !WorkEntry) return;
                    try { await pet.ChangeWorkspaceGoalAsync(boundSession,expected,next); }
                    catch (Exception error) { ShowError(error); }
                });
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
            internal ConversationWorkOptions GoalOptions;
        }

        readonly FlowDocumentScrollViewer viewer = new FlowDocumentScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            IsToolBarVisible = false, IsSelectionEnabled = true
        };
        readonly Func<ConversationWorkOptions,ConversationWorkOptions,Task> changeGoal;
        readonly WrapPanel actions;
        readonly Button pause;
        readonly TextBlock paused;
        Content current;
        bool changing;
        internal bool IsPlan { get; private set; }

        internal WorkspacePlanGoalPane(bool plan, Func<ConversationWorkOptions,ConversationWorkOptions,Task> changeGoal=null)
        {
            IsPlan = plan;
            Name = plan ? "WorkspacePlanPane" : "WorkspaceGoalPane";
            Background = ThemeManager.Surface;
            this.changeGoal=changeGoal;
            if (plan || changeGoal==null) { Child=viewer; return; }

            var layout=new DockPanel();
            actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(14,10,14,0), Visibility=Visibility.Collapsed };
            paused=LocalizedUi.InlineText(()=>TextResources.Get("workspaceGoal.paused"),12,Ui.Muted);
            paused.Margin=new Thickness(0,0,8,4);
            paused.VerticalAlignment=VerticalAlignment.Center;
            actions.Children.Add(paused);
            pause=AddAction("PauseWorkspaceGoal",PauseLabel);
            pause.Click+=async delegate
            {
                var options=CurrentGoal;
                if(options==null)return;
                await Change(options,IsGoalActive(options)?options.WithGoalPaused(true):options.WithMode("goal").WithGoalPaused(false));
            };
            var edit=AddAction("EditWorkspaceGoal",()=>TextResources.Get("workspaceGoal.edit"));
            edit.Click+=async delegate
            {
                var options=CurrentGoal;
                if(options==null)return;
                string updated=EditGoal(options.Goal);
                if(updated!=null)await Change(options,options.WithGoal(updated));
            };
            var delete=AddAction("DeleteWorkspaceGoal",()=>TextResources.Get("common.delete"));
            delete.Click+=async delegate
            {
                var options=CurrentGoal;
                if(options!=null&&ConfirmDelete())
                    await Change(options,options.WithGoal("").WithMode(options.Mode=="goal"?"off":options.Mode));
            };
            DockPanel.SetDock(actions,Dock.Top);
            layout.Children.Add(actions);
            layout.Children.Add(viewer);
            Child=layout;
        }

        ConversationWorkOptions CurrentGoal { get { return current==null?null:current.GoalOptions; } }
        string PauseLabel()
        {
            return IsGoalActive(CurrentGoal)
                ? TextResources.Get("workspaceGoal.pause") : TextResources.Get("workspaceGoal.resume");
        }
        static bool IsGoalActive(ConversationWorkOptions options)
        {
            return options!=null&&options.Mode=="goal"&&!options.GoalPaused;
        }

        Button AddAction(string name,Func<string> label)
        {
            var button=LocalizedUi.Button(label);
            button.Name=name;
            button.Padding=new Thickness(10,4,10,4);
            button.Margin=new Thickness(4,0,0,4);
            button.MinHeight=30;
            actions.Children.Add(button);
            return button;
        }

        async Task Change(ConversationWorkOptions expected,ConversationWorkOptions next)
        {
            if(changing||!ReferenceEquals(CurrentGoal,expected))return;
            changing=true;
            actions.IsEnabled=false;
            try { await changeGoal(expected,next); }
            finally { changing=false;actions.IsEnabled=true; }
        }

        string EditGoal(string currentText)
        {
            var dialog=NewDialog("EditWorkspaceGoalDialog",()=>TextResources.Get("workspaceGoal.edit"),325);
            var field=Ui.Field(currentText,true);
            field.Name="WorkspaceGoalInput";
            field.MaxLength=4000;
            field.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
            LocalizedUi.Automation(field,()=>TextResources.Get("conversationComposer.openAddMenu.addMenuLabel"));
            var save=LocalizedUi.Button(()=>TextResources.Get("common.save"),true);
            save.Name="SaveWorkspaceGoal";
            save.IsDefault=true;
            save.IsEnabled=!String.IsNullOrWhiteSpace(field.Text);
            field.TextChanged+=delegate{save.IsEnabled=!String.IsNullOrWhiteSpace(field.Text);};
            save.Click+=delegate{if(save.IsEnabled)dialog.DialogResult=true;};
            MountDialog(dialog,field,"CancelWorkspaceGoalEdit",save);
            dialog.Loaded+=delegate{field.Focus();field.CaretIndex=field.Text.Length;};
            return dialog.ShowDialog()==true?field.Text.Trim():null;
        }

        bool ConfirmDelete()
        {
            var dialog=NewDialog("DeleteWorkspaceGoalDialog",()=>TextResources.Get("workspaceGoal.deleteTitle"),230);
            var description=LocalizedUi.Text(()=>TextResources.Get("workspaceGoal.deleteDescription"),13,Ui.Muted);
            description.TextWrapping=TextWrapping.Wrap;
            var confirm=LocalizedUi.Button(()=>TextResources.Get("common.delete"));
            confirm.Name="ConfirmDeleteWorkspaceGoal";
            confirm.Click+=delegate{dialog.DialogResult=true;};
            MountDialog(dialog,description,"KeepWorkspaceGoal",confirm);
            return dialog.ShowDialog()==true;
        }

        Window NewDialog(string name,Func<string> title,double height)
        {
            var dialog=new Window { Name=name,Owner=Window.GetWindow(this) };
            LocalizedUi.Window(dialog,title,450,height);
            return dialog;
        }

        static void MountDialog(Window dialog,UIElement content,string cancelName,Button primary)
        {
            var body=new DockPanel();
            var row=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,14,0,0) };
            var cancel=LocalizedUi.Button(()=>TextResources.Get("common.cancel"));
            cancel.Name=cancelName;
            cancel.IsCancel=true;
            cancel.IsDefault=!primary.IsDefault;
            cancel.Click+=delegate{dialog.DialogResult=false;};
            row.Children.Add(cancel);
            primary.Margin=new Thickness(8,0,0,0);
            row.Children.Add(primary);
            DockPanel.SetDock(row,Dock.Bottom);
            body.Children.Add(row);
            body.Children.Add(content);
            DialogUi.ApplyTitled(dialog,body);
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

        internal static Content Goal(ConversationWorkOptions options)
        {
            string goal=options==null?null:options.Goal;
            if (String.IsNullOrWhiteSpace(goal)) return null;
            return new Content { Title = SummaryLine(goal), Text = goal, Steps = new AgentPlanStep[0], GoalOptions=options };
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
            bool sameDocument=current != null && next != null && current.Text == next.Text &&
                current.Steps.Length == next.Steps.Length &&
                current.Steps.Zip(next.Steps, (a, b) => a.Status == b.Status && a.Text == b.Text).All(same => same);
            current = next;
            if(actions!=null)
            {
                actions.Visibility=CurrentGoal==null?Visibility.Collapsed:Visibility.Visible;
                paused.Visibility=CurrentGoal!=null&&CurrentGoal.GoalPaused?Visibility.Visible:Visibility.Collapsed;
                LocalizedUi.Set(pause,"Content",PauseLabel);
            }
            if(sameDocument)return;
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
