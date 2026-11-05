using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;

internal static class TrayNotificationUiChecks
{
    internal static int Run(string directory)
    {
        int checks=0,opened=0;
        void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        T Named<T>(Control root,string name)where T:Control=>root.GetLogicalDescendants().OfType<T>().Single(c=>c.Name==name);
        DispatcherTimer Clock(TrayNotificationWindow card)=>(DispatcherTimer)typeof(TrayNotificationWindow).GetField("lifetime",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(card)!;
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Pump(int milliseconds=30){var end=DateTime.UtcNow.AddMilliseconds(milliseconds);do{Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}while(DateTime.UtcNow<end);Dispatcher.UIThread.RunJobs();}
        Directory.CreateDirectory(directory);DesktopTheme.Apply("light");
        var host=new Window{Width=500,Height=400,Content=new TextBox{Text="Existing conversation"}};host.Show();host.Activate();Pump();
        var cards=new List<TrayNotificationWindow>();
        TrayNotificationWindow Card(Func<string>? message=null){var card=new TrayNotificationWindow(()=>TextResources.Get("common.close"),message??(()=>"Your companion has a new update ready to view."),()=>TextResources.Get("common.close"),()=>opened++);cards.Add(card);return card;}
        try{
            var card=Card();card.ShowNearTray(host);Pump();
            Check(card.IsVisible&&card.Owner==null&&!card.ShowActivated&&!card.ShowInTaskbar&&card.Topmost&&host.IsActive,"native tray reminder is independent, passive, and preserves current conversation focus");
            var frame=(Border)card.Content!;Check(card.WindowDecorations==WindowDecorations.None&&frame.CornerRadius.TopLeft==20&&((ISolidColorBrush)frame.Background!).Color==Color.Parse("#FAFAFB"),"native tray reminder uses the shared rounded application popup surface");
            Check(card.Bounds.Width<=360&&card.Bounds.Height<260&&Named<TextBlock>(card,"TrayNotificationMessage").TextWrapping==TextWrapping.Wrap,"native short tray reminder wraps naturally without empty dialog space");
            var screen=card.Screens.ScreenFromWindow(host)??card.Screens.Primary!;var work=screen.WorkingArea;double scale=screen.Scaling;
            Check(card.Position.X>=work.X&&card.Position.Y>=work.Y&&card.Position.X+card.Bounds.Width*scale<=work.Right+1&&card.Position.Y+card.Bounds.Height*scale<=work.Bottom+1&&work.Right-card.Position.X-card.Bounds.Width*scale<40&&work.Bottom-card.Position.Y-card.Bounds.Height*scale<40,"native tray reminder sits inside selected monitor bottom-right work area");
            Check(card.GetLogicalDescendants().OfType<Button>().Count(b=>b.Name=="OpenTrayNotification")==1&&Named<Button>(card,"CloseDialog")!=null,"native tray reminder offers one action and shared close control");
            string initial=Named<TextBlock>(card,"TrayNotificationTitle").Text!;TextResources.ApplyLanguage(TextResources.PrepareLanguage(TextResources.CurrentLanguage=="en_US"?"zh_CN":"en_US"));Pump();Check(Named<TextBlock>(card,"TrayNotificationTitle").Text==TextResources.Get("common.close")&&Named<TextBlock>(card,"TrayNotificationTitle").Text!=initial,"native tray reminder text refreshes with live language changes");
            foreach(string theme in new[]{"light","dark"}){DesktopTheme.Apply(theme);Pump();AvaloniaHeadlessPlatform.ForceRenderTimerTick();using var image=card.CaptureRenderedFrame();image!.Save(Path.Combine(directory,"tray-notification-"+theme+".png"),new PngBitmapEncoderOptions());}
            Check(((ISolidColorBrush)frame.Background!).Color!=Color.Parse("#FAFAFB"),"native tray reminder surface follows dark theme live");DesktopTheme.Apply("light");
            card.MouseMove(new Point(50,50));Pump();Check(card.IsPointerOver&&!Clock(card).IsEnabled,"pointer interaction pauses native tray reminder expiry");card.MouseMove(new Point(-20,-20));Pump();Check(!card.IsPointerOver&&Clock(card).IsEnabled,"leaving native tray reminder resumes remaining lifetime");
            card.Activate();Named<Button>(card,"OpenTrayNotification").Focus();Pump();Check(card.IsKeyboardFocusWithin&&!Clock(card).IsEnabled,"keyboard interaction pauses native tray reminder expiry");
            bool closedBeforeOpen=false;card.Closed+=(_,_)=>closedBeforeOpen=opened==0;var action=Named<Button>(card,"OpenTrayNotification");Click(action);Check(!card.IsVisible&&closedBeforeOpen&&opened==1&&!Clock(card).IsEnabled,"native tray action closes before dispatch and stops expiry work");Click(action);Check(opened==1,"native closed reminder action cannot dispatch twice");
            host.Hide();var dismissed=Card();dismissed.ShowNearTray(host);Pump();Check(dismissed.IsVisible&&dismissed.Owner==null&&!host.IsVisible,"native tray reminder stays visible when its anchor is hidden");Click(Named<Button>(dismissed,"CloseDialog"));Check(!dismissed.IsVisible&&opened==1,"native close dismisses reminder without dispatching destination");
            var longCard=Card(()=>string.Join(" ",Enumerable.Repeat("A longer notification message.",1000)));longCard.ShowNearTray(host);Pump();var longAction=Named<Button>(longCard,"OpenTrayNotification");var bottom=longAction.TranslatePoint(new Point(0,longAction.Bounds.Height),longCard)!.Value;
            Check(longCard.Position.Y>=work.Y&&longCard.Position.Y+longCard.Bounds.Height*scale<=work.Bottom+1&&bottom.Y<=longCard.Bounds.Height,"native long reminder scrolls content and keeps action reachable inside work area");longCard.Close();
            host.Show();host.Activate();var expiring=Card();expiring.ShowNearTray(host);expiring.MouseMove(new Point(-20,-20));host.Activate();((TextBox)host.Content!).Focus();Pump();
            Check(!expiring.IsPointerOver&&!expiring.IsKeyboardFocusWithin&&Clock(expiring).IsEnabled,"native expiry fixture is unattended with its timer running");
            using(var timeout=new CancellationTokenSource(TimeSpan.FromMilliseconds(8300)))Dispatcher.UIThread.MainLoop(timeout.Token);
            Check(!expiring.IsVisible&&opened==1&&!Clock(expiring).IsEnabled,"native unattended tray reminder expires after eight seconds without opening destination");
            return checks;
        }finally{foreach(var card in cards)card.Close();host.Close();DesktopTheme.Apply("light");TextResources.ApplyLanguage(TextResources.PrepareLanguage("zh_CN"));}
    }
}
