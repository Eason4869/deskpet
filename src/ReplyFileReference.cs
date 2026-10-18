using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
namespace Dafeiyu
{
    // Final-answer references are local paths, not model-input attachments. ZIPs
    // and large deliverables remain on disk and are never uploaded as text.
    internal sealed class ReplyFileReference
    {
        internal readonly string Path;
        internal string Name{get{return System.IO.Path.GetFileName(Path);}}
        internal bool Available{get{return File.Exists(Path);}}
        internal bool Web{get{return new[]{".html",".htm"}.Contains(System.IO.Path.GetExtension(Path).ToLowerInvariant());}}
        internal bool TextFile{get{return new[]{".txt",".md",".html",".htm",".css",".js",".ts",".tsx",".jsx",".json",".xml",".yaml",".yml",".csv",".tsv",".py",".cs",".ps1",".sh",".sql",".log",".ini",".toml",".svg"}.Contains(System.IO.Path.GetExtension(Path).ToLowerInvariant());}}
        ReplyFileReference(string path){Path=path;}
        internal static List<ReplyFileReference> Parse(string markdown,string root=null)
        {
            var result=new List<ReplyFileReference>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var node in Links(MarkdownContent.Parse(markdown??""))){ReplyFileReference file;if(TryCreate(node.Url,root,out file)&&seen.Add(file.Path))result.Add(file);if(result.Count>=32)break;}return result;
        }
        static IEnumerable<MarkdownNode> Links(IEnumerable<MarkdownNode> nodes){foreach(var node in nodes){if(node.Kind=="codeblock"||node.Kind=="code")continue;if(node.Kind=="link"||node.Kind=="image")yield return node;foreach(var child in Links(node.Children))yield return child;}}
        internal static bool TryCreate(string value,string root,out ReplyFileReference file)
        {
            file=null;try{
                string path=(value??"").Trim();if(path.Length==0||path.IndexOf('\0')>=0)return false;Uri uri;if(Uri.TryCreate(path,UriKind.Absolute,out uri)){if(!uri.IsFile||!String.IsNullOrEmpty(uri.Host))return false;path=uri.LocalPath;}
                if(path.StartsWith("\\",StringComparison.Ordinal))return false;
                if(!System.IO.Path.IsPathRooted(path)){if(String.IsNullOrEmpty(root))return false;path=System.IO.Path.Combine(root,path);}
                if(Environment.OSVersion.Platform==PlatformID.Win32NT&&(path.Length<3||!Char.IsLetter(path[0])||path[1]!=':'||path[2]!='\\'&&path[2]!='/'))return false;
                foreach(string part in path.Split('\\','/'))if(part.EndsWith(".",StringComparison.Ordinal)&&part!="."&&part!=".."||part.EndsWith(" ",StringComparison.Ordinal)||part.Equals(".deskpet-agent-kernel",StringComparison.OrdinalIgnoreCase)||part.Equals(".deskpet-agent-backups",StringComparison.OrdinalIgnoreCase))return false;
                path=System.IO.Path.GetFullPath(path);if(path.Substring(Math.Min(2,path.Length)).Contains(":"))return false;
                if(Directory.Exists(path)||System.IO.Path.GetFileName(path).Length==0)return false;file=new ReplyFileReference(path);return true;
            }catch(ArgumentException){return false;}catch(NotSupportedException){return false;}catch(IOException){return false;}
        }
        internal string ReadText()
        {
            if(!TextFile)throw new InvalidOperationException("此文件为二进制格式，不支持复制文字内容。");
            using(var stream=new FileStream(Path,FileMode.Open,FileAccess.Read,FileShare.Read)){if(stream.Length>ChatAttachment.MaximumBytes)throw new InvalidOperationException("文件过大，请使用打开文件或另存为。");using(var reader=new StreamReader(stream,new UTF8Encoding(false,true),true)){string text=reader.ReadToEnd();if(text.IndexOf('\0')>=0)throw new InvalidOperationException("此文件包含二进制内容。");return text;}}
        }
        internal void SaveTo(string destination){if(String.Equals(System.IO.Path.GetFullPath(destination),Path,StringComparison.OrdinalIgnoreCase))return;File.Copy(Path,destination,true);}
    }
}
