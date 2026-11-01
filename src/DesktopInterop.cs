using System;
using System.Runtime.InteropServices;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Dafeiyu
{
    public static class DesktopInterop
    {
        [StructLayout(LayoutKind.Sequential)] private struct LastInput {public uint Size,Tick;}
        [DllImport("user32.dll")]private static extern bool GetLastInputInfo(ref LastInput input);
        public static bool TryLastInputTick(out uint tick)
        {
            var input=new LastInput{Size=(uint)Marshal.SizeOf(typeof(LastInput))};bool ok=GetLastInputInfo(ref input);tick=input.Tick;return ok;
        }
        public static double UserIdleSeconds()
        {
            var input=new LastInput{Size=(uint)Marshal.SizeOf(typeof(LastInput))};
            return GetLastInputInfo(ref input)?unchecked((uint)Environment.TickCount-input.Tick)/1000.0:Double.NaN;
        }
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
        public static bool IsWindowAtPoint(IntPtr window,Point point){return WindowFromPoint(new NativePoint{X=(int)point.X,Y=(int)point.Y})==window;}
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr handle,System.Text.StringBuilder name,int count);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll",EntryPoint="GetWindowLong")] private static extern int GetWindowLong32(IntPtr handle,int index);
        [DllImport("user32.dll",EntryPoint="SetWindowLong")] private static extern int SetWindowLong32(IntPtr handle,int index,int value);
        [DllImport("user32.dll",EntryPoint="GetWindowLongPtr")] private static extern IntPtr GetWindowLong64(IntPtr handle,int index);
        [DllImport("user32.dll",EntryPoint="SetWindowLongPtr")] private static extern IntPtr SetWindowLong64(IntPtr handle,int index,IntPtr value);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle,out uint processId);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
        public static void SetClickThrough(IntPtr handle,bool enabled)
        {
            // WS_EX_LAYERED + WS_EX_TRANSPARENT makes the entire WPF window pass input across processes.
            long style=IntPtr.Size==8 ? GetWindowLong64(handle,-20).ToInt64() : GetWindowLong32(handle,-20);
            style=enabled ? style|0x80000|0x20 : style&~0x20;
            if(IntPtr.Size==8)SetWindowLong64(handle,-20,new IntPtr(style));else SetWindowLong32(handle,-20,(int)style);
            SetWindowPos(handle,IntPtr.Zero,0,0,0,0,0x0001|0x0002|0x0004|0x0010|0x0020);
        }
        public static bool ForegroundIsFullScreen(IntPtr pet)
        {
            IntPtr foreground=GetForegroundWindow();if(foreground==IntPtr.Zero || !IsWindowVisible(foreground) || IsIconic(foreground))return false;
            uint process;GetWindowThreadProcessId(foreground,out process);
            if(process==(uint)System.Diagnostics.Process.GetCurrentProcess().Id)return false;
            var name=new System.Text.StringBuilder(128);GetClassName(foreground,name,name.Capacity);
            string cls=name.ToString();if(cls=="Progman" || cls=="WorkerW" || cls=="Shell_TrayWnd" || cls=="Shell_SecondaryTrayWnd")return false;
            var screen=Forms.Screen.FromHandle(pet);if(Forms.Screen.FromHandle(foreground).DeviceName!=screen.DeviceName)return false;
            Rect bounds=Bounds(foreground);var area=screen.Bounds;
            return bounds.Left<=area.Left+1 && bounds.Top<=area.Top+1 && bounds.Right>=area.Right-1 && bounds.Bottom>=area.Bottom-1;
        }
        public static bool ForegroundBelongsToPet()
        {
            uint process;IntPtr foreground=GetForegroundWindow();if(foreground==IntPtr.Zero)return false;
            GetWindowThreadProcessId(foreground,out process);return process==(uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        }
        public static void MakeToolWindow(IntPtr handle)
        {
            if(IntPtr.Size==8) SetWindowLong64(handle,-20,new IntPtr(GetWindowLong64(handle,-20).ToInt64() | 0x80 | 0x08000000));
            else SetWindowLong32(handle,-20,GetWindowLong32(handle,-20) | 0x80 | 0x08000000);
        }
        public static Point Cursor()
        { NativePoint point; GetCursorPos(out point); return new Point(point.X, point.Y); }
        public static Rect Bounds(IntPtr handle)
        { NativeRect rect; GetWindowRect(handle, out rect); return new Rect(rect.Left, rect.Top, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top)); }
        public static Rect WorkArea(Point point)
        {
            var rect = Forms.Screen.FromPoint(new System.Drawing.Point((int)point.X, (int)point.Y)).WorkingArea;
            return new Rect(rect.Left, rect.Top, rect.Width, rect.Height);
        }
        public static Rect WorkArea(IntPtr handle)
        {
            var rect = Forms.Screen.FromHandle(handle).WorkingArea;
            return new Rect(rect.Left, rect.Top, rect.Width, rect.Height);
        }
        public static void Move(IntPtr handle, Point point)
        { SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(point.X), (int)Math.Round(point.Y), 0, 0, 0x0001 | 0x0004 | 0x0010); }
        public static void ClampWindow(IntPtr handle)
        {
            Rect bounds = Bounds(handle); Rect area = WorkArea(handle);
            Move(handle, PetMath.ClampPosition(bounds.X, bounds.Y, bounds.Width, bounds.Height, area));
        }
        public static void PlaceBeside(IntPtr window, IntPtr pet)
        {
            Rect mine = Bounds(window), companion = Bounds(pet), area = WorkArea(pet);
            Move(window, PetMath.ClampPosition(companion.X - mine.Width - 18, companion.Bottom - mine.Height, mine.Width, mine.Height, area));
        }
        public static bool ForegroundIsFullScreen()
        {
            var foreground=GetForegroundWindow();if(foreground==IntPtr.Zero)return false;
            var name=new System.Text.StringBuilder(128);GetClassName(foreground,name,name.Capacity);
            if(name.ToString()=="Progman" || name.ToString()=="WorkerW")return false;
            Rect bounds=Bounds(foreground);var screen=Forms.Screen.FromHandle(foreground).Bounds;
            return bounds.Left<=screen.Left+1 && bounds.Top<=screen.Top+1 && bounds.Right>=screen.Right-1 && bounds.Bottom>=screen.Bottom-1;
        }
        public static void ForegroundMenu(System.Windows.Controls.ContextMenu menu)
        {
            var source=PresentationSource.FromVisual(menu) as System.Windows.Interop.HwndSource;
            if(source!=null)SetForegroundWindow(source.Handle);
        }
    }
}
