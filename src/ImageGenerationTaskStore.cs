using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

namespace Dafeiyu
{
    public sealed class ImageGenerationTaskStore
    {
        public const int MaximumBytes=64*1024*1024;
        public const int MaximumTerminalRecords=2000;
        readonly object gate=new object();
        readonly string directory,path;
        bool recoveryRequired;
        public string Warning {get;private set;}
        public string DirectoryPath {get{return directory;}}
        [DataContract] sealed class TaskFile {[DataMember] public List<ImageGenerationTaskRecord> Tasks {get;set;}}
        public ImageGenerationTaskStore(string dataDirectory)
        {
            if(String.IsNullOrWhiteSpace(dataDirectory))throw new ArgumentException("请指定任务存储目录。","dataDirectory");
            directory=Path.Combine(Path.GetFullPath(dataDirectory),"generated-images");path=Path.Combine(directory,"tasks.dat");Warning="";
        }
        public IList<ImageGenerationTaskRecord> Load()
        {
            lock(gate){
                if(!File.Exists(path))return new List<ImageGenerationTaskRecord>();
                try{
                    CheckPath();long length=new FileInfo(path).Length;if(length<=0 || length>MaximumBytes)throw new InvalidDataException("任务记录大小无效。");
                    byte[] plain=SecretProtection.Unprotect(File.ReadAllBytes(path));if(plain.Length<=0 || plain.Length>MaximumBytes)throw new InvalidDataException("任务记录大小无效。");
                    TaskFile file;using(var stream=new MemoryStream(plain))file=(TaskFile)new DataContractJsonSerializer(typeof(TaskFile)).ReadObject(stream);
                    if(file==null || file.Tasks==null)throw new SerializationException();Validate(file.Tasks);
                    recoveryRequired=false;Warning="";return Retain(file.Tasks);
                }catch(Exception e){
                    if(!StorageError(e))throw;recoveryRequired=true;Warning="图片任务记录无法读取，原文件已保留；请备份后恢复任务记录，未自动重新请求图片。";
                    return new List<ImageGenerationTaskRecord>();
                }
            }
        }
        public void Save(IList<ImageGenerationTaskRecord> records)
        {
            if(records==null)throw new ArgumentNullException("records");
            lock(gate){
                if(recoveryRequired)throw new InvalidDataException(Warning);
                // Check existing data before replacing it, even when callers have
                // not loaded yet. A corrupt previous file always remains intact.
                if(File.Exists(path)){Load();if(recoveryRequired)throw new InvalidDataException(Warning);}
                Validate(records);var retained=Retain(records);byte[] plain;
                using(var stream=new MemoryStream()){
                    new DataContractJsonSerializer(typeof(TaskFile)).WriteObject(stream,new TaskFile{Tasks=retained});
                    if(stream.Length>MaximumBytes)throw new InvalidDataException("图片任务记录超过 64 MB。");plain=stream.ToArray();
                }
                byte[] encrypted=SecretProtection.Protect(plain);if(encrypted.Length>MaximumBytes)throw new InvalidDataException("图片任务记录超过 64 MB。");
                Directory.CreateDirectory(directory);CheckPath();
                string temporary=Path.Combine(directory,Guid.NewGuid().ToString("N")+".tmp");
                try{using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(encrypted,0,encrypted.Length);stream.Flush(true);}AtomicFile.Commit(temporary,path);Warning="";}
                finally{if(File.Exists(temporary))try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
            }
        }
        static List<ImageGenerationTaskRecord> Retain(IEnumerable<ImageGenerationTaskRecord> records)
        {
            var copies=records.Where(r=>r.Persistent).Select(r=>r.Copy()).ToList();
            var keep=new HashSet<string>(copies.Where(r=>r.IsTerminal).OrderByDescending(r=>r.CompletedUtc??r.UpdatedUtc??r.QueuedUtc).Take(MaximumTerminalRecords).Select(r=>r.Id),StringComparer.Ordinal);
            return copies.Where(r=>!r.IsTerminal || keep.Contains(r.Id)).ToList();
        }
        static void Validate(IEnumerable<ImageGenerationTaskRecord> records)
        {
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var record in records){
                if(record==null || String.IsNullOrWhiteSpace(record.Id) || record.Id.Length>128 || !ids.Add(record.Id) || !Enum.IsDefined(typeof(ImageGenerationTaskState),record.State) || !Enum.IsDefined(typeof(ImageTaskDeliveryState),record.DeliveryState) || (record.Origin!=null && (!Enum.IsDefined(typeof(ImageGenerationSource),record.Origin.Source) || record.Origin.Sequence<0)))throw new InvalidDataException("图片任务记录无效。");
                if((record.Prompt??"").Length>16000 || (record.Error??"").Length>2000 || (record.DeliveryError??"").Length>2000 || (record.ReferenceImageIds!=null && (record.ReferenceImageIds.Count>20 || record.ReferenceImageIds.Any(id=>String.IsNullOrWhiteSpace(id) || id.Length>128))))throw new InvalidDataException("图片任务字段超出限制。");
            }
        }
        void CheckPath()
        {
            string current=directory;while(!String.IsNullOrEmpty(current)){if(Directory.Exists(current) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("图片任务目录链接不可用。");current=Path.GetDirectoryName(current);}
            if(File.Exists(path) && (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("图片任务文件链接不可用。");
        }
        static bool StorageError(Exception e){return e is IOException || e is UnauthorizedAccessException || e is CryptographicException || e is SerializationException || e is ArgumentException || e is System.Xml.XmlException;}
    }
}
