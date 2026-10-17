using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class AgentChangesTests
    {
        static int n;static void Check(bool ok,string name){if(!ok)throw new Exception(name);n++;Console.WriteLine("PASS "+name);}
        static string Call(AgentWorkspace w,string tool,object args){return w.ExecuteAsync(tool,new JavaScriptSerializer().Serialize(args),CancellationToken.None).GetAwaiter().GetResult();}
        public static int Main(){try{
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"changes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var w=new AgentWorkspace(root);
            File.WriteAllText(Path.Combine(root,"a.txt"),"alpha",new UTF8Encoding(false));File.WriteAllText(Path.Combine(root,"b.txt"),"beta",new UTF8Encoding(false));
            Call(w,"read_files",new{paths=new[]{"a.txt","b.txt"}});
            var patch=new{edits=new[]{new{path="a.txt",old_text="alpha",new_text="A"},new{path="b.txt",old_text="missing",new_text="B"}}};
            Check(Call(w,"apply_patch",patch).StartsWith("错误：") && File.ReadAllText(Path.Combine(root,"a.txt"))=="alpha","failed multi-file preflight writes nothing");
            string result=Call(w,"apply_patch",new{edits=new[]{new{path="a.txt",old_text="alpha",new_text="A"},new{path="b.txt",old_text="beta",new_text="B"}}});
            Check(!result.StartsWith("错误：") && File.ReadAllText(Path.Combine(root,"a.txt"))=="A" && File.ReadAllText(Path.Combine(root,"b.txt"))=="B","two-file patch updates actual files");
            Check(Call(w,"list_changes",new{}).Contains("a.txt") && Call(w,"list_changes",new{}).Contains("b.txt"),"changes retain source paths and provenance");
            Check(w.Changes.All(c=>c.AddedLines==1 && c.RemovedLines==1),"line counts use real replacements rather than file or character counts");
            File.WriteAllText(Path.Combine(root,"lines.txt"),"same\r\nold\r\nend\r\n");Call(w,"read_file",new{path="lines.txt"});Call(w,"write_file",new{path="lines.txt",content="same\nnew\nextra\nend\n"});Call(w,"write_file",new{path="lines.txt",content="same\nnew\nextra\nthird\nend\n"});
            var lineChange=w.Changes.Single(c=>Path.GetFileName(c.Path)=="lines.txt");Check(lineChange.AddedLines==3 && lineChange.RemovedLines==1,"repeated edits count against original backup and normalize line endings");
            int? added,removed;AgentLineChanges.Count("a\nb\nc","b\na\nc",out added,out removed);Check(added==1 && removed==1,"reordered lines use an actual common subsequence");
            AgentLineChanges.Count(String.Join("\n",Enumerable.Repeat("a",1100)),String.Join("\n",Enumerable.Repeat("b",1100)),out added,out removed);Check(!added.HasValue && !removed.HasValue,"large unrelated edits omit counters rather than inventing a diff");
            File.WriteAllText(Path.Combine(root,"a.txt"),"external",new UTF8Encoding(false));
            Check(Call(w,"write_file",new{path="a.txt",content="overwrite"}).StartsWith("错误：") && File.ReadAllText(Path.Combine(root,"a.txt"))=="external","external edit cannot be silently overwritten");
            Check(Call(w,"restore_file",new{path="a.txt"}).StartsWith("错误：") && File.ReadAllText(Path.Combine(root,"a.txt"))=="external","restore refuses overwriting newer external edits");
            Check(!Call(w,"restore_file",new{path="b.txt"}).StartsWith("错误：") && File.ReadAllText(Path.Combine(root,"b.txt"))=="beta","restore uses original file backup");
            Check(!Call(w,"apply_patch",new{edits=new[]{new{path="nested/new/code.txt",content="created"}}}).StartsWith("错误：") && File.ReadAllText(Path.Combine(root,"nested/new/code.txt"))=="created","patch can create nested files without preflight directory mutations");
            File.WriteAllText(Path.Combine(root,"parent.txt"),"regular file");File.WriteAllText(Path.Combine(root,"preflight.txt"),"unchanged");
            Check(Call(w,"apply_patch",new{edits=new object[]{new{path="preflight.txt",old_text="unchanged",new_text="bad"},new{path="parent.txt/child.txt",content="bad"}}}).StartsWith("错误：") && File.ReadAllText(Path.Combine(root,"preflight.txt"))=="unchanged","regular-file parent is rejected before any patch writes");
            Check(Call(w,"apply_patch",new{edits=new[]{new{path="tree",content="file"},new{path="tree/child.txt",content="child"}}}).StartsWith("错误：") && !File.Exists(Path.Combine(root,"tree")),"conflicting patch ancestor paths fail before creating files");
            var capped=new AgentWorkspace(root);for(int batch=0;batch<16;batch++){var edits=Enumerable.Range(batch*16,16).Select(i=>new{path="cap-"+i+".txt",content="original"}).ToArray();Check(!Call(capped,"apply_patch",new{edits=edits}).StartsWith("错误："),"bounded change batch "+batch);}
            Check(Call(capped,"write_file",new{path="overflow.txt",content="excess"}).StartsWith("错误：") && !File.Exists(Path.Combine(root,"overflow.txt")) && capped.Changes.Count==256,"change limit rejects new mutations while retaining all recovery records");
            w.ReadOnly=true;Check(Call(w,"apply_patch",patch).StartsWith("错误：") && Call(w,"restore_file",new{path="b.txt"}).StartsWith("错误："),"plan mode rejects patch and restore");
            Console.WriteLine("Agent changes tests: "+n+" passed");return 0;
        }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}}
    }
}
