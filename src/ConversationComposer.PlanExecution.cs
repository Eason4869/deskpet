using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Dafeiyu
{
    internal sealed partial class ConversationComposer
    {
        const int PlanAttachmentCharacters = 100000;
        const int PlanAttachmentCount = 4;
        static readonly ConditionalWeakTable<ConversationSession, PlanOfferReceipt> planOfferReceipts =
            new ConditionalWeakTable<ConversationSession, PlanOfferReceipt>();
        Border planExecutionCard;
        Button executeGeneratedPlan;
        ConversationSession planOfferSource;
        PlanOfferReceipt planOfferReceipt;
        bool submittingGeneratedPlan;

        sealed class PlanOfferReceipt
        {
            readonly ConversationSession source;
            readonly object gate = new object();
            PlanCompletion latest, pending;
            bool submitting;
            internal event Action Changed;

            internal PlanOfferReceipt(ConversationSession source)
            {
                this.source = source;
                source.MessageUpdated += RememberPlan;
                source.MessageAdded += InvalidateForNewTurn;
                source.HistoryChanged += InvalidateForHistoryChange;
            }

            internal PlanCompletion Pending { get { lock (gate) return pending; } }

            void RememberPlan(ChatMessage plan)
            {
                if (!plan.IsGeneratedPlan || plan.Status != "complete" || String.IsNullOrWhiteSpace(plan.Content)) return;
                // The final reply arrives before the task publishes its terminal status.
                // The corresponding tool-produced steps are already final at this point.
                var task = String.IsNullOrEmpty(plan.AgentTaskId) ? null : source.CurrentAgentTask;
                var steps = task != null && task.Id == plan.AgentTaskId && task.Plan != null && (task.Status == "running" || task.Status == "complete")
                    ? task.Plan.Where(step => step != null && !String.IsNullOrWhiteSpace(step.Text)).ToArray() : new AgentPlanStep[0];
                var completion = new PlanCompletion { Plan = plan, History = source.History, Persona = source.PersonaId,
                    ProjectId = source.KernelSession == null ? null : source.KernelSession.ProjectId, Workspace = source.ActiveAgentWorkspace,
                    Steps = steps };
                if (!PlanReplyCurrent(completion)) return;
                lock (gate)
                {
                    if (latest != null && latest.Matches(plan)) return;
                    latest = pending = completion;
                }
                NotifyChanged();
            }

            void InvalidateForNewTurn(ChatMessage message)
            {
                if (message.Role != "user") return;
                var current = Pending;
                if (current != null && ReferenceEquals(current.History, source.History) && current.Plan.TurnId != message.TurnId)
                    Dismiss(current);
            }

            void InvalidateForHistoryChange()
            {
                var current = Pending;
                if (current != null && ReferenceEquals(current.History, source.History) && !PlanReplyCurrent(current))
                    Dismiss(current);
                else NotifyChanged();
            }

            internal void Dismiss(PlanCompletion completion)
            {
                lock (gate)
                {
                    if (completion == null || pending != completion) return;
                    pending = null;
                }
                NotifyChanged();
            }

            internal bool BeginSubmission(PlanCompletion completion)
            {
                lock (gate)
                {
                    if (pending != completion || submitting) return false;
                    submitting = true;
                    return true;
                }
            }

            internal void FinishSubmission(PlanCompletion completion, bool enqueued)
            {
                lock (gate)
                {
                    submitting = false;
                    if (enqueued && pending == completion) pending = null;
                }
                NotifyChanged();
            }

            void NotifyChanged()
            {
                var changed = Changed;
                if (changed != null) changed();
            }
        }

        sealed class PlanCompletion
        {
            internal ConversationLog History;
            internal ChatMessage Plan;
            internal AgentPlanStep[] Steps;
            internal string Persona, ProjectId, Workspace;

            internal bool Matches(ChatMessage message)
            {
                return message != null && message.Id == Plan.Id && message.AgentTaskId == Plan.AgentTaskId &&
                    message.ProcessingStartedAt == Plan.ProcessingStartedAt && message.Content == Plan.Content;
            }
        }

        FrameworkElement BuildPlanExecutionOffer()
        {
            var heading = new DockPanel();
            var close = SmallIcon("close", () => TextResources.Get("common.close"), "ClosePlanExecutionOffer");
            close.Click += delegate { DismissPlanExecutionOffer(); };
            DockPanel.SetDock(close, Dock.Right);
            heading.Children.Add(close);
            var question = LocalizedUi.Text(() => TextResources.Get("conversationComposer.planExecution.question"), 13, Ui.Ink);
            question.VerticalAlignment = VerticalAlignment.Center;
            heading.Children.Add(question);

            executeGeneratedPlan = LocalizedUi.Button(() => TextResources.Get("conversationComposer.planExecution.execute"), true);
            executeGeneratedPlan.Name = "AcceptPlanExecution";
            executeGeneratedPlan.Click += delegate { AcceptPlanExecutionOffer(); };
            ToolTipService.SetShowOnDisabled(executeGeneratedPlan, true);
            var decline = LocalizedUi.Button(() => TextResources.Get("conversationComposer.planExecution.decline"));
            decline.Name = "DeclinePlanExecution";
            decline.Click += delegate { DismissPlanExecutionOffer(); };
            var choices = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
            choices.Children.Add(executeGeneratedPlan);
            choices.Children.Add(decline);
            var content = new StackPanel();
            content.Children.Add(heading);
            content.Children.Add(choices);
            planExecutionCard = new Border { Name = "PlanExecutionOffer", Child = content, Visibility = Visibility.Collapsed,
                CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 8, 10, 8), Margin = new Thickness(10, 0, 10, 7),
                Background = ThemeManager.Brush("#FAFBFD", "#151D2A"), BorderBrush = Ui.Line, BorderThickness = new Thickness(1) };
            return planExecutionCard;
        }

        void BindPlanExecutionOffer()
        {
            if (ReferenceEquals(planOfferSource, session)) return;
            DisposePlanExecutionOffer();
            planOfferSource = session;
            planOfferReceipt = planOfferReceipts.GetValue(session, source => new PlanOfferReceipt(source));
            planOfferReceipt.Changed += PlanOfferChanged;
            RefreshPlanExecutionOffer();
        }

        void DisposePlanExecutionOffer()
        {
            if (planOfferReceipt != null) planOfferReceipt.Changed -= PlanOfferChanged;
            planOfferReceipt = null;
            planOfferSource = null;
            HidePlanExecutionOffer();
        }

        void PlanOfferChanged()
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(RefreshPlanExecutionOffer));
        }

        ConversationWorkspace PlanWorkspace(ConversationSession source, ConversationQueue sourceQueue, PlanCompletion completion)
        {
            var owner = pet.Workspaces.OpenWorkspaces.FirstOrDefault(item => ReferenceEquals(item.Session, source) && ReferenceEquals(item.Queue, sourceQueue));
            if (owner == null || owner.IsDisposed || (!String.IsNullOrEmpty(completion.ProjectId) && owner.ProjectId != completion.ProjectId) ||
                (!String.IsNullOrEmpty(completion.Workspace) && !SamePlanWorkspace(owner.EffectiveSettings().AgentWorkspace, completion.Workspace))) return null;
            return owner;
        }

        static bool SamePlanWorkspace(string current, string confirmed)
        {
            if (String.IsNullOrWhiteSpace(current)) return false;
            try { return String.Equals(Path.GetFullPath(current).TrimEnd('\\', '/'), Path.GetFullPath(confirmed).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase); }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (IOException) { return false; }
        }

        bool PlanBindingCurrent(ConversationSession source, ConversationQueue sourceQueue, PlanCompletion completion)
        {
            return !closed && CanUseComposerWorkModes() && ReferenceEquals(session, source) && ReferenceEquals(queue, sourceQueue) &&
                ReferenceEquals(source.History, completion.History) && source.PersonaId == completion.Persona && pet.ProactiveSessionOwnerCurrent(source);
        }

        static bool PlanReplyCurrent(PlanCompletion completion)
        {
            var latest = completion.History.LatestGeneratedPlan;
            var last = completion.History.Messages.LastOrDefault();
            return completion.Matches(latest) && last != null && last.Id == completion.Plan.Id;
        }

        bool PlanExecutionReady()
        {
            return !session.IsBusy && !queue.IsRunning && !queue.HasPending && !pet.IsUpdating && !pet.LiveActive && !dictation.IsBusy;
        }

        void RefreshPlanExecutionOffer()
        {
            if (closed || planExecutionCard == null || submittingGeneratedPlan) return;
            var pending = planOfferReceipt == null ? null : planOfferReceipt.Pending;
            if (pending == null || !PlanBindingCurrent(planOfferSource, queue, pending))
            {
                HidePlanExecutionOffer();
                return;
            }
            if (pet.ConversationWorkMode(planOfferSource) != "plan" || PlanWorkspace(planOfferSource, queue, pending) == null)
            {
                planOfferReceipt.Dismiss(pending);
                HidePlanExecutionOffer();
                return;
            }
            executeGeneratedPlan.IsEnabled = PlanExecutionReady();
            LocalizedUi.Tooltip(executeGeneratedPlan, () => executeGeneratedPlan.IsEnabled ? "" : TextResources.Get("conversationComposer.planExecution.waiting"));
            planExecutionCard.Tag = pending;
            planExecutionCard.Visibility = Visibility.Visible;
        }

        void DismissPlanExecutionOffer()
        {
            if (planExecutionCard != null && planOfferReceipt != null) planOfferReceipt.Dismiss(planExecutionCard.Tag as PlanCompletion);
            HidePlanExecutionOffer();
        }

        void HidePlanExecutionOffer()
        {
            if (planExecutionCard == null) return;
            planExecutionCard.Tag = null;
            planExecutionCard.Visibility = Visibility.Collapsed;
        }

        void AcceptPlanExecutionOffer()
        {
            var offer = planExecutionCard.Tag as PlanCompletion;
            var source = planOfferSource;
            var sourceQueue = queue;
            var receipt = planOfferReceipt;
            if (offer == null || receipt == null || submittingGeneratedPlan) return;
            if (!PlanBindingCurrent(source, sourceQueue, offer) || pet.ConversationWorkMode(source) != "plan" || !PlanReplyCurrent(offer) ||
                PlanWorkspace(source, sourceQueue, offer) == null)
            {
                DismissPlanExecutionOffer();
                return;
            }
            if (!PlanExecutionReady()) return;
            var attachments = CreatePlanExecutionAttachments(offer.Plan.Content, offer.Steps);
            if (attachments == null)
            {
                LocalizedUi.Set(status, "Text", () => TextResources.Get("conversationComposer.planExecution.tooLong"));
                return;
            }

            if (!receipt.BeginSubmission(offer)) return;
            submittingGeneratedPlan = true;
            executeGeneratedPlan.IsEnabled = false;
            bool enqueued = false;
            try
            {
                using (source.EnterRequestActivity())
                {
                    var rollback = PrepareSubmission == null ? null : PrepareSubmission();
                    Action restoreMode = null;
                    try
                    {
                        restoreMode = pet.SetConversationWorkMode(source, "");
                        if (pet.ConversationWorkMode(source) != "" || !PlanBindingCurrent(source, sourceQueue, offer) || !PlanReplyCurrent(offer) ||
                            PlanWorkspace(source, sourceQueue, offer) == null || !PlanExecutionReady())
                            throw new InvalidOperationException();
                        sourceQueue.Enqueue(TextResources.Get("conversationComposer.planExecution.request"), attachments, false);
                        enqueued = true;
                    }
                    catch
                    {
                        try { if (restoreMode != null) restoreMode(); }
                        finally { if (rollback != null) rollback(); }
                        throw;
                    }
                    HidePlanExecutionOffer();
                    if (Submitted != null) Submitted();
                }
            }
            catch (Exception)
            {
                if (!closed) LocalizedUi.Set(status, "Text", () => TextResources.Get("conversationComposer.planExecution.failed"));
            }
            finally
            {
                receipt.FinishSubmission(offer, enqueued);
                submittingGeneratedPlan = false;
                if (!closed) RefreshAction();
            }
        }

        internal static List<ChatAttachment> CreatePlanExecutionAttachments(string content, IList<AgentPlanStep> steps = null)
        {
            if (String.IsNullOrWhiteSpace(content)) return null;
            if (steps != null && steps.Count > 0)
                content = String.Join("\n", steps.Select(step => "[" + step.Status + "] " + step.Text)) + "\n\n" + content;
            if (content.Length > PlanAttachmentCharacters * PlanAttachmentCount) return null;
            var attachments = new List<ChatAttachment>();
            for (int offset = 0; offset < content.Length;)
            {
                int length = Math.Min(PlanAttachmentCharacters, content.Length - offset);
                if (offset + length < content.Length && Char.IsHighSurrogate(content[offset + length - 1])) length--;
                if (attachments.Count == PlanAttachmentCount) return null;
                string name = "approved-plan-" + (attachments.Count + 1) + ".md";
                attachments.Add(ChatAttachment.FromText(name, content.Substring(offset, length)));
                offset += length;
            }
            return attachments;
        }
    }
}
