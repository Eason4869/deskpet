using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class AgentCommandTests
    {
        static int n;
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);n++;Console.WriteLine("PASS "+name);}
        static string Call(AgentWorkspace w,string id,CancellationToken token){return w.ExecuteAsync("run_command",new JavaScriptSerializer().Serialize(new{command_id=id}),token).GetAwaiter().GetResult();}
        public static int Main(string[] args)
        {
            if(args.Length>0 && args[0]=="--fixture")return Fixture(args);
            try{
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"command-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string exe=Assembly.GetExecutingAssembly().Location;
                var w=new AgentWorkspace(root);Check(Call(w,"ok",CancellationToken.None).StartsWith("错误："),"unconfigured commands cannot execute");
                var configure=typeof(AgentWorkspace).GetMethod("ConfigureCommands");Check(configure!=null,"project command configuration is available");
                Func<string,string[],object> recipe=(id,argv)=>new{Id=id,Executable=exe,Arguments=argv,Workspace=root};
                string childPid=Path.Combine(root,"child.pid");string json=new JavaScriptSerializer().Serialize(new[]{recipe("ok",new[]{"--fixture","ok"}),recipe("fail",new[]{"--fixture","fail"}),recipe("flood",new[]{"--fixture","flood"}),recipe("wait",new[]{"--fixture","wait"}),recipe("child",new[]{"--fixture","child",childPid}),recipe("background",new[]{"--fixture","background",childPid}),recipe("args",new[]{"--fixture","args","a b","quote\"inside","tail\\","$(literal);&"})});
                configure.Invoke(w,new object[]{true,json});
                var settings=new PetSettings();var enabled=typeof(PetSettings).GetProperty("AgentCommandsEnabled");var recipes=typeof(PetSettings).GetProperty("AgentCommandRecipes");
                Check(enabled!=null && recipes!=null,"command policy is a persistent setting");enabled.SetValue(settings,true,null);recipes.SetValue(settings,json,null);
                var store=new SettingsStore(Path.Combine(root,"settings.json"));store.Save(settings.Copy());var loaded=store.Load();
                Check((bool)enabled.GetValue(loaded,null) && (string)recipes.GetValue(loaded,null)==json,"command recipes survive copy save and reopen");
                string result=Call(w,"ok",CancellationToken.None);Check(result.Contains("fixture-output") && result.Contains("\"ExitCode\":0"),"real successful command reports output and exit zero");
                result=Call(w,"fail",CancellationToken.None);Check(result.StartsWith("错误：") && result.Contains("7") && result.Contains("fixture-error"),"nonzero exit is a tool error with actual diagnostics");
                result=Call(w,"flood",CancellationToken.None);Check(result.Length<24500 && result.Contains("Truncated"),"output flood is bounded without blocking the child");
                typeof(AgentWorkspace).GetProperty("CommandTimeoutSeconds").SetValue(w,1,null);result=Call(w,"wait",CancellationToken.None);Check(result.StartsWith("错误：") && result.Contains("\"TimedOut\":true"),"timeout terminates the command and reports its terminal reason");
                var cancel=new CancellationTokenSource();cancel.CancelAfter(300);bool stopped=false;try{Call(w,"child",cancel.Token);}catch(OperationCanceledException){stopped=true;}
                Check(stopped,"stop cancels a running command");if(File.Exists(childPid)){int pid=Int32.Parse(File.ReadAllText(childPid));Thread.Sleep(150);bool gone=false;try{using(var child=Process.GetProcessById(pid))gone=child.HasExited;}catch(ArgumentException){gone=true;}Check(gone,"stop reaps command descendants");}else throw new Exception("child fixture never started");
                result=Call(w,"args",CancellationToken.None);var argumentResult=(System.Collections.Generic.Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(result);Check((string)argumentResult["Output"]=="--fixture|args|a b|quote\"inside|tail\\|$(literal);&"+Environment.NewLine,"configured argument boundaries and shell metacharacters remain literal");
                Check(w.CommandResults[w.CommandResults.Count-2].Cancelled,"cancelled command retains its actual terminal record");
                result=Call(w,"background",CancellationToken.None);int backgroundPid=Int32.Parse(File.ReadAllText(childPid));bool reaped=false;try{using(var child=Process.GetProcessById(backgroundPid))reaped=child.HasExited;}catch(ArgumentException){reaped=true;}Check(reaped,"normal command exit also reaps background descendants");
                Directory.CreateDirectory(Path.Combine(root,"other"));var other=new AgentWorkspace(Path.Combine(root,"other"));other.ConfigureCommands(true,json);Check(Call(other,"ok",CancellationToken.None).StartsWith("错误："),"command permission does not transfer to another project");
                w.ReadOnly=true;Check(Call(w,"ok",CancellationToken.None).StartsWith("错误："),"plan mode rejects configured commands");
                Console.WriteLine("Agent command tests: "+n+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
        static int Fixture(string[] args)
        {
            string mode=args[1];if(mode=="ok"){Console.WriteLine("fixture-output");return 0;}if(mode=="fail"){Console.Error.WriteLine("fixture-error");return 7;}
            if(mode=="flood"){for(int i=0;i<2000;i++)Console.WriteLine(new string('x',1024));return 0;}
            if(mode=="args"){Console.WriteLine(String.Join("|",args));return 0;}
            if(mode=="background"){var bg=Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--fixture wait"){UseShellExecute=false,CreateNoWindow=true});File.WriteAllText(args[2],bg.Id.ToString());bg.Dispose();return 0;}
            if(mode=="child"){var child=Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--fixture wait"){UseShellExecute=false,CreateNoWindow=true});File.WriteAllText(args[2],child.Id.ToString());child.Dispose();}
            Thread.Sleep(15000);return 0;
        }
    }
}
