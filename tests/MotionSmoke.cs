using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace Dafeiyu.Tests
{
    public static class MotionSmoke
    {
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd,int msg,IntPtr w,IntPtr l);
        [DllImport("user32.dll",SetLastError=true)] static extern bool SetCursorPos(int x,int y);
        static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static int passed;
        static object Field(PetWindow p,string n){return typeof(PetWindow).GetField(n,Private).GetValue(p);}
        static void Update(PetWindow p){typeof(PetWindow).GetMethod("UpdateFrame",Private).Invoke(p,new object[]{0d});}
        [STAThread] public static int Main()
        {
            PetWindow pet=null;var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Point cursor=DesktopInterop.Cursor();
            try {
                string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"motion-legacy-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
                var cfg=new PetSettings{FollowCursor=false,GreetingEnabled=false,Wander=false,AutoUpdate=false,HideOnFullscreen=false,Scale=1.25,QuietMode=true};
                foreach(string key in new List<string>(cfg.Hotkeys.Keys))cfg.Hotkeys[key]="";
                pet=new PetWindow(null,cfg,new SettingsStore(Path.Combine(folder,"settings.json")),null);pet.Show();Pump(.2);
                DesktopInterop.Move(new WindowInteropHelper(pet).Handle,new Point(700,500));
                var image=(Image)pet.Content;Check(image.ToolTip==null,"pet body has no hover tutorial");
                var report=new List<string>{"pet,render_ticks_per_second,changed_frames_per_second"};
                double[] periods={.17,.22,.22,.16,.13,.16,.19,.18,.18};
                string[] actions={"lifted","landing","petting","poked","feeding","happy","sleeping","special"};double[] durations={1,.64,1.6,.8,1.6,1.2,3.2,2};
                foreach(string id in new[]{"e-bao","dafeiyu","marfu"}) {
                    Check(pet.SelectSkin(pet.Catalog.Find(id)),"select "+id);pet.Settings.FollowCursor=false;
                    var renderer=(EventHandler)Delegate.CreateDelegate(typeof(EventHandler),pet,typeof(PetWindow).GetMethod("RenderFrame",Private));
                    CompositionTarget.Rendering-=renderer;
                    var atlas=(SpriteAtlas)Field(pet,"atlas");var extra=(InteractionAtlas)Field(pet,"interactionAtlas");
                    for(int row=0;row<AnimationPlayer.States.Length;row++) {
                        pet.PlayAction(AnimationPlayer.States[row],0);pet.SetPaused(true);
                        var sequence=new List<BitmapSource>();
                        for(int frame=0;frame<AnimationPlayer.Counts[row];frame++) {
                            if(frame>0)pet.Animation.Advance(periods[row]);else pet.Animation.Advance(.00001);Update(pet);
                            int shownRow=pet.Skin.ActionRow(row),shownCol=pet.Skin.ActionFrame(row,frame);
                            var clip=((AdditionalInteractions)Field(pet,"additionalInteractions")).Get(pet.Skin,AnimationPlayer.States[row]);Check(clip==null?Field(pet,"shownMotion")==null && ReferenceEquals(image.Source,atlas.GetFrame(shownRow,shownCol)):ReferenceEquals(image.Source,clip.Frame(clip.Sample(pet.Animation.Elapsed))),id+" "+AnimationPlayer.States[row]+" configured animation frame "+frame);
                            sequence.Add(Capture(pet,Path.Combine(folder,id+"-"+AnimationPlayer.States[row]+"-"+frame+".png")));
                        }
                        Contact(sequence,Path.Combine(folder,id+"-"+AnimationPlayer.States[row]+"-sequence.png"));
                        Contact(sequence.Skip(Math.Max(0,sequence.Count-2)).Concat(sequence.Take(2)).ToList(),Path.Combine(folder,id+"-"+AnimationPlayer.States[row]+"-loop-seam.png"));
                        pet.Animation.Advance(periods[row]);Update(pet);
                        Check(row==4?pet.Animation.State=="jumping"||pet.Animation.State=="idle":pet.Animation.Frame==0,id+" "+AnimationPlayer.States[row]+(row==4?" uses its configured natural duration":" original cycle wraps to first phase"));
                        if(row==2 || row==4) {HitTest(pet);Capture(pet,Path.Combine(folder,id+"-"+AnimationPlayer.States[row]+".png"));}
                    }
                    for(int row=0;row<actions.Length;row++) {
                        pet.PlayInteraction(actions[row]);var clock=(InteractionAnimation)Field(pet,"interaction");var type=typeof(InteractionAnimation);int frames=((int[])type.GetField("counts",Private).GetValue(clock))[row];double length=((double[])type.GetField("durations",Private).GetValue(clock))[row];
                        var sequence=new List<BitmapSource>();
                        for(int frame=0;frame<frames;frame++) {if(frame>0)clock.Advance(length/frames);Update(pet);var clip=((AdditionalInteractions)Field(pet,"additionalInteractions")).Get(pet.Skin,clock.Name+((bool)Field(pet,"originalInteraction")?"-original":""));Check(clip==null?Field(pet,"shownMotion")==null && ReferenceEquals(image.Source,extra.GetFrame(row,frame)):ReferenceEquals(image.Source,clip.Frame(clip.Sample(clock.Elapsed))),id+" "+actions[row]+" configured interaction frame "+frame);sequence.Add(Capture(pet,Path.Combine(folder,id+"-"+actions[row]+"-"+frame+".png")));}
                        Contact(sequence,Path.Combine(folder,id+"-"+actions[row]+"-sequence.png"));
                        if(row==0 || row==6)Contact(sequence.Skip(2).Concat(sequence.Take(2)).ToList(),Path.Combine(folder,id+"-"+actions[row]+"-loop-seam.png"));
                        if(row==7){HitTest(pet);Capture(pet,Path.Combine(folder,id+"-special.png"));}
                    }
                    CompositionTarget.Rendering+=renderer;
                    pet.PlayAction("running-right",0);pet.SetPaused(false);Pump(.1);
                    double previous=(double)Field(pet,"previousTick");ImageSource source=image.Source;int ticks=0,changes=0;
                    EventHandler observer=delegate{double now=(double)Field(pet,"previousTick");if(now!=previous){previous=now;ticks++;}if(source!=image.Source){source=image.Source;changes++;}};
                    CompositionTarget.Rendering+=observer;var timer=Stopwatch.StartNew();Pump(3);CompositionTarget.Rendering-=observer;double seconds=timer.Elapsed.TotalSeconds;
                    report.Add(String.Format(System.Globalization.CultureInfo.InvariantCulture,"{0},{1:F1},{2:F1}",id,ticks/seconds,changes/seconds));
                    Check(changes/seconds>=4 && changes/seconds<=9,"configured idle cadence renders steadily in WPF "+id);
                    pet.SetPaused(true);var frozen=image.Source;Pump(.15);Check(ReferenceEquals(frozen,image.Source),"pause freezes original frame "+id);
                    pet.SetPaused(false);pet.HidePet();double hidden=(double)Field(pet,"previousTick");Pump(.15);Check(hidden==(double)Field(pet,"previousTick"),"hidden pet stops rendering "+id);pet.RestorePet();
                    pet.PlayAction("jumping",.7);Pump(1.2);Check(pet.Animation.State=="idle","jump completes within its configured natural duration "+id);
                    if(id=="e-bao") {
                        pet.Settings.FollowCursor=true;var b=DesktopInterop.Bounds(new WindowInteropHelper(pet).Handle);
                        if(!SetCursorPos((int)(b.X+b.Width*.5),(int)(b.Y+b.Height*.4-220)))throw new Exception("SetCursorPos error "+Marshal.GetLastWin32Error());Pump(.2);
                        Check(ReferenceEquals(image.Source,atlas.GetFrame(9,0)),"native cursor movement selects exact original gaze");
                        Pump(1.5);Check(!(bool)Field(pet,"gazeActive") && (int)Field(pet,"shownRow")==0,"stationary cursor returns to idle after original 1.4 second timeout");
                        if(!SetCursorPos((int)(b.X+b.Width*.5+220),(int)(b.Y+b.Height*.4)))throw new Exception("Cursor movement failed");Pump(.2);
                        Check((bool)Field(pet,"gazeActive") && ReferenceEquals(image.Source,atlas.GetFrame(9,4)),"cursor movement resumes exact original gaze");
                        pet.Settings.FollowCursor=false;
                    }
                }
                File.WriteAllLines(Path.Combine(folder,"render-rates.csv"),report);foreach(string line in report)Console.WriteLine(line);
                pet.Close();pet=null;Pump(.1);Check(app.Windows.Count==0,"windows released");Console.WriteLine("Motion legacy smoke: "+passed+" passed; "+folder);return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
            finally{SetCursorPos((int)cursor.X,(int)cursor.Y);if(pet!=null)pet.Close();app.Shutdown();}
        }
        static void HitTest(PetWindow pet)
        {
            pet.UpdateLayout();var image=(Image)pet.Content;var bitmap=(BitmapSource)image.Source;int width=bitmap.PixelWidth,height=bitmap.PixelHeight;var pixels=new byte[width*height*4];new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0).CopyPixels(pixels,width*4,0);bool body=false,clear=false;
            Func<int,int,byte> alpha=(x,y)=>pixels[(y*width+x)*4+3];
            for(int y=15;y<height-15 && !(body&&clear);y+=7)for(int x=15;x<width-15 && !(body&&clear);x+=7){bool opaque=alpha(x,y)>240;if(alpha(x,y)!=0&&!opaque || (opaque?body:clear))continue;bool uniform=true;for(int dy=-3;dy<=3;dy++)for(int dx=-3;dx<=3;dx++)if((alpha(x+dx,y+dy)>12)!=opaque)uniform=false;if(!uniform)continue;var screen=image.PointToScreen(new Point((x+.5)*image.ActualWidth/width,(y+.5)*image.ActualHeight/height));int sx=(int)Math.Round(screen.X),sy=(int)Math.Round(screen.Y);bool through=SendMessage(new WindowInteropHelper(pet).Handle,0x84,IntPtr.Zero,new IntPtr((sy<<16)|(sx&0xffff))).ToInt32()==-1;Check(through!=opaque,"native alpha follows original transformed frame x="+x+" y="+y+" screen="+screen+" size="+image.ActualWidth+","+image.ActualHeight+" bounds="+DesktopInterop.Bounds(new WindowInteropHelper(pet).Handle));if(opaque)body=true;else clear=true;}
            Check(body&&clear,"opaque and transparent frame hit areas verified");
        }
        static BitmapSource Capture(Window w,string file){w.UpdateLayout();Pump(.035);var b=new RenderTargetBitmap((int)Math.Ceiling(w.ActualWidth),(int)Math.Ceiling(w.ActualHeight),96,96,PixelFormats.Pbgra32);b.Render(w);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(b));using(var s=File.Create(file))png.Save(s);return b;}
        static void Contact(IList<BitmapSource> frames,string file)
        {
            var drawing=new DrawingVisual();using(var dc=drawing.RenderOpen()){dc.DrawRectangle(Brushes.White,null,new Rect(0,0,frames.Count*180,210));for(int i=0;i<frames.Count;i++)dc.DrawImage(frames[i],new Rect(i*180+10,12,160,174));}
            var bitmap=new RenderTargetBitmap(frames.Count*180,210,96,96,PixelFormats.Pbgra32);bitmap.Render(drawing);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(file))png.Save(stream);
        }
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);passed++;Console.WriteLine("PASS "+message);}
        static void Pump(double seconds){var f=new DispatcherFrame();var t=new DispatcherTimer{Interval=TimeSpan.FromSeconds(seconds)};t.Tick+=delegate{t.Stop();f.Continue=false;};t.Start();Dispatcher.PushFrame(f);}
    }
}
