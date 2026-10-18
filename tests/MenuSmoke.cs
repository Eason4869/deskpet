using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    public static class MenuSmoke
    {
        [StructLayout(LayoutKind.Sequential)]struct PointNative{public int X,Y;}
        [DllImport("user32.dll",SetLastError=true)]static extern bool SetCursorPos(int x,int y);
        [DllImport("user32.dll")]static extern void mouse_event(uint flags,uint x,uint y,uint data,IntPtr extra);
        [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(PointNative point);
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
        static int passed;
        [STAThread]public static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;Process blank=null;Point cursor=DesktopInterop.Cursor();
            try
            {
                blank=Process.Start(new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"MenuBlankFixture.exe")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Normal});Pump(.4);
                string data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"menu-smoke-"+Guid.NewGuid().ToString("N"));
                var settings=new PetSettings{FollowCursor=false,Wander=false,AutoUpdate=false,GreetingEnabled=false,HideOnFullscreen=false,OnboardingState="deferred",OnboardingStep=4};
                foreach(string key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
                pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(data,"settings.json")),null);pet.Show();Pump(.2);
                pet.ContextMenu.Opened+=delegate{Console.WriteLine("EVENT opened "+DateTime.UtcNow.ToString("HH:mm:ss.fff"));};
                pet.ContextMenu.Closed+=delegate{Console.WriteLine("EVENT closed "+DateTime.UtcNow.ToString("HH:mm:ss.fff"));};
                Exercise(pet,blank,"without settings",false,data);
                pet.ShowSettings();Pump(.3);
                Exercise(pet,blank,"with settings open",false,data);
                pet.ShowOnboarding();Pump(.3);
                Check(app.Windows.OfType<OnboardingWindow>().Single().Step==4,"guide starts at menu lesson");
                Exercise(pet,blank,"with guide open",true,data);
                app.Windows.OfType<OnboardingWindow>().Single().Close();Pump(.2);
                Check(pet.ContextMenu.Items.OfType<MenuItem>().First().IsEnabled,"guide closing restores normal actions");
                Console.WriteLine("Menu smoke: "+passed+" passed; screenshots: "+data);return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL Menu: "+e);return 1;}
            finally{mouse_event(4|16,0,0,0,IntPtr.Zero);SetCursorPos((int)cursor.X,(int)cursor.Y);if(pet!=null)pet.Close();if(blank!=null){blank.CloseMainWindow();if(!blank.WaitForExit(2000))blank.Kill();blank.Dispose();}app.Shutdown();}
        }
        static void Exercise(PetWindow pet,Process blank,string scenario,bool guided,string folder)
        {
            pet.SetPaused(true);Place(pet,blank);Point target=OpaquePoint(pet);Move(target);Pump(.05);RequireHit(target,new WindowInteropHelper(pet).Handle);
            mouse_event(8,0,0,0,IntPtr.Zero);Pump(.12);mouse_event(16,0,0,0,IntPtr.Zero);Pump(.8);
            Check(pet.ContextMenu.IsOpen,scenario+": native right click leaves context menu open");
            var appearance=pet.ContextMenu.Items.OfType<MenuItem>().Single(m=>m.Name=="QuickAppearanceMenu");
            Point item=appearance.PointToScreen(new Point(appearance.ActualWidth/2,appearance.ActualHeight/2));Move(item);Pump(.05);
            var source=(HwndSource)PresentationSource.FromVisual(appearance);RequireHit(item,source.Handle);
            mouse_event(2,0,0,0,IntPtr.Zero);mouse_event(4,0,0,0,IntPtr.Zero);Pump(.4);
            Check(appearance.IsSubmenuOpen && pet.ContextMenu.IsOpen,scenario+": native click expands appearance submenu");
            Check(appearance.Items.OfType<MenuItem>().All(m=>m.IsEnabled!=guided),scenario+": submenu action availability is correct");
            Directory.CreateDirectory(folder);var menu=pet.ContextMenu;var bitmap=new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth),(int)Math.Ceiling(menu.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(menu);
            using(var file=File.Create(Path.Combine(folder,scenario.Replace(' ','-')+".png"))){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));png.Save(file);}
            Point? outside=null;blank.Refresh();var blankBounds=DesktopInterop.Bounds(blank.MainWindowHandle);if(blank.MainWindowHandle==IntPtr.Zero||blankBounds.Width<20||blankBounds.Height<20)blankBounds=new Rect(30,40,600,450);for(double y=blankBounds.Top+25;y<blankBounds.Bottom-25&&!outside.HasValue;y+=35)for(double x=blankBounds.Left+25;x<blankBounds.Right-25;x+=35){uint process;GetWindowThreadProcessId(WindowFromPoint(new PointNative{X=(int)x,Y=(int)y}),out process);if(process==(uint)blank.Id){outside=new Point(x,y);break;}}if(!outside.HasValue)throw new Exception("No unobstructed owned blank fixture point: exited="+blank.HasExited+" handle="+blank.MainWindowHandle+" bounds="+blankBounds);Move(outside.Value);Pump(.03);
            mouse_event(2,0,0,0,IntPtr.Zero);mouse_event(4,0,0,0,IntPtr.Zero);Pump(.4);
            Check(!pet.ContextMenu.IsOpen,scenario+": native outside click dismisses menu");
        }
        static void Place(PetWindow pet,Process blank)
        {
            var handle=new WindowInteropHelper(pet).Handle;var area=DesktopInterop.WorkArea(handle);var size=DesktopInterop.Bounds(handle);
            var blockers=Application.Current.Windows.OfType<Window>().Where(w=>w!=pet&&w.IsVisible).Select(w=>DesktopInterop.Bounds(new WindowInteropHelper(w).Handle)).Concat(new[]{new Rect(30,40,600,450)}).ToArray();
            for(double y=area.Top+100;y+size.Height<area.Bottom-50;y+=size.Height+30)for(double x=area.Left+100;x+size.Width<area.Right-50;x+=size.Width+30){var rect=new Rect(x,y,size.Width,size.Height);rect.Inflate(45,45);if(blockers.Any(b=>b.IntersectsWith(rect)))continue;DesktopInterop.Move(handle,new Point(x,y));Pump(.1);return;}
            throw new Exception("No unobstructed fixture placement");
        }
        static Point OpaquePoint(PetWindow pet)
        {
            var image=(Image)pet.Content;var bitmap=(BitmapSource)image.Source;int width=bitmap.PixelWidth,height=bitmap.PixelHeight;
            var converted=new FormatConvertedBitmap(bitmap,PixelFormats.Pbgra32,null,0);var pixels=new byte[width*height*4];converted.CopyPixels(pixels,width*4,0);
            for(int y=height/3;y<height*2/3;y+=3)for(int x=width/3;x<width*2/3;x+=3){bool solid=true;for(int dy=-12;dy<=12&&solid;dy+=3)for(int dx=-12;dx<=12;dx+=3)if(pixels[((y+dy)*width+x+dx)*4+3]<240){solid=false;break;}if(solid){var p=image.PointToScreen(new Point((x+.5)/width*image.ActualWidth,(y+.5)/height*image.ActualHeight));if(WindowFromPoint(new PointNative{X=(int)Math.Round(p.X),Y=(int)Math.Round(p.Y)})==new WindowInteropHelper(pet).Handle)return p;}}
            throw new Exception("No opaque current-frame target");
        }
        static void RequireHit(Point p,IntPtr handle){if(WindowFromPoint(new PointNative{X=(int)Math.Round(p.X),Y=(int)Math.Round(p.Y)})!=handle)throw new Exception("Native click target is not the fixture");}
        static void Move(Point p){if(!SetCursorPos((int)Math.Round(p.X),(int)Math.Round(p.Y)))throw new Exception("SetCursorPos error "+Marshal.GetLastWin32Error());}
        static void Check(bool value,string message){if(!value)throw new Exception(message);passed++;Console.WriteLine("PASS "+message);}
        static void Pump(double seconds){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(seconds)};timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
    }
}
