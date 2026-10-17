using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class AgentChangePreviewTests
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        static string Call(AgentWorkspace w,string tool,object args){return w.ExecuteAsync(tool,new JavaScriptSerializer().Serialize(args),CancellationToken.None).GetAwaiter().GetResult();}
        public static int Main()
        {
            try
            {
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var w=new AgentWorkspace(root);
                Call(w,"write_file",new{path="edit.txt",content="a\nb\nc"});Call(w,"write_file",new{path="edit.txt",content="a\nx\nc"});
                var preview=w.PreviewChange("edit.txt",CancellationToken.None);
                Check(preview.AddedLines==1 && preview.RemovedLines==1 && !preview.Unknown,"AgentChangePreview exposes exact diff counts");
                Check(preview.Diff.Any(l=>l.Kind=="removed" && l.Text=="b") && preview.Diff.Any(l=>l.Kind=="added" && l.Text=="x"),"AgentChangePreview exposes line-level added and removed rows");
                File.WriteAllText(Path.Combine(root,"edit.txt"),"external",new UTF8Encoding(false));var changed=w.PreviewChange("edit.txt",CancellationToken.None);
                Check(changed.ChangedAgain && changed.Diff.Count>0,"external modification is flagged without losing diff preview");
                Console.WriteLine("Agent change preview tests: "+checks+" passed");return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
    }
}
