using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace Dafeiyu
{
    public static class MessagePresentation
    {
        public static string Status(string value)
        { return value == "pending" ? TextResources.Get("messagePresentation.status.text") : value == "cancelled" ? TextResources.Get("messagePresentation.status.text.alternativeA") : value == "interrupted" ? TextResources.Get("messagePresentation.status.text.alternativeB") : value == "error" ? TextResources.Get("messagePresentation.status.text.alternativeC") : ""; }
    }
    // Network callbacks replace snapshots; only the latest snapshot per message reaches WPF.
    public sealed class MessageUpdatePump : IDisposable
    {
        readonly ConversationSession session;
        readonly Action<ChatMessage> display;
        readonly Dictionary<string, ChatMessage> pending = new Dictionary<string, ChatMessage>();
        readonly DispatcherTimer timer;
        readonly Dispatcher dispatcher;
        readonly object gate = new object();
        bool disposed, wakeScheduled;
        public MessageUpdatePump(ConversationSession source, Action<ChatMessage> update)
        {
            session = source; display = update; dispatcher = Dispatcher.CurrentDispatcher; timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(50) };
            session.MessageUpdated += Receive; timer.Tick += Tick;
        }
        void Receive(ChatMessage message)
        {
            lock (gate)
            {
                if (disposed || dispatcher.HasShutdownStarted) return;
                pending[message.Id] = message;
                if (wakeScheduled) return; wakeScheduled = true;
                // Network callbacks can arrive off-thread. Schedule one wakeup
                // for the whole burst; idle chat windows need no polling timer.
                dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { lock (gate) { if (!disposed) timer.Start(); } }));
            }
        }
        void Tick(object sender, EventArgs args)
        {
            ChatMessage[] updates; lock (gate) { timer.Stop(); wakeScheduled = false; if (disposed) return; updates = new ChatMessage[pending.Count]; pending.Values.CopyTo(updates, 0); pending.Clear(); }
            foreach (var message in updates) { lock (gate) { if (disposed) return; } display(message); }
        }
        public void Dispose() { lock (gate) { disposed = true; pending.Clear(); } session.MessageUpdated -= Receive; timer.Stop(); timer.Tick -= Tick; }
    }
}
