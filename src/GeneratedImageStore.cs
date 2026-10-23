using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace Dafeiyu
{
    // Bytes live outside history/settings. Index entries never contain image data or paths.
    public sealed partial class GeneratedImageStore : IDisposable
    {
        public const int MaximumBytes=50*1024*1024;
        public const int MaximumItems=20000;
        const int MaximumIndexBytes=64*1024*1024;
        readonly object gate=new object();
        readonly string directory,indexPath,temporaryDirectory,referenceDirectory;
        readonly List<GeneratedImage> images=new List<GeneratedImage>();
        readonly HashSet<string> temporaryCopies=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool disposed,recoveryRequired;
        string lastPurgeId;
        ImageAssetUsageRegistry assetUsage;
        internal void BindUsage(ImageAssetUsageRegistry value){lock(gate){if(assetUsage!=null&&!Object.ReferenceEquals(assetUsage,value))throw new InvalidOperationException(TextResources.Get("generatedImageStore.bindUsage.text"));assetUsage=value;if(value!=null)value.MarkDeleted(purgedIds.Concat(purgedJobKeys));}}
        public event Action Changed;
        public string Warning {get;private set;}
        [DataContract] sealed class Index {[DataMember] public List<GeneratedImage> Images {get;set;}[DataMember(EmitDefaultValue=false)] public string LastPurgeId {get;set;}}
        public GeneratedImageStore(string dataDirectory,ImageAssetUsageRegistry usage=null)
        {
            if(String.IsNullOrWhiteSpace(dataDirectory))throw new ArgumentException(TextResources.Get("generatedImageStore.generatedImageStore.text"),"dataDirectory");
            assetUsage=usage;directory=Path.Combine(Path.GetFullPath(dataDirectory),"generated-images");indexPath=Path.Combine(directory,"index.dat");
            referenceDirectory=Path.Combine(directory,"references");
            temporaryDirectory=Path.Combine(directory,"temporary-"+Guid.NewGuid().ToString("N"));Warning="";
            Directory.CreateDirectory(directory);CheckDirectory(directory);Load();RecoverPurgeTransactions();
        }
        public GeneratedImage Save(byte[] bytes,GeneratedImage metadata,bool persistent)
        {return SaveCore(bytes,metadata,persistent,false);}
        public GeneratedImage SaveReference(byte[] bytes,GeneratedImage metadata,bool persistent)
        {return SaveCore(bytes,metadata,persistent,true);}
        internal GeneratedImage SaveReferenceDeferred(byte[] bytes,GeneratedImage metadata,bool persistent,ICollection<Action> notifications)
        {return SaveCore(bytes,metadata,persistent,true,notifications);}
        GeneratedImage SaveCore(byte[] bytes,GeneratedImage metadata,bool persistent,bool reference,ICollection<Action> notifications=null)
        {
            if(bytes==null || bytes.Length==0 || bytes.Length>MaximumBytes)throw new InvalidDataException(TextResources.Get("generatedImageStore.saveCore.text"));
            bytes=(byte[])bytes.Clone();var format=GeneratedImageValidation.Validate(bytes);
            var item=(metadata??new GeneratedImage()).Copy();item.Id=Guid.NewGuid().ToString("N");item.MediaType=format.MediaType;item.Width=format.Width;item.Height=format.Height;item.Bytes=bytes.Length;item.CreatedUtc=DateTime.UtcNow;item.Temporary=!persistent;
            item.IsReference=reference;
            item.Prompt=Limit(item.Prompt,16000);item.ProviderId=Limit(item.ProviderId,80);item.Model=Limit(item.Model,256);item.Size=Limit(item.Size,64);item.Quality=Limit(item.Quality,64);item.SessionId=Limit(item.SessionId,128);item.TaskId=Limit(item.TaskId,128);item.MessageId=Limit(item.MessageId,128);item.ReferenceImageId=ValidId(item.ReferenceImageId)?item.ReferenceImageId:"";
            using(var referenceLease=assetUsage==null||String.IsNullOrEmpty(item.ReferenceImageId)?null:assetUsage.Acquire(item.ReferenceImageId,"asset-reference"))
            lock(gate){
                EnsureOpen();if(images.Count>=MaximumItems)throw new InvalidDataException(TextResources.Get("generatedImageStore.saveCore.text.alternativeA"));
                CheckDirectory(directory);
                if(persistent && recoveryRequired)throw new InvalidDataException(TextResources.Get("generatedImageStore.saveCore.text.alternativeB"));
                string folder=Folder(item);Directory.CreateDirectory(folder);CheckDirectory(folder);
                string path=FilePath(item);WriteAtomic(path,bytes);
                try{if(persistent)SaveIndex(images.Concat(new[]{item}));images.Add(item);}
                catch(Exception e){if(!StorageError(e))throw;Warning=TextResources.Get("generatedImageStore.saveCore.warning");throw new IOException(Warning);}
            }
            if(notifications==null)Notify();else notifications.Add(Notify);return item.Copy();
        }
        public GeneratedImage FindByJobId(string id){if(String.IsNullOrEmpty(id))return null;lock(gate){EnsureOpen();var item=images.Find(i=>!i.IsReference && i.ImageJobId==id);return item==null?null:item.Copy();}}
        public GeneratedImage Get(string id){ValidateId(id);lock(gate){EnsureOpen();var item=images.Find(i=>i.Id==id);return item==null?null:item.Copy();}}
        public IList<GeneratedImage> List(){lock(gate){EnsureOpen();return images.Where(i=>!i.IsReference && !i.TrashedUtc.HasValue).OrderByDescending(i=>i.CreatedUtc).Select(i=>i.Copy()).ToList();}}
        public string PathFor(string id)
        {
            ValidateId(id);lock(gate){EnsureOpen();var item=images.Find(i=>i.Id==id);if(item==null)throw new FileNotFoundException(TextResources.Get("generatedImageStore.pathFor.text"));return ExistingPath(item);}
        }
        public byte[] Read(string id)
        {
            ValidateId(id);lock(gate){EnsureOpen();var item=images.Find(i=>i.Id==id);if(item==null)throw new FileNotFoundException(TextResources.Get("generatedImageStore.read.text"));
                string path=ExistingPath(item);var info=new FileInfo(path);if(info.Length<=0 || info.Length>MaximumBytes || info.Length!=item.Bytes)throw new InvalidDataException(TextResources.Get("generatedImageStore.read.text.alternativeA"));
                byte[] bytes=File.ReadAllBytes(path);var format=GeneratedImageValidation.Validate(bytes);if(format.MediaType!=item.MediaType || format.Width!=item.Width || format.Height!=item.Height)throw new InvalidDataException(TextResources.Get("generatedImageStore.read.text.alternativeB"));return bytes;}
        }
        public GeneratedImage Promote(string id){return PromoteCore(id,null);}
        // A coordinator can defer view notification until its own mutation completes.
        internal GeneratedImage PromoteDeferred(string id,ICollection<Action> notifications){return PromoteCore(id,notifications);}
        GeneratedImage PromoteCore(string id,ICollection<Action> notifications)
        {
            ValidateId(id);GeneratedImage result;bool changed=false;
            lock(gate){
                EnsureOpen();CheckDirectory(directory);
                var original=images.Find(i=>i.Id==id);if(original==null)throw new FileNotFoundException(TextResources.Get("generatedImageStore.promoteCore.text"));
                var pending=new Queue<string>();var visited=new HashSet<string>(StringComparer.Ordinal);var promoted=new Dictionary<string,GeneratedImage>(StringComparer.Ordinal);var sources=new Dictionary<string,string>(StringComparer.Ordinal);
                pending.Enqueue(id);
                // Walk references iteratively: shared references and damaged cycles cannot recurse forever.
                while(pending.Count>0){
                    string currentId=pending.Dequeue();if(!visited.Add(currentId))continue;ValidateId(currentId);
                    var current=images.Find(i=>i.Id==currentId);if(current==null)throw new FileNotFoundException(TextResources.Get("generatedImageStore.promoteCore.text.alternativeA"));
                    if(!String.IsNullOrEmpty(current.ReferenceImageId))pending.Enqueue(current.ReferenceImageId);
                    if(current.Temporary){var durable=current.Copy();durable.Temporary=false;promoted.Add(currentId,durable);sources.Add(currentId,ExistingPath(current));}
                }
                if(promoted.Count==0)return original.Copy();
                if(recoveryRequired)throw new InvalidDataException(TextResources.Get("generatedImageStore.promoteCore.text.alternativeB"));
                try{
                    foreach(var pair in promoted){
                        var durable=pair.Value;byte[] bytes=Read(pair.Key);string folder=Folder(durable);Directory.CreateDirectory(folder);CheckDirectory(folder);string destination=FilePath(durable);
                        if(File.Exists(destination)){
                            if((File.GetAttributes(destination)&FileAttributes.ReparsePoint)!=0 || new FileInfo(destination).Length!=bytes.Length)throw new InvalidDataException(TextResources.Get("generatedImageStore.promoteCore.text.alternativeC"));
                            byte[] existing=File.ReadAllBytes(destination);if(!existing.SequenceEqual(bytes))throw new InvalidDataException(TextResources.Get("generatedImageStore.promoteCore.text.alternativeC"));
                        }else WriteAtomic(destination,bytes);
                    }
                    var committed=images.Select(i=>promoted.ContainsKey(i.Id)?promoted[i.Id]:i).ToList();
                    // Commit one encrypted index containing the result and every promoted reference.
                    // Until this succeeds, callers still see and can read the original temporary assets.
                    SaveIndex(committed);images.Clear();images.AddRange(committed);changed=true;result=images.Find(i=>i.Id==id).Copy();
                }catch(Exception e){if(!StorageError(e))throw;Warning=TextResources.Get("generatedImageStore.promoteCore.warning");throw new IOException(Warning);}
                foreach(string path in sources.Values){temporaryCopies.Add(path);try{File.Delete(path);temporaryCopies.Remove(path);}catch(IOException){Warning=TextResources.Get("generatedImageStore.promoteCore.warning.alternativeA");}catch(UnauthorizedAccessException){Warning=TextResources.Get("generatedImageStore.promoteCore.warning.alternativeB");}}
            }
            if(changed){if(notifications==null)Notify();else notifications.Add(Notify);}return result;
        }
        public int Recover()
        {
            int count=0,invalid=0;lock(gate){EnsureOpen();CheckDirectory(directory);if(purgeRecoveryPending)RecoverPurgeTransactions();var recovered=images.Select(i=>i.Copy()).ToList();var ids=new HashSet<string>(recovered.Select(i=>i.Id),StringComparer.Ordinal);
                var files=new List<string>(Directory.GetFiles(directory));if(Directory.Exists(referenceDirectory)){CheckDirectory(referenceDirectory);files.AddRange(Directory.GetFiles(referenceDirectory));}
                foreach(string path in files){
                    string id=Path.GetFileNameWithoutExtension(path);string ext=Path.GetExtension(path).ToLowerInvariant();
                    if(!ValidId(id) || (ext!=".png" && ext!=".jpg" && ext!=".gif" && ext!=".webp") || ids.Contains(id) || purgedIds.Contains(id))continue;
                    if(recovered.Count>=MaximumItems){invalid++;continue;}
                    try{if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException();var info=new FileInfo(path);if(info.Length<=0 || info.Length>MaximumBytes)throw new InvalidDataException();var format=GeneratedImageValidation.Validate(File.ReadAllBytes(path));if(Extension(format.MediaType)!=ext)throw new InvalidDataException();
                        recovered.Add(new GeneratedImage{Id=id,Prompt="恢复的图片（原元数据不可用）",MediaType=format.MediaType,Width=format.Width,Height=format.Height,Bytes=info.Length,CreatedUtc=info.CreationTimeUtc,Temporary=false,IsReference=Path.GetDirectoryName(path)==referenceDirectory});ids.Add(id);count++;}
                    catch(Exception e){if(!StorageError(e))throw;invalid++;}
                }
                SaveIndex(recovered);images.Clear();images.AddRange(recovered);recoveryRequired=false;Warning=invalid==0?"":TextResources.Get("generatedImageStore.recover.warningInvalid");
            }
            Notify();return count;
        }
        void Load()
        {
            if(File.Exists(indexPath))try{
                var info=new FileInfo(indexPath);if(info.Length<=0 || info.Length>MaximumIndexBytes)throw new SerializationException();
                byte[] plain=SecretProtection.Unprotect(File.ReadAllBytes(indexPath));if(plain.Length>MaximumIndexBytes)throw new SerializationException();Index index;
                using(var stream=new MemoryStream(plain))index=(Index)new DataContractJsonSerializer(typeof(Index)).ReadObject(stream);
                if(index==null || index.Images==null || index.Images.Count>MaximumItems)throw new SerializationException();
                var ids=new HashSet<string>(StringComparer.Ordinal);foreach(var item in index.Images){if(item==null || !ValidId(item.Id) || !ids.Add(item.Id) || item.Temporary || Extension(item.MediaType)==null || item.Bytes<1 || item.Bytes>MaximumBytes || !GeneratedImageValidation.ValidDimensions(item.Width,item.Height) || (item.Prompt??"").Length>16000)throw new SerializationException();}
                images.AddRange(index.Images.Select(i=>i.Copy()));lastPurgeId=index.LastPurgeId;
            }catch(Exception e){if(!StorageError(e))throw;images.Clear();recoveryRequired=true;Warning=TextResources.Get("generatedImageStore.load.warning");
                try{File.Copy(indexPath,indexPath+".invalid-"+Guid.NewGuid().ToString("N"),false);}catch(IOException){}catch(UnauthorizedAccessException){}
            }
            var files=new List<string>(Directory.GetFiles(directory));if(Directory.Exists(referenceDirectory)){CheckDirectory(referenceDirectory);files.AddRange(Directory.GetFiles(referenceDirectory));}
            var indexedIds=new HashSet<string>(images.Select(i=>i.Id),StringComparer.Ordinal);
            if(files.Any(p=>ValidId(Path.GetFileNameWithoutExtension(p)) && ExtensionForFile(p)!=null && !indexedIds.Contains(Path.GetFileNameWithoutExtension(p))))Warning=TextResources.Get("generatedImageStore.load.warning.alternativeA");
            // Earlier sessions' temporary directories are deliberately preserved: another process
            // may still own them. This instance cleans only its own temporary assets on disposal.
        }
        void SaveIndex(IEnumerable<GeneratedImage> entries,string transactionId=null)
        {
            byte[] plain;using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(Index)).WriteObject(stream,new Index{LastPurgeId=transactionId??lastPurgeId,Images=entries.Where(i=>!i.Temporary).Select(i=>i.Copy()).ToList()});if(stream.Length>MaximumIndexBytes)throw new InvalidDataException(TextResources.Get("generatedImageStore.saveIndex.text"));plain=stream.ToArray();}
            byte[] encrypted=SecretProtection.Protect(plain);if(encrypted.Length>MaximumIndexBytes)throw new InvalidDataException(TextResources.Get("generatedImageStore.saveIndex.text"));WriteAtomic(indexPath,encrypted);if(transactionId!=null)lastPurgeId=transactionId;
        }
        static void WriteAtomic(string path,byte[] bytes)
        {
            string temporary=Path.Combine(Path.GetDirectoryName(path),Guid.NewGuid().ToString("N")+".tmp");try{using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}AtomicFile.Commit(temporary,path);}
            finally{if(File.Exists(temporary))try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
        }
        string ExistingPath(GeneratedImage image){string path=FilePath(image);CheckDirectory(directory);CheckDirectory(Path.GetDirectoryName(path));if(!File.Exists(path))throw new FileNotFoundException(TextResources.Get("generatedImageStore.existingPath.text"));if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException(TextResources.Get("generatedImageStore.existingPath.text.alternativeA"));return path;}
        string Folder(GeneratedImage image){return image.Temporary?temporaryDirectory:(image.IsReference?referenceDirectory:directory);}
        string FilePath(GeneratedImage image){ValidateId(image.Id);string extension=Extension(image.MediaType);if(extension==null)throw new InvalidDataException(TextResources.Get("generatedImageStore.filePath.text"));string folder=Folder(image);string path=Path.GetFullPath(Path.Combine(folder,image.Id+extension));if(!path.StartsWith(folder+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException(TextResources.Get("generatedImageStore.filePath.text.alternativeA"));return path;}
        static string Extension(string type){switch(type){case "image/png":return ".png";case "image/jpeg":return ".jpg";case "image/gif":return ".gif";case "image/webp":return ".webp";default:return null;}}
        static string ExtensionForFile(string path){switch(Path.GetExtension(path).ToLowerInvariant()){case ".png":case ".jpg":case ".gif":case ".webp":return Path.GetExtension(path);default:return null;}}
        static string Limit(string value,int length){value=value??"";return value.Length>length?value.Substring(0,length):value;}
        static bool ValidId(string id){if(id==null || id.Length!=32)return false;foreach(char c in id)if(!((c>='0'&&c<='9') || (c>='a'&&c<='f')))return false;return true;}
        static void ValidateId(string id){if(!ValidId(id))throw new ArgumentException(TextResources.Get("generatedImageStore.validateId.text"),"id");}
        static void CheckDirectory(string path){if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException(TextResources.Get("generatedImageStore.checkDirectory.text"));}
        static bool StorageError(Exception e){return e is IOException || e is UnauthorizedAccessException || e is CryptographicException || e is SerializationException || e is System.Xml.XmlException || e is ArgumentException || e is FormatException;}
        void EnsureOpen(){if(disposed)throw new ObjectDisposedException("GeneratedImageStore");}
        void Notify(){var handler=Changed;if(handler!=null)foreach(Action listener in handler.GetInvocationList())try{listener();}catch(Exception){/* A view refresh cannot invalidate a committed asset. */}}
        public void Dispose()
        {
            lock(gate){if(disposed)return;disposed=true;foreach(var image in images.Where(i=>i.Temporary))temporaryCopies.Add(FilePath(image));foreach(string path in temporaryCopies)try{File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}temporaryCopies.Clear();if(Directory.Exists(temporaryDirectory))try{Directory.Delete(temporaryDirectory,false);}catch(IOException){}catch(UnauthorizedAccessException){}images.RemoveAll(i=>i.Temporary);}
        }
    }
    internal sealed class GeneratedImageFormat {public string MediaType;public int Width,Height;}
    internal static class GeneratedImageValidation
    {
        internal static bool ValidDimensions(int width,int height){return width>0 && height>0 && width<=32768 && height<=32768 && (long)width*height<=100000000;}
        static int Big(byte[] bytes,int offset){return unchecked((int)(((uint)bytes[offset]<<24)|((uint)bytes[offset+1]<<16)|((uint)bytes[offset+2]<<8)|bytes[offset+3]));}
        static int Little24(byte[] b,int p){return b[p]|(b[p+1]<<8)|(b[p+2]<<16);}
        internal static GeneratedImageFormat Validate(byte[] bytes)
        {
            if(bytes==null || bytes.Length<12 || bytes.Length>GeneratedImageStore.MaximumBytes)throw new InvalidDataException(TextResources.Get("generatedImageStore.validate.text"));
            var result=new GeneratedImageFormat();
            try{
                if(bytes.Length>=33 && bytes[0]==137 && Encoding.ASCII.GetString(bytes,1,7)=="PNG\r\n\u001a\n"){
                    result.MediaType="image/png";bool header=false,data=false,end=false;int position=8;var compressed=new MemoryStream();
                    while(position+12<=bytes.Length){int length=Big(bytes,position);if(length<0 || length>bytes.Length-position-12)throw new InvalidDataException();string type=Encoding.ASCII.GetString(bytes,position+4,4);
                        uint crc=0xffffffff;for(int n=position+4;n<position+8+length;n++){crc^=bytes[n];for(int bit=0;bit<8;bit++)crc=(crc&1)!=0?0xedb88320^(crc>>1):crc>>1;}uint expected=unchecked((uint)Big(bytes,position+8+length));if((crc^0xffffffff)!=expected)throw new InvalidDataException();
                        if(!header){if(type!="IHDR" || length!=13)throw new InvalidDataException();result.Width=Big(bytes,position+8);result.Height=Big(bytes,position+12);header=true;}
                        else if(type=="IHDR")throw new InvalidDataException();
                        if(type=="IDAT" && length>0){data=true;compressed.Write(bytes,position+8,length);}
                        if(type=="IEND"){if(length!=0 || position+12!=bytes.Length)throw new InvalidDataException();end=true;break;}position+=length+12;
                    }if(!header || !data || !end || !ValidDimensions(result.Width,result.Height))throw new InvalidDataException();ValidatePngPixels(bytes,compressed.ToArray(),result.Width,result.Height);
                }else if(bytes[0]==255 && bytes[1]==216 && bytes[bytes.Length-2]==255 && bytes[bytes.Length-1]==217){
                    result.MediaType="image/jpeg";int p=2;bool frame=false,scan=false;
                    while(p+3<bytes.Length){if(bytes[p++]!=255)throw new InvalidDataException();while(p<bytes.Length && bytes[p]==255)p++;int marker=bytes[p++];if(marker==217)break;if(marker==0 || marker==216)throw new InvalidDataException();if(marker>=208 && marker<=215)continue;
                        int size=(bytes[p]<<8)|bytes[p+1];if(size<2 || size>bytes.Length-p)throw new InvalidDataException();
                        if((marker>=192 && marker<=195)||(marker>=197 && marker<=199)||(marker>=201 && marker<=203)||(marker>=205 && marker<=207)){if(size<8)throw new InvalidDataException();result.Height=(bytes[p+3]<<8)|bytes[p+4];result.Width=(bytes[p+5]<<8)|bytes[p+6];frame=true;}
                        if(marker==218){scan=true;break;}p+=size;
                    }if(!frame || !scan)throw new InvalidDataException();
                }else if((Encoding.ASCII.GetString(bytes,0,6)=="GIF87a" || Encoding.ASCII.GetString(bytes,0,6)=="GIF89a") && bytes[bytes.Length-1]==59){
                    result.MediaType="image/gif";result.Width=bytes[6]|bytes[7]<<8;result.Height=bytes[8]|bytes[9]<<8;int p=13;if((bytes[10]&128)!=0)p+=3*(1<<((bytes[10]&7)+1));bool frame=false;
                    while(p<bytes.Length-1){int kind=bytes[p++];if(kind==33){if(p>=bytes.Length)throw new InvalidDataException();p++;}else if(kind==44){if(p+9>=bytes.Length)throw new InvalidDataException();int flags=bytes[p+8];p+=9;if((flags&128)!=0)p+=3*(1<<((flags&7)+1));p++;frame=true;}else throw new InvalidDataException();
                        while(true){if(p>=bytes.Length)throw new InvalidDataException();int size=bytes[p++];if(size==0)break;if(size>bytes.Length-p)throw new InvalidDataException();p+=size;}
                    }if(!frame || p!=bytes.Length-1)throw new InvalidDataException();
                }else if(Encoding.ASCII.GetString(bytes,0,4)=="RIFF" && Encoding.ASCII.GetString(bytes,8,4)=="WEBP"){
                    result=WebPValidation.Validate(bytes);
                }else throw new InvalidDataException();
                if(!ValidDimensions(result.Width,result.Height))throw new InvalidDataException();
#if !PORTABLE
                // GDI+ decodes PNG/JPEG/GIF instead of trusting header dimensions alone.
                if(result.MediaType!="image/webp")using(var stream=new MemoryStream(bytes))using(var decoded=System.Drawing.Image.FromStream(stream,false,true))using(var bitmap=new System.Drawing.Bitmap(decoded)){if(bitmap.Width!=result.Width || bitmap.Height!=result.Height)throw new InvalidDataException();bitmap.GetPixel(0,0);}
#endif
                return result;
            }catch(Exception e){if(!(e is IOException || e is ArgumentException || e is OverflowException || e is IndexOutOfRangeException || e is System.Runtime.InteropServices.ExternalException))throw;throw new InvalidDataException(TextResources.Get("generatedImageStore.validate.text.alternativeA"));}
        }
        static void ValidatePngPixels(byte[] png,byte[] compressed,int width,int height)
        {
            int depth=png[24],color=png[25],channels=color==0 || color==3?1:color==2?3:color==4?2:color==6?4:0;
            if(channels==0 || (depth!=8 && depth!=16 && !(depth==1 || depth==2 || depth==4) || (depth<8 && color!=0 && color!=3)) || (color==3 && depth==16) || png[26]!=0 || png[27]!=0 || png[28]>1)throw new InvalidDataException();
            if(compressed.Length<6 || (compressed[0]&15)!=8 || (compressed[0]>>4)>7 || (((int)compressed[0]<<8)|compressed[1])%31!=0 || (compressed[1]&32)!=0)throw new InvalidDataException();
            uint a=1,b=0;byte[] buffer=new byte[32768];
            using(var memory=new MemoryStream(compressed,2,compressed.Length-6))using(var inflate=new DeflateStream(memory,CompressionMode.Decompress)){
                int[] startsX=png[28]==0?new[]{0}:new[]{0,4,0,2,0,1,0},startsY=png[28]==0?new[]{0}:new[]{0,0,4,0,2,0,1};
                int[] stepsX=png[28]==0?new[]{1}:new[]{8,8,4,4,2,2,1},stepsY=png[28]==0?new[]{1}:new[]{8,8,8,4,4,2,2};
                for(int pass=0;pass<startsX.Length;pass++){
                    int columns=width<=startsX[pass]?0:(width-startsX[pass]+stepsX[pass]-1)/stepsX[pass];int rows=height<=startsY[pass]?0:(height-startsY[pass]+stepsY[pass]-1)/stepsY[pass];
                    if(columns==0 || rows==0)continue;int rowBytes=checked((int)(((long)columns*channels*depth+7)/8));
                    for(int row=0;row<rows;row++){
                        int filter=inflate.ReadByte();if(filter<0 || filter>4)throw new InvalidDataException();a=(a+(uint)filter)%65521;b=(b+a)%65521;
                        int remaining=rowBytes;while(remaining>0){int count=inflate.Read(buffer,0,Math.Min(buffer.Length,remaining));if(count==0)throw new InvalidDataException();for(int i=0;i<count;i++){a=(a+buffer[i])%65521;b=(b+a)%65521;}remaining-=count;}
                    }
                }
                if(inflate.ReadByte()!=-1 || ((b<<16)|a)!=unchecked((uint)Big(compressed,compressed.Length-4)))throw new InvalidDataException();
            }
        }
    }
}
