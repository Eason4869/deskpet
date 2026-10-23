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
            string expected;if(observed.TryGetValue(path,out expected) && expected!=ContentHash(bytes))throw new ArgumentException(TextResources.Format("agentChanges.checkExpected.template",Relative(path)));
        }
        void EnsureChangeCapacity(IEnumerable<string> paths){lock(changes){int additional=paths.Distinct(StringComparer.OrdinalIgnoreCase).Count(path=>!changes.Any(c=>c.Status!="restored" && String.Equals(c.Path,path,StringComparison.OrdinalIgnoreCase)));if(changes.Count+additional>256)throw new ArgumentException(TextResources.Get("agentChanges.ensureChangeCapacity.text"));}}
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
            object raw;if(!args.TryGetValue("edits",out raw) || !(raw is object[]))throw new ArgumentException(TextResources.Get("agentChanges.applyPatch.text"));
            var edits=(object[])raw;if(edits.Length<1 || edits.Length>20)throw new ArgumentException(TextResources.Get("agentChanges.applyPatch.text.alternativeA"));
            var prepared=new List<KeyValuePair<string,string>>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(object item in edits){
                token.ThrowIfCancellationRequested();var edit=item as Dictionary<string,object>;if(edit==null)throw new ArgumentException(TextResources.Get("agentChanges.applyPatch.text.alternativeB"));
                string path=Resolve(GetString(edit,"path",null));if(!seen.Add(path))throw new ArgumentException(TextResources.Get("agentChanges.applyPatch.text.alternativeC"));
                string parent=Path.GetDirectoryName(path);while(!Directory.Exists(parent)){if(File.Exists(parent))throw new ArgumentException(TextResources.Format("agentChanges.applyPatch.template",Relative(parent)));token.ThrowIfCancellationRequested();parent=Path.GetDirectoryName(parent);if(parent==null)throw new DirectoryNotFoundException();}
                using(LockDirectories(parent,false)){
                    string content;
                    if(edit.ContainsKey("content")){
                        if(File.Exists(path) || Directory.Exists(path))throw new ArgumentException(TextResources.Get("agentChanges.applyPatch.text.alternativeD"));content=GetString(edit,"content",null);observed[path]=null;
                    }else{
                        if(DocumentFormats.IsDocument(path))throw new ArgumentException(TextResources.Format("agentChanges.applyPatch.template.alternativeA",Relative(path)));
                        byte[] bytes=ReadBytes(path,token);CheckExpected(path,bytes);RememberRead(path,bytes);content=DecodeText(bytes);
                        string old=GetString(edit,"old_text",null),replacement=GetString(edit,"new_text",null);
                        if(old.Length==0)throw new ArgumentException(TextResources.Get("agentChanges.applyPatch.text.alternativeE"));int start=content.IndexOf(old,StringComparison.Ordinal);
                        if(start<0 || content.IndexOf(old,start+old.Length,StringComparison.Ordinal)>=0)throw new ArgumentException(TextResources.Format("agentChanges.applyPatch.template.alternativeB",Relative(path)));
                        content=content.Substring(0,start)+replacement+content.Substring(start+old.Length);
                    }
                    if(Utf8.GetByteCount(content)>MaxFileBytes || content.IndexOf('\0')>=0)throw new ArgumentException(TextResources.Get("agentChanges.applyPatch.text.alternativeF"));
                    prepared.Add(new KeyValuePair<string,string>(path,content));
                }
            }
            foreach(var file in prepared)if(prepared.Any(other=>!String.Equals(file.Key,other.Key,StringComparison.OrdinalIgnoreCase) && other.Key.StartsWith(file.Key+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException(TextResources.Get("agentChanges.applyPatch.text.alternativeG"));
            EnsureChangeCapacity(prepared.Select(p=>p.Key));
            int applied=0;var output=new StringBuilder();
            foreach(var file in prepared){token.ThrowIfCancellationRequested();try{output.AppendLine(WriteFile(file.Key,file.Value,token));applied++;}
                catch(Exception e){if(!(e is IOException || e is UnauthorizedAccessException || e is ArgumentException))throw;return TextResources.Format("agentChanges.applyPatch.template.alternativeC",applied,prepared.Count);}
            }
            return Clip(output.ToString());
        }
        string RestoreFile(string path,CancellationToken token)
        {
            AgentChange change;lock(changes)change=changes.LastOrDefault(c=>String.Equals(c.Path,path,StringComparison.OrdinalIgnoreCase) && c.Status=="modified");
            if(change==null || String.IsNullOrEmpty(change.BackupPath))throw new ArgumentException(TextResources.Get("agentChanges.restoreFile.text"));
            using(LockDirectories(Path.GetDirectoryName(path),false))using(LockDirectories(Path.GetDirectoryName(change.BackupPath),false)){
                byte[] current=ReadBytes(path,token);if(ContentHash(current)!=change.AfterHash)throw new ArgumentException(TextResources.Format("agentChanges.restoreFile.template",Relative(path)));
                byte[] backup=ReadBytes(change.BackupPath,CancellationToken.None);if(ContentHash(backup)!=change.BeforeHash)throw new ArgumentException(TextResources.Get("agentChanges.restoreFile.text.alternativeA"));
                string restored=null;try{restored=DecodeText(backup);}catch(ArgumentException){}
                WriteFile(path,restored==null?"":restored,token,backup);lock(changes)change.Status="restored";return TextResources.Format("agentChanges.restoreFile.template.alternativeA",Relative(path));
            }
        }
        string ListChanges(){return new JavaScriptSerializer().Serialize(Changes.Select(c=>new{path=Relative(c.Path),before=c.BeforeHash,after=c.AfterHash,backup=c.BackupPath,status=c.Status}).ToArray());}
        public AgentChangePreview PreviewChange(string input,CancellationToken token)
        {
            string path=Resolve(input);AgentChange change;lock(changes)change=changes.LastOrDefault(c=>String.Equals(c.Path,path,StringComparison.OrdinalIgnoreCase));if(change==null)throw new ArgumentException(TextResources.Get("agentChanges.previewChange.text"));
            string before="",current="";bool changed=true;
            if(change.BackupPath!=null)using(LockDirectories(Path.GetDirectoryName(change.BackupPath),false))before=DecodeText(ReadBytes(change.BackupPath,CancellationToken.None));
            using(LockDirectories(Path.GetDirectoryName(path),false)){if(File.Exists(path)){byte[] bytes=ReadBytes(path,token);current=DecodeText(bytes);changed=ContentHash(bytes)!=change.AfterHash;}else current=TextResources.Get("agentChanges.previewChange.current");}
            var diff=AgentDiff.Build(before,current);return new AgentChangePreview{Path=path,Before=before.Length>12000?before.Substring(0,12000):before,Current=current.Length>12000?current.Substring(0,12000):current,Truncated=before.Length>12000 || current.Length>12000 || diff.Truncated,ChangedAgain=changed,AddedLines=diff.AddedLines,RemovedLines=diff.RemovedLines,Unknown=diff.Unknown,Diff=diff.Lines};
        }
    }
}
