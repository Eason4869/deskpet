using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Dafeiyu.ImChannels
{
    public sealed class ImChannelStore
    {
        [DataContract] sealed class Envelope{[DataMember] public int Version=1;[DataMember] public string Payload;}
        readonly string path;readonly object gate=new object();bool readFailed;
        public ImChannelStore(string root){path=Path.Combine(root,"im-channels.json");}
        public IList<ImChannelProfile> Load()
        {
            lock(gate)try{
                if(!File.Exists(path))return new List<ImChannelProfile>();
                if(new FileInfo(path).Length>2*1024*1024)throw new SerializationException();
                Envelope saved;using(var stream=File.OpenRead(path))saved=(Envelope)new DataContractJsonSerializer(typeof(Envelope)).ReadObject(stream);
                if(saved==null||saved.Version!=1||String.IsNullOrEmpty(saved.Payload))throw new SerializationException();
                byte[] plain=SecretProtection.Unprotect(Convert.FromBase64String(saved.Payload));
                try{using(var stream=new MemoryStream(plain)){var list=(List<ImChannelProfile>)new DataContractJsonSerializer(typeof(List<ImChannelProfile>)).ReadObject(stream);Validate(list);return list;}}finally{Array.Clear(plain,0,plain.Length);}
            }catch{readFailed=true;throw new InvalidOperationException("im.storageFailed");}
        }
        static void Validate(IList<ImChannelProfile> profiles){if(profiles==null||profiles.Count>16||profiles.Any(x=>x==null)||profiles.Select(x=>x.Id).Distinct().Count()!=profiles.Count)throw new InvalidOperationException("im.invalidConfig");foreach(var p in profiles)p.Validate();}
        public void Save(IEnumerable<ImChannelProfile> profiles)
        {
            lock(gate){if(readFailed)throw new InvalidOperationException("im.storageFailed");if(File.Exists(path))Load();var snapshot=profiles.Select(x=>x.Copy()).ToList();Validate(snapshot);
                byte[] cipher;using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(List<ImChannelProfile>)).WriteObject(stream,snapshot);byte[] plain=stream.ToArray();try{cipher=SecretProtection.Protect(plain);}finally{Array.Clear(plain,0,plain.Length);}}
                byte[] encoded;using(var stream=new MemoryStream()){new DataContractJsonSerializer(typeof(Envelope)).WriteObject(stream,new Envelope{Payload=Convert.ToBase64String(cipher)});if(stream.Length>2*1024*1024)throw new InvalidOperationException("im.invalidConfig");encoded=stream.ToArray();}
                Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try{using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(encoded,0,encoded.Length);stream.Flush(true);}if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
            }
        }
    }
}
