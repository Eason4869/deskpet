using Dafeiyu;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DeskPet.Desktop;

internal sealed partial class DesktopController
{
    private sealed record ShownProactive(ImageDescriptionOverride Fallback);
    private readonly Dictionary<string,ShownProactive> shownProactive=new();
    private readonly Dictionary<string,ProactiveConversationBinding> proactiveBindings=new();
    private readonly Dictionary<(string Event,bool Quick),ChatWindow> proactiveViews=new();
    private readonly object proactiveGate=new();
    private readonly Dictionary<string,Task> proactiveRetiring=new();
    private string? lastProactive;
    private Window? proactiveHistory;
    private ProactiveConversationBinding[] ProactiveBindingsSnapshot(){lock(proactiveGate)return proactiveBindings.Values.ToArray();}
    internal bool ProactiveBusy {get{lock(proactiveGate)return proactiveRetiring.Values.Any(task=>task.Status!=TaskStatus.RanToCompletion)||proactiveBindings.Values.Any(binding=>binding.Session.IsBusy||binding.Queue.IsRunning||binding.Queue.HasPending);}}
    internal void RegisterShownProactive(ProactiveEvent item,PetSettings global)
    {
        PruneProactive();Observation.Events.Register(item);
        // Stable reference only: never retain a provider object or credentials.
        var fallback=global.ImageDescriptionModel==null?new ImageDescriptionOverride{Mode="disabled"}:new ImageDescriptionOverride{Mode="selected",Model=global.ImageDescriptionModel.Copy()};
        shownProactive[item.Id]=new ShownProactive(fallback);lastProactive=item.Id;
    }
    private void PruneProactive()
    {
        lock(proactiveGate)foreach(var key in proactiveRetiring.Where(pair=>pair.Value.Status==TaskStatus.RanToCompletion).Select(pair=>pair.Key).ToArray())proactiveRetiring.Remove(key);
        foreach(var id in shownProactive.Keys.Where(id=>Observation.Events.TryGet(id)==null).ToArray())
        {
            shownProactive.Remove(id);
            foreach(var binding in ProactiveBindingsSnapshot().Where(binding=>binding.EventId==id))if(!proactiveViews.Keys.Any(key=>key.Event==binding.Id))RetireUnanswered(binding.Id);
        }
    }
    internal void ReplyLastProactive(){if(lastProactive!=null)ReplyToProactive(lastProactive);}
    internal ChatWindow? ReplyToProactive(string id)
    {
        PruneProactive();var item=Observation.Events.TryGet(id);
        if(exiting||item==null||item.CompanionId!=Skin.Id||!shownProactive.TryGetValue(id,out var shown))return null;
        Observation.Invalidate(false);
        ProactiveConversationBinding binding;
        lock(proactiveGate)
        {
            string? conversation=item.ConversationId;
            if(conversation!=null&&proactiveRetiring.TryGetValue(conversation,out var retiring)&&!retiring.IsCompleted)return null;
            if(conversation==null||!proactiveBindings.TryGetValue(conversation,out binding!)){binding=new ProactiveConversationBinding(this,runtime,item,shown.Fallback);proactiveBindings.Add(binding.Id,binding);}
        }
        return ShowProactive(binding,true);
    }
    private ChatWindow ShowProactive(ProactiveConversationBinding binding,bool quick)
    {
        var key=(binding.Id,quick);
        if(!proactiveViews.TryGetValue(key,out var window))
        {
            window=new ChatWindow(this,quick,binding);proactiveViews.Add(key,window);
            window.Closed+=(_,_)=>{proactiveViews.Remove(key);if(!proactiveViews.Keys.Any(view=>view.Event==key.Id))RetireUnanswered(key.Id);};
        }
        window.Show();window.Activate();return window;
    }
    internal ChatWindow ExpandProactive(ProactiveConversationBinding binding)=>ShowProactive(binding,false);
    internal ChatWindow? ReopenProactiveConversation(string id)
    {
        if(exiting)return null;
        var saved=WorkBindings.ProactiveConversations(Skin.Id).SingleOrDefault(row=>row.Id==id);if(saved==null)return null;
        Observation.Invalidate(false);
        ProactiveConversationBinding binding;
        lock(proactiveGate){if(!proactiveBindings.TryGetValue(id,out binding!))proactiveBindings.Add(id,binding=new ProactiveConversationBinding(this,runtime,saved));}
        return ShowProactive(binding,false);
    }
    internal void ShowProactiveHistory()
    {
        if(exiting)return;Observation.Invalidate(false);
        if(proactiveHistory?.IsVisible==true){proactiveHistory.Activate();return;}
        var rows=WorkBindings.ProactiveConversations(Skin.Id);int skip=0;var list=Ui.Column();
        var dialog=LocalizedUi.Window(()=>TextResources.Get("desktopObservation.history"),new Border(),500,550);dialog.Background=DesktopTheme.Surface;proactiveHistory=dialog;dialog.Closed+=(_,_)=>proactiveHistory=null;
        var more=SettingsVisual.Button(()=>TextResources.Get("desktopObservation.historyMore"),()=>{});
        void ShowPage()
        {
            list.Children.Clear();
            if(rows.Length==0)list.Children.Add(SettingsVisual.Text(()=>TextResources.Get("desktopObservation.historyEmpty"),14,true));
            foreach(var row in rows.Skip(skip).Take(20))
            {
                string id=row.Id;var button=SettingsVisual.Button(row.Name,()=>{dialog.Close();ReopenProactiveConversation(id);});
                button.HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch;button.HorizontalContentAlignment=Avalonia.Layout.HorizontalAlignment.Left;
                if(button.Content is TextBlock text){text.TextWrapping=TextWrapping.NoWrap;text.TextTrimming=TextTrimming.CharacterEllipsis;}
                ToolTip.SetTip(button,row.Name);list.Children.Add(button);
            }
            more.IsVisible=rows.Length>skip+20;
        }
        more.Click+=(_,_)=>{skip+=20;ShowPage();};ShowPage();
        var shell=new DockPanel{Margin=new Thickness(18)};var actions=SettingsVisual.Wrap(more,SettingsVisual.Button(()=>TextResources.Get("common.close"),dialog.Close));DockPanel.SetDock(actions,Dock.Bottom);shell.Children.Add(actions);shell.Children.Add(new ScrollViewer{Content=list,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled});dialog.Content=shell;dialog.Show();
    }
    private void RetireUnanswered(string id)
    {
        lock(proactiveGate)
        {
            if(!proactiveBindings.TryGetValue(id,out var binding)||binding.Accepted||binding.Session.IsBusy)return;
            proactiveBindings.Remove(id);
            proactiveRetiring[id]=Task.Run(async()=>{try{await binding.DrainAsync().ConfigureAwait(false);}finally{binding.Dispose();}});
        }
    }
    private async Task DrainProactiveBindingsAsync()
    {
        foreach(var binding in ProactiveBindingsSnapshot())await binding.DrainAsync().ConfigureAwait(false);
        Task[] retiring;lock(proactiveGate)retiring=proactiveRetiring.Values.ToArray();await Task.WhenAll(retiring).ConfigureAwait(false);
    }
    private void DisposeProactiveBindings(){var bindings=ProactiveBindingsSnapshot();lock(proactiveGate)proactiveBindings.Clear();foreach(var binding in bindings)binding.Dispose();}
    internal void ClearProactiveForMaintenance(){Observation.Events.Clear();shownProactive.Clear();lastProactive=null;foreach(var binding in ProactiveBindingsSnapshot())binding.ClearFallbackForMaintenance();}
}
