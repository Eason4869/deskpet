using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
namespace Dafeiyu
{
    public sealed class LiveCallView : UserControl
    {
        readonly PetWindow pet;readonly GeminiLiveSession session;readonly VoiceOrbit orbit;readonly TextBlock status,subtitle,mode;
        readonly Button connect,end;readonly DispatcherTimer meter;bool connecting,closed,stopping,started,hostCloseRequested;
        readonly Window host;readonly object previousContent;readonly string previousTitle;
        System.Threading.Tasks.Task closeTask;
        public event Action Closed;
        public Window Host {get{return host;}}
        public LiveCallView(PetWindow companion,Window origin)
        {
            pet=companion;session=pet.Live;host=origin;previousContent=host.Content;previousTitle=host.Title;
            bool compact=host is QuickChatWindow;Background=Ui.Brush("#F7F9FD");
            var root=new Grid{Margin=new Thickness(compact?16:28,compact?12:22,compact?16:28,compact?12:24)};root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});root.RowDefinitions.Add(new RowDefinition());root.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var header=new DockPanel();var back=UiIcons.Button("back",(host is SettingsWindow || host is ChatWindow && ((ChatWindow)host).CurrentPage=="settings")?TextResources.Get("liveCallWindow.liveCallView.uiIcons"):compact?TextResources.Get("liveCallWindow.liveCallView.uiIcons.alternativeA"):TextResources.Get("liveCallWindow.liveCallView.uiIcons.alternativeB"));back.Name="LiveBackToText";back.Click+=delegate{Close();};DockPanel.SetDock(back,Dock.Right);header.Children.Add(back);
            var heading=new StackPanel();var title=Ui.Text(()=>(pet.Skin.DisplayName),compact?16:24);title.FontWeight=FontWeights.SemiBold;heading.Children.Add(title);heading.Children.Add(LocalizedUi.Text(()=>(compact?TextResources.Get("liveCallWindow.liveCallView.text"):TextResources.Get("liveCallWindow.liveCallView.text.alternativeA")),11,Ui.Muted));header.Children.Add(heading);root.Children.Add(header);
            var center=new Grid{Margin=new Thickness(0,compact?2:12,0,compact?4:12)};center.RowDefinitions.Add(new RowDefinition());center.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            var stage=new Grid{MaxWidth=compact?146:330,MaxHeight=compact?146:330};orbit=new VoiceOrbit{Name="LiveVoiceOrbit",ClipToBounds=true};stage.Children.Add(orbit);
            var avatar=new Image{Source=SkinCatalog.GetAvatar(pet.Skin),Width=compact?66:118,Height=compact?66:118,Stretch=Stretch.Uniform,Clip=new RectangleGeometry(new Rect(0,0,compact?66:118,compact?66:118),compact?13.2:23.6,compact?13.2:23.6)};RenderOptions.SetBitmapScalingMode(avatar,BitmapScalingMode.HighQuality);stage.Children.Add(avatar);center.Children.Add(stage);
            var captions=new StackPanel{Margin=new Thickness(0,10,0,0)};mode=LocalizedUi.Text(()=>(TextResources.Get("liveCallWindow.liveCallView.text.alternativeB")),15,Ui.Blue);mode.HorizontalAlignment=HorizontalAlignment.Center;captions.Children.Add(mode);
            status=Ui.Text("",11,Ui.Muted);status.Name="LiveCallStatus";status.TextAlignment=TextAlignment.Center;status.Margin=new Thickness(0,5,0,10);captions.Children.Add(status);
            subtitle=LocalizedUi.Text(()=>(TextResources.Get("liveCallWindow.liveCallView.text.alternativeC")),14);subtitle.Name="LiveCallTranscript";subtitle.TextAlignment=TextAlignment.Center;
            captions.Children.Add(new Border{Child=new ScrollViewer{Content=subtitle,MaxHeight=compact?38:88,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},Padding=new Thickness(compact?7:14),Background=ThemeManager.Surface,CornerRadius=new CornerRadius(16)});Grid.SetRow(captions,1);center.Children.Add(captions);Grid.SetRow(center,1);root.Children.Add(center);
            var footer=new StackPanel();var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};
            connect=LocalizedUi.Button(()=>(TextResources.Get("liveCallWindow.liveCallView.text.alternativeD")),true);connect.Name="LiveReconnect";connect.Click+=async delegate{await ConnectAsync();};buttons.Children.Add(connect);
            end=UiIcons.Button("phone",TextResources.Get("liveCallWindow.liveCallView.uiIcons.alternativeC"));end.Name="LiveEndCall";end.Content=UiIcons.Image("phone",Ui.Brush("#C7425D"));end.Width=compact?44:56;end.Height=compact?36:48;end.Background=Ui.Brush("#FCE8EC");end.BorderBrush=Ui.Brush("#E6A5B5");end.Click+=delegate{Close();};buttons.Children.Add(end);
            var settings=UiIcons.Button("settings",TextResources.Get("liveCallWindow.liveCallView.uiIcons.alternativeD"));settings.Name="LiveCallSettings";settings.Width=44;settings.Height=36;settings.Margin=new Thickness(8,0,0,0);settings.VerticalAlignment=VerticalAlignment.Center;settings.Click+=delegate{pet.ShowLiveSettings();};buttons.Children.Add(settings);footer.Children.Add(buttons);
            var privacy=LocalizedUi.Text(()=>(TextResources.Get("liveCallWindow.liveCallView.text.alternativeE")),11,Ui.Muted);privacy.TextAlignment=TextAlignment.Center;privacy.Margin=new Thickness(0,compact?5:14,0,0);footer.Children.Add(privacy);Grid.SetRow(footer,2);root.Children.Add(footer);Content=new Border{Child=root,Background=Background,CornerRadius=new CornerRadius(compact?20:0),BorderBrush=Ui.Line,BorderThickness=new Thickness(compact?1:0)};
            meter=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(40)};meter.Tick+=delegate{orbit.Level=Math.Max(session.InputLevel,session.OutputLevel);LocalizedUi.Set(mode,"Text",()=>(connecting?TextResources.Get("liveCallWindow.liveCallView.outputLevelSessionInputLevel"):!session.IsActive?TextResources.Get("liveCallWindow.liveCallView.outputLevelSessionInputLevel.alternativeA"):session.OutputLevel>.025?TextResources.Get("liveCallWindow.liveCallView.outputLevelSessionInputLevel.alternativeB"):session.InputLevel>.025?TextResources.Get("liveCallWindow.liveCallView.outputLevelSessionInputLevel.alternativeC"):TextResources.Get("liveCallWindow.liveCallView.outputLevelSessionInputLevel.alternativeD")));};
            session.StateChanged+=Refresh;session.PartialTranscript+=Transcript;
            Loaded+=async delegate{if(started)return;started=true;meter.Start();await ConnectAsync();};
            host.Closing+=ClosingHost;host.IsVisibleChanged+=HostVisibilityChanged;
            PreviewKeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e){if(e.Key==System.Windows.Input.Key.Escape){e.Handled=true;Close();}};
        }
        public async System.Threading.Tasks.Task ConnectAsync()
        {
            if(closed || stopping || connecting || session.IsActive)return;connecting=true;Refresh();
            try{await pet.StartLiveAsync();}catch(Exception e){LocalizedUi.Set(status,"Text",()=>(e is ApiException || e is InvalidOperationException?e.Message:TextResources.Get("liveCallWindow.connectAsync.invalidOperationExceptionEMessage")));}
            finally{connecting=false;if(!closed){connect.Visibility=session.IsActive?Visibility.Collapsed:Visibility.Visible;connect.IsEnabled=true;}}
        }
        void Refresh(){if(closed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(Refresh));return;}status.Text=session.Status;connect.Visibility=session.IsActive || connecting?Visibility.Collapsed:Visibility.Visible;connect.IsEnabled=!connecting;}
        void Transcript(string role,string text){if(closed)return;if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(()=>Transcript(role,text)));return;}LocalizedUi.Set(subtitle,"Text",()=>((role=="user"?TextResources.Get("liveCallWindow.transcript.subtitle"):pet.Skin.DisplayName+"：")+text));}
        public void Show()
        {
            var main=host as ChatWindow;if(main!=null)main.MountLiveContent(this);else host.Content=this;LocalizedUi.Set(host,"Title",()=>(TextResources.Format("liveCallWindow.show.petSkinName",pet.Skin.DisplayName)));Activate();Focus();
        }
        public void Activate()
        {
            if(!host.IsVisible)host.Show();
            if(host.WindowState==WindowState.Minimized)host.WindowState=WindowState.Normal;
            host.Activate();
        }
        public async void Close(){await CloseAsync();}
        public System.Threading.Tasks.Task CloseAsync()
        {
            return closeTask ?? (closeTask=StopAndRestoreAsync());
        }
        async System.Threading.Tasks.Task StopAndRestoreAsync()
        {
            if(closed)return;stopping=true;IsEnabled=false;meter.Stop();
            try{await session.StopAsync();}
            finally
            {
                closed=true;session.StateChanged-=Refresh;session.PartialTranscript-=Transcript;
                host.Closing-=ClosingHost;host.IsVisibleChanged-=HostVisibilityChanged;
                var main=host as ChatWindow;if(main!=null)main.UnmountLiveContent(this);else host.Content=previousContent;host.Title=previousTitle;
                if(Closed!=null)Closed();
                var quick=host as QuickChatWindow;if(quick!=null && quick.IsVisible)quick.FocusInput();
            }
        }
        async void ClosingHost(object sender,CancelEventArgs args)
        {
            if(closed)return;args.Cancel=true;if(hostCloseRequested)return;hostCloseRequested=true;
            await CloseAsync();
            DispatcherOperation pending=host.Dispatcher.BeginInvoke(new Action(host.Close));
        }
        void HostVisibilityChanged(object sender,DependencyPropertyChangedEventArgs args)
        {
            if(!host.IsVisible && !closed)Close();
        }
    }

    public sealed class VoiceOrbit : FrameworkElement
    {
        static readonly Pen outline=new Pen(Ui.Line,1),blue=new Pen(Ui.Blue,2.5),mint=new Pen(Ui.Brush("#6AAFA4"),2.5);
        readonly Func<double> animationClock;
        public VoiceOrbit():this(()=>System.Diagnostics.Stopwatch.GetTimestamp()/(double)System.Diagnostics.Stopwatch.Frequency){}
        internal VoiceOrbit(Func<double> clock){animationClock=clock;}
        double level;
        public double Level {get{return level;}set{double target=Double.IsNaN(value) || Double.IsInfinity(value)?0:Math.Max(0,Math.Min(1,value));double weight=target>level?.82:.30;level+=(target-level)*weight;InvalidateVisual();}}
        protected override void OnRender(DrawingContext drawing)
        {
            Point center=new Point(ActualWidth/2,ActualHeight/2);double size=Math.Min(ActualWidth,ActualHeight),radius=size*.27,gap=size*.045;
            drawing.DrawEllipse(Ui.Pale,null,center,radius,radius);drawing.DrawEllipse(null,outline,center,radius+gap*.45,radius+gap*.45);
            // Make ordinary speech visibly expressive without increasing audio gain.
            // Reserve space for the stroke even at full volume in the compact view.
            double phase=animationClock()*3.2;double response=VoiceVisual.Response(level),maxLength=Math.Max(0,size/2-2.5-radius-gap);
            for(int i=0;i<64;i++){double angle=i*Math.PI/32;double envelope=.28+.45*Math.Abs(Math.Sin(angle*3+response*7+phase))+.27*Math.Abs(Math.Sin(angle*5-phase*1.37));double length=Math.Min(maxLength,size*.012+response*maxLength*1.35*envelope);
                drawing.DrawLine(i<32?blue:mint,new Point(center.X+Math.Cos(angle)*(radius+gap),center.Y+Math.Sin(angle)*(radius+gap)),new Point(center.X+Math.Cos(angle)*(radius+gap+length),center.Y+Math.Sin(angle)*(radius+gap+length)));}
        }
    }
}
