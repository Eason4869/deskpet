using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using System.Web.Script.Serialization;
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
                if(plain.Length>MaxBytes)throw new SerializationException(TextResources.Get("sessionStore.save.text"));var cipher=SecretProtection.Protect(plain);if(cipher.Length>MaxBytes)throw new SerializationException();
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
                    if(model==null || model.ConversationId!=conversationId || model.Pending==null || model.Delivered==null || model.Events==null)throw new SerializationException(TextResources.Get("sessionStore.load.text"));
                    if(found!=null)throw new SerializationException(TextResources.Get("sessionStore.load.text.alternativeA"));found=model;source=candidate;legacy=!encrypted;
                }
                if(found==null)return null;found.ProjectId=projectId;
                if(legacy || source!=path){Save(found);if(source!=path)File.Delete(source);}return found;
            }
        }
        public void Clear(){lock(gate){if(Directory.Exists(directory))foreach(string path in Directory.GetFiles(directory,"*.session"))File.Delete(path);}}
        // Maintenance must not use Load: legacy loading renames/re-encrypts data
        // and changes project ownership. Inspection recognizes only this schema
        // and never creates a directory, temporary file or migration output.
        internal static AgentSessionModel InspectFileStrict(string path)
        {
            Maintenance.CachePaths.Check(path);var info=new FileInfo(path);
            if(!info.Exists||info.Length==0||info.Length>MaxBytes+Header.Length)throw InvalidSession();
            byte[] bytes=File.ReadAllBytes(path);bool encrypted=bytes.Length>=Header.Length&&bytes.Take(Header.Length).SequenceEqual(Header);
            byte[] plain=encrypted?SecretProtection.Unprotect(bytes.Skip(Header.Length).ToArray()):bytes;
            if(plain==null||plain.Length==0||plain.Length>MaxBytes)throw InvalidSession();
            string json=new UTF8Encoding(false,true).GetString(plain).TrimStart('\ufeff');
            // Checking member names prevents a future JSON version from being
            // silently accepted by DataContractJsonSerializer's unknown-field policy.
            var tree=InspectMembers(new JavaScriptSerializer{MaxJsonLength=MaxBytes}.DeserializeObject(json),"SessionId","ProjectId","ConversationId","Pending","Delivered","Events","Sequence","Step","RecoveryState");
            foreach(string key in new[]{"SessionId","ProjectId","ConversationId","RecoveryState"})if(!(tree[key] is string))throw InvalidSession();
            if(!Counter(tree["Sequence"])||!Counter(tree["Step"]))throw InvalidSession();
            foreach(string key in new[]{"Pending","Delivered"})foreach(object item in InspectArray(tree[key])){
                var input=InspectMembers(item,"IdempotencyId","Kind","Text","Resume","Steer","AttachmentIds");
                foreach(string name in new[]{"IdempotencyId","Kind","Text"})if(!(input[name] is string))throw InvalidSession();
                if(!(input["Resume"] is bool)||!(input["Steer"] is bool)||InspectArray(input["AttachmentIds"]).Any(id=>!(id is string)||String.IsNullOrWhiteSpace((string)id)))throw InvalidSession();
            }
            foreach(object item in InspectArray(tree["Events"])){
                var entry=InspectMembers(item,"Sequence","Type","InputId","StepId","CreatedUtc");
                if(!Counter(entry["Sequence"]))throw InvalidSession();
                foreach(string name in new[]{"Type","InputId","StepId"})if(!(entry[name] is string))throw InvalidSession();
            }
            AgentSessionModel model;using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(json)))model=(AgentSessionModel)new DataContractJsonSerializer(typeof(AgentSessionModel)).ReadObject(stream);
            if(model==null||String.IsNullOrWhiteSpace(model.SessionId)||String.IsNullOrWhiteSpace(model.ConversationId)||model.Pending==null||model.Delivered==null||model.Events==null||!new[]{"idle","running","failed","interrupted"}.Contains(model.RecoveryState))throw InvalidSession();
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var input in model.Pending.Concat(model.Delivered))if(input==null||String.IsNullOrWhiteSpace(input.IdempotencyId)||input.IdempotencyId.Length>200||!ids.Add(input.IdempotencyId)||!new[]{"prompt","resume","steer"}.Contains(input.Kind)||input.Text==null||input.AttachmentIds==null)throw InvalidSession();
            long sequence=0;foreach(var entry in model.Events){if(entry==null||entry.Sequence<=sequence||entry.Sequence>model.Sequence||String.IsNullOrWhiteSpace(entry.Type)||entry.CreatedUtc==DateTime.MinValue)throw InvalidSession();sequence=entry.Sequence;}
            return model;
        }
        static Dictionary<string,object> InspectMembers(object value,params string[] members)
        {var fields=value as Dictionary<string,object>;if(fields==null||fields.Count!=members.Length||members.Any(name=>!fields.ContainsKey(name)))throw InvalidSession();return fields;}
        static object[] InspectArray(object value){var items=value as object[];if(items==null)throw InvalidSession();return items;}
        static bool Counter(object value){return (value is int||value is long)&&Convert.ToInt64(value)>=0;}
        static SerializationException InvalidSession(){return new SerializationException(TextResources.Get("sessionStore.load.text"));}
        string PathFor(string conversationId)
        {
            if(String.IsNullOrWhiteSpace(conversationId))throw new ArgumentException(TextResources.Get("sessionStore.pathFor.text"));
            using(var sha=SHA256.Create()){string id=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(conversationId))).Replace("-","").ToLowerInvariant();return Path.Combine(directory,id+".session");}
        }
        internal static bool StorageError(Exception e){return e is IOException || e is UnauthorizedAccessException || e is CryptographicException || e is SerializationException || e is ArgumentException || e is System.Xml.XmlException;}
    }
}
