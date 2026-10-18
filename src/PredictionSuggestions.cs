using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Dafeiyu
{
    // Predicts the user's next message and shows it as light ghost text inside
    // the empty composer. The ghost reuses the composer placeholder, so it adds
    // no layout. Enter fills the draft once; a second Enter after release sends
    // normally; Escape dismisses it for the completed reply.
    internal sealed class PredictionSuggestions : StackPanel,IDisposable
    {
        readonly PetWindow pet;readonly MarkdownEditor input;readonly Window host;ConversationSession session;ConversationQueue queue;
        readonly DispatcherTimer debounce=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};
        bool subscribed,closed,ghost,enterHeld;string shown="";
        TextBlock placeholder;string defaultPlaceholder="";
        internal Func<bool> AdditionalBlocker {get;set;}
        internal bool GhostActive{get{return ghost;}}
        public PredictionSuggestions(PetWindow pet,ConversationSession sessionOverride,ConversationQueue queueOverride,MarkdownEditor input,Window host)
        {
            this.pet=pet;this.input=input;this.host=host;session=sessionOverride??pet.Conversation;queue=queueOverride??pet.InputQueue;
            Name="PredictedMessageSuggestions";Visibility=Visibility.Collapsed;
            debounce.Tick+=Request;Loaded+=OnLoaded;Unloaded+=OnUnloaded;host.Closed+=OnClosed;
        }
        internal void Rebind(ConversationSession nextSession,ConversationQueue nextQueue)
        {
            if(nextSession==null || nextQueue==null)throw new ArgumentNullException("session");if(ReferenceEquals(session,nextSession) && ReferenceEquals(queue,nextQueue))return;
            if(subscribed){session.StateChanged-=StateChanged;session.HistoryChanged-=StateChanged;session.PredictionsChanged-=SuggestionsChanged;queue.Changed-=StateChanged;session.RemovePredictionInput(this);}
            session=nextSession;queue=nextQueue;
            if(subscribed){session.StateChanged+=StateChanged;session.HistoryChanged+=StateChanged;session.PredictionsChanged+=SuggestionsChanged;queue.Changed+=StateChanged;session.UpdatePredictionInput(this,Blocked());}
            RefreshState();
        }
        void OnLoaded(object sender,RoutedEventArgs args)
        {
            if(closed || subscribed)return;subscribed=true;
            input.TextChanged+=InputChanged;input.PreviewKeyDown+=InputKeyDown;input.PreviewKeyUp+=InputKeyUp;input.LostKeyboardFocus+=InputLostFocus;
            host.IsVisibleChanged+=HostVisibilityChanged;
            session.StateChanged+=StateChanged;session.HistoryChanged+=StateChanged;session.PredictionsChanged+=SuggestionsChanged;
            queue.Changed+=StateChanged;pet.Dictation.Changed+=StateChanged;pet.Live.StateChanged+=StateChanged;RefreshState();
        }
        void OnUnloaded(object sender,RoutedEventArgs args){Unsubscribe();}
        void OnClosed(object sender,EventArgs args){Dispose();}
        void HostVisibilityChanged(object sender,DependencyPropertyChangedEventArgs args){RefreshState();}
        void InputChanged(object sender,TextChangedEventArgs args){RefreshState();}
        void InputLostFocus(object sender,KeyboardFocusChangedEventArgs args){enterHeld=false;}
        void StateChanged(){Dispatch(RefreshState);}
        void SuggestionsChanged(){Dispatch(Render);}
        void Dispatch(Action action)
        {
            if(closed || !subscribed || Dispatcher.HasShutdownStarted)return;
            if(Dispatcher.CheckAccess())action();else Dispatcher.BeginInvoke(action);
        }
        bool Blocked()
        {
            return !String.IsNullOrWhiteSpace(input.Text) || (AdditionalBlocker!=null && AdditionalBlocker()) || session.IsBusy || queue.IsRunning || queue.Items.Count>0 || pet.LiveActive || pet.DictationActive || pet.IsUpdating;
        }
        public void RefreshState()
        {
            if(closed || !subscribed)return;
            if(!Dispatcher.CheckAccess()){Dispatch(RefreshState);return;}
            debounce.Stop();bool blocked=Blocked();session.UpdatePredictionInput(this,blocked);session.RefreshPredictionSettings(pet.Settings);
            Render();
            if(!blocked && pet.Settings.ShowPredictedMessages && host.IsVisible)debounce.Start();
        }
        async void Request(object sender,EventArgs args)
        {
            debounce.Stop();if(closed || !subscribed || !host.IsVisible || Blocked() || !pet.Settings.ShowPredictedMessages)return;
            await session.RequestPredictionsAsync(pet.Settings);
            if(!closed && subscribed)Render();
        }
        void Render()
        {
            if(closed || !subscribed)return;
            string text=GhostText();
            if(text==null){HideGhost();return;}
            var box=Placeholder();if(box==null)return;
            if(!ghost || shown!=text){box.Text=text;ghost=true;shown=text;}
        }
        string GhostText()
        {
            if(closed || !pet.Settings.ShowPredictedMessages || Blocked())return null;
            var list=session.Predictions;if(list.Count==0)return null;
            string text=list[0];return String.IsNullOrWhiteSpace(text)?null:text;
        }
        void HideGhost()
        {
            if(!ghost)return;ghost=false;shown="";
            var box=Placeholder();if(box!=null)box.Text=defaultPlaceholder;
        }
        TextBlock Placeholder()
        {
            if(placeholder==null){placeholder=ChatUi.GetComposerPlaceholder(input);if(placeholder!=null){string text=placeholder.Text;if(!String.IsNullOrEmpty(text))defaultPlaceholder=text;}}
            return placeholder;
        }
        // One physical press performs one action: a held Enter confirms the
        // ghost and its repeats are swallowed, so they can never skip to
        // sending. The next press after release sends the filled draft.
        internal bool AllowEnter()
        {
            if(closed || enterHeld)return false;
            enterHeld=true;return true;
        }
        internal bool TryAcceptGhost()
        {
            if(closed || !ghost)return false;
            string text=GhostText();if(text==null){HideGhost();return false;}
            input.Text=text;input.CaretIndex=text.Length;
            return true;
        }
        internal bool DismissGhost()
        {
            if(closed || !ghost)return false;
            session.DismissPredictions(pet.Settings);HideGhost();
            return true;
        }
        // Escape dismissal runs only when no earlier handler consumed the key,
        // so dictation cancel and the quick-window shortcuts keep priority.
        void InputKeyDown(object sender,KeyEventArgs e)
        {
            if(e.Key==Key.Escape && ghost && DismissGhost())e.Handled=true;
        }
        void InputKeyUp(object sender,KeyEventArgs e){if(e.Key==Key.Enter)enterHeld=false;}
        void Unsubscribe()
        {
            debounce.Stop();if(!subscribed)return;subscribed=false;
            input.TextChanged-=InputChanged;input.PreviewKeyDown-=InputKeyDown;input.PreviewKeyUp-=InputKeyUp;input.LostKeyboardFocus-=InputLostFocus;
            host.IsVisibleChanged-=HostVisibilityChanged;
            session.StateChanged-=StateChanged;session.HistoryChanged-=StateChanged;session.PredictionsChanged-=SuggestionsChanged;
            queue.Changed-=StateChanged;pet.Dictation.Changed-=StateChanged;pet.Live.StateChanged-=StateChanged;session.RemovePredictionInput(this);
            HideGhost();
        }
        public void Dispose(){if(closed)return;Unsubscribe();closed=true;host.Closed-=OnClosed;Loaded-=OnLoaded;Unloaded-=OnUnloaded;debounce.Tick-=Request;}
    }
}
