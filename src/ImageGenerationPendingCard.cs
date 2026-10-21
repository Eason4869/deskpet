using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
namespace Dafeiyu
{
    internal sealed class ImageGenerationPendingCard : Grid
    {
        readonly ImageGenerationTaskManager manager;readonly string id;readonly Func<bool> enabled;readonly Func<string,FrameworkElement> completed;readonly Action cancel;
        readonly ImageGenerationDots dots=new ImageGenerationDots();readonly TextBlock label=Ui.Text("",12,Ui.Muted),elapsed=Ui.Text("",11,Ui.Muted);readonly Button stop;
        readonly DispatcherTimer frames=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(34)},seconds=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
        Window host;bool listening,replaced;ImageGenerationTaskSnapshot snapshot;
        internal bool ClockRunning {get{return frames.IsEnabled;}}
        bool motionEnabled=true;internal bool MotionEnabled {get{return motionEnabled;}set{motionEnabled=value;UpdateClock();}}
        internal ImageGenerationPendingCard(ImageGenerationTaskManager tasks,string taskId,Func<bool> motion,Action cancelAction,Func<string,FrameworkElement> result)
        {
            Name="ImageGenerationPendingCard";manager=tasks;id=taskId;enabled=motion;cancel=cancelAction;completed=result;MotionEnabled=true;HorizontalAlignment=HorizontalAlignment.Left;MaxWidth=224;
            var body=new StackPanel();body.Children.Add(dots);body.Children.Add(label);body.Children.Add(elapsed);stop=Ui.Button("取消生成");stop.Click+=delegate{if(cancel!=null)cancel();};body.Children.Add(stop);Children.Add(body);
            frames.Tick+=delegate{Draw();};seconds.Tick+=delegate{UpdateClock();};Loaded+=delegate{Attach();};Unloaded+=delegate{Detach();};IsVisibleChanged+=delegate{UpdateClock();};
        }
        internal static ImageGenerationPendingCard Create(PetWindow pet,Window host,string id,Action<GeneratedImage> regenerate=null)
        {return new ImageGenerationPendingCard(pet.ImageTasks,id,()=>pet.Settings.ImageGenerationMotionEnabled,()=>pet.ImageTasks.CancelAsync(id),asset=>new GeneratedImageCard(pet,host,pet.ImageStore.Get(asset)??throwMissing(),regenerate));}
        static GeneratedImage throwMissing(){throw new System.IO.FileNotFoundException("图片文件缺失");}
        void Attach(){if(listening)return;listening=true;manager.Changed+=Changed;host=Window.GetWindow(this);if(host!=null)host.StateChanged+=HostChanged;SystemParameters.StaticPropertyChanged+=SystemChanged;snapshot=manager.Get(id);Update();}
        void Detach(){if(!listening)return;listening=false;manager.Changed-=Changed;if(host!=null)host.StateChanged-=HostChanged;host=null;SystemParameters.StaticPropertyChanged-=SystemChanged;frames.Stop();seconds.Stop();}
        void HostChanged(object sender,EventArgs e){UpdateClock();}void SystemChanged(object sender,System.ComponentModel.PropertyChangedEventArgs e){UpdateClock();}
        void Changed(ImageGenerationTaskSnapshot value){if(value.Id!=id&&(snapshot==null||snapshot.State!=ImageGenerationTaskState.Queued))return;Dispatcher.BeginInvoke(new Action(delegate{if(!listening)return;snapshot=value.Id==id?value:manager.Get(id);Update();}));}
        void Update()
        {
            label.Text=ImageGenerationMotion.Label(snapshot);if(snapshot!=null&&snapshot.State==ImageGenerationTaskState.Queued){int position=1;foreach(var task in manager.List())if(task.State==ImageGenerationTaskState.Queued&&task.QueuedUtc<snapshot.QueuedUtc)position++;label.Text+=" · 第 "+position+" 项";}stop.Visibility=snapshot!=null&&!snapshot.IsTerminal?Visibility.Visible:Visibility.Collapsed;
            if(snapshot!=null&&snapshot.State==ImageGenerationTaskState.Succeeded&&!replaced&&!String.IsNullOrEmpty(snapshot.ResultImageId))try{
                var view=completed(snapshot.ResultImageId);Children.Clear();Children.Add(view);replaced=true;frames.Stop();seconds.Stop();if(enabled()&&SystemParameters.ClientAreaAnimation)view.BeginAnimation(OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(180)));return;
            }catch(System.IO.IOException){label.Text="图片文件缺失";}catch(ArgumentException){label.Text="图片文件无法读取";}
            UpdateClock();Draw();
        }
        void UpdateClock(bool updateElapsed=true){if(replaced){frames.Stop();seconds.Stop();return;}bool active=listening&&IsVisible&&(host==null||host.WindowState!=WindowState.Minimized);if(active&&(snapshot==null||!snapshot.IsTerminal))seconds.Start();else seconds.Stop();if(snapshot!=null&&ImageGenerationMotion.ShouldAnimate(snapshot.State,MotionEnabled&&enabled(),active,false,!SystemParameters.ClientAreaAnimation))frames.Start();else frames.Stop();if(updateElapsed&&snapshot!=null){elapsed.Text=ImageGenerationMotion.ElapsedSeconds(snapshot,DateTime.UtcNow)+" 秒";}}
        void Draw(){UpdateClock(false);double t=snapshot==null?0:(DateTime.UtcNow-(snapshot.StartedUtc??snapshot.QueuedUtc)).TotalSeconds/5;dots.Phase=frames.IsEnabled?t:0;dots.InvalidateVisual();}
    }
    internal sealed class ImageGenerationDots : FrameworkElement
    {
        internal double Phase;
        internal ImageGenerationDots(){MaxWidth=224;Margin=new Thickness(0,8,0,8);}
        protected override Size MeasureOverride(Size available){double size=Math.Min(224,Double.IsInfinity(available.Width)?224:available.Width);return new Size(size,size);}
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);double width=Math.Min(224,ActualWidth),height=Math.Min(224,ActualHeight);dc.DrawRoundedRectangle(Ui.Pale,null,new Rect(0,0,width,height),18,18);var sample=ImageGenerationMotion.Sample(Phase);
            for(double y=16;y<height-12;y+=12)for(double x=16;x<width-12;x+=12){dc.PushOpacity(ImageGenerationMotion.DotOpacity(sample,x/width,y/height));dc.DrawEllipse(Ui.Muted,null,new Point(x,y),.75,.75);dc.Pop();}
        }
    }
}
