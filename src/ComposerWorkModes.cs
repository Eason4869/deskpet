using System;
using System.Windows;
using System.Windows.Controls;

namespace Dafeiyu
{
    internal sealed partial class ConversationComposer
    {
        Button workModeChip;
        Border workModeChipIcon;
        TextBlock workModeChipLabel;
        string shownWorkMode, shownPlaceholder;

        bool CanUseComposerWorkModes()
        {
            var chat = host as ChatWindow;
            return !conversationOnly && chat != null && chat.WorkEntry;
        }

        string ComposerWorkMode { get { return CanUseComposerWorkModes() ? pet.ConversationWorkMode(session) : ""; } }

        Button CreateWorkModeChip()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            workModeChipIcon = new Border { Width = 15, Height = 15, Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(workModeChipIcon);
            workModeChipLabel = Ui.InlineText("", 12, Ui.Muted);
            row.Children.Add(workModeChipLabel);
            var chip = SmallText("", "ComposerWorkMode");
            chip.Padding = new Thickness(8, 3, 8, 3);
            chip.Margin = new Thickness(0, 0, 4, 0);
            chip.MinWidth = 0;
            chip.MinHeight = 27;
            chip.VerticalAlignment = VerticalAlignment.Center;
            chip.Content = row;
            chip.Visibility = Visibility.Collapsed;
            chip.Click += delegate { ChangeComposerWorkMode(""); };
            chip.MouseEnter += delegate { PaintWorkModeChip(); };
            chip.MouseLeave += delegate { PaintWorkModeChip(); };
            chip.IsKeyboardFocusWithinChanged += delegate { PaintWorkModeChip(); };
            workModeChip = chip;
            return chip;
        }

        void PaintWorkModeChip()
        {
            if (workModeChip == null || String.IsNullOrEmpty(shownWorkMode)) return;
            bool close = workModeChip.IsMouseOver || workModeChip.IsKeyboardFocusWithin;
            var image = UiIcons.Image(close ? "close" : shownWorkMode == "plan" ? "list" : "target", Ui.Muted);
            image.Width = image.Height = 15;
            workModeChipIcon.Child = image;
        }

        void ChangeComposerWorkMode(string mode)
        {
            if (!CanUseComposerWorkModes() || (mode.Length > 0 && (session.IsBusy || queue.IsRunning))) return;
            Try(delegate
            {
                pet.SetConversationWorkMode(session, mode);
                RefreshAction();
                input.Focus();
            });
        }

        void SelectGoalMode()
        {
            if (!CanUseComposerWorkModes() || session.IsBusy || queue.IsRunning) return;
            var options=pet.ConversationGoalOptions(session);
            if (options!=null&&options.GoalPaused) ((ChatWindow)host).OpenWorkspacePlanGoal(false);
            else if (ComposerWorkMode == "goal") ShowGoal();
            else ChangeComposerWorkMode("goal");
        }

        bool SaveInitialComposerGoal()
        {
            if (ComposerWorkMode != "goal" || !String.IsNullOrWhiteSpace(pet.ConversationGoal(session))) return true;
            string text = (input.Text ?? "").Trim();
            if (text.Length == 0)
            {
                LocalizedUi.Set(status, "Text", () => TextResources.Get("conversationComposer.workMode.goalPlaceholder"));
                return false;
            }
            if (text.Length > 4000)
            {
                LocalizedUi.Set(status, "Text", () => TextResources.Get("workSettingsPanel.save.text.alternativeB"));
                return false;
            }
            try
            {
                pet.SetConversationGoal(session, text);
                RefreshAction();
                return !String.IsNullOrWhiteSpace(pet.ConversationGoal(session));
            }
            catch (Exception error) { Error(error); return false; }
        }

        string ComposerPlaceholderText()
        {
            switch (ComposerWorkMode)
            {
                case "plan": return TextResources.Get("conversationComposer.workMode.planPlaceholder");
                case "goal": return String.IsNullOrWhiteSpace(pet.ConversationGoal(session))
                    ? TextResources.Get("conversationComposer.workMode.goalPlaceholder")
                    : TextResources.Get("conversationComposer.workMode.goalFollowUpPlaceholder");
                default:
                    return conversationOnly ? TextResources.Get("conversationComposer.conversationComposer.chatUiGetComposerPlaceholder")
                        : TextResources.Get("chatUi.composer.text");
            }
        }

        void RefreshComposerWorkMode()
        {
            string mode = ComposerWorkMode;
            if (workModeChip != null && shownWorkMode != mode)
            {
                shownWorkMode = mode;
                workModeChip.Visibility = mode.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
                if (mode.Length > 0)
                {
                    LocalizedUi.Set(workModeChipLabel, "Text", () => mode == "plan" ? TextResources.Get("workSummary.plan") :
                        TextResources.Get("conversationComposer.openAddMenu.addMenuLabel"));
                    Hint(workModeChip, () => mode == "plan" ? TextResources.Get("conversationComposer.workMode.exitPlan") :
                        TextResources.Get("conversationComposer.workMode.exitGoal"));
                    PaintWorkModeChip();
                }
            }
            string placeholder = ComposerPlaceholderText();
            if (shownPlaceholder == placeholder) return;
            shownPlaceholder = placeholder;
            var label = ChatUi.GetComposerPlaceholder(input);
            if (label != null)
            {
                if (predictionSuggestions != null) predictionSuggestions.DismissGhost();
                LocalizedUi.Set(label, "Text", ComposerPlaceholderText);
            }
        }
    }
}
