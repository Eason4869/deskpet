using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed partial class DesktopController
{
    private sealed record ShownProactive(ImageDescriptionOverride Fallback);
    private readonly Dictionary<string,ShownProactive> shownProactive=new();
    private readonly Dictionary<string,ProactiveConversationBinding> proactiveBindings=new();
    private readonly Dictionary<(string Event,bool Quick),ChatWindow> proactiveViews=new();
    private string? lastProactive;
    internal bool ProactiveBusy=>proactiveBindings.Values.Any(binding=>binding.Session.IsBusy||binding.Queue.IsRunning||binding.Queue.HasPending);
    internal void RegisterShownProactive(ProactiveEvent item,PetSettings global)
    {
        PruneProactive();Observation.Events.Register(item);
        // Stable reference only: never retain a provider object or credentials.
        var fallback=global.ImageDescriptionModel==null?new ImageDescriptionOverride{Mode="disabled"}:new ImageDescriptionOverride{Mode="selected",Model=global.ImageDescriptionModel.Copy()};
        shownProactive[item.Id]=new ShownProactive(fallback);lastProactive=item.Id;
    }
    private void PruneProactive()
    {
        foreach(var id in shownProactive.Keys.Where(id=>Observation.Events.TryGet(id)==null).ToArray())
        {
            shownProactive.Remove(id);
            if(proactiveBindings.TryGetValue(id,out var binding)&&!binding.Accepted&&!binding.Session.IsBusy&&!proactiveViews.Keys.Any(key=>key.Event==id)){binding.Dispose();proactiveBindings.Remove(id);}
        }
    }
    internal void ReplyLastProactive(){if(lastProactive!=null)ReplyToProactive(lastProactive);}
    internal ChatWindow? ReplyToProactive(string id)
    {
        PruneProactive();var item=Observation.Events.TryGet(id);
        if(exiting||item==null||item.CompanionId!=Skin.Id||!shownProactive.TryGetValue(id,out var shown))return null;
        Observation.Invalidate(false);
        if(!proactiveBindings.TryGetValue(id,out var binding)){binding=new ProactiveConversationBinding(this,runtime,item,shown.Fallback);proactiveBindings.Add(id,binding);}
        return ShowProactive(binding,true);
    }
    private ChatWindow ShowProactive(ProactiveConversationBinding binding,bool quick)
    {
        var key=(binding.EventId,quick);
        if(!proactiveViews.TryGetValue(key,out var window))
        {
            window=new ChatWindow(this,quick,binding);proactiveViews.Add(key,window);
            window.Closed+=(_,_)=>proactiveViews.Remove(key);
        }
        window.Show();window.Activate();return window;
    }
    internal ChatWindow ExpandProactive(ProactiveConversationBinding binding)=>ShowProactive(binding,false);
    private async Task DrainProactiveBindingsAsync()
    {
        foreach(var binding in proactiveBindings.Values.ToArray())await binding.DrainAsync().ConfigureAwait(false);
    }
    private void DisposeProactiveBindings(){foreach(var binding in proactiveBindings.Values)binding.Dispose();proactiveBindings.Clear();}
}
