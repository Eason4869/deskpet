using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
namespace Dafeiyu.Tests
{
    // Explicit opt-in only. Launches fresh isolated profiles and synthetic local files.
    public static class DesktopObservationCompatibilityTests
    {
        [STAThread] public static int Main(string[] args)
        {
            Process process = null;
            try
            {
                if(args.Length != 2) throw new ArgumentException("Expected executable and mode (browser/editor/video).");
                string mode=args[1], root=Path.Combine(Environment.CurrentDirectory,"work","observation-compat-"+Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                string name="DeskPetSyntheticObservation", file=Path.Combine(root,name+(mode=="editor"?".cs":".html"));
                if(mode=="editor") File.WriteAllText(file,"// Synthetic capture compatibility fixture only.\nclass DeskPetSyntheticObservation { static int Answer() { return 42; } }\n");
                else File.WriteAllText(file,"<title>"+name+"</title><body style='margin:0;background:#236f91;color:white;font:40px monospace'><h1 style='font:32px monospace'>"+name+"</h1><div style='height:240px;background:#ffff00;color:#123'>SYNTHETIC ONLY 42</div>"+(mode=="video"?"<video onplaying=\"document.title=&#39;DeskPetSyntheticObservation VIDEO_PLAYING&#39;\" autoplay muted loop width='640' height='360' src='"+new Uri(Path.Combine(Environment.CurrentDirectory,"work","observation-synthetic.mp4")).AbsoluteUri+"'></video>":"<pre>class Example { return 42; }</pre>")+"</body>");
                string arguments=mode=="editor"?"--user-data-dir=\""+Path.Combine(root,"profile")+"\" --extensions-dir=\""+Path.Combine(root,"extensions")+"\" --disable-extensions --disable-workspace-trust --skip-welcome --skip-release-notes --new-window \""+file+"\"":"--user-data-dir=\""+Path.Combine(root,"profile")+"\" --no-first-run --disable-background-networking --disable-sync --disable-extensions --autoplay-policy=no-user-gesture-required --app=\""+new Uri(file).AbsoluteUri+"\"";
                process=Process.Start(new ProcessStartInfo { FileName=args[0], Arguments=arguments,UseShellExecute=false });
                if(!SpinWait.SpinUntil(delegate { if(process.HasExited)return false;process.Refresh();return process.MainWindowHandle!=IntPtr.Zero; },12000)) throw new Exception("Isolated application did not expose its own main window; no capture attempted.");
                IntPtr window=process.MainWindowHandle; uint ignored; uint thread=GetWindowThreadProcessId(GetForegroundWindow(),out ignored), own=GetCurrentThreadId();
                bool attached=thread!=own&&AttachThreadInput(own,thread,true);
                try { SetForegroundWindow(window); } finally { if(attached)AttachThreadInput(own,thread,false); }
                Thread.Sleep(1800);
                if(GetForegroundWindow()!=window)throw new Exception("Isolated application is not foreground; no capture attempted.");
                ApplicationSnapshotTarget target=null;
                if(!SpinWait.SpinUntil(delegate
                {
                    if(GetForegroundWindow()!=window)return false;
                    target=ApplicationSnapshots.Foreground(new DesktopCaptureOptions());
                    return target!=null&&target.ProcessId==process.Id&&target.OriginalTitle.IndexOf(name,StringComparison.Ordinal)>=0;
                },8000))throw new Exception("Synthetic window identity/title precondition failed; no capture attempted.");
                if(mode=="video"&&target.OriginalTitle.IndexOf("VIDEO_PLAYING",StringComparison.Ordinal)<0)throw new Exception("Synthetic video did not confirm playback; no capture attempted.");
                using(var frame=new WindowsDesktopObservationCapture().CaptureForegroundAsync(new DesktopCaptureOptions(),CancellationToken.None).GetAwaiter().GetResult())
                {
                    if(frame==null)throw new Exception("Capture skipped.");
                    using(var stream=new MemoryStream(frame.EncodedPng))using(var image=new Bitmap(stream))
                    {
                        int synthetic=0;
                        for(int y=0;y<24;y++)for(int x=0;x<32;x++)
                        {
                            Color c=image.GetPixel((x*2+1)*image.Width/64,(y*2+1)*image.Height/48);
                            if((Math.Abs(c.R-35)<10&&Math.Abs(c.G-111)<10&&Math.Abs(c.B-145)<10)||(c.R>240&&c.G>240&&c.B<15))synthetic++;
                        }
                        if(mode!="editor"&&synthetic<50)throw new Exception("Synthetic browser content not rendered in captured image.");
                        Console.WriteLine("Compatibility "+mode+": capture success; "+image.Width+"x"+image.Height+", "+frame.EncodedPng.Length+" bytes; synthetic palette samples="+synthetic+"; no screenshot persisted.");
                    }
                }
                return 0;
            }
            catch(Exception ex){Console.Error.WriteLine("Compatibility INCONCLUSIVE/FAILED: "+ex.Message);return 1;}
            finally
            {
                if(process!=null){try{if(!process.HasExited){process.CloseMainWindow();if(!process.WaitForExit(4000)){process.Kill();process.WaitForExit(3000);}}}finally{process.Dispose();}}
            }
        }
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint from,uint to,bool attach);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    }
}
