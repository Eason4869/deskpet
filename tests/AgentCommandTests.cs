using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
namespace Dafeiyu.Tests
{
    public static class AgentCommandTests
    {
        static int n;
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);n++;Console.WriteLine("PASS "+name);}
        static CommandResult Run(string root,int timeout,CancellationToken token,params string[] args){return WindowsCommandRunner.Run(new AgentCommand{Id=Guid.NewGuid().ToString("N"),Executable=Assembly.GetExecutingAssembly().Location,Workspace=root,Arguments=args},timeout,token);}
        static CommandResult RunLabelled(string root,int timeout,CancellationToken token,params string[] args){return WindowsCommandRunner.Run(new AgentCommand{Id=Guid.NewGuid().ToString("N"),Executable=Assembly.GetExecutingAssembly().Location,Workspace=root,Arguments=args},timeout,token,true);}
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
                var labelled=RunLabelled(root,5,CancellationToken.None,"--fixture","labelled");Check(labelled.ExitCode==7&&labelled.Output.Contains("[stdout] 中文测试"),"labelled stdout preserves Unicode across pipe reads");Check(labelled.Output.Contains("[stderr] 错误诊断"),"labelled stderr preserves Unicode across pipe reads");Check(labelled.Output.Contains("[stdout] 未换行"),"labelled output flushes final line without newline");
                var longLine=RunLabelled(root,5,CancellationToken.None,"--fixture","long-line");Check(longLine.Truncated&&longLine.Output.Length<=256*1024,"labelled output bounds a long unterminated line and capture");
                string partialReady=Path.Combine(root,"partial.ready");using(var cancel=new CancellationTokenSource()){var stopping=System.Threading.Tasks.Task.Run(delegate{for(int i=0;i<500&&!File.Exists(partialReady);i++)Thread.Sleep(10);cancel.Cancel();});var partial=RunLabelled(root,10,cancel.Token,"--fixture","partial-wait",partialReady);stopping.GetAwaiter().GetResult();Check(File.Exists(partialReady)&&partial.Cancelled&&partial.Output.Contains("[stdout] 未换行"),"cancelled labelled process flushes buffered partial line");}
                var partialTimeout=RunLabelled(root,5,CancellationToken.None,"--fixture","partial-wait",Path.Combine(root,"partial-timeout.ready"));Check(partialTimeout.TimedOut&&partialTimeout.Output.Contains("[stdout] 未换行"),"timed out labelled process flushes buffered partial line");
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
            if(mode=="labelled"){Console.OutputEncoding=new UTF8Encoding(false);Console.Write("中文");Thread.Sleep(250);Console.WriteLine("测试");Console.Error.Write("错误");Thread.Sleep(250);Console.Error.WriteLine("诊断");Console.Write("未换行");return 7;}
            if(mode=="long-line"){Console.Write(new string('x',400000));return 0;}
            if(mode=="partial-wait"){Console.OutputEncoding=new UTF8Encoding(false);Console.Write("未换行");File.WriteAllText(args[2],"ready");Thread.Sleep(15000);return 0;}
            if(mode=="flood"){for(int i=0;i<2000;i++)Console.WriteLine(new string('x',1024));return 0;}
            if(mode=="args"){Console.WriteLine(String.Join("|",args));return 0;}
            if(mode=="background"){var bg=Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--fixture wait"){UseShellExecute=false,CreateNoWindow=true});File.WriteAllText(args[2],bg.Id.ToString());bg.Dispose();return 0;}
            if(mode=="child"){var child=Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--fixture wait"){UseShellExecute=false,CreateNoWindow=true});File.WriteAllText(args[2],child.Id.ToString());child.Dispose();}
            Thread.Sleep(15000);return 0;
        }
    }
}
