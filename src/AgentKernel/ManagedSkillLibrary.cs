using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    public sealed class ManagedSkillInfo
    {
        public string Id,Name,Description,Directory;
        public bool Enabled;
        public SkillOrigin Origin;
    }

    // Only explicit imports enter this registry. Project trust is a separate decision.
    public sealed class ManagedSkillLibrary
    {
        public sealed class RegistryEntry { public string Id {get;set;} public bool Enabled {get;set;} public SkillOrigin Origin {get;set;} }
        public sealed class Registry { public int Version {get;set;} public List<RegistryEntry> Skills {get;set;} }
        static readonly object Gate=new object();
        const int MaxFiles=2048,MaxEntries=4096,MaxDepth=64;
        const long MaxBytes=64L*1024*1024;
        readonly string directory;
        public static string ActiveDirectory {get;set;}
        public string DirectoryPath {get{return directory;}}
        public string Warning {get;private set;}
        public event Action Changed;

        public ManagedSkillLibrary(string directory)
        {
            if(String.IsNullOrWhiteSpace(directory))throw new ArgumentException(TextResources.Get("managedSkillLibrary.managedSkillLibrary.text"), "directory");
            this.directory=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            if(this.directory.Length==0||String.Equals(this.directory,Path.GetPathRoot(this.directory).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase))throw new ArgumentException(TextResources.Get("managedSkillLibrary.managedSkillLibrary.text.alternativeA"));
            AgentInstructions.RejectLinks(this.directory);Warning="";
        }

        public IList<ManagedSkillInfo> List()
        {
            lock(Gate){var state=ReadRegistry();if(state==null)return new ManagedSkillInfo[0];var items=new List<ManagedSkillInfo>();
                foreach(var entry in state.Skills){try{items.Add(ReadInfo(entry));}catch(Exception e){if(!Recoverable(e))throw;AddWarning(TextResources.Format("managedSkillLibrary.list.addWarning",entry.Id,e.Message));}}
                return items.OrderBy(s=>s.Name,StringComparer.Ordinal).ToArray();}
        }

        public ManagedSkillInfo Import(string sourceDirectory,SkillOrigin origin=null,string expectedContentHash=null)
        {
            ManagedSkillInfo result;
            lock(Gate){var state=RequireRegistry();if(state.Skills.Count>=128)throw new InvalidOperationException(TextResources.Get("managedSkillLibrary.import.text"));
                if(String.IsNullOrWhiteSpace(sourceDirectory))throw new ArgumentException(TextResources.Get("managedSkillLibrary.import.text.alternativeA"));
                string source=Path.GetFullPath(sourceDirectory).TrimEnd('\\','/');AgentInstructions.RejectLinks(source);
                if(IsWithin(directory,source)||IsWithin(source,directory))throw new UnauthorizedAccessException(TextResources.Get("managedSkillLibrary.import.text.alternativeB"));
                if(!System.IO.Directory.Exists(source))throw new ArgumentException(TextResources.Get("managedSkillLibrary.import.text.alternativeC"));
                var info=AgentSkills.ReadInfo(source);string id=ValidateId(info.Name),destination=SkillDirectory(id);
                if(state.Skills.Any(s=>s.Id==id)||System.IO.Directory.Exists(destination)||File.Exists(destination))throw new InvalidOperationException(TextResources.Format("managedSkillLibrary.import.template",id));
                // Walk before creating a destination so a rejected import leaves the source untouched.
                var files=new List<string>();var folders=new List<string>();long bytes=0;int entries=0;Scan(source,source,0,files,folders,ref bytes,ref entries);
                System.IO.Directory.CreateDirectory(directory);AgentInstructions.RejectLinks(directory);
                string staging=Path.Combine(directory,".import-"+Guid.NewGuid().ToString("N"));AgentInstructions.RequireWithin(directory,staging);System.IO.Directory.CreateDirectory(staging);
                foreach(string folder in folders){string target=Path.Combine(staging,Relative(source,folder));AgentInstructions.RequireWithin(staging,target);System.IO.Directory.CreateDirectory(target);}
                long copied=0;
                foreach(string file in files){AgentInstructions.RequireWithin(source,file);string target=Path.Combine(staging,Relative(source,file));AgentInstructions.RequireWithin(staging,target);CopyBounded(file,target,ref copied);}
                var copiedInfo=AgentSkills.ReadInfo(staging);if(copiedInfo.Name!=id)throw new InvalidOperationException(TextResources.Get("managedSkillLibrary.import.text.alternativeD"));
                if(expectedContentHash!=null&&AgentInstructions.Read(Path.Combine(staging,"SKILL.md")).Hash!=expectedContentHash)throw new InvalidOperationException(TextResources.Get("managedSkillLibrary.import.text.alternativeE"));
                AgentInstructions.RequireWithin(directory,destination);System.IO.Directory.Move(staging,destination);
                var entry=new RegistryEntry{Id=id,Enabled=true,Origin=origin};state.Skills.Add(entry);
                try{WriteRegistry(state);}
                catch(Exception registryError){
                    // Promotion is not a successful import until the registry commits. Keep the
                    // owned copy recoverable, but release its public name so the user can retry.
                    try{AgentInstructions.RequireWithin(directory,destination);string quarantine=Path.Combine(directory,".failed-import-"+id+"-"+Guid.NewGuid().ToString("N"));AgentInstructions.RequireWithin(directory,quarantine);System.IO.Directory.Move(destination,quarantine);}
                    catch(Exception quarantineError){throw new IOException(TextResources.Format("managedSkillLibrary.import.template.alternativeA",destination),new AggregateException(registryError,quarantineError));}
                    throw;
                }
                result=ReadInfo(entry);
            }
            NotifyChanged();return result;
        }

        public void SetEnabled(string id,bool enabled)
        {
            bool changed=false;
            lock(Gate){ValidateId(id);var state=RequireRegistry();var entry=Find(state,id);if(entry.Enabled!=enabled){if(enabled)ReadInfo(entry);entry.Enabled=enabled;WriteRegistry(state);changed=true;}}
            if(changed)NotifyChanged();
        }

        public void Remove(string id)
        {
            lock(Gate){ValidateId(id);var state=RequireRegistry();var entry=Find(state,id);string path=SkillDirectory(id);AgentInstructions.RequireWithin(directory,path);
                // A blocked rename must leave registration intact. If the later registry commit
                // fails, restore the original directory before reporting that removal failed.
                string quarantine=null;
                if(System.IO.Directory.Exists(path)){quarantine=Path.Combine(directory,".removed-"+id+"-"+Guid.NewGuid().ToString("N"));AgentInstructions.RequireWithin(directory,quarantine);System.IO.Directory.Move(path,quarantine);}
                state.Skills.Remove(entry);
                try{WriteRegistry(state);}
                catch(Exception registryError){
                    if(quarantine!=null){try{AgentInstructions.RequireWithin(directory,quarantine);AgentInstructions.RequireWithin(directory,path);System.IO.Directory.Move(quarantine,path);}
                        catch(Exception restoreError){throw new IOException(TextResources.Format("managedSkillLibrary.remove.template",quarantine),new AggregateException(registryError,restoreError));}}
                    throw;
                }
            }
            NotifyChanged();
        }

        public AgentInstructionSource Read(string id)
        {
            lock(Gate){ValidateId(id);var state=RequireRegistry();var entry=Find(state,id);var info=ReadInfo(entry);return AgentInstructions.Read(Path.Combine(info.Directory,"SKILL.md"));}
        }

        Registry ReadRegistry()
        {
            Warning="";
            try{AgentInstructions.RejectLinks(directory);string path=Path.Combine(directory,"registry.json");if(!File.Exists(path))return new Registry{Version=1,Skills=new List<RegistryEntry>()};
                var state=SkillStorage.ReadJson<Registry>(path,1024*1024);
                if(state==null||state.Version!=1||state.Skills==null||state.Skills.Count>128)throw new InvalidOperationException(TextResources.Get("managedSkillLibrary.readRegistry.text"));
                var ids=new HashSet<string>(StringComparer.Ordinal);foreach(var entry in state.Skills){if(entry==null||!ids.Add(ValidateId(entry.Id)))throw new InvalidOperationException(TextResources.Get("managedSkillLibrary.readRegistry.text.alternativeA"));}return state;
            }catch(Exception e){if(!Recoverable(e))throw;Warning=TextResources.Format("managedSkillLibrary.readRegistry.warningEMessage",e.Message);return null;}
        }

        Registry RequireRegistry(){var state=ReadRegistry();if(state==null)throw new InvalidOperationException(Warning);return state;}
        RegistryEntry Find(Registry state,string id){var entry=state.Skills.FirstOrDefault(s=>s.Id==id);if(entry==null)throw new ArgumentException(TextResources.Get("managedSkillLibrary.find.text"));return entry;}
        ManagedSkillInfo ReadInfo(RegistryEntry entry)
        {
            string path=SkillDirectory(entry.Id);AgentInstructions.RequireWithin(directory,path);var info=AgentSkills.ReadInfo(path);
            if(info.Name!=entry.Id)throw new InvalidOperationException(TextResources.Get("managedSkillLibrary.readInfo.text"));return new ManagedSkillInfo{Id=entry.Id,Name=info.Name,Description=info.Description,Directory=path,Enabled=entry.Enabled,Origin=entry.Origin};
        }
        string SkillDirectory(string id){ValidateId(id);string path=Path.Combine(directory,id);AgentInstructions.RequireWithin(directory,path);return path;}
        static string ValidateId(string id){if(id==null||!Regex.IsMatch(id,@"\A[a-z0-9][a-z0-9_-]{0,63}\z"))throw new ArgumentException(TextResources.Get("managedSkillLibrary.validateId.text"));return id;}
        void AddWarning(string text){Warning=String.IsNullOrEmpty(Warning)?text:Warning+"\n"+text;}
        static bool Recoverable(Exception e){return e is IOException||e is UnauthorizedAccessException||e is ArgumentException||e is InvalidOperationException;}
        static bool IsWithin(string root,string path){root=Path.GetFullPath(root).TrimEnd('\\','/');path=Path.GetFullPath(path).TrimEnd('\\','/');return String.Equals(root,path,StringComparison.OrdinalIgnoreCase)||path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
        static string Relative(string root,string path){return path.Substring(root.Length+1);}

        static void Scan(string root,string current,int depth,List<string> files,List<string> folders,ref long bytes,ref int entries)
        {
            if(depth>MaxDepth)throw new InvalidOperationException(TextResources.Get("managedSkillLibrary.scan.text"));AgentInstructions.RequireWithin(root,current);
            foreach(string child in System.IO.Directory.EnumerateFileSystemEntries(current)){
                if(++entries>MaxEntries)throw new InvalidOperationException(TextResources.Get("managedSkillLibrary.scan.text.alternativeA"));AgentInstructions.RequireWithin(root,child);var attributes=File.GetAttributes(child);
                if((attributes&FileAttributes.ReparsePoint)!=0)throw new UnauthorizedAccessException(TextResources.Get("managedSkillLibrary.scan.text.alternativeB"));
                if((attributes&FileAttributes.Directory)!=0){folders.Add(child);Scan(root,child,depth+1,files,folders,ref bytes,ref entries);}
                else{files.Add(child);bytes+=new FileInfo(child).Length;if(files.Count>MaxFiles||bytes>MaxBytes)throw new InvalidOperationException(TextResources.Get("managedSkillLibrary.scan.text.alternativeC"));}
            }
        }

        static void CopyBounded(string source,string target,ref long copied)
        {
            AgentInstructions.RejectLinks(source);using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                byte[] buffer=new byte[81920];int read;while((read=input.Read(buffer,0,buffer.Length))>0){copied+=read;if(copied>MaxBytes)throw new InvalidOperationException(TextResources.Get("managedSkillLibrary.copyBounded.text"));output.Write(buffer,0,read);}output.Flush(true);
            }
        }

        void WriteRegistry(Registry state)
        {
            System.IO.Directory.CreateDirectory(directory);AgentInstructions.RejectLinks(directory);string path=Path.Combine(directory,"registry.json");AgentInstructions.RequireWithin(directory,path);
            string temporary=Path.Combine(directory,".registry-"+Guid.NewGuid().ToString("N")+".tmp");AgentInstructions.RequireWithin(directory,temporary);
            byte[] content=new UTF8Encoding(false).GetBytes(new JavaScriptSerializer{MaxJsonLength=1024*1024}.Serialize(state));
            if(content.Length>1024*1024)throw new InvalidOperationException(TextResources.Get("managedSkillLibrary.writeRegistry.text"));
            using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(content,0,content.Length);file.Flush(true);}
            AgentInstructions.RejectLinks(path);
            if(File.Exists(path)){string backup=Path.Combine(directory,".registry-"+Guid.NewGuid().ToString("N")+".bak");AgentInstructions.RequireWithin(directory,backup);File.Replace(temporary,path,backup);}else File.Move(temporary,path);
        }
        void NotifyChanged(){var changed=Changed;if(changed!=null)changed();}
    }
}
