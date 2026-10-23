using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Dafeiyu
{
    // Data-only packages: validation belongs to the frontend before activation.
    public static class CompanionPackStorage
    {
        const long Maximum=256L*1024*1024;
        public static HashSet<string> Hidden(string data)
        {
            string path=AssetPath(Path.Combine(data,"companion-packs"),"removed.txt");
            if(!File.Exists(path))return new HashSet<string>(StringComparer.Ordinal);
            if(new FileInfo(path).Length>65536)throw new InvalidDataException(TextResources.Get("companionPackStorage.hidden.text"));
            return new HashSet<string>(File.ReadAllLines(path).Where(p=>p.Length>0 && p.Length<=120 && p.IndexOfAny(new[]{'\r','\n','\0'})<0),StringComparer.Ordinal);
        }
        public static void SetHidden(string data,string id,bool hidden)
        {
            if(String.IsNullOrWhiteSpace(id) || id.Length>120 || id.IndexOfAny(new[]{'\r','\n','\0'})>=0)throw new InvalidDataException(TextResources.Get("companionPackStorage.setHidden.text"));
            var ids=Hidden(data);if(hidden)ids.Add(id);else ids.Remove(id);WriteHidden(data,ids);
        }
        public static void RestoreHidden(string data) { WriteHidden(data,new string[0]); }
        static void WriteHidden(string data,IEnumerable<string> ids)
        {
            string root=Path.Combine(data,"companion-packs");DirectorySafe(root);Directory.CreateDirectory(root);
            string path=AssetPath(root,"removed.txt"),temporary=Path.Combine(root,".removed-"+Guid.NewGuid().ToString("N"));
            try {File.WriteAllLines(temporary,ids.OrderBy(p=>p,StringComparer.Ordinal),new UTF8Encoding(false));AtomicFile.Commit(temporary,path);}
            finally {if(File.Exists(temporary))File.Delete(temporary);}
        }
        public static string Identity(string data,string id)
        {
            if(!ValidId(id))throw new InvalidDataException(TextResources.Get("companionPackStorage.identity.text"));
            string path=AssetPath(Path.Combine(data,"companion-packs",id),"identity.txt");
            if(!File.Exists(path))return id;
            if(new FileInfo(path).Length>128)throw new InvalidDataException(TextResources.Get("companionPackStorage.identity.text.alternativeA"));
            string value=File.ReadAllText(path).Trim();
            if(value!=id && !Regex.IsMatch(value,"^skin-[a-f0-9]{32}$"))throw new InvalidDataException(TextResources.Get("companionPackStorage.identity.text.alternativeA"));
            return value;
        }
        public static string RetainIdentity(string data,string id,string previous)
        {
            if(!ValidId(id) || !Regex.IsMatch(previous,"^skin-[a-f0-9]{32}$"))throw new InvalidDataException(TextResources.Get("companionPackStorage.retainIdentity.text"));
            string root=Path.Combine(data,"companion-packs",id);DirectorySafe(root);Directory.CreateDirectory(root);
            string path=AssetPath(root,"identity.txt");
            if(!File.Exists(path))using(var stream=new FileStream(path,FileMode.CreateNew))using(var writer=new StreamWriter(stream,new UTF8Encoding(false)))writer.Write(previous);
            return Identity(data,id);
        }
        public static bool ValidId(string id) { return id!=null && Regex.IsMatch(id,"^[a-z0-9][a-z0-9-]{0,47}$") && id!="con" && id!="prn" && id!="aux" && id!="nul" && !Regex.IsMatch(id,"^(com|lpt)[0-9]$"); }
        static void DirectorySafe(string path)
        {
            var current=Path.GetFullPath(path);
            while(!String.IsNullOrEmpty(current)) {
                if(Directory.Exists(current) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException(TextResources.Get("companionPackStorage.directorySafe.text"));
                current=Path.GetDirectoryName(current);
            }
        }
        public static void CheckRelative(string relative)
        {
            if(String.IsNullOrWhiteSpace(relative) || relative.Length>200 || relative.IndexOfAny(new[]{'\\',':','\0'})>=0 || relative.StartsWith("/") || relative.Split('/').Any(p=>p.Length==0 || p=="." || p==".." || p.EndsWith(".") || p.EndsWith(" ") || p.IndexOfAny(Path.GetInvalidFileNameChars())>=0))throw new InvalidDataException(TextResources.Get("companionPackStorage.checkRelative.text"));
        }
        public static string AssetPath(string root,string relative)
        {
            CheckRelative(relative);
            DirectorySafe(root);
            string current=root;
            foreach(string part in relative.Split('/')) {
                current=Path.Combine(current,part);
                if((File.Exists(current)||Directory.Exists(current)) && (File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException(TextResources.Get("companionPackStorage.assetPath.text"));
            }
            return current;
        }
        public static T ReadZip<T>(string zip,string data,Func<string,T> consume)
        {
            string parent=Path.Combine(data,"companion-packs");DirectorySafe(parent);Directory.CreateDirectory(parent);
            string stage=Path.Combine(parent,".extract-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);
            try {
                using(var file=File.OpenRead(zip))using(var archive=new ZipArchive(file,ZipArchiveMode.Read)) {
                    if(archive.Entries.Count>128)throw new InvalidDataException(TextResources.Get("companionPackStorage.readZip.text"));
                    long total=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach(var entry in archive.Entries) {
                        string relative=entry.FullName.Replace('\\','/');
                        bool directory=relative.EndsWith("/");if(directory)relative=relative.Substring(0,relative.Length-1);
                        string target=AssetPath(stage,relative);
                        if(!names.Add(relative) || ((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException(TextResources.Get("companionPackStorage.readZip.text.alternativeA"));
                        if(directory){Directory.CreateDirectory(target);continue;}
                        string extension=Path.GetExtension(relative).ToLowerInvariant();
                        if(extension!=".png" && extension!=".json" && extension!=".txt" && extension!=".md")throw new InvalidDataException(TextResources.Get("companionPackStorage.readZip.text.alternativeB"));
                        long limit=relative=="pet.json"?65536:extension==".png"?25L*1024*1024:1024L*1024;
                        total+=entry.Length;if(entry.Length>limit || total>Maximum)throw new InvalidDataException(TextResources.Get("companionPackStorage.readZip.text.alternativeC"));
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using(var input=entry.Open())using(var output=new FileStream(target,FileMode.CreateNew)) {
                            var buffer=new byte[65536];int count;long written=0;
                            while((count=input.Read(buffer,0,buffer.Length))!=0){written+=count;if(written>limit || written>entry.Length)throw new InvalidDataException(TextResources.Get("companionPackStorage.readZip.text.alternativeD"));output.Write(buffer,0,count);}
                            if(written!=entry.Length)throw new InvalidDataException(TextResources.Get("companionPackStorage.readZip.text.alternativeE"));
                        }
                    }
                }
                string manifest=Path.Combine(stage,"pet.json");if(!File.Exists(manifest))throw new InvalidDataException(TextResources.Get("companionPackStorage.readZip.text.alternativeF"));
                return consume(manifest);
            } finally { if(Directory.Exists(stage)){DirectorySafe(stage);Directory.Delete(stage,true);} }
        }
        public static IEnumerable<KeyValuePair<string,string>> Installed(string data)
        {
            string root=Path.Combine(data,"companion-packs");DirectorySafe(root);if(!Directory.Exists(root))yield break;
            foreach(string directory in Directory.GetDirectories(root).OrderBy(p=>p,StringComparer.Ordinal)) {
                string id=Path.GetFileName(directory);if(!ValidId(id))continue;
                string current=null;try {current=Current(directory);}catch(Exception e){if(!(e is IOException || e is InvalidDataException || e is UnauthorizedAccessException))throw;}
                if(current!=null)yield return new KeyValuePair<string,string>(id,current);
            }
        }
        static string Current(string root)
        {
            DirectorySafe(root);string pointer=AssetPath(root,"current.txt");if(!File.Exists(pointer))return null;
            if(new FileInfo(pointer).Length>128)throw new InvalidDataException(TextResources.Get("companionPackStorage.current.text"));
            string version=File.ReadAllText(pointer).Trim();if(!Regex.IsMatch(version,"^v-[a-f0-9]{32}$"))throw new InvalidDataException(TextResources.Get("companionPackStorage.current.text"));
            string path=AssetPath(root,version);return Directory.Exists(path)?path:null;
        }
        public static string Install(string source,string data,string id,IEnumerable<string> references,Action<string> validate)
        {
            if(!ValidId(id))throw new InvalidDataException(TextResources.Get("companionPackStorage.install.text"));
            string root=Path.Combine(data,"companion-packs",id);DirectorySafe(root);Directory.CreateDirectory(root);
            var files=references.Concat(new[]{"pet.json"}).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            string current=Current(root);
            if(current!=null && files.All(p=>Same(AssetPath(source,p),AssetPath(current,p)))) { validate(current);return current; }
            string version="v-"+Guid.NewGuid().ToString("N"),destination=Path.Combine(root,version),pointer=Path.Combine(root,"current.txt"),temporary=Path.Combine(root,".current-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(destination);bool published=false;
            try {
                long total=0;
                foreach(string relative in files) {
                    string from=AssetPath(source,relative),to=AssetPath(destination,relative);total+=new FileInfo(from).Length;
                    if(total>Maximum)throw new InvalidDataException(TextResources.Get("companionPackStorage.install.text.alternativeA"));
                    Directory.CreateDirectory(Path.GetDirectoryName(to));File.Copy(from,to,false);
                }
                validate(destination);
                File.WriteAllText(temporary,version,new UTF8Encoding(false));
                AtomicFile.Commit(temporary,pointer);
                published=true;return destination;
            } finally {
                if(File.Exists(temporary))File.Delete(temporary);
                if(!published && Directory.Exists(destination)){DirectorySafe(destination);Directory.Delete(destination,true);}
            }
        }
        static bool Same(string left,string right)
        {
            if(!File.Exists(right) || new FileInfo(left).Length!=new FileInfo(right).Length)return false;
            using(var a=File.OpenRead(left))using(var b=File.OpenRead(right))using(var sha=SHA256.Create())return sha.ComputeHash(a).SequenceEqual(sha.ComputeHash(b));
        }
    }
}
