using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class ActionPaletteSmoke
    {
        static int passed;
        [STAThread] public static int Main()
        {
            var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
            string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"action-palette-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            PetWindow pet=null;ActionPaletteWindow palette=null;
            try
            {
                var cfg=new PetSettings {FollowCursor=false,GreetingEnabled=false,HideOnFullscreen=false,Wander=false,AutoUpdate=false,QuietMode=true,ReadReplies=false,SaveChatHistory=false};
                foreach(string key in cfg.Hotkeys.Keys.ToArray())cfg.Hotkeys[key]="";
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(folder,"settings.json")),null);pet.Show();Pump();
                foreach(string skin in new[]{"e-bao","dafeiyu","marfu","xiaobei"})
                {
                    Check(pet.SelectSkin(pet.Catalog.Find(skin)),"select "+skin);
                    var pointer=DesktopInterop.Cursor();palette=new ActionPaletteWindow(pet);palette.SourceInitialized+=delegate{Check(palette.Opacity==0,"palette remains transparent until native placement completes");};palette.ShowForPet();Pump();
                    var petBounds=DesktopInterop.Bounds(new WindowInteropHelper(pet).Handle);var paletteBounds=DesktopInterop.Bounds(new WindowInteropHelper(palette).Handle);var workArea=DesktopInterop.WorkArea(new WindowInteropHelper(pet).Handle);
                    var expected=PetMath.ClampPosition(pointer.X-paletteBounds.Width/2,pointer.Y-paletteBounds.Height*220/490,paletteBounds.Width,paletteBounds.Height,DesktopInterop.WorkArea(pointer));
                    Check((paletteBounds.TopLeft-expected).Length<2 && palette.Opacity==1,"wheel is centered at invocation cursor, with screen-edge clamping");
                    Check(palette.Width<=370 && palette.Height<=410,"wheel reduced by about twenty percent");
                    Check(palette.IsVisible && palette.Owner==pet && !palette.ShowInTaskbar,"owned compact palette "+skin);
                    Check(Actions(palette).Count()==4,"four skill choices "+skin);
                    Check(!Actions(palette).Any(b=>b.IsKeyboardFocused),"opening the wheel does not preselect the top skill "+skin);
                    Capture(palette,Path.Combine(folder,skin+"-neutral.png"));
                    var hoverTarget=Actions(palette).Last();var hoverPreview=All(hoverTarget).OfType<Image>().Single();
                    var hoverPoint=hoverPreview.PointToScreen(new Point(hoverPreview.ActualWidth/2,hoverPreview.ActualHeight/2));
                    bool pointerMoved=MovePointer((int)hoverPoint.X,(int)hoverPoint.Y);Pump();
                    var nativeTitle=new System.Text.StringBuilder(160);GetWindowText(GetForegroundWindow(),nativeTitle,nativeTitle.Capacity);
                    Console.WriteLine("Hover diagnostic: requested="+hoverPoint+" actual="+DesktopInterop.Cursor()+" SetCursorPos="+pointerMoved+" target="+hoverTarget.Name+" mouseover="+hoverTarget.IsMouseOver+" nativePalette="+DesktopInterop.IsWindowAtPoint(new WindowInteropHelper(palette).Handle,hoverPoint)+" foreground="+nativeTitle);
                    Check(hoverTarget.IsMouseOver && Highlighted(hoverTarget) && !Highlighted(Actions(palette).First()),"only the hovered sector highlights after opening "+skin);
                    Capture(palette,Path.Combine(folder,skin+"-hover.png"));
                    var hubPoint=palette.PointToScreen(new Point(palette.ActualWidth/2,palette.ActualHeight*220/490));MovePointer((int)hubPoint.X,(int)hubPoint.Y);Pump();
                    Check(!Actions(palette).Any(Highlighted),"moving back to the hub clears all sector highlights "+skin);
                    palette.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(palette),0,Key.Right){RoutedEvent=Keyboard.PreviewKeyDownEvent});Pump();
                    Check(Actions(palette).First().IsKeyboardFocused && Highlighted(Actions(palette).First()),"arrow navigation still highlights the first skill "+skin);
                    Check(All(palette).OfType<Image>().Count(i=>i.Source!=null)==4,"four actual pet previews "+skin);
                    foreach(var button in Actions(palette))
                    {
                        var preview=All(button).OfType<Image>().Single();var p=preview.TransformToAncestor(palette).Transform(new Point(preview.ActualWidth/2,preview.ActualHeight/2));
                        DependencyObject hit=palette.InputHitTest(p) as DependencyObject;while(hit!=null && !(hit is Button))hit=VisualTreeHelper.GetParent(hit);
                        Check(ReferenceEquals(hit,button),"each quarter owns its preview hit target "+skin+button.Name);
                    }
                    var previewPositions=All(palette).OfType<Image>().Select(i=>i.TransformToAncestor(palette).Transform(new Point(i.ActualWidth/2,i.ActualHeight/2))).ToArray();
                    Check(previewPositions.Max(p=>p.Y)-previewPositions.Min(p=>p.Y)>60,"skill choices are arranged around a wheel, not a grid");
                    Check(!Actions(palette).Any(b=>b.Name.Contains("petting") || b.Name.Contains("poked") || b.Name.Contains("happy") || b.Name.Contains("lifted") || b.Name.Contains("landing")),"gesture actions excluded "+skin);
                    Capture(palette,Path.Combine(folder,skin+"-interactions.png"));
                    Click(palette,"PaletteBasicTab");Pump();
                    Check(Actions(palette).Count()==9,"nine base action choices "+skin);
                    Check(All(palette).OfType<Image>().Count(i=>i.Source!=null)==9,"nine actual base previews "+skin);
                    Check(All(palette).OfType<Button>().Where(b=>b.Name.StartsWith("PaletteAction_")).All(b=>BoundsInside(b,palette)),"all base actions visible "+skin);
                    foreach(var button in Actions(palette))
                    {
                        var preview=All(button).OfType<Image>().Single();
                        Point point=preview.TransformToAncestor(palette).Transform(new Point(preview.ActualWidth/2,preview.ActualHeight/2));
                        DependencyObject hit=palette.InputHitTest(point) as DependencyObject;
                        while(hit!=null && !(hit is Button))hit=VisualTreeHelper.GetParent(hit);
                        Check(ReferenceEquals(hit,button),"wheel pointer selects only its own sector "+button.Name);
                    }
                    Capture(palette,Path.Combine(folder,skin+"-base.png"));
                    Click(palette,"PaletteAction_waving");Check(!palette.IsVisible && pet.Animation.State=="waving","base action plays and closes "+skin);
                    palette=new ActionPaletteWindow(pet);palette.ShowForPet();Pump();Click(palette,"PaletteInteraction_feeding");
                    Check(!palette.IsVisible && State(pet).Name=="feeding","interaction plays and closes "+skin);
                    palette=new ActionPaletteWindow(pet);palette.ShowForPet();Pump();Click(palette,"PaletteInteraction_special2");
                    Check(!palette.IsVisible && State(pet).Name=="special2","second unique action plays and closes "+skin);
                }
                pet.PlayInteraction("sleeping");palette=new ActionPaletteWindow(pet);palette.ShowForPet();Pump();
                var wake=Find<Button>(palette,"PaletteInteraction_sleeping");Check(System.Windows.Automation.AutomationProperties.GetName(wake)=="叫醒","sleep action becomes wake");
                Click(palette,"PaletteInteraction_sleeping");Check(!pet.IsSleeping && !palette.IsVisible,"wake cancels sleep and closes");
                palette=new ActionPaletteWindow(pet);palette.ShowForPet();Pump();
                palette.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(palette),0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});
                Check(!palette.IsVisible,"Escape closes palette");
                palette=new ActionPaletteWindow(pet);palette.ShowForPet();Pump();
                var outside=new Window {Width=180,Height=100,ShowInTaskbar=false,Title="Palette outside fixture"};outside.Show();outside.Activate();Pump();
                Check(!palette.IsVisible,"outside activation closes palette");outside.Close();
                var petHandle=new WindowInteropHelper(pet).Handle;var area=DesktopInterop.WorkArea(petHandle);DesktopInterop.Move(petHandle,new Point(area.Left,area.Top));
                palette=new ActionPaletteWindow(pet);palette.ShowForPet();Pump();
                Rect bounds=DesktopInterop.Bounds(new WindowInteropHelper(palette).Handle);Check(area.Contains(bounds),"palette clamps at monitor edge");
                pet.Conversation.Dispose();var api=new PendingApi();var session=new ConversationSession(api,new SilentSpeech(),()=>{throw new InvalidOperationException();});session.SelectPersona(pet.Skin.Id);
                typeof(PetWindow).GetField("conversation",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(pet,session);
                cfg.Text=new ConnectionSettings {BaseUrl="http://127.0.0.1:9876",Model="palette-fixture",StreamReplies=false};
                var request=session.SendTextAsync("busy",cfg,pet.Skin.Name);Pump(.3);
                Check(pet.DialogueBusy && Actions(palette).All(b=>b.IsEnabled),"busy conversation keeps every supported action available");
                palette.Close();var stopped=session.StopAsync();var deadline=DateTime.UtcNow.AddSeconds(5);while((!stopped.IsCompleted || !request.IsCompleted) && DateTime.UtcNow<deadline)Pump();Check(stopped.IsCompleted && request.IsCompleted,"busy fixture stops cleanly");try{request.GetAwaiter().GetResult();}catch(OperationCanceledException){}
                string legacy=Path.Combine(folder,"legacy.png");using(var input=typeof(SpriteAtlas).Assembly.GetManifestResourceStream("Dafeiyu.Atlas"))using(var output=File.Create(legacy))input.CopyTo(output);
                Check(pet.SelectSkin(pet.Catalog.Import(legacy)),"imported pet selected");palette=new ActionPaletteWindow(pet);palette.ShowForPet();Pump();
                Check(All(palette).OfType<Image>().Count(i=>i.Source!=null)==3,"imported pet gets base artwork fallback previews");
                Check(pet.ContextMenu.Items.OfType<MenuItem>().Any(i=>(string)i.Header=="做个动作") && pet.ContextMenu.Items.OfType<MenuItem>().Any(i=>(string)i.Header=="互动"),"traditional right-click menus retained");
                Console.WriteLine("Action palette smoke: "+passed+" passed; previews: "+folder);return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
            finally{if(palette!=null && palette.IsVisible)palette.Close();if(pet!=null)pet.Close();Pump();app.Shutdown();}
        }
        sealed class PendingApi:IConversationApi
        {
            public async Task<string> ChatAsync(ConnectionSettings c,IList<ChatMessage> m,CancellationToken token){await Task.Delay(30000,token);return "done";}
            public Task<string> TranscribeAsync(ConnectionSettings c,byte[] b,CancellationToken token){throw new InvalidOperationException();}
            public Task<byte[]> SpeechAsync(ConnectionSettings c,string v,string t,CancellationToken token){throw new InvalidOperationException();}
            public void Dispose(){}
        }
        sealed class SilentSpeech:IReplySpeech {public Task SpeakAsync(string text,PetSettings cfg,CancellationToken token){return Task.FromResult(0);}public void SetVolume(int v){}public void Stop(){}public void Dispose(){}}
        static InteractionAnimation State(PetWindow pet){return (InteractionAnimation)typeof(PetWindow).GetField("interaction",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(pet);}
        [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="SetCursorPos")] static extern bool MovePointer(int x,int y);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [System.Runtime.InteropServices.DllImport("user32.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)] static extern int GetWindowText(IntPtr window,System.Text.StringBuilder text,int count);
        static bool Highlighted(Button button){var sector=(System.Windows.Shapes.Path)button.Template.FindName("sector",button);var stroke=sector.Stroke as SolidColorBrush;return stroke!=null && stroke.Color==(Color)ColorConverter.ConvertFromString("#456DE6");}
        static IEnumerable<Button> Actions(Window window){return All(window).OfType<Button>().Where(b=>b.Name.StartsWith("PaletteInteraction_") || b.Name.StartsWith("PaletteAction_"));}
        static bool BoundsInside(FrameworkElement element,Window window){Rect rect=element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));return rect.Left>=0 && rect.Top>=0 && rect.Right<=window.ActualWidth && rect.Bottom<=window.ActualHeight;}
        static void Click(Window window,string name){var button=Find<Button>(window,name);if(button==null)throw new Exception("missing "+name);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
        static T Find<T>(DependencyObject root,string name) where T:FrameworkElement{return All(root).OfType<T>().FirstOrDefault(x=>x.Name==name);}
        static IEnumerable<DependencyObject> All(DependencyObject root){yield return root;foreach(object child in LogicalTreeHelper.GetChildren(root)){var node=child as DependencyObject;if(node!=null)foreach(var item in All(node))yield return item;}}
        static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;Console.WriteLine("PASS "+name);}
        static void Pump(double seconds=.12){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(seconds)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
        static void Capture(Window window,string path){var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(window);using(var stream=File.Create(path)){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));png.Save(stream);}}
    }
}
