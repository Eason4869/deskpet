using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    [DataContract] public sealed class AgentChange
    {
        [DataMember] public string Path,BeforeHash,AfterHash,BackupPath,Status;
        [DataMember] public int? AddedLines,RemovedLines;
        public AgentChange Copy(){return (AgentChange)MemberwiseClone();}
    }
    public sealed class AgentChangePreview
    {
        public string Path,Before,Current;public bool ChangedAgain,Truncated,Unknown;public int? AddedLines,RemovedLines;public List<AgentDiffLine> Diff=new List<AgentDiffLine>();
    }
    public sealed partial class AgentWorkspace
    {
        readonly Dictionary<string,string> observed=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        readonly List<AgentChange> changes=new List<AgentChange>();
        public IList<AgentChange> Changes {get{lock(changes)return changes.Select(c=>c.Copy()).ToList().AsReadOnly();}}
        static string ContentHash(byte[] data){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
        void RememberRead(string path,byte[] bytes){if(!observed.ContainsKey(path))observed[path]=ContentHash(bytes);}
        void CheckExpected(string path,byte[] bytes)
        {
            string expected;if(observed.TryGetValue(path,out expected) && expected!=ContentHash(bytes))throw new ArgumentException("文件在读取后已被外部修改："+Relative(path)+"。请重新读取并核对后重试。");
        }
        void EnsureChangeCapacity(IEnumerable<string> paths){lock(changes){int additional=paths.Distinct(StringComparer.OrdinalIgnoreCase).Count(path=>!changes.Any(c=>c.Status!="restored" && String.Equals(c.Path,path,StringComparison.OrdinalIgnoreCase)));if(changes.Count+additional>256)throw new ArgumentException("本任务已达到 256 个文件变更记录上限，请完成或清除任务记录后再开始新任务。已有恢复记录已保留。");}}
        void RecordChange(string path,string before,string after,string backup,string beforeText,string afterText,CancellationToken token)
        {
            lock(changes){var change=changes.FirstOrDefault(c=>String.Equals(c.Path,path,StringComparison.OrdinalIgnoreCase) && c.Status!="restored");
                if(change==null){change=new AgentChange{Path=path,BeforeHash=before,BackupPath=backup};changes.Add(change);}
                // The original baseline never moves. A backup captured after a
                // task-created file's first write is generated output, not a
                // pre-task original, and must not become an undo target.
                else if(change.BackupPath==null && backup!=null && change.BeforeHash!=null && change.BeforeHash==before)change.BackupPath=backup;
                change.AfterHash=after;change.Status="modified";
                bool known=true;if(change.BeforeHash==null)beforeText="";else if(change.BackupPath!=null){try{byte[] original=ReadBytes(change.BackupPath,CancellationToken.None);if(ContentHash(original)!=change.BeforeHash)known=false;else beforeText=DecodeText(original);}catch(Exception e){if(!(e is IOException || e is UnauthorizedAccessException || e is ArgumentException))throw;known=false;}}else if(change.BeforeHash!=before)known=false;
                int? add=null,remove=null;if(known)AgentLineChanges.Count(beforeText,afterText,out add,out remove);change.AddedLines=add;change.RemovedLines=remove;
            }
            observed[path]=after;
        }
        string ApplyPatch(Dictionary<string,object> args,CancellationToken token)
        {
            object raw;if(!args.TryGetValue("edits",out raw) || !(raw is object[]))throw new ArgumentException("edits 必须是文件编辑数组。");
            var edits=(object[])raw;if(edits.Length<1 || edits.Length>20)throw new ArgumentException("一次补丁必须包含 1～20 个文件。");
            var prepared=new List<KeyValuePair<string,string>>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(object item in edits){
                token.ThrowIfCancellationRequested();var edit=item as Dictionary<string,object>;if(edit==null)throw new ArgumentException("编辑项目必须是对象。");
                string path=Resolve(GetString(edit,"path",null));if(!seen.Add(path))throw new ArgumentException("补丁不能重复编辑同一文件。");
                string parent=Path.GetDirectoryName(path);while(!Directory.Exists(parent)){if(File.Exists(parent))throw new ArgumentException("补丁父路径是文件："+Relative(parent));token.ThrowIfCancellationRequested();parent=Path.GetDirectoryName(parent);if(parent==null)throw new DirectoryNotFoundException();}
                using(LockDirectories(parent,false)){
                    string content;
                    if(edit.ContainsKey("content")){
                        if(File.Exists(path) || Directory.Exists(path))throw new ArgumentException("新建目标已存在，请使用 old_text 精确修改。");content=GetString(edit,"content",null);observed[path]=null;
                    }else{
                        if(DocumentFormats.IsDocument(path))throw new ArgumentException("Word/Excel 文档不支持 old_text 精确修改；请用 write_file 以文字完整重写："+Relative(path));
                        byte[] bytes=ReadBytes(path,token);CheckExpected(path,bytes);RememberRead(path,bytes);content=DecodeText(bytes);
                        string old=GetString(edit,"old_text",null),replacement=GetString(edit,"new_text",null);
                        if(old.Length==0)throw new ArgumentException("old_text 不能为空。");int start=content.IndexOf(old,StringComparison.Ordinal);
                        if(start<0 || content.IndexOf(old,start+old.Length,StringComparison.Ordinal)>=0)throw new ArgumentException("补丁匹配失败或不唯一："+Relative(path));
                        content=content.Substring(0,start)+replacement+content.Substring(start+old.Length);
                    }
                    if(Utf8.GetByteCount(content)>MaxFileBytes || content.IndexOf('\0')>=0)throw new ArgumentException("补丁结果不是受支持的 UTF-8 文本。");
                    prepared.Add(new KeyValuePair<string,string>(path,content));
                }
            }
            foreach(var file in prepared)if(prepared.Any(other=>!String.Equals(file.Key,other.Key,StringComparison.OrdinalIgnoreCase) && other.Key.StartsWith(file.Key+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("补丁文件路径与另一个文件的父目录冲突。");
            EnsureChangeCapacity(prepared.Select(p=>p.Key));
            int applied=0;var output=new StringBuilder();
            foreach(var file in prepared){token.ThrowIfCancellationRequested();try{output.AppendLine(WriteFile(file.Key,file.Value,token));applied++;}
                catch(Exception e){if(!(e is IOException || e is UnauthorizedAccessException || e is ArgumentException))throw;return "错误：补丁部分完成（"+applied+"/"+prepared.Count+"）。已完成变更及备份已记录；请读取文件核对后继续。";}
            }
            return Clip(output.ToString());
        }
        string RestoreFile(string path,CancellationToken token)
        {
            AgentChange change;lock(changes)change=changes.LastOrDefault(c=>String.Equals(c.Path,path,StringComparison.OrdinalIgnoreCase) && c.Status=="modified");
            if(change==null || String.IsNullOrEmpty(change.BackupPath))throw new ArgumentException("此文件没有可恢复的原文件备份。");
            using(LockDirectories(Path.GetDirectoryName(path),false))using(LockDirectories(Path.GetDirectoryName(change.BackupPath),false)){
                byte[] current=ReadBytes(path,token);if(ContentHash(current)!=change.AfterHash)throw new ArgumentException("文件在任务修改后又发生变化，已拒绝恢复："+Relative(path));
                byte[] backup=ReadBytes(change.BackupPath,CancellationToken.None);if(ContentHash(backup)!=change.BeforeHash)throw new ArgumentException("备份校验失败，原文件保持不变。");
                string restored=null;try{restored=DecodeText(backup);}catch(ArgumentException){}
                WriteFile(path,restored==null?"":restored,token,backup);lock(changes)change.Status="restored";return "已恢复 "+Relative(path)+"。";
            }
        }
        string ListChanges(){return new JavaScriptSerializer().Serialize(Changes.Select(c=>new{path=Relative(c.Path),before=c.BeforeHash,after=c.AfterHash,backup=c.BackupPath,status=c.Status}).ToArray());}
        public AgentChangePreview PreviewChange(string input,CancellationToken token)
        {
            string path=Resolve(input);AgentChange change;lock(changes)change=changes.LastOrDefault(c=>String.Equals(c.Path,path,StringComparison.OrdinalIgnoreCase));if(change==null)throw new ArgumentException("当前任务没有此文件的变更记录。");
            string before="",current="";bool changed=true;
            if(change.BackupPath!=null)using(LockDirectories(Path.GetDirectoryName(change.BackupPath),false))before=DecodeText(ReadBytes(change.BackupPath,CancellationToken.None));
            using(LockDirectories(Path.GetDirectoryName(path),false)){if(File.Exists(path)){byte[] bytes=ReadBytes(path,token);current=DecodeText(bytes);changed=ContentHash(bytes)!=change.AfterHash;}else current="[当前文件不存在]";}
            var diff=AgentDiff.Build(before,current);return new AgentChangePreview{Path=path,Before=before.Length>12000?before.Substring(0,12000):before,Current=current.Length>12000?current.Substring(0,12000):current,Truncated=before.Length>12000 || current.Length>12000 || diff.Truncated,ChangedAgain=changed,AddedLines=diff.AddedLines,RemovedLines=diff.RemovedLines,Unknown=diff.Unknown,Diff=diff.Lines};
        }
    }
}
