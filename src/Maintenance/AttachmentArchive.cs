using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Dafeiyu.Maintenance
{
    // Content-addressed, independently openable originals. No conversation text,
    // source paths, prompts or account metadata is copied into this directory.
    internal sealed class AttachmentArchive
    {
        readonly string directory;
        internal AttachmentArchive(string path){directory=path;}
        internal void Preserve(IEnumerable<ChatAttachment> attachments)
        {
            foreach(var file in attachments??new ChatAttachment[0]){
                if(file==null)throw new InvalidDataException(TextResources.Get("dataMaintenance.storageFailure"));
                if(file.MediaType=="text/plain"&&String.IsNullOrEmpty(file.Data))continue;
                string extension;switch(file.MediaType){case "image/png":extension=".png";break;case "image/jpeg":extension=".jpg";break;case "image/gif":extension=".gif";break;case "image/webp":extension=".webp";break;case "application/pdf":extension=".pdf";break;default:throw new InvalidDataException(TextResources.Get("dataMaintenance.storageFailure"));}
                byte[] bytes=Convert.FromBase64String(file.Data??"");if(bytes.Length==0||bytes.Length>ChatAttachment.MaximumBytes)throw new InvalidDataException(TextResources.Get("dataMaintenance.storageFailure"));
                if(file.IsImage){if(GeneratedImageValidation.Validate(bytes).MediaType!=file.MediaType)throw new InvalidDataException(TextResources.Get("dataMaintenance.storageFailure"));}
                else if(bytes.Length<5||System.Text.Encoding.ASCII.GetString(bytes,0,5)!="%PDF-")throw new InvalidDataException(TextResources.Get("dataMaintenance.storageFailure"));
                string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
                CachePaths.Check(directory);Directory.CreateDirectory(directory);
                using(var pins=CacheDirectoryPins.OpenDirectory(directory)){
                    string destination=Path.Combine(directory,hash+extension);CachePaths.Check(destination);
                    if(!File.Exists(destination)){
                        string temporary=Path.Combine(directory,Guid.NewGuid().ToString("N")+".tmp");
                        try{using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}AtomicFile.Commit(temporary,destination);}
                        finally{if(File.Exists(temporary))File.Delete(temporary);}
                    }
                    // A previous failed/corrupt archive entry is never accepted as
                    // preservation. Verify durable bytes before any removal.
                    CachePaths.Check(destination);if(!File.ReadAllBytes(destination).SequenceEqual(bytes))throw new IOException(TextResources.Get("dataMaintenance.storageFailure"));
                }
            }
        }
    }
}
