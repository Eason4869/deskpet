using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
namespace Dafeiyu.Tests
{
    public static class AgentCommandTests
    {
        static int n;
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);n++;Console.WriteLine("PASS "+name);}
        static CommandResult Run(string root,int timeout,CancellationToken token,params string[] args){return WindowsCommandRunner.Run(new AgentCommand{Id=Guid.NewGuid().ToString("N"),Executable=Assembly.GetExecutingAssembly().Location,Workspace=root,Arguments=args},timeout,token);}
        static bool Gone(int pid){try{using(var process=Process.GetProcessById(pid))return process.HasExited;}catch(ArgumentException){return true;}}
        public static int Main(string[] args)
        {
            if(args.Length>0 && args[0]=="--fixture")return Fixture(args);
            try{
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"command-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                Check(typeof(AgentWorkspace).GetMethod("ConfigureCommands")==null,"retired recipe execution API is absent");
                var cfg=new PetSettings{AgentCommandsEnabled=true,AgentCommandRecipes="invalid legacy recipe"};cfg.Normalize();Check(!cfg.AgentCommandsEnabled&&cfg.AgentCommandRecipes=="[]","legacy grants are ignored without parsing recipes");
                var ok=Run(root,5,CancellationToken.None,"--fixture","ok");Check(ok.ExitCode==0&&ok.Output.Contains("fixture-output"),"native runner preserves exit status and output");
                var fail=Run(root,5,CancellationToken.None,"--fixture","fail");Check(fail.ExitCode==7&&fail.Output.Contains("fixture-error"),"native runner preserves nonzero diagnostics");
                var flood=Run(root,5,CancellationToken.None,"--fixture","flood");Check(flood.Truncated&&flood.Output.Length<1024*1024,"native runner bounds output flood");
                var timeout=Run(root,1,CancellationToken.None,"--fixture","wait");Check(timeout.TimedOut,"native runner enforces timeout");
                string child=Path.Combine(root,"child.pid");using(var cancel=new CancellationTokenSource()){cancel.CancelAfter(700);var stopped=Run(root,10,cancel.Token,"--fixture","child",child);Check(stopped.Cancelled,"native runner records cancellation");Check(File.Exists(child)&&Gone(Int32.Parse(File.ReadAllText(child))),"cancellation reaps descendants");}
                var arguments=Run(root,5,CancellationToken.None,"--fixture","args","a b","quote\"inside","tail\\","$(literal);&");Check(arguments.Output=="--fixture|args|a b|quote\"inside|tail\\|$(literal);&"+Environment.NewLine,"argument boundaries and metacharacters stay literal");
                Run(root,5,CancellationToken.None,"--fixture","background",child);Check(Gone(Int32.Parse(File.ReadAllText(child))),"normal exit reaps background descendants");
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
