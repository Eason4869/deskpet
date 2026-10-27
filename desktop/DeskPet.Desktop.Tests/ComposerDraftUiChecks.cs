using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;

internal static class ComposerDraftUiChecks
{
    private sealed class Runtime(string data, ISecretProtector protector) : DesktopRuntime
    {
        public override string DataDirectory => data;
        public override bool ShowTray => false;
        public override void InitializeProtection() => SecretProtection.Configure(protector);
        public override IReplySpeech CreateSpeech(ApiClient api) => new TestSpeech();
        public override IAudioRecorder CreateRecorder() => new TestRecorder();
        public override IPcmMicrophone CreateMicrophone() => throw new InvalidOperationException();
        public override ILiveAudioOutput CreateLiveOutput() => throw new InvalidOperationException();
    }
    internal static int Run(string root)
    {
        int count = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); count++; }
        void Pump() => Dispatcher.UIThread.RunJobs();
        TextBox Input(ChatWindow window) => window.GetLogicalDescendants().OfType<TextBox>().Single(c => c.Name == "ChatDraft");
        void Mode(ChatWindow window, bool work) { typeof(ChatWindow).GetMethod("SelectMode", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { work }); Pump(); }
        void Finish(DesktopController owner)
        {
            owner.Pet.Close(); owner.Dispose();
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!owner.ImageServicesShutdown.IsCompleted) { if (timer.ElapsedMilliseconds > 15000) throw new TimeoutException(); Pump(); Thread.Sleep(1); }
            owner.ImageServicesShutdown.GetAwaiter().GetResult(); Pump();
        }
        foreach (bool quickFirst in new[] { true, false })
        {
            string data = Path.Combine(root, quickFirst ? "quick-first" : "full-first");
            var protector = new TestProtector();
            DesktopController Open() => new(new ClassicDesktopStyleApplicationLifetime(), new Runtime(data, protector));
            var owner = Open(); owner.Settings.AgentEnabled = false; owner.Settings.SaveChatHistory = true;
            var full = new ChatWindow(owner, false); full.Show(); Input(full).Text = "persistent full draft"; Pump(); full.Close(); Finish(owner);
            owner = Open();
            ChatWindow quick;
            if (quickFirst) { quick = new ChatWindow(owner, true); quick.Show(); full = new ChatWindow(owner, false); full.Show(); }
            else { full = new ChatWindow(owner, false); full.Show(); quick = new ChatWindow(owner, true); quick.Show(); }
            Check(Input(full).Text == "persistent full draft", "full draft survives restart and either window opening order");
            Check(Input(quick).Text == "", "quick draft stays temporary and separate from full");
            Input(quick).Text = "temporary quick message"; Pump();
            owner.Settings.Text.BaseUrl = "http://127.0.0.1:1"; owner.Settings.Text.Model="fixture"; owner.Queue.Hold();
            typeof(ChatWindow).GetMethod("Send", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(quick, null); Pump();
            Check(Input(quick).Text == "" && Input(full).Text == "persistent full draft", "successful quick enqueue cannot clear full draft");
            quick.Close(); full.Close(); Finish(owner);
            owner = Open(); full = new ChatWindow(owner, false); full.Show();
            Check(Input(full).Text == "persistent full draft", "full draft remains durable after quick send and restart");
            if (OperatingSystem.IsWindows())
            {
                Mode(full, true); Check(Input(full).Text == "", "unbound work mode has a separate draft");
                Input(full).Text = "work draft"; Pump(); Mode(full, false);
                Check(Input(full).Text == "persistent full draft", "mode switch restores chat draft");
                Mode(full, true); Check(Input(full).Text == "work draft", "mode switch restores work draft");
                full.Close(); Finish(owner); owner = Open(); full = new ChatWindow(owner, false); full.Show();
                Check(Input(full).Text == "work draft", "work mode draft survives restart");
                Mode(full, false); Check(Input(full).Text == "persistent full draft", "chat mode draft survives work restart");
            }
            full.Close(); Finish(owner);
        }
        return count;
    }
}
