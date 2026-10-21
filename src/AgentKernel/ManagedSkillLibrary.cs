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
            if(String.IsNullOrWhiteSpace(directory))throw new ArgumentException("请选择技能库目录。", "directory");
            this.directory=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
            if(this.directory.Length==0||String.Equals(this.directory,Path.GetPathRoot(this.directory).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("技能库不能使用磁盘根目录。");
            AgentInstructions.RejectLinks(this.directory);Warning="";
        }

        public IList<ManagedSkillInfo> List()
        {
            lock(Gate){var state=ReadRegistry();if(state==null)return new ManagedSkillInfo[0];var items=new List<ManagedSkillInfo>();
                foreach(var entry in state.Skills){try{items.Add(ReadInfo(entry));}catch(Exception e){if(!Recoverable(e))throw;AddWarning("技能不可用："+entry.Id+"。"+e.Message);}}
                return items.OrderBy(s=>s.Name,StringComparer.Ordinal).ToArray();}
        }

        public ManagedSkillInfo Import(string sourceDirectory,SkillOrigin origin=null,string expectedContentHash=null)
        {
            ManagedSkillInfo result;
            lock(Gate){var state=RequireRegistry();if(state.Skills.Count>=128)throw new InvalidOperationException("技能库超过 128 项。");
                if(String.IsNullOrWhiteSpace(sourceDirectory))throw new ArgumentException("请选择包含 SKILL.md 的文件夹。");
                string source=Path.GetFullPath(sourceDirectory).TrimEnd('\\','/');AgentInstructions.RejectLinks(source);
                if(IsWithin(directory,source)||IsWithin(source,directory))throw new UnauthorizedAccessException("导入源不能包含技能库，也不能位于技能库内。");
                if(!System.IO.Directory.Exists(source))throw new ArgumentException("技能源文件夹不存在。");
                var info=AgentSkills.ReadInfo(source);string id=ValidateId(info.Name),destination=SkillDirectory(id);
                if(state.Skills.Any(s=>s.Id==id)||System.IO.Directory.Exists(destination)||File.Exists(destination))throw new InvalidOperationException("已存在同名技能："+id+"。请先移除已有副本。");
                // Walk before creating a destination so a rejected import leaves the source untouched.
                var files=new List<string>();var folders=new List<string>();long bytes=0;int entries=0;Scan(source,source,0,files,folders,ref bytes,ref entries);
                System.IO.Directory.CreateDirectory(directory);AgentInstructions.RejectLinks(directory);
                string staging=Path.Combine(directory,".import-"+Guid.NewGuid().ToString("N"));AgentInstructions.RequireWithin(directory,staging);System.IO.Directory.CreateDirectory(staging);
                foreach(string folder in folders){string target=Path.Combine(staging,Relative(source,folder));AgentInstructions.RequireWithin(staging,target);System.IO.Directory.CreateDirectory(target);}
                long copied=0;
                foreach(string file in files){AgentInstructions.RequireWithin(source,file);string target=Path.Combine(staging,Relative(source,file));AgentInstructions.RequireWithin(staging,target);CopyBounded(file,target,ref copied);}
                var copiedInfo=AgentSkills.ReadInfo(staging);if(copiedInfo.Name!=id)throw new InvalidOperationException("导入时技能名称发生变化，请重试。");
                if(expectedContentHash!=null&&AgentInstructions.Read(Path.Combine(staging,"SKILL.md")).Hash!=expectedContentHash)throw new InvalidOperationException("技能正文与预览不一致，请重新预览后导入。");
                AgentInstructions.RequireWithin(directory,destination);System.IO.Directory.Move(staging,destination);
                var entry=new RegistryEntry{Id=id,Enabled=true,Origin=origin};state.Skills.Add(entry);
                try{WriteRegistry(state);}
                catch(Exception registryError){
                    // Promotion is not a successful import until the registry commits. Keep the
                    // owned copy recoverable, but release its public name so the user can retry.
                    try{AgentInstructions.RequireWithin(directory,destination);string quarantine=Path.Combine(directory,".failed-import-"+id+"-"+Guid.NewGuid().ToString("N"));AgentInstructions.RequireWithin(directory,quarantine);System.IO.Directory.Move(destination,quarantine);}
                    catch(Exception quarantineError){throw new IOException("技能登记失败，导入副本无法隔离，仍保留于："+destination+"。请检查目录占用或权限后重试。",new AggregateException(registryError,quarantineError));}
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
                        catch(Exception restoreError){throw new IOException("技能移除未完成，登记保留，副本无法还原，仍保留于："+quarantine+"。请检查目录占用或权限。",new AggregateException(registryError,restoreError));}}
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
                if(state==null||state.Version!=1||state.Skills==null||state.Skills.Count>128)throw new InvalidOperationException("技能库登记格式无效。");
                var ids=new HashSet<string>(StringComparer.Ordinal);foreach(var entry in state.Skills){if(entry==null||!ids.Add(ValidateId(entry.Id)))throw new InvalidOperationException("技能库登记存在重复项。");}return state;
            }catch(Exception e){if(!Recoverable(e))throw;Warning="技能库登记无法读取，已暂停加载。原文件已保留："+e.Message;return null;}
        }

        Registry RequireRegistry(){var state=ReadRegistry();if(state==null)throw new InvalidOperationException(Warning);return state;}
        RegistryEntry Find(Registry state,string id){var entry=state.Skills.FirstOrDefault(s=>s.Id==id);if(entry==null)throw new ArgumentException("未安装该技能。");return entry;}
        ManagedSkillInfo ReadInfo(RegistryEntry entry)
        {
            string path=SkillDirectory(entry.Id);AgentInstructions.RequireWithin(directory,path);var info=AgentSkills.ReadInfo(path);
            if(info.Name!=entry.Id)throw new InvalidOperationException("技能名称与登记不一致。");return new ManagedSkillInfo{Id=entry.Id,Name=info.Name,Description=info.Description,Directory=path,Enabled=entry.Enabled,Origin=entry.Origin};
        }
        string SkillDirectory(string id){ValidateId(id);string path=Path.Combine(directory,id);AgentInstructions.RequireWithin(directory,path);return path;}
        static string ValidateId(string id){if(id==null||!Regex.IsMatch(id,@"\A[a-z0-9][a-z0-9_-]{0,63}\z"))throw new ArgumentException("技能名称须为 1–64 位小写字母、数字、下划线或连字符。");return id;}
        void AddWarning(string text){Warning=String.IsNullOrEmpty(Warning)?text:Warning+"\n"+text;}
        static bool Recoverable(Exception e){return e is IOException||e is UnauthorizedAccessException||e is ArgumentException||e is InvalidOperationException;}
        static bool IsWithin(string root,string path){root=Path.GetFullPath(root).TrimEnd('\\','/');path=Path.GetFullPath(path).TrimEnd('\\','/');return String.Equals(root,path,StringComparison.OrdinalIgnoreCase)||path.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
        static string Relative(string root,string path){return path.Substring(root.Length+1);}

        static void Scan(string root,string current,int depth,List<string> files,List<string> folders,ref long bytes,ref int entries)
        {
            if(depth>MaxDepth)throw new InvalidOperationException("技能资源层级超过 64 层。");AgentInstructions.RequireWithin(root,current);
            foreach(string child in System.IO.Directory.EnumerateFileSystemEntries(current)){
                if(++entries>MaxEntries)throw new InvalidOperationException("技能资源条目超过 4096 项。");AgentInstructions.RequireWithin(root,child);var attributes=File.GetAttributes(child);
                if((attributes&FileAttributes.ReparsePoint)!=0)throw new UnauthorizedAccessException("技能资源不能包含链接。");
                if((attributes&FileAttributes.Directory)!=0){folders.Add(child);Scan(root,child,depth+1,files,folders,ref bytes,ref entries);}
                else{files.Add(child);bytes+=new FileInfo(child).Length;if(files.Count>MaxFiles||bytes>MaxBytes)throw new InvalidOperationException("技能资源超过 2048 个文件或 64 MiB。");}
            }
        }

        static void CopyBounded(string source,string target,ref long copied)
        {
            AgentInstructions.RejectLinks(source);using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                byte[] buffer=new byte[81920];int read;while((read=input.Read(buffer,0,buffer.Length))>0){copied+=read;if(copied>MaxBytes)throw new InvalidOperationException("技能资源超过 64 MiB。");output.Write(buffer,0,read);}output.Flush(true);
            }
        }

        void WriteRegistry(Registry state)
        {
            System.IO.Directory.CreateDirectory(directory);AgentInstructions.RejectLinks(directory);string path=Path.Combine(directory,"registry.json");AgentInstructions.RequireWithin(directory,path);
            string temporary=Path.Combine(directory,".registry-"+Guid.NewGuid().ToString("N")+".tmp");AgentInstructions.RequireWithin(directory,temporary);
            byte[] content=new UTF8Encoding(false).GetBytes(new JavaScriptSerializer{MaxJsonLength=1024*1024}.Serialize(state));
            if(content.Length>1024*1024)throw new InvalidOperationException("技能库登记超过大小限制。");
            using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(content,0,content.Length);file.Flush(true);}
            AgentInstructions.RejectLinks(path);
            if(File.Exists(path)){string backup=Path.Combine(directory,".registry-"+Guid.NewGuid().ToString("N")+".bak");AgentInstructions.RequireWithin(directory,backup);File.Replace(temporary,path,backup);}else File.Move(temporary,path);
        }
        void NotifyChanged(){var changed=Changed;if(changed!=null)changed();}
    }
}
