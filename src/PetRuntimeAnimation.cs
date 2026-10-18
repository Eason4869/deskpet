using System;
using System.Collections.Generic;
using System.Linq;
namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        readonly Dictionary<ConversationSession,int> observedTerminals=new Dictionary<ConversationSession,int>();
        string completionAction="idle";
        double completionUntil;
        bool runtimeOwnsAnimation,runtimeWasBusy;
        private void ConversationChanged()
        {
            if(closing)return;
            if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(ConversationChanged));return;}
            bool busy=DialogueBusy;
            if(busy&&!runtimeWasBusy&&interaction.IsSleeping)CancelInteraction();runtimeWasBusy=busy;
            if(busy || DictationActive)DismissGreeting();
            var sessions=new List<ConversationSession>();if(conversation!=null)sessions.Add(conversation);
            if(workspaces!=null)sessions.AddRange(workspaces.OpenWorkspaces.Select(w=>w.Session).Where(s=>s!=null));
            double now=watch.Elapsed.TotalSeconds;
            foreach(var old in observedTerminals.Keys.Where(s=>!sessions.Contains(s)).ToArray())observedTerminals.Remove(old);
            foreach(var session in sessions)
            {
                int previous;observedTerminals.TryGetValue(session,out previous);
                if(previous!=session.TerminalRevision){completionAction=session.RuntimeAction;completionUntil=now+3;}
                observedTerminals[session]=session.TerminalRevision;
            }
            var active=sessions.Where(s=>s.IsBusy).Select(s=>s.RuntimeAction).ToArray();
            string state=active.Contains("waiting")?"waiting":active.Contains("failed")?"failed":active.Contains("running")?"running":active.FirstOrDefault()??"idle";
            if(LiveActive || DictationActive)state="waiting";
            if(state=="idle"&&sessions.Any(s=>s.NeedsExecutionReconciliation))state="waiting";
            if(state=="idle")state=HasQueuedMessages?"waiting":now<completionUntil?completionAction:"idle";
            if(dragging || interaction.Active || dropMotion.Active || Paused || !IsPetVisible || now<manualActionUntil)return;
            // Wandering and explicit actions retain their normal duration outside a run.
            if(state=="idle" && !runtimeOwnsAnimation)return;
            if(state!="idle")walking=false;
            runtimeOwnsAnimation=state!="idle";
            if(Animation.State!=state){Animation.Play(state,0);UpdateFrame();}
        }
    }
}
