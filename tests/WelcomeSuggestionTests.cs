using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class WelcomeSuggestionTests
    {
        static int checks; static readonly BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
        static IEnumerable<T> All<T>(DependencyObject node) where T : DependencyObject { if (node is T) yield return (T)node; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) foreach (var x in All<T>(VisualTreeHelper.GetChild(node, i))) yield return x; }
        static void Pump(int milliseconds = 150) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) }; timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
        static string Text(Button button) { return ((TextBlock)button.Content).Text; }
        [STAThread]
        public static int Main()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; PetWindow pet = null; string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "welcome-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); try
            {
                Check(new PetSettings().ShowPredictedMessages, "prediction preference defaults on for new settings"); using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"ShowPredictedMessages\":false}"))) { var disabled = (PetSettings)new DataContractJsonSerializer(typeof(PetSettings)).ReadObject(stream); disabled.Normalize(); Check(!disabled.ShowPredictedMessages && !disabled.Copy().ShowPredictedMessages, "explicitly disabled preference survives deserialization, normalization and copying"); }
                var cfg = new PetSettings { AgentEnabled = true, ReadReplies = false, AutoUpdate = false, GreetingEnabled = false, OnboardingState = "completed" }; foreach (var key in cfg.Hotkeys.Keys.ToArray()) cfg.Hotkeys[key] = ""; cfg.Text = new ConnectionSettings { BaseUrl = "http://127.0.0.1:1/v1", Model = "fixture", StreamReplies = false }; pet = new PetWindow(null, cfg, new SettingsStore(Path.Combine(root, "settings.json")), null); var api = new HeldApi(); typeof(PetWindow).GetField("workspaces", hidden).SetValue(pet, new ConversationWorkspaceManager(root, pet.Skin.Id, () => api, () => new Silent(), () => null, () => cfg, () => false));
                var project = ProjectCatalogStore.CreateProject(pet.Projects, "欢迎页项目", root); project.Goal = "核对界面布局"; var source = pet.Workspaces.CreateWorkspace(project, project.Conversations[0]); source.Session.History.Commit("请先检查新手引导", "可以先看语音配置和项目流程。"); project.Conversations[0].Name = "语音与项目流程";
                var draft = pet.CreateDraftConversation(project, true); var chat = new ChatWindow(pet, draft); chat.Show(); chat.UpdateLayout(); Pump(1100);
                Check(All<Image>(chat).Any(i => i.Name == "WelcomeCharacter" && i.Source != null) && All<TextBlock>(chat).Any(t => t.Name == "WelcomeHeading" && t.Text == "一起聊聊今天的想法"), "work mode preserves the pet illustration and welcome heading"); Check(All<Button>(chat).Where(b => b.Name == "WelcomeSuggestion").Any(b => Text(b).Contains("欢迎页项目")), "project metadata produces relevant immediate starter suggestions");
                Check(api.Calls == 1, "welcome reuses one bounded model prediction for related shared work history"); api.Complete("[\"先验证云端朗读的开关和试听\",\"检查项目选择器首次打开的状态\"]"); Pump(); var suggestions = All<Button>(chat).Where(b => b.Name == "WelcomeSuggestion").ToArray(); Check(suggestions.Length == 2 && Text(suggestions[0]).Contains("云端朗读"), "returned predictions replace static welcome buttons");
                int before = draft.Session.History.Messages.Count; suggestions[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); Check(All<MarkdownEditor>(chat).Single(t => t.Name == "MessageInput").Text.Contains("云端朗读") && draft.Session.History.Messages.Count == before && api.Calls == 1, "clicking a prediction fills a draft without submitting or adding history");
                var bitmap = new RenderTargetBitmap((int)chat.ActualWidth, (int)chat.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(chat); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using (var output = File.Create(Path.Combine(root, "work-welcome-predictions.png"))) png.Save(output); chat.Close(); Pump();
                var records = new[] { new ConversationDefinition { Name = "另一个伙伴的私密话题", Mode = "chat", PetId = "other", LastOpenedUtcTicks = 100 }, new ConversationDefinition { Name = "当前伙伴的话题", Mode = "chat", PetId = pet.Skin.Id, LastOpenedUtcTicks = 10 }, new ConversationDefinition { Name = "共享工作", Mode = "work", LastOpenedUtcTicks = 200 } }; cfg.AgentEnabled = false; var privateSuggestions = WelcomeSuggestions.Defaults(cfg, null, records, pet.Skin.Id); Check(privateSuggestions.Any(t => t.Contains("当前伙伴")) && !privateSuggestions.Any(t => t.Contains("私密") || t.Contains("共享工作")), "ordinary chat predictions exclude other pets and shared work contexts");
                cfg.ShowPredictedMessages = false; Check(WelcomeSuggestions.Defaults(cfg, project, records, pet.Skin.Id).SequenceEqual(new[] { "帮我梳理一个想法", "一起检查这段代码" }), "turning predictions off restores neutral starter buttons"); var noPredict = new ChatWindow(pet, pet.CreateDraftConversation(project, true)); noPredict.Show(); Pump(1100); Check(api.Calls == 1, "disabled welcome predictions make no model requests"); noPredict.Close();
                Console.WriteLine("Welcome suggestion tests: " + checks + " passed; screenshot: " + root); return 0;
            }
            catch (Exception e) { Console.WriteLine("FAIL " + e); return 1; }
            finally { foreach (var c in app.Windows.OfType<ChatWindow>().ToArray()) c.Close(); if (pet != null) pet.Close(); app.Shutdown(); }
        }
        sealed class HeldApi : IConversationApi { public int Calls; TaskCompletionSource<string> pending; public Task<string> ChatAsync(ConnectionSettings cfg, IList<ChatMessage> messages, CancellationToken token) { Calls++; pending = new TaskCompletionSource<string>(); token.Register(() => pending.TrySetCanceled()); return pending.Task; } public void Complete(string text) { pending.TrySetResult(text); } public Task<string> TranscribeAsync(ConnectionSettings cfg, byte[] audio, CancellationToken token) { throw new Exception("Unexpected ASR"); } public Task<byte[]> SpeechAsync(ConnectionSettings cfg, string voice, string text, CancellationToken token) { throw new Exception("Unexpected TTS"); } public void Dispose() { if (pending != null) pending.TrySetCanceled(); } }
        sealed class Silent : IReplySpeech { public Task SpeakAsync(string text, PetSettings cfg, CancellationToken token) { return Task.FromResult(0); } public void Stop() { } public void SetVolume(int volume) { } public void Dispose() { } }
    }
}
