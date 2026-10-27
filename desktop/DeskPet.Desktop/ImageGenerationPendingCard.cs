using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dafeiyu;
namespace DeskPet.Desktop;
internal sealed class ImageGenerationPendingCard : Grid
{
    readonly Action<GeneratedImage>? regenerate;readonly DesktopController owner;readonly Window host;readonly string id;
    readonly ImageGenerationDots dots=new();readonly TextBlock label=Ui.Text("",12),elapsed=Ui.Text("",11);readonly Button cancel;
    readonly DispatcherTimer frames=new(){Interval=TimeSpan.FromMilliseconds(34)},seconds=new(){Interval=TimeSpan.FromSeconds(1)};
    readonly List<Visual> ancestors=new();ImageGenerationTaskSnapshot? snapshot;bool attached,replaced;GeneratedImageCard? result;
    internal bool ClockRunning=>frames.IsEnabled;
    bool motionEnabled=true;internal bool MotionEnabled{get=>motionEnabled;set{motionEnabled=value;UpdateClock();}}
    bool reducedMotion;internal bool ReducedMotion{get=>reducedMotion;set{reducedMotion=value;UpdateClock();}}
    internal ImageGenerationPendingCard(DesktopController controller,Window window,string taskId,Action<GeneratedImage>? repeat=null)
    {
        regenerate=repeat;owner=controller;host=window;id=taskId;Name="ImageGenerationPendingCard";HorizontalAlignment=HorizontalAlignment.Left;MaxWidth=224;
        cancel=LocalizedUi.Button(()=>(TextResources.Get("imageGenerationPendingCard.imageGenerationPendingCard.text")),()=>{_=owner.ImageTasks.CancelAsync(id);});Children.Add(Ui.Column(dots,label,elapsed,cancel));
        frames.Tick+=(_,_)=>Draw();seconds.Tick+=(_,_)=>UpdateClock();AttachedToVisualTree+=(_,_)=>Attach();DetachedFromVisualTree+=(_,_)=>Detach();PropertyChanged+=(_,e)=>{if(e.Property==IsVisibleProperty)UpdateClock();};
    }
    void Attach(){if(attached)return;attached=true;foreach(var parent in this.GetVisualAncestors()){ancestors.Add(parent);parent.PropertyChanged+=HostChanged;}owner.ImageTasks.Changed+=Changed;host.PropertyChanged+=HostChanged;snapshot=owner.ImageTasks.Get(id);Update();}
    void Detach(){attached=false;foreach(var parent in ancestors)parent.PropertyChanged-=HostChanged;ancestors.Clear();owner.ImageTasks.Changed-=Changed;host.PropertyChanged-=HostChanged;frames.Stop();seconds.Stop();result?.Dispose();result=null;if(replaced)replaced=false;}
    void HostChanged(object? sender,AvaloniaPropertyChangedEventArgs e){if(e.Property==Window.WindowStateProperty||e.Property==IsVisibleProperty)UpdateClock();}
    void Changed(ImageGenerationTaskSnapshot value){if(value.Id==id||snapshot?.State==ImageGenerationTaskState.Queued)Dispatcher.UIThread.Post(()=>{if(!attached)return;snapshot=value.Id==id?value:owner.ImageTasks.Get(id);Update();});}
    void Update()
    {
        label.Text=ImageGenerationMotion.Label(snapshot);if(snapshot!=null&&snapshot.State==ImageGenerationTaskState.Queued){int position=1;foreach(var task in owner.ImageTasks.List())if(task.State==ImageGenerationTaskState.Queued&&task.QueuedUtc<snapshot.QueuedUtc)position++;label.Text+=TextResources.Format("imageGenerationPendingCard.update.labelPosition",position);}cancel.IsVisible=snapshot!=null&&!snapshot.IsTerminal;
        if(snapshot?.State==ImageGenerationTaskState.Succeeded&&!replaced&&!string.IsNullOrEmpty(snapshot.ResultImageId))try{result=new GeneratedImageCard(owner,host,owner.ImageStore.Get(snapshot.ResultImageId)??throw new FileNotFoundException(),regenerate);Children.Clear();Children.Add(result);if(MotionEnabled&&owner.Settings.ImageGenerationMotionEnabled&&!ReducedMotion&&!ImageGenerationMotion.SystemReducedMotion){result.Opacity=0;result.Transitions=new Avalonia.Animation.Transitions{new Avalonia.Animation.DoubleTransition{Property=OpacityProperty,Duration=TimeSpan.FromMilliseconds(180)}};var view=result;Dispatcher.UIThread.Post(()=>{if(attached)view.Opacity=1;});}replaced=true;frames.Stop();seconds.Stop();return;}catch(IOException){LocalizedUi.Set(label,"Text",()=>(TextResources.Get("imageGenerationPendingCard.update.label")));}catch(ArgumentException){LocalizedUi.Set(label,"Text",()=>(TextResources.Get("imageGenerationPendingCard.update.label.alternativeA")));}
        UpdateClock();Draw();
    }
    void UpdateClock(bool updateElapsed=true)
    {
        if(replaced)return;bool visible=attached&&IsEffectivelyVisible&&host.IsVisible&&host.WindowState!=WindowState.Minimized;
        if(visible&&snapshot?.IsTerminal!=true)seconds.Start();else seconds.Stop();
        if(snapshot!=null&&ImageGenerationMotion.ShouldAnimate(snapshot.State,MotionEnabled&&owner.Settings.ImageGenerationMotionEnabled,visible,false,ReducedMotion||ImageGenerationMotion.SystemReducedMotion))frames.Start();else frames.Stop();
        if(updateElapsed&&snapshot!=null)elapsed.Text=TextResources.Format("imageGenerationPendingCard.updateClock.elapsedImageGenerationMotionElapsedSeconds",ImageGenerationMotion.ElapsedSeconds(snapshot,DateTime.UtcNow));
    }
    void Draw(){UpdateClock(false);dots.Phase=frames.IsEnabled&&snapshot!=null?(DateTime.UtcNow-(snapshot.StartedUtc??snapshot.QueuedUtc)).TotalSeconds/5:0;dots.InvalidateVisual();}
}
internal sealed class ImageGenerationDots : Control
{
    internal double Phase;
    internal ImageGenerationDots(){MaxWidth=224;}
    protected override Size MeasureOverride(Size available){double size=Math.Min(224,Double.IsInfinity(available.Width)?224:available.Width);return new Size(size,size);}
        public override void Render(DrawingContext context)
    {
        base.Render(context);double width=Math.Min(224,Bounds.Width),height=Math.Min(224,Bounds.Height);context.DrawRectangle(DesktopTheme.Brush("#EDF3FF"),null,new RoundedRect(new Rect(0,0,width,height),18));var sample=ImageGenerationMotion.Sample(Phase);
        for(double y=16;y<height-12;y+=12)for(double x=16;x<width-12;x+=12){using(context.PushOpacity(ImageGenerationMotion.DotOpacity(sample,x/width,y/height)))context.DrawEllipse(DesktopTheme.Brush("#61738B"),null,new Point(x,y),.75,.75);}
    }
}
