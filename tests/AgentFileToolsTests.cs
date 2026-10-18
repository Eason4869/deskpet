using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class AgentFileToolsTests
    {
        static int count;
        static void Check(bool ok,string label){if(!ok)throw new Exception(label);count++;Console.WriteLine("PASS "+label);}
        static string Call(AgentWorkspace w,string tool,object args){return w.ExecuteAsync(tool,new JavaScriptSerializer().Serialize(args),CancellationToken.None).GetAwaiter().GetResult();}
        public static int Main()
        {
            try{
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"file-tools-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                for(int i=0;i<205;i++)File.WriteAllText(Path.Combine(root,"file-"+i.ToString("D3")+".txt"),"item "+i,new UTF8Encoding(false));
                Directory.CreateDirectory(Path.Combine(root,"src"));File.WriteAllText(Path.Combine(root,"src","one.cs"),"hello needle",new UTF8Encoding(false));
                Directory.CreateDirectory(Path.Combine(root,"node_modules"));File.WriteAllText(Path.Combine(root,"node_modules","hidden.cs"),"needle",new UTF8Encoding(false));
                var w=new AgentWorkspace(root);
                string first=Call(w,"list_files",new {limit=200}),last=Call(w,"list_files",new {offset=200,limit=200});
                Check(first.Contains("next_offset=200") && !first.Contains("file-204.txt"),"first directory page has an actionable cursor");
                Check(last.Contains("file-204.txt") && !last.Contains("file-000.txt"),"next directory page reaches entries after the original cap");
                string found=Call(w,"find_files",new {pattern="**/*.cs"});Check(found.Contains("src/one.cs") && !found.Contains("hidden.cs"),"glob search excludes dependency trees");
                Check(Call(w,"search_files",new {query="needle",pattern="**/*.cs"}).Contains("src/one.cs"),"content search accepts a file pattern");
                Check(Call(w,"search_files",new {query="n[e]+dle",regex=true,pattern="**/*.cs"}).Contains("src/one.cs"),"bounded regular expression search matches code text");
                Check(Call(w,"search_files",new {query="[",regex=true}).StartsWith("错误："),"invalid regular expression reports a tool error");
                string batch=Call(w,"read_files",new {paths=new[]{"file-000.txt","src/one.cs"}});Check(batch.Contains("item 0") && batch.Contains("hello needle"),"batch read returns both real files");
                Check(Call(w,"read_files",new {paths=new[]{"../outside.txt"}}).StartsWith("错误："),"batch read preserves traversal protection");
                Check(Call(w,"read_files",new {paths=Enumerable.Repeat("file-000.txt",9).ToArray()}).StartsWith("错误："),"batch size is bounded");
                w.ReadOnly=true;string schemas=new JavaScriptSerializer().Serialize(w.OpenAITools);Check(schemas.Contains("find_files") && schemas.Contains("read_files") && !schemas.Contains("write_file"),"planning exposes discovery but no mutating tools");
                Check(Call(w,"find_files",new {path="../",pattern="*"}).StartsWith("错误："),"find cannot leave the root");
                Console.WriteLine("Agent file tools tests: "+count+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
    }
}
