using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    // One owner per application data root. Unsent text never creates a history row.
    public sealed class ComposerDraftStore : IDisposable
    {
        readonly object gate=new object();
        readonly string directory;
        readonly Dictionary<string,string> pending=new Dictionary<string,string>();
        readonly HashSet<string> blocked=new HashSet<string>(),readScopes=new HashSet<string>();
        readonly Dafeiyu.Maintenance.DataMaintenanceGate maintenance;
        readonly IDisposable registration;
        Timer timer;bool disposed;
        public string Warning {get;private set;}
        public event Action WarningChanged;
        public ComposerDraftStore(string root,Dafeiyu.Maintenance.DataMaintenanceGate activityGate=null)
        {
            directory=Path.Combine(root,"composer-drafts");
            maintenance=activityGate??new Dafeiyu.Maintenance.DataMaintenanceGate();
            registration=maintenance.Register(()=>{Flush();return Task.FromResult(0);},owner:this);
        }
        public static string Scope(string role,string mode,string project,string section)
        {return new JavaScriptSerializer().Serialize(new[]{role??"",mode??"",project??"",section??""});}
        string FileName(string scope)
        {
            using(var sha=SHA256.Create())return Path.Combine(directory,
                BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(scope))).Replace("-","").ToLowerInvariant()+".draft");
        }
        void Fail()
        {Warning=TextResources.Get("composerDraft.storageError");var changed=WarningChanged;if(changed!=null)changed();}
        public string Get(string scope)
        {
            lock(gate) {
                string value;if(pending.TryGetValue(scope,out value))return value;
                readScopes.Add(scope);string path=FileName(scope);
                try {
                    Dafeiyu.Maintenance.CachePaths.Check(path);
                    if(!File.Exists(path)){blocked.Remove(scope);return "";}
                    if(new FileInfo(path).Length>4*1024*1024)throw new InvalidDataException();
                    var bytes=SecretProtection.Unprotect(File.ReadAllBytes(path));
                    if(bytes.Length>2*1024*1024)throw new InvalidDataException();
                    var data=new JavaScriptSerializer{MaxJsonLength=2*1024*1024,RecursionLimit=8}
                        .Deserialize<Dictionary<string,string>>(new UTF8Encoding(false,true).GetString(bytes));
                    if(data==null||data["version"]!="1"||data["scope"]!=scope)throw new InvalidDataException();
                    blocked.Remove(scope);return data["text"]??"";
                }catch(Exception error){if(!StorageError(error))throw;blocked.Add(scope);Fail();return "";}
            }
        }
        public void Set(string scope,string text)
        {
            IDisposable activity;if(!maintenance.TryEnterActivity(out activity))return;
            using(activity)lock(gate) {
                if(disposed)return;
                // Even an owner that did not restore first must not overwrite unread bad data.
                if(!readScopes.Contains(scope))Get(scope);
                if(blocked.Contains(scope)){Fail();return;}
                text=text??"";
                if(Encoding.UTF8.GetByteCount(text)>1024*1024){Fail();return;}
                pending[scope]=text;
                if(timer==null)timer=new Timer(_=>Flush(),null,350,Timeout.Infinite);
                else timer.Change(350,Timeout.Infinite);
            }
        }
        internal void SaveNow(string scope,string text)
        {
            using(maintenance.EnterActivity())lock(gate)
            {
                if(disposed)throw new ObjectDisposedException("ComposerDraftStore");
                if(!readScopes.Contains(scope))Get(scope);
                if(blocked.Contains(scope))throw new IOException(TextResources.Get("composerDraft.storageError"));
                try{Write(scope,text);pending.Remove(scope);}
                catch(Exception error){if(!StorageError(error))throw;Fail();throw new IOException(TextResources.Get("composerDraft.storageError"),error);}
                if(pending.Count==0&&blocked.Count==0)Warning=null;
            }
        }

        void Write(string scope,string text)
        {
            string path=FileName(scope),temp=Path.Combine(directory,Guid.NewGuid().ToString("N")+".tmp");
            try
            {
                Dafeiyu.Maintenance.CachePaths.Check(path);Directory.CreateDirectory(directory);
                if(text.Length==0){if(File.Exists(path))File.Delete(path);return;}
                var json=new JavaScriptSerializer().Serialize(new Dictionary<string,string>{{"version","1"},{"scope",scope},{"text",text}});
                var cipher=SecretProtection.Protect(Encoding.UTF8.GetBytes(json));
                using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {
                    file.Write(cipher,0,cipher.Length);file.Flush(true);
                }
                AtomicFile.Commit(temp,path);
            }
            finally {try{File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){}}
        }

        public void Flush()
        {
            lock(gate) {
                if(disposed)return;
                foreach(var item in new List<KeyValuePair<string,string>>(pending)) {
                    try {
                        Write(item.Key,item.Value);
                        pending.Remove(item.Key);
                        if(pending.Count==0&&blocked.Count==0)Warning=null;
                    }catch(Exception error){if(!StorageError(error))throw;Fail();}
                }
            }
        }
        static bool StorageError(Exception error)
        {return error is IOException||error is UnauthorizedAccessException||error is CryptographicException||error is InvalidOperationException
            ||error is ArgumentException||error is KeyNotFoundException||error is System.Runtime.Serialization.SerializationException;}
        public void Dispose()
        {
            lock(gate){if(disposed)return;Flush();disposed=true;if(timer!=null)timer.Dispose();registration.Dispose();}
        }
    }
}
