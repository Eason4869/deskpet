using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class GuidanceUiTests
    {
        static int checks;
        static void Check(bool ok, string text) { if (!ok) throw new Exception(text); checks++; Console.WriteLine("PASS " + text); }
        static IEnumerable<T> All<T>(DependencyObject root) where T : DependencyObject { if (root is T) yield return (T)root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var node in All<T>(VisualTreeHelper.GetChild(root, i))) yield return node; }
        static void Pump() { Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(delegate { })); }
        static void Until(Func<bool> ready) { var end = DateTime.UtcNow.AddSeconds(8); while (!ready() && DateTime.UtcNow < end) { Pump(); Thread.Sleep(5); } Check(ready(), "asynchronous operation reaches expected boundary"); }
        [STAThread]
        public static int Main()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; PetWindow pet = null; ChatWindow full = null; QuickChatWindow quick = null;
            try
            {
                string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "guide-live-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
                var cfg = new PetSettings { AgentEnabled = true, AgentWorkspace = root, AutoUpdate = false, GreetingEnabled = false, ReadReplies = false, SaveChatHistory = false, ShowPredictedMessages = false }; cfg.Text.BaseUrl = "http://127.0.0.1:1/v1"; cfg.Text.Model = "fixture"; foreach (var key in cfg.Hotkeys.Keys.ToArray()) cfg.Hotkeys[key] = "";
                pet = new PetWindow(null, cfg, new SettingsStore(Path.Combine(root, "settings.json")), null); var api = new Api(); var session = new ConversationSession(api, new Speech(), () => null); session.SelectPersona(pet.Skin.Id); typeof(PetWindow).GetField("conversation", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(pet, session);
                full = new ChatWindow(pet); quick = new QuickChatWindow(pet); full.Show(); quick.Show(); var operation = session.SendTextAsync("original task", cfg, "fixture"); Until(() => api.Started); Pump();
                string taskId = session.CurrentAgentTask.Id; var began = session.History.Messages.Last(m => m.Role == "assistant").ProcessingStartedAt;
                cfg.FollowUpBehavior="steer";var input=All<MarkdownEditor>(full).Single(t=>t.Name=="MessageInput");input.Text="first correction";All<Button>(full).Single(b=>b.Name=="SendMessage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Until(()=>session.History.Messages.Any(m=>m.Content=="first correction"));Check(input.Text=="","default guide submission clears the successfully submitted draft");cfg.FollowUpBehavior="queue";input.Text="queued correction";All<Button>(full).Single(b=>b.Name=="SendMessage").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(!session.History.Messages.Any(m=>m.Content=="queued correction") && pet.InputQueue.Items.Any(m=>!m.IsGuidance && m.Text=="queued correction"),"queue preference defers a follow-up without turning it into guidance");pet.InputQueue.Remove(pet.InputQueue.Items.Single(m=>m.Text=="queued correction").Id); cfg.SendOnEnter=false;input.Text="shortcut correction";var enter=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(full),Environment.TickCount,System.Windows.Input.Key.Enter){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent};input.RaiseEvent(enter);Check(input.Text=="shortcut correction" && !pet.InputQueue.Items.Any(m=>m.Text=="shortcut correction"),"Enter does not send when Ctrl+Enter is configured");cfg.SendOnEnter=true;enter=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(full),Environment.TickCount,System.Windows.Input.Key.Enter){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent};input.RaiseEvent(enter);Check(input.Text=="" && pet.InputQueue.Items.Any(m=>m.Text=="shortcut correction"),"Enter submits through the configured queue path");pet.InputQueue.Remove(pet.InputQueue.Items.Single(m=>m.Text=="shortcut correction").Id); pet.InputQueue.SteerAsync("second correction").GetAwaiter().GetResult(); Pump();
                var history = session.History.Messages; var pending = history.Single(m => m.Role == "assistant" && m.Status == "pending"); var latest = history.Last(m => m.Role == "user");
                Check(latest.Content == "second correction" && history.IndexOf(pending) > history.IndexOf(latest), "active reply is presented immediately below the latest guidance");
                Check(pending.ProcessingStartedAt == began && pending.ToolEvents.Any(t => t.Id == "current" && t.Status == "running"), "current tool and processing clock continue under guidance");
                Check(api.Calls == 1 && !api.Token.IsCancellationRequested && session.CurrentAgentTask.Id == taskId, "guidance neither cancels the request nor replaces the task");
                Check(!history.Any(m => m.Status == "cancelled" || m.Status == "interrupted") && !history.SelectMany(m => m.ToolEvents ?? new List<AgentToolEvent>()).Any(t => t.Status == "error" || t.Summary.Contains("已中断")), "guidance creates no interrupted warning or failed tool");
                foreach (Window host in new Window[] { full, quick })
                {
                    var view = All<ChatMessageView>(host).Single(v => (string)v.Tag == pending.Id); var label = All<TextBlock>(view).Single(t => t.Name == "ProcessStatus"); Check(label.IsVisible && label.Foreground is LinearGradientBrush, "continued activity remains animated in " + host.GetType().Name);
                    var stack = (StackPanel)view.Parent; var guide = All<ChatMessageView>(host).Single(v => (string)v.Tag == latest.Id); Check(stack.Children.IndexOf(view) > stack.Children.IndexOf(guide), "activity is positioned after guidance in " + host.GetType().Name);
                }
                api.Boundary.SetResult(true); Until(() => api.Admitted);
                Check(session.IsBusy && pet.InputQueue.Items.Count == 0 && session.CurrentAgentTask.Guidance.Count == 2, "guidance is admitted before the original operation completes");
                api.Finish.SetResult(true); Until(() => operation.IsCompleted); operation.GetAwaiter().GetResult();
                Check(session.CurrentAgentTask.Id == taskId && session.CurrentAgentTask.Goal == "original task" && session.History.Messages.Last().Content == "adjusted result", "the same task completes after applying both corrections");
                Console.WriteLine("Guidance UI tests: " + checks + " passed"); return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
            finally { if (quick != null) quick.Close(); if (full != null) full.Close(); if (pet != null) pet.Close(); app.Shutdown(); }
        }
        sealed class Api : IConversationApi, IAgentConversationApi
        {
            public bool Started, Admitted; public int Calls; public CancellationToken Token; public readonly TaskCompletionSource<bool> Boundary = new TaskCompletionSource<bool>(), Finish = new TaskCompletionSource<bool>();
            public async Task<string> ChatAgentAsync(ConnectionSettings c, IList<ChatMessage> messages, AgentWorkspace workspace, Action<string> delta, Action<string> reasoning, Action<AgentToolEvent> tool, bool stream, CancellationToken cancel)
            { Calls++; Token = cancel; tool(new AgentToolEvent { Id = "current", Name = "edit_file", Summary = "正在编辑 current.txt", Status = "running" }); Started = true; await Boundary.Task; tool(new AgentToolEvent { Id = "current", Name = "edit_file", Summary = "已编辑 current.txt", Status = "complete" }); var guides = await workspace.TakeGuidanceAsync(cancel); if (guides.Count != 2) throw new Exception("both corrections must reach the next decision"); Admitted = true; await Finish.Task; return "adjusted result"; }
            public Task<string> ChatAsync(ConnectionSettings c, IList<ChatMessage> m, CancellationToken t) { throw new NotSupportedException(); }
            public Task<string> TranscribeAsync(ConnectionSettings c, byte[] b, CancellationToken t) { throw new NotSupportedException(); }
            public Task<byte[]> SpeechAsync(ConnectionSettings c, string v, string text, CancellationToken t) { throw new NotSupportedException(); }
            public void Dispose() { }
        }
        sealed class Speech : IReplySpeech { public Task SpeakAsync(string text, PetSettings cfg, CancellationToken t) { return Task.FromResult(0); } public void Stop() { } public void SetVolume(int v) { } public void Dispose() { } }
    }
}
