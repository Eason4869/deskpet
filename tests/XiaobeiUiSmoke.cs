using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    // Pass an approved v2 pet.json. All settings/imports are isolated under work;
    // source bytes are hashed before and after, never rewritten by this fixture.
    public static class XiaobeiUiSmoke
    {
        static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        [DllImport("user32.dll",SetLastError=true)] static extern bool SetCursorPos(int x,int y);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,int message,IntPtr w,IntPtr l);
        static int passed,skippedNativeGaze;static string output;
        [STAThread] public static int Main(string[] args)
        {
            bool withoutNativeInput=args.Length==2 && args[1]=="--without-native-input";
            if((args.Length!=1 && !withoutNativeInput) || !File.Exists(args[0])){Console.Error.WriteLine("Usage: XiaobeiUiSmoke.exe <approved v2 pet.json> [--without-native-input]");return 2;}
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
            Point cursor=DesktopInterop.Cursor();EventHandler renderer=null;bool renderingDetached=false,cursorMoved=false;
            string manifestPath=Path.GetFullPath(args[0]),source=Path.GetDirectoryName(manifestPath);
            var hashes=HashFiles(source);output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"xiaobei-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
            try
            {
                Console.WriteLine(withoutNativeInput?"MODE without native input: stationary mouse gaze scenarios will be skipped; other checks remain enabled.":"MODE strict native input: all stationary mouse gaze scenarios are required.");
                var settings=new PetSettings{FollowCursor=false,Wander=false,GreetingEnabled=false,AutoUpdate=false,HideOnFullscreen=false,QuietMode=true,SaveChatHistory=false,Scale=1.25};
                foreach(string key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
                pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(output,"settings.json")),null);pet.Show();Pump(.15);
                var imported=pet.Catalog.Import(manifestPath);Check(imported.Illustrated,"actual v2 package imported");Check(pet.SelectSkin(imported),"actual imported companion selected");
                Check(Field(pet,"interactionAtlas")==null,"v2 interactions do not depend on embedded artwork");
                Check(imported.Source.StartsWith(output+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"runtime reads isolated imported copy");
                var hwnd=new WindowInteropHelper(pet).Handle;var area=DesktopInterop.WorkArea(hwnd);var bounds=DesktopInterop.Bounds(hwnd);
                DesktopInterop.Move(hwnd,new Point(area.Left+(area.Width-bounds.Width)/2,area.Top+(area.Height-bounds.Height)/2));
                if(!withoutNativeInput){if(!SetCursorPos((int)area.Left+8,(int)area.Top+8))throw new InvalidOperationException("Cannot move fixture cursor; Win32 "+Marshal.GetLastWin32Error()+". Use --without-native-input only for an explicitly partial run.");cursorMoved=true;}Pump(.1);
                renderer=(EventHandler)Delegate.CreateDelegate(typeof(EventHandler),pet,typeof(PetWindow).GetMethod("RenderFrame",Private));
                CompositionTarget.Rendering-=renderer;renderingDetached=true;
                var shots=new List<KeyValuePair<string,BitmapSource>>();
                for(int action=0;action<IllustratedPack.Actions.Length;action++)
                {
                    string name=IllustratedPack.Actions[action];var spec=imported.Manifest.Animations[name];bool interaction=action>=9;
                    Start(pet,name,interaction);var clock=(InteractionAnimation)Field(pet,"interaction");var clip=(MotionClip)Field(pet,"shownMotion");
                    Check(clip!=null && clip.Width==480 && clip.Height==520 && clip.Count==spec.Frames && Math.Abs(clip.Duration-spec.Frames/spec.Fps)<1e-9 && clip.Loop==spec.Loop,name+" uses manifest frame count, FPS and loop flag");
                    var original=IllustratedPack.ReadImage(IllustratedPack.AssetPath(source,spec.File),480*spec.Columns,520*((spec.Frames+spec.Columns-1)/spec.Columns));
                    Check((int)Field(pet,"shownSample")==0,name+" switches from previous action to its own first frame");
                    var sequence=new List<KeyValuePair<string,BitmapSource>>();
                    foreach(int frame in Enumerable.Range(0,spec.Frames))
                    {
                        Start(pet,name,interaction);double at=(frame+.1)/spec.Fps;
                        if(interaction)clock.Advance(at);else pet.Animation.Advance(at);Update(pet);
                        int actual=(int)Field(pet,"shownSample");clip=(MotionClip)Field(pet,"shownMotion");var image=(Image)pet.Content;
                        Check(actual==frame && Object.ReferenceEquals(image.Source,clip.Frame(actual)),name+" renders actual source frame "+frame);
                        var crop=new CroppedBitmap(original,new Int32Rect(frame%spec.Columns*480,frame/spec.Columns*520,480,520));
                        Check(Pixels((BitmapSource)image.Source).SequenceEqual(Pixels(crop)),name+" frame "+frame+" pixels match original atlas");
                        Check(image.RenderTransform.Value.IsIdentity,name+" has no synthetic warp or mirror");
                        BitmapSource shot=Capture(pet,name+"-"+frame+".png");if(frame==spec.Frames/2)shots.Add(new KeyValuePair<string,BitmapSource>(name,shot));
                        sequence.Add(new KeyValuePair<string,BitmapSource>(name+" "+frame,shot));
                        if(frame==spec.Frames/2)HitTest(pet,clip,actual,null,0,0);
                    }
                    Contact(sequence,Math.Min(8,spec.Frames),name+"-sequence.png");
                    if(spec.Loop)Contact(sequence.Skip(Math.Max(0,sequence.Count-2)).Concat(sequence.Take(2)).ToList(),4,name+"-loop-seam.png");
                    Start(pet,name,interaction);double duration=spec.Frames/spec.Fps;
                    if(interaction){clock.Advance(duration+.001);Check(spec.Loop?clock.Name==name:name=="feeding"?clock.Name=="happy":!clock.Active,name+" obeys imported completion/loop timing");}
                    else{pet.Animation.Advance(duration+.001);Check(pet.Animation.State==(spec.Loop?name:"idle"),name+" obeys imported completion/loop timing");}
                    Update(pet);
                    if(spec.Loop)Check((int)Field(pet,"shownSample")==0 && ReferenceEquals(((Image)pet.Content).Source,clip.Frame(0)),name+" loop wraps from last frame to original first frame");
                    else Check((int)Field(pet,"shownSample")==0,name+" completion starts the next action at its first frame");
                }
                Contact(shots,4,"actions-contact.png");
                var gazeShots=new List<KeyValuePair<string,BitmapSource>>();var gaze=IllustratedPack.ReadImage(IllustratedPack.AssetPath(source,imported.Manifest.Gaze.File),1920,2080);
                // This verifies imported atlas mapping, not cursor handling or
                // stationary gaze retention. Keep it distinct from native tests.
                var atlas=(SpriteAtlas)Field(pet,"atlas");
                for(int direction=0;direction<16;direction++)
                {
                    var crop=new CroppedBitmap(gaze,new Int32Rect(direction%4*480,direction/4*520,480,520));
                    Check(Pixels(atlas.GetFrame(9+direction/8,direction%8)).SequenceEqual(Pixels(crop)),"loaded gaze atlas preserves original source direction "+direction+" (not a mouse dwell test)");
                }
                if(withoutNativeInput)
                {
                    for(int direction=0;direction<16;direction++){skippedNativeGaze++;Console.WriteLine("SKIP native stationary gaze sector "+direction+": cursor movement/dwell and displayed gaze hit tests require native input.");}
                }
                else
                {
                pet.PlayAction("idle",0);pet.Settings.FollowCursor=true;CompositionTarget.Rendering+=renderer;renderingDetached=false;
                bounds=DesktopInterop.Bounds(hwnd);Point center=new Point(bounds.Left+bounds.Width*.5,bounds.Top+bounds.Height*.4);
                double radius=Math.Min(260,Math.Min(Math.Min(center.X-area.Left,area.Right-center.X),Math.Min(center.Y-area.Top,area.Bottom-center.Y))-15);
                Check(radius>Math.Max(bounds.Width,bounds.Height)*.7,"desktop has room for all gaze cursor sectors outside pet");
                for(int direction=0;direction<16;direction++)
                {
                    double angle=direction*Math.PI/8;int x=(int)Math.Round(center.X+Math.Sin(angle)*radius),y=(int)Math.Round(center.Y-Math.Cos(angle)*radius);
                    if(!SetCursorPos(x,y))throw new InvalidOperationException("Cannot move fixture cursor; Win32 "+Marshal.GetLastWin32Error());
                    for(int attempt=0;attempt<100;attempt++){if(!SetCursorPos(x+(attempt%2),y))throw new InvalidOperationException("Cannot move owned fixture cursor");Pump(.02);if((bool)Field(pet,"gazeActive") && (int)Field(pet,"shownRow")==9+direction/8 && (int)Field(pet,"shownCol")==direction%8)break;}CompositionTarget.Rendering-=renderer;renderingDetached=true;pet.UpdateLayout();
                    Console.WriteLine("GAZE "+direction+" cursor="+DesktopInterop.Cursor()+" target="+x+","+y+" last="+Field(pet,"lastCursor")+" elapsed="+((System.Diagnostics.Stopwatch)Field(pet,"watch")).Elapsed.TotalSeconds+" moved="+Field(pet,"lastCursorMove")+" row="+Field(pet,"shownRow")+" col="+Field(pet,"shownCol")+" state="+pet.Animation.State+" hover="+Field(pet,"hover"));
                    Check((bool)Field(pet,"gazeActive") && (int)Field(pet,"shownRow")==9+direction/8 && (int)Field(pet,"shownCol")==direction%8,"brief cursor dwell shows original gaze sector "+direction);
                    var crop=new CroppedBitmap(gaze,new Int32Rect(direction%4*480,direction/4*520,480,520));
                    Check(Pixels((BitmapSource)((Image)pet.Content).Source).SequenceEqual(Pixels(crop)),"stationary gaze pixels equal original direction "+direction);
                    gazeShots.Add(new KeyValuePair<string,BitmapSource>("gaze "+direction,Capture(pet,"gaze-"+direction+".png")));
                    if(direction==0 || direction==8)HitTest(pet,null,0,(SpriteAtlas)Field(pet,"atlas"),9+direction/8,direction%8);
                    CompositionTarget.Rendering+=renderer;renderingDetached=false;
                }
                Contact(gazeShots,4,"gaze-contact.png");
                Pump(1.5);Check(!(bool)Field(pet,"gazeActive") && (int)Field(pet,"shownRow")==0,"stationary cursor returns to idle after 1.4 seconds");
                if(!SetCursorPos((int)Math.Round(center.X+radius),(int)Math.Round(center.Y)))throw new InvalidOperationException("Cannot move fixture cursor; Win32 "+Marshal.GetLastWin32Error());
                Pump(.2);Check((bool)Field(pet,"gazeActive") && (int)Field(pet,"shownRow")==9 && (int)Field(pet,"shownCol")==4,"new cursor movement resumes exact original gaze");
                }
                pet.Settings.FollowCursor=false;pet.PlayAction("idle",0);pet.ShowActionPalette();Pump(.1);var palette=app.Windows.OfType<ActionPaletteWindow>().Single();
                var special=Find<Button>(palette,"PaletteInteraction_special");Check(special!=null && System.Windows.Automation.AutomationProperties.GetName(special)==imported.SpecialAction,"palette exposes imported special action name");
                Capture(palette,"special-palette.png");special.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(((InteractionAnimation)Field(pet,"interaction")).Name=="special" && Field(pet,"shownMotion")!=null,"named special action launches imported motion");
                Check(SameHashes(hashes,HashFiles(source)),"original package bytes unchanged after import and desktop use");
                File.WriteAllLines(Path.Combine(output,"source-sha256.txt"),hashes.Select(p=>p.Value+"  "+p.Key));
                Console.WriteLine("XiaobeiUiSmoke: "+passed+" passed assertions; "+skippedNativeGaze+" native gaze scenarios skipped; native mouse dwell coverage: "+(withoutNativeInput?"NOT TESTED":"16/16 sectors passed")+"; screenshots: "+output);return 0;
            }
            catch(Exception e){Console.Error.WriteLine("FAIL "+e);return 1;}
            finally
            {
                if(pet!=null){if(renderingDetached && renderer!=null)CompositionTarget.Rendering+=renderer;pet.Close();Pump(.1);}if(cursorMoved)SetCursorPos((int)cursor.X,(int)cursor.Y);app.Shutdown();
                if(!SameHashes(hashes,HashFiles(source)))throw new InvalidOperationException("Original package changed during smoke run.");
            }
        }
        static void Start(PetWindow pet,string name,bool interaction){if(interaction)pet.PlayInteraction(name);else pet.PlayAction(name,0);}
        static object Field(PetWindow pet,string name){return typeof(PetWindow).GetField(name,Private).GetValue(pet);}
        static void Update(PetWindow pet){typeof(PetWindow).GetMethod("UpdateFrame",Private).Invoke(pet,new object[]{0d});}
        static void HitTest(PetWindow pet,MotionClip clip,int sample,SpriteAtlas atlas,int row,int column)
        {
            IntPtr hwnd=new WindowInteropHelper(pet).Handle;Rect b=DesktopInterop.Bounds(hwnd);bool body=false,clear=false;
            for(int y=5;y<(int)b.Height-5 && !(body && clear);y+=3)for(int x=5;x<(int)b.Width-5 && !(body && clear);x+=3)
            {
                int sx=(int)Math.Floor(x/b.Width*480),sy=(int)Math.Floor(y/b.Height*520);byte alpha=clip==null?atlas.AlphaAt(row,column,sx,sy):clip.AlphaAt(sample,sx,sy);bool opaque=alpha>240;
                if(alpha!=0 && !opaque || (opaque?body:clear))continue;
                bool uniform=true;for(int dy=-3;dy<=3;dy++)for(int dx=-3;dx<=3;dx++){byte a=clip==null?atlas.AlphaAt(row,column,sx+dx,sy+dy):clip.AlphaAt(sample,sx+dx,sy+dy);if((a>=12)!=opaque)uniform=false;}if(!uniform)continue;
                int px=(int)b.Left+x,py=(int)b.Top+y;bool through=SendMessage(hwnd,0x84,IntPtr.Zero,new IntPtr((py<<16)|(px&0xffff))).ToInt32()==-1;
                Check(through!=opaque,"native hit test follows displayed "+(opaque?"body":"transparent margin"));if(opaque)body=true;else clear=true;
            }
            Check(body && clear,"native hit test covers both visible body and transparent margin");
        }
        static byte[] Pixels(BitmapSource source){var converted=new FormatConvertedBitmap(source,PixelFormats.Pbgra32,null,0);var bytes=new byte[source.PixelWidth*source.PixelHeight*4];converted.CopyPixels(bytes,source.PixelWidth*4,0);return bytes;}
        static BitmapSource Capture(Window window,string file){window.UpdateLayout();Pump(.035);var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(window);bitmap.Freeze();Save(bitmap,file);return bitmap;}
        static void Contact(IList<KeyValuePair<string,BitmapSource>> pictures,int columns,string file)
        {
            const int width=180,height=220;var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawRectangle(Brushes.White,null,new Rect(0,0,columns*width,((pictures.Count+columns-1)/columns)*height));for(int i=0;i<pictures.Count;i++){double x=i%columns*width,y=i/columns*height;dc.DrawImage(pictures[i].Value,new Rect(x+10,y+8,160,174));dc.DrawText(new FormattedText(pictures[i].Key,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),12,Brushes.Black,1),new Point(x+10,y+188));}}var result=new RenderTargetBitmap(columns*width,((pictures.Count+columns-1)/columns)*height,96,96,PixelFormats.Pbgra32);result.Render(visual);Save(result,file);
        }
        static void Save(BitmapSource bitmap,string file){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(output,file)))png.Save(stream);}
        static Dictionary<string,string> HashFiles(string root){var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);foreach(string file in Directory.GetFiles(root,"*",SearchOption.AllDirectories)){using(var input=File.OpenRead(file))using(var sha=SHA256.Create())result[file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar)]=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();}return result;}
        static bool SameHashes(Dictionary<string,string> a,Dictionary<string,string> b){return a.Count==b.Count && a.All(p=>b.ContainsKey(p.Key) && b[p.Key]==p.Value);}
        static T Find<T>(DependencyObject node,string name)where T:FrameworkElement{var found=node as T;if(found!=null && found.Name==name)return found;for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){found=Find<T>(VisualTreeHelper.GetChild(node,i),name);if(found!=null)return found;}return null;}
        static void Check(bool value,string text){if(!value)throw new Exception(text);passed++;Console.WriteLine("PASS "+text);}
        static void Pump(double seconds){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(seconds)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
    }
}
