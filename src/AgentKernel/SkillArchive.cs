using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Dafeiyu
{
    // Extract once into an owned temporary directory; never run archive programs or follow links.
    public sealed class SkillArchive : IDisposable
    {
        readonly string directory;
        readonly string archiveName,digest;
        public IList<RepositorySkill> Skills {get;private set;}
        public int InvalidCount {get;private set;}
        public SkillArchive(string path)
        {
            AgentInstructions.RejectLinks(path);directory=Path.Combine(Path.GetTempPath(),"deskpet-zip-"+Guid.NewGuid().ToString("N"));archiveName=Path.GetFileName(path);
            try{
                // Hash and extract the same open stream so the source cannot change between the two operations.
                using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
                    if(input.Length>64L*1024*1024)throw new InvalidOperationException(TextResources.Get("skillArchive.skillArchive.text"));
                    using(var sha=SHA256.Create())digest=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();input.Position=0;
                    using(var archive=new ZipArchive(input,ZipArchiveMode.Read,true)){
                        if(archive.Entries.Count>4096)throw new InvalidOperationException(TextResources.Get("skillArchive.skillArchive.text.alternativeA"));
                        long total=0;int files=0;var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach(var entry in archive.Entries){string name=entry.FullName.TrimEnd('/');SkillRepositoryClient.SafePath(name);
                            if(!paths.Add(name))throw new InvalidOperationException(TextResources.Get("skillArchive.skillArchive.text.alternativeB"));
                            int kind=(entry.ExternalAttributes>>16)&0xF000;
                            if(kind==0xA000||(entry.ExternalAttributes&(int)FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException(TextResources.Get("skillArchive.skillArchive.text.alternativeC"));
                            if(entry.FullName.EndsWith("/"))continue;
                            total+=entry.Length;if(++files>2048||entry.Length<0||total>64L*1024*1024)throw new InvalidOperationException(TextResources.Get("skillArchive.skillArchive.text.alternativeD"));
                        }
                        Directory.CreateDirectory(directory);
                        long copied=0;
                        foreach(var entry in archive.Entries){string target=Path.Combine(directory,entry.FullName.TrimEnd('/').Replace('/',Path.DirectorySeparatorChar));AgentInstructions.RequireWithin(directory,target);
                            if(entry.FullName.EndsWith("/")){Directory.CreateDirectory(target);continue;}
                            Directory.CreateDirectory(Path.GetDirectoryName(target));
                            using(var source=entry.Open())using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                                var buffer=new byte[81920];int count;long current=0;while((count=source.Read(buffer,0,buffer.Length))>0){copied+=count;current+=count;if(copied>64L*1024*1024||current>entry.Length)throw new InvalidOperationException(TextResources.Get("skillArchive.skillArchive.text.alternativeE"));output.Write(buffer,0,count);}if(current!=entry.Length)throw new InvalidOperationException(TextResources.Get("skillArchive.skillArchive.text.alternativeF"));}
                        }
                    }
                }
                var found=new List<RepositorySkill>();
                foreach(string file in Directory.EnumerateFiles(directory,"SKILL.md",SearchOption.AllDirectories)){
                    if(Path.GetFileName(file)!="SKILL.md")continue;
                    try{var info=AgentSkills.ReadInfo(Path.GetDirectoryName(file));found.Add(new RepositorySkill{Name=info.Name,Description=info.Description,Path=Path.GetDirectoryName(file).Substring(directory.Length).TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar,'/'),Content=AgentInstructions.Read(file).Content});}
                    catch(InvalidOperationException){InvalidCount++;}
                }
                Skills=found.OrderBy(s=>s.Name,StringComparer.Ordinal).ToArray();
            }catch{Dispose();throw;}
        }
        public ManagedSkillInfo Install(RepositorySkill selected,ManagedSkillLibrary library)
        {
            var skill=Skills.SingleOrDefault(s=>s.Name==selected.Name&&s.Path==selected.Path&&s.Content==selected.Content);
            if(skill==null)throw new InvalidOperationException(TextResources.Get("skillArchive.install.text"));
            string source=Path.Combine(directory,skill.Path.Replace('/',Path.DirectorySeparatorChar));AgentInstructions.RequireWithin(directory,source);
            return library.Import(source,new SkillOrigin{Kind="zip",Repository=archiveName,Commit=digest,Path=skill.Path,Branch=""});
        }
        public void Dispose(){if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
}
