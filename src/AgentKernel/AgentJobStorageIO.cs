using System;
using System.IO;
namespace Dafeiyu
{
    // A small I/O boundary permits controlled slow/failing disks without production fault switches.
    internal class AgentJobStorageIO
    {
        internal virtual void Commit(string temporary,string destination){AtomicFile.Commit(temporary,destination);}
        internal virtual void Append(Stream stream,byte[] bytes){stream.Write(bytes,0,bytes.Length);}
        internal virtual long Length(string path){return File.Exists(path)?new FileInfo(path).Length:0;}
        internal void WriteAtomic(string path,byte[] bytes)
        {
            AgentInstructions.RejectLinks(path);string temporary=Path.Combine(Path.GetDirectoryName(path),Guid.NewGuid().ToString("N")+".tmp");
            try
            {
                using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
                AgentInstructions.RejectLinks(path);Commit(temporary,path);
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
