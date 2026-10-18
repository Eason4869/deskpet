using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace Dafeiyu
{
    public sealed class AgentInstructionSource {public string Path,Hash,Content;}
    public static class AgentInstructions
    {
        public static IList<AgentInstructionSource> Load(string projectRoot,string directory,bool trusted,string globalPath="")
        {
            var result=new List<AgentInstructionSource>();if(!trusted)return result;
            string root=System.IO.Path.GetFullPath(projectRoot),target=System.IO.Path.GetFullPath(directory??projectRoot);RequireWithin(root,target);
            var directories=new List<string>();for(var current=new DirectoryInfo(target);current!=null;current=current.Parent){RequireWithin(root,current.FullName);directories.Add(current.FullName);if(String.Equals(current.FullName.TrimEnd('\\','/'),root.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase))break;}directories.Reverse();
            if(!String.IsNullOrWhiteSpace(globalPath))result.Add(Read(globalPath));foreach(string item in directories){string file=System.IO.Path.Combine(item,"AGENTS.md");if(File.Exists(file))result.Add(Read(file));}
            int total=0;foreach(var item in result){total+=Encoding.UTF8.GetByteCount(item.Content);if(total>128*1024)throw new InvalidOperationException("项目说明总量超过 128 KiB，请缩小范围。");}return result;
        }
        internal static AgentInstructionSource Read(string path)
        {
            path=System.IO.Path.GetFullPath(path);RejectLinks(path);byte[] bytes;using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){if(file.Length>64*1024)throw new InvalidOperationException("说明或技能超过 64 KiB："+path);bytes=new byte[(int)file.Length];int n=0;while(n<bytes.Length){int read=file.Read(bytes,n,bytes.Length-n);if(read==0)throw new EndOfStreamException();n+=read;}}
            using(var hash=SHA256.Create())return new AgentInstructionSource{Path=path,Hash=BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(),Content=new UTF8Encoding(false,true).GetString(bytes).TrimStart('\ufeff')};
        }
        internal static void RequireWithin(string root,string path){root=System.IO.Path.GetFullPath(root).TrimEnd('\\','/');path=System.IO.Path.GetFullPath(path);if(!String.Equals(path.TrimEnd('\\','/'),root,StringComparison.OrdinalIgnoreCase)&&!path.StartsWith(root+System.IO.Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("说明或技能路径超出获准目录。");RejectLinks(path);}
        internal static void RejectLinks(string path){for(var current=new DirectoryInfo(path);current!=null;current=current.Parent)if((File.Exists(current.FullName)||Directory.Exists(current.FullName))&&(File.GetAttributes(current.FullName)&FileAttributes.ReparsePoint)!=0)throw new UnauthorizedAccessException("说明与技能不能穿过符号链接。");}
    }
}
