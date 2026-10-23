using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace Dafeiyu
{
    // Predictions reuse the existing bounded, cached predictor. Never merge its
    // reference history into the new conversation or submit a suggested message.
    internal sealed class WelcomeSuggestions : WrapPanel, IDisposable
    {
        readonly PetWindow pet; readonly ChatWindow host; readonly ConversationSession session; readonly ConversationWorkspace workspace; readonly MarkdownEditor input;
        readonly DispatcherTimer debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        ConversationSession reference; bool disposed;
        internal WelcomeSuggestions(PetWindow pet, ChatWindow host, ConversationSession session, ConversationWorkspace workspace, MarkdownEditor input)
        {
            this.pet = pet; this.host = host; this.session = session; this.workspace = workspace; this.input = input; Name = "WelcomeSuggestions"; Margin = new Thickness(0, 18, 0, 0);
            host.IsVisibleChanged += VisibilityChanged; Loaded += LoadedView; Unloaded += UnloadedView; debounce.Tick += Request; Render(null);
        }
        internal static string[] Defaults(PetSettings settings, ProjectDefinition project, IEnumerable<ConversationDefinition> records, string petId)
        {
            if (!settings.ShowPredictedMessages) return new[] { TextResources.Get("welcomeSuggestions.defaults.text"), TextResources.Get("welcomeSuggestions.defaults.text.alternativeA") };
            var recent = records.Where(c => !c.Archived && (settings.AgentEnabled ? c.Mode == "work" : c.Mode != "work" && c.PetId == petId)).OrderByDescending(c => c.LastOpenedUtcTicks).FirstOrDefault(c => !String.IsNullOrWhiteSpace(c.Name) && c.Name != "新对话");
            var result = new List<string>();
            if (settings.AgentEnabled && project != null && project.Id != ProjectCatalogStore.UnassignedProjectId && project.Id != ProjectCatalogStore.QuickProjectId) { result.Add(TextResources.Format("welcomeSuggestions.defaults.result",Short(project.Name))); if (!String.IsNullOrWhiteSpace(project.Goal)) result.Add(TextResources.Format("welcomeSuggestions.defaults.result.alternativeA",Short(project.Goal))); }
            if (recent != null) result.Add(TextResources.Format("welcomeSuggestions.defaults.result.alternativeB",Short(recent.Name)));
            result.Add(settings.AgentEnabled ? TextResources.Get("welcomeSuggestions.defaults.result.alternativeC") : TextResources.Get("welcomeSuggestions.defaults.result.alternativeD")); result.Add(settings.AgentEnabled ? TextResources.Get("welcomeSuggestions.defaults.result.alternativeE") : TextResources.Get("welcomeSuggestions.defaults.result.alternativeF")); return result.Distinct().Take(3).ToArray();
        }
        static string Short(string value) { value = value.Trim(); return value.Length > 32 ? value.Substring(0, 32) + "…" : value; }
        PetSettings Settings() { return workspace == null ? pet.ConversationSettings(session) : workspace.EffectiveSettings(); }
        void LoadedView(object sender, RoutedEventArgs args) { Refresh(); }
        void UnloadedView(object sender, RoutedEventArgs args) { Dispose(); }
        void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs args) { if (host.IsVisible) Refresh(); else debounce.Stop(); }
        internal void Refresh()
        {
            if (disposed) return; debounce.Stop(); if (reference != null) { reference.PredictionsChanged -= PredictionsChanged; reference = null; }
            var cfg = Settings(); Render(null); if (!cfg.ShowPredictedMessages || pet.GuideIsRunning) return;
            var candidates = pet.Workspaces.OpenWorkspaces.Where(w => w.Session != session && !w.IsDraft && !w.Definition.Archived && !w.Project.Archived && w.Session != null && !w.Session.IsBusy && w.EffectiveSettings().AgentEnabled == cfg.AgentEnabled && (cfg.AgentEnabled ? (workspace == null || w.ProjectId == workspace.ProjectId) : w.Definition.PetId == pet.Skin.Id)).OrderByDescending(w => w.Definition.LastOpenedUtcTicks);
            var recent = candidates.FirstOrDefault(w => w.Session.History.Messages.Any(m => m.Role == "assistant" && m.Status == "complete")); if (recent == null) return;
            reference = recent.Session; reference.RefreshPredictionSettings(recent.EffectiveSettings()); reference.PredictionsChanged += PredictionsChanged; if (reference.Predictions.Count > 0) Render(reference.Predictions); if (host.IsVisible && !host.HasDraft) debounce.Start();
        }
        async void Request(object sender, EventArgs args)
        {
            debounce.Stop(); if (disposed || reference == null || !host.IsVisible || host.HasDraft || pet.GuideIsRunning || !Settings().ShowPredictedMessages) return;
            var source = pet.Workspaces.OpenWorkspaces.FirstOrDefault(w => w.Session == reference); if (source != null) await reference.RequestPredictionsAsync(source.EffectiveSettings());
        }
        void PredictionsChanged() { if (disposed || Dispatcher.HasShutdownStarted) return; if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(PredictionsChanged)); return; } if (!host.HasDraft && Settings().ShowPredictedMessages && reference != null) Render(reference.Predictions); }
        void Render(IList<string> predictions)
        {
            Children.Clear(); var cfg = Settings(); var project = workspace == null ? null : workspace.Project; var records = workspace != null && cfg.AgentEnabled && project.Id != ProjectCatalogStore.UnassignedProjectId ? project.Conversations : ProjectCatalogStore.AllConversations(pet.Projects); var values = predictions != null && predictions.Count > 0 ? predictions : Defaults(cfg, project, records, pet.Skin.Id);
            foreach (var text in values.Take(3)) { string draft = text; var button = Ui.Button(text); button.Name = "WelcomeSuggestion"; button.MaxWidth = 330; button.Margin = new Thickness(0, 0, 8, 8); button.ToolTip = TextResources.Get("welcomeSuggestions.render.buttonToolTip"); var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }; button.Content = label; button.Click += delegate { input.Text = draft; input.CaretIndex = input.Text.Length; input.Focus(); }; Children.Add(button); }
        }
        public void Dispose() { if (disposed) return; disposed = true; debounce.Stop(); debounce.Tick -= Request; host.IsVisibleChanged -= VisibilityChanged; Loaded -= LoadedView; Unloaded -= UnloadedView; if (reference != null) reference.PredictionsChanged -= PredictionsChanged; reference = null; }
    }
}
