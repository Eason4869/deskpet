using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
namespace Dafeiyu
{
    public sealed class AgentSessionStore
    {
        const int MaxBytes=8*1024*1024;
        static readonly byte[] Header=Encoding.ASCII.GetBytes("DPS1");
        readonly string directory;readonly object gate=new object();
        public AgentSessionStore(string root){directory=Path.GetFullPath(root);}
        public void Save(AgentSessionModel session)
        {
            if(session==null)throw new ArgumentNullException("session");string path=PathFor(session.ConversationId),temporary=Path.Combine(directory,Guid.NewGuid().ToString("N")+".tmp");
            lock(gate){try{
                byte[] plain;using(var memory=new MemoryStream()){new DataContractJsonSerializer(typeof(AgentSessionModel)).WriteObject(memory,session);plain=memory.ToArray();}
                if(plain.Length>MaxBytes)throw new SerializationException("会话恢复记录超出大小限制。");var cipher=SecretProtection.Protect(plain);if(cipher.Length>MaxBytes)throw new SerializationException();
                Directory.CreateDirectory(directory);using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(Header,0,Header.Length);stream.Write(cipher,0,cipher.Length);stream.Flush(true);}
                AtomicFile.Commit(temporary,path);
            }finally{if(File.Exists(temporary))File.Delete(temporary);}}
        }
        public AgentSessionModel Load(string projectId,string conversationId)
        {
            string path=PathFor(conversationId);lock(gate){
                if(!Directory.Exists(directory))return null;
                string[] candidates=File.Exists(path)?new[]{path}:Directory.GetFiles(directory,"*.session");
                AgentSessionModel found=null;string source=null;bool legacy=false;
                foreach(string candidate in candidates){
                    if(new FileInfo(candidate).Length>MaxBytes+Header.Length)throw new SerializationException();byte[] bytes=File.ReadAllBytes(candidate);bool encrypted=bytes.Length>=Header.Length && bytes.Take(Header.Length).SequenceEqual(Header);
                    byte[] plain=encrypted?SecretProtection.Unprotect(bytes.Skip(Header.Length).ToArray()):bytes;if(plain.Length>MaxBytes)throw new SerializationException();AgentSessionModel model;
                    using(var memory=new MemoryStream(plain))model=(AgentSessionModel)new DataContractJsonSerializer(typeof(AgentSessionModel)).ReadObject(memory);
                    if(model==null || model.ConversationId!=conversationId || model.Pending==null || model.Delivered==null || model.Events==null)throw new SerializationException("会话恢复记录身份不匹配。");
                    if(found!=null)throw new SerializationException("会话恢复记录重复，原文件已保留。");found=model;source=candidate;legacy=!encrypted;
                }
                if(found==null)return null;found.ProjectId=projectId;
                if(legacy || source!=path){Save(found);if(source!=path)File.Delete(source);}return found;
            }
        }
        public void Clear(){lock(gate){if(Directory.Exists(directory))foreach(string path in Directory.GetFiles(directory,"*.session"))File.Delete(path);}}
        string PathFor(string conversationId)
        {
            if(String.IsNullOrWhiteSpace(conversationId))throw new ArgumentException("conversation ID 不能为空。");
            using(var sha=SHA256.Create()){string id=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(conversationId))).Replace("-","").ToLowerInvariant();return Path.Combine(directory,id+".session");}
        }
        internal static bool StorageError(Exception e){return e is IOException || e is UnauthorizedAccessException || e is CryptographicException || e is SerializationException || e is ArgumentException || e is System.Xml.XmlException;}
    }
}
