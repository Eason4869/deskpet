using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
namespace Dafeiyu
{
    public sealed class AgentSessionStore
    {
        readonly string directory;readonly object gate=new object();
        public AgentSessionStore(string root){directory=Path.GetFullPath(root);}
        public void Save(AgentSessionModel session)
        {
            if(session==null)throw new ArgumentNullException("session");string path=PathFor(session.ProjectId,session.ConversationId),temporary=path+".tmp";
            lock(gate){Directory.CreateDirectory(directory);using(var stream=new FileStream(temporary,FileMode.Create,FileAccess.Write,FileShare.None)){new DataContractJsonSerializer(typeof(AgentSessionModel)).WriteObject(stream,session);}if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);}
        }
        public AgentSessionModel Load(string projectId,string conversationId)
        {
            string path=PathFor(projectId,conversationId);lock(gate){if(!File.Exists(path))return null;using(var stream=File.OpenRead(path))return (AgentSessionModel)new DataContractJsonSerializer(typeof(AgentSessionModel)).ReadObject(stream);}
        }
        string PathFor(string projectId,string conversationId)
        {
            if(String.IsNullOrWhiteSpace(projectId) || String.IsNullOrWhiteSpace(conversationId))throw new ArgumentException("project 和 conversation ID 不能为空。");
            using(var sha=SHA256.Create()){string id=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(projectId+"\n"+conversationId))).Replace("-","").ToLowerInvariant();return Path.Combine(directory,id+".session");}
        }
    }
}
