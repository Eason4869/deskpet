using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
namespace Dafeiyu
{
    public sealed partial class AgentWorkspace
    {
        static readonly HashSet<string> IgnoredDirectories=new HashSet<string>(StringComparer.OrdinalIgnoreCase){".git",".svn","node_modules","bin","obj","dist","build",".venv","venv","__pycache__",BackupFolder};
        string ListPage(string path,int offset,int limit,CancellationToken token)
        {
            using(LockDirectories(path,false)){
                var entries=Directory.EnumerateFileSystemEntries(path).Where(p=>Path.GetFileName(p)!=BackupFolder && (!RestrictToRoot || !Path.GetFileName(p).Equals(".git",StringComparison.OrdinalIgnoreCase))).OrderBy(p=>Path.GetFileName(p),StringComparer.OrdinalIgnoreCase).ToArray();
                var output=new StringBuilder();foreach(string p in entries.Skip(offset).Take(limit)){
                    token.ThrowIfCancellationRequested();var a=File.GetAttributes(p);output.Append((a&FileAttributes.ReparsePoint)!=0?"[跳过链接] ":(a&FileAttributes.Directory)!=0?"[目录] ":"[文件] ").AppendLine(Path.GetFileName(p));
                }
                if(offset+limit<entries.Length)output.AppendLine("[已截断：next_offset="+(offset+limit)+"]");
                return output.Length==0?"目录为空，或 offset 已超过末尾。":Clip(output.ToString());
            }
        }
        static Regex FilePattern(string pattern)
        {
            if(String.IsNullOrEmpty(pattern) || pattern.Length>200 || pattern.Any(Char.IsControl))throw new ArgumentException("pattern 应为 1～200 个字符的文件路径模式。");
            string value=pattern.Replace('\\','/');var regex=new StringBuilder("^");
            for(int i=0;i<value.Length;i++){
                if(value[i]=='*' && i+1<value.Length && value[i+1]=='*'){
                    i++;if(i+1<value.Length && value[i+1]=='/'){i++;regex.Append("(?:.*/)?");}else regex.Append(".*");
                }else if(value[i]=='*')regex.Append("[^/]*");else if(value[i]=='?')regex.Append("[^/]");else regex.Append(Regex.Escape(value[i].ToString()));
            }
            regex.Append('$');return new Regex(regex.ToString(),RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        }
        IEnumerable<string> Discover(string path,CancellationToken token)
        {
            var dirs=new Queue<KeyValuePair<string,int>>();dirs.Enqueue(new KeyValuePair<string,int>(path,0));int seen=0;
            while(dirs.Count>0){var next=dirs.Dequeue();using(LockDirectories(next.Key,false)){
                foreach(string entry in Directory.EnumerateFileSystemEntries(next.Key).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase)){
                    token.ThrowIfCancellationRequested();if(++seen>1500)yield break;var attr=File.GetAttributes(entry);if((attr&FileAttributes.ReparsePoint)!=0)continue;
                    if((attr&FileAttributes.Directory)!=0){if(next.Value<12 && !IgnoredDirectories.Contains(Path.GetFileName(entry)))dirs.Enqueue(new KeyValuePair<string,int>(entry,next.Value+1));}
                    else yield return entry;
                }
            }}
        }
        string FindFiles(string path,string pattern,int offset,int limit,CancellationToken token)
        {
            var match=FilePattern(pattern);var files=Discover(path,token).Where(p=>match.IsMatch(Relative(p))).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).ToList();
            var output=new StringBuilder();foreach(string p in files.Skip(offset).Take(limit))output.AppendLine(Relative(p));
            if(offset+limit<files.Count)output.AppendLine("[已截断：next_offset="+(offset+limit)+"]");
            output.AppendLine("[匹配 "+files.Count+" 项；扫描最多 1500 项，目录深度最多 12 层]");return Clip(output.ToString());
        }
        string ReadFiles(Dictionary<string,object> args,CancellationToken token)
        {
            object value;if(!args.TryGetValue("paths",out value) || !(value is object[]))throw new ArgumentException("paths 必须是文件路径数组。");
            var paths=(object[])value;if(paths.Length<1 || paths.Length>8 || paths.Any(p=>!(p is string)))throw new ArgumentException("一次读取 1～8 个文件。");
            var resolved=paths.Select(p=>Resolve((string)p)).ToArray();int offset=GetInt(args,"offset",0,0,1000000),limit=GetInt(args,"limit",100,1,1000);
            var output=new StringBuilder();foreach(string p in resolved){token.ThrowIfCancellationRequested();string content=ReadFile(p,offset,limit,token);if(output.Length+content.Length>MaxResultChars-300){output.AppendLine("[批量输出已截断；请对 "+Relative(p)+" 使用 read_file 分段读取]");break;}output.AppendLine("--- "+Relative(p)+" ---").AppendLine(content);}
            return Clip(output.ToString());
        }
    }
}
