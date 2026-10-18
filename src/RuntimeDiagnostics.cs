using System;
using System.IO;
using System.Text;
namespace Dafeiyu
{
    internal static class RuntimeDiagnostics
    {
        static readonly object gate=new object();
        // Exclude exception messages and data: providers can put request contents
        // or credentials there. Keep only types and code locations for diagnosis.
        internal static void Record(Exception error)
        {
            try{lock(gate){string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","diagnostics");Directory.CreateDirectory(directory);string path=Path.Combine(directory,"runtime.log");if(File.Exists(path)&&new FileInfo(path).Length>262144)File.WriteAllText(path,"",Encoding.UTF8);var text=new StringBuilder(DateTime.UtcNow.ToString("o")+" version="+ReleaseInfo.DisplayVersion+Environment.NewLine);for(int depth=0;error!=null&&depth<8;depth++,error=error.InnerException){text.AppendLine(error.GetType().FullName);text.AppendLine(error.StackTrace);}File.AppendAllText(path,text.ToString(),Encoding.UTF8);}}catch(Exception){}
        }
    }
}
