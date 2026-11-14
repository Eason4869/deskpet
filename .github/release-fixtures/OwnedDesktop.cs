using System;
using System.IO;
using System.Runtime.InteropServices;
class OwnedDesktop {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] struct Startup {
        public int cb; public string reserved,desktop,title;
        public int x,y,xsize,ysize,xchars,ychars,fill,flags; public short show,reservedSize;
        public IntPtr reserved2,input,output,error;
    }
    [StructLayout(LayoutKind.Sequential)] struct ProcessInfo {public IntPtr process,thread;public int pid,tid;}
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateDesktop(string name,IntPtr device,IntPtr mode,int flags,uint access,IntPtr security);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CreateProcess(string app,string command,IntPtr processSecurity,IntPtr threadSecurity,bool inherit,int flags,IntPtr env,string cwd,ref Startup startup,out ProcessInfo info);
    [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int id);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle,uint timeout);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr handle,out uint code);
    [DllImport("kernel32.dll")] static extern bool TerminateProcess(IntPtr handle,uint code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    static int Main(string[] args) {
        if(args.Length!=1)return 2;
        string exe=Path.GetFullPath(args[0]);
        if(!File.Exists(exe))return 2;
        string name="DeskPet-Release-Fixture-"+Guid.NewGuid().ToString("N");
        IntPtr desktop=CreateDesktop(name,IntPtr.Zero,IntPtr.Zero,0,0x10000000,IntPtr.Zero);
        if(desktop==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        ProcessInfo info=new ProcessInfo();
        try {
            Startup startup=new Startup{cb=Marshal.SizeOf(typeof(Startup)),desktop=name,flags=0x100,input=GetStdHandle(-10),output=GetStdHandle(-11),error=GetStdHandle(-12)};
            if(!CreateProcess(null,"\""+exe+"\"",IntPtr.Zero,IntPtr.Zero,true,0,IntPtr.Zero,Path.GetDirectoryName(exe),ref startup,out info))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            if(WaitForSingleObject(info.process,150000)!=0){TerminateProcess(info.process,124);WaitForSingleObject(info.process,5000);return 124;}
            uint code;if(!GetExitCodeProcess(info.process,out code))return 125;return (int)code;
        } finally {if(info.thread!=IntPtr.Zero)CloseHandle(info.thread);if(info.process!=IntPtr.Zero)CloseHandle(info.process);CloseDesktop(desktop);}
    }
}
