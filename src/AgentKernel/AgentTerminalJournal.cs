using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    internal sealed class AgentTerminalRecord {public string ExecutionId,PayloadHash,State;public AgentCommandResult Result;}
    internal sealed class AgentTerminalJournal
    {
        readonly string directory;
        internal AgentTerminalJournal(string directory){this.directory=directory;}
        string PathFor(AgentToolInvocation invocation){using(var hash=SHA256.Create())return Path.Combine(directory,BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(invocation.Key))).Replace("-","")+".bin");}
        internal AgentTerminalRecord Read(AgentToolInvocation invocation)
        {string path=PathFor(invocation);if(!File.Exists(path))return null;var bytes=File.ReadAllBytes(path);if(bytes.Length>2*1024*1024)throw new IOException(TextResources.Get("agentTerminalJournal.read.text"));return new JavaScriptSerializer{MaxJsonLength=2*1024*1024}.Deserialize<AgentTerminalRecord>(Encoding.UTF8.GetString(SecretProtection.Unprotect(bytes)));}
        internal void Save(AgentToolInvocation invocation,AgentTerminalRecord record)
        {
            Directory.CreateDirectory(directory);string path=PathFor(invocation),temporary=Path.Combine(directory,Guid.NewGuid().ToString("N")+".tmp");
            try{var bytes=Encoding.UTF8.GetBytes(new JavaScriptSerializer{MaxJsonLength=2*1024*1024}.Serialize(record));File.WriteAllBytes(temporary,SecretProtection.Protect(bytes));AtomicFile.Commit(temporary,path);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
