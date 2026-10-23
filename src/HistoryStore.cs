using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Dafeiyu
{
    // All filesystem mutations run in order on a background worker, including deletion.
    public sealed class HistoryStore
    {
        private const int MaximumBytes=8*1024*1024;
        private readonly string directory;
        private readonly object gate=new object();
        private Task worker=Task.FromResult(0);
        private readonly HashSet<string> unreadableFiles=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string warning="";
        readonly Dictionary<string,List<IDisposable>> pendingImageReferences=new Dictionary<string,List<IDisposable>>(StringComparer.OrdinalIgnoreCase);
        void ReleasePendingImages(string path){lock(gate){List<IDisposable> pending;if(pendingImageReferences.TryGetValue(path,out pending)){pendingImageReferences.Remove(path);foreach(var lease in pending)lease.Dispose();}}}
        private readonly Dictionary<string,string> warnings=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        public string Warning { get { lock(gate)return warning; } }
        public event Action WarningChanged;
        [DataContract] private sealed class Document
        {
            [DataMember] public int Version=1;
            [DataMember] public List<ChatMessage> Messages;
        }
        public string DirectoryPath {get{return directory;}}
        internal bool HasSavedHistory(string petId){return File.Exists(FilePath(petId));}
        internal bool IsUnreadable(string petId){lock(gate)return unreadableFiles.Contains(FilePath(petId));}
        public HistoryStore(string directoryPath=null)
        { directory=Path.GetFullPath(directoryPath??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"DeskPet","history")); }
        private string FilePath(string petId)
        {
            using(var hash=SHA256.Create())return Path.Combine(directory,BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(petId??""))).Replace("-","").ToLowerInvariant()+".history");
        }
        private void SetWarning(string path,string value)
        { Action changed;lock(gate){if(String.IsNullOrEmpty(value))warnings.Remove(path);else warnings[path]=value;string merged=String.Join(" ",warnings.Values.Distinct());if(warning==merged)return;warning=merged;changed=WarningChanged;}if(changed!=null)changed(); }
        private Task Enqueue(Action action)
        {
            lock(gate) { worker=worker.ContinueWith(delegate { action(); },TaskScheduler.Default);return worker; }
        }
        public Task FlushAsync() { lock(gate)return worker; }
        public IList<ChatMessage> Load(string petId)
        {
            FlushAsync().GetAwaiter().GetResult();string path=FilePath(petId);
            try {
                if(!File.Exists(path))return new List<ChatMessage>();
                if(new FileInfo(path).Length>MaximumBytes)throw new SerializationException();
                byte[] plain=SecretProtection.Unprotect(File.ReadAllBytes(path));
                if(plain.Length>MaximumBytes)throw new SerializationException();
                Document document;using(var stream=new MemoryStream(plain))document=(Document)new DataContractJsonSerializer(typeof(Document)).ReadObject(stream);
                if(document==null || document.Version!=1 || document.Messages==null || document.Messages.Count>400)throw new SerializationException();
                var log=new ConversationLog();log.Replace(document.Messages);lock(gate)unreadableFiles.Remove(path);SetWarning(path,"");return log.Messages;
            }
            catch(Exception e) { if(!IsStorageError(e))throw;lock(gate)unreadableFiles.Add(path);SetWarning(path,TextResources.Get("historyStore.load.setWarning"));return new List<ChatMessage>(); }
        }
        internal ImageAssetUsageRegistry ImageUsage {get;set;}
        internal static IList<ChatMessage> InspectFileStrict(string path)
        {
            var info=new FileInfo(path);if(!info.Exists||info.Length<=0||info.Length>MaximumBytes||(info.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException(TextResources.Get("historyStore.inspectFileStrict.text"));
            byte[] plain=SecretProtection.Unprotect(File.ReadAllBytes(path));if(plain.Length>MaximumBytes)throw new SerializationException();Document document;
            using(var stream=new MemoryStream(plain))document=(Document)new DataContractJsonSerializer(typeof(Document)).ReadObject(stream);
            if(document==null||document.Version!=1||document.Messages==null||document.Messages.Count>400||document.Messages.Any(m=>m==null))throw new SerializationException();return document.Messages;
        }
        public Task SaveAsync(string petId,IList<ChatMessage> messages)
        {
            var copy=messages.Select(m=>m.Copy()).ToList();string path=FilePath(petId);var protection=ImageUsage==null?null:ImageUsage.AcquireMessages(copy);
            return Enqueue(delegate {
                if(protection!=null)lock(gate){List<IDisposable> pending;if(!pendingImageReferences.TryGetValue(path,out pending)){pending=new List<IDisposable>();pendingImageReferences[path]=pending;}pending.Add(protection);}
                string temporary=null;
                try {
                    bool blocked;lock(gate)blocked=unreadableFiles.Contains(path);
                    if(blocked){SetWarning(path,TextResources.Get("historyStore.saveAsync.setWarning"));return;}
                    Directory.CreateDirectory(directory);byte[] encrypted;
                    while(true) {
                        byte[] plain;using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(Document)).WriteObject(stream,new Document {Messages=copy});plain=stream.ToArray();}
                        encrypted=SecretProtection.Protect(plain);
                        if(encrypted.Length<=MaximumBytes && copy.Select(m=>m.TurnId).Distinct().Count()<=200)break;
                        if(copy.Count==0)throw new SerializationException();string turn=copy[0].TurnId;copy.RemoveAll(m=>m.TurnId==turn);
                    }
                    // A 64-character history hash plus another GUID can push a
                    // valid destination beyond MAX_PATH. Keep the temporary
                    // name short in the same directory for an atomic commit.
                    temporary=Path.Combine(directory,Guid.NewGuid().ToString("N")+".tmp");
                    using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(encrypted,0,encrypted.Length);stream.Flush(true);}
                    AtomicFile.Commit(temporary,path);
                    temporary=null;ReleasePendingImages(path);SetWarning(path,"");
                }
                catch(Exception e) { if(!IsStorageError(e))throw;SetWarning(path,TextResources.Get("historyStore.saveAsync.setWarning.alternativeA")); }
                finally { if(temporary!=null)try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){} }
            });
        }
        public Task ClearAsync(string petId)
        {
            string path=FilePath(petId);return Enqueue(delegate {
                try {
                    try{File.Delete(path);}catch(DirectoryNotFoundException){}
                    lock(gate)unreadableFiles.Remove(path);ReleasePendingImages(path);SetWarning(path,"");
                }
                catch(Exception e){if(!IsStorageError(e))throw;SetWarning(path,TextResources.Get("historyStore.clearAsync.setWarning"));}
            });
        }
        private static bool IsStorageError(Exception e)
        { return e is IOException || e is UnauthorizedAccessException || e is CryptographicException || e is SerializationException || e is System.Xml.XmlException || e is System.Security.SecurityException || e is ArgumentException; }
    }
}
