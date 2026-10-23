using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
namespace Dafeiyu.Tests
{
    public static class PowerShellStartupTests
    {
        static int checks,failures;
        static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        static string Q(string value){return "'"+value.Replace("'","''")+"'";}
        static CommandResult Run(string root,string script,IDictionary<string,string> environment=null)
        {
            var command=new AgentCommand{Executable=AgentCommandExecutor.PowerShellPath(),Workspace=root,Arguments=new[]{"-NoLogo","-NoProfile","-NonInteractive","-OutputFormat","Text","-EncodedCommand",Convert.ToBase64String(Encoding.Unicode.GetBytes(script))}};
            var result=WindowsCommandRunner.Run(command,60,CancellationToken.None,true,null,environment);
            Console.WriteLine("STARTUP PROBE: elapsedMs="+result.ElapsedMilliseconds+" exit="+result.ExitCode+" timeout="+result.TimedOut+" output="+result.Output);
            return result;
        }
        static void Case(string name,Action test){try{test();}catch(Exception error){failures++;Console.Error.WriteLine("FAIL "+name+": "+error);}}
        static void WithoutDiscovery(string root,bool sandboxBootstrap)
        {
            string script="$ErrorActionPreference='Stop'; $PSModuleAutoLoadingPreference='None'; Remove-Module Microsoft.PowerShell.Utility,Microsoft.PowerShell.Management -Force -ErrorAction SilentlyContinue; [Console]::WriteLine('BEFORE-BOOTSTRAP'); "+
                AgentCommandExecutor.PowerShellBootstrap(sandboxBootstrap,root)+
                "if ($OutputEncoding.WebName -ne 'utf-8' -or [Console]::OutputEncoding.WebName -ne 'utf-8') { throw 'UTF-8 is not configured' }; if ($PSModuleAutoLoadingPreference -ne 'None') { throw 'Autoload preference changed' }; Write-Output 'BOOTSTRAP-READY'; Get-Item -LiteralPath "+Q(root)+" | Out-Null";
            var result=Run(root,script);
            Check(result.ExitCode==0&&!result.TimedOut&&result.Output.Contains("BOOTSTRAP-READY"),(sandboxBootstrap?"sandbox":"ordinary command")+" initializes built-in commands and UTF-8 without module auto-discovery");
        }
        static void ShadowReadLine(string root)
        {
            string modules=Path.Combine(root,"modules"),module=Path.Combine(modules,"PSReadLine","99.0.0"),marker=Path.Combine(root,"unexpected-module.txt");Directory.CreateDirectory(module);
            File.WriteAllText(Path.Combine(module,"PSReadLine.psd1"),"@{RootModule='PSReadLine.psm1';ModuleVersion='99.0.0';GUID='cd02b572-a18c-47ec-9f7c-b030ad840962';FunctionsToExport=@('Set-PSReadLineOption');CmdletsToExport=@();AliasesToExport=@()}",new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(module,"PSReadLine.psm1"),"function Set-PSReadLineOption { param($HistorySaveStyle) }; [IO.File]::WriteAllText("+Q(marker)+",[Environment]::StackTrace); [Threading.Thread]::Sleep(3000)",new UTF8Encoding(false));
            string systemModules=Path.Combine(Path.GetDirectoryName(AgentCommandExecutor.PowerShellPath()),"Modules");
            string tools=Path.Combine(modules,"StartupFixture");Directory.CreateDirectory(tools);File.WriteAllText(Path.Combine(tools,"StartupFixture.psm1"),"function Get-StartupFixtureText { 'THIRD-PARTY-OK' }",new UTF8Encoding(false));
            var environment=new Dictionary<string,string>{{"PSModulePath",modules+Path.PathSeparator+systemModules},{"PSModuleAnalysisCachePath",Path.Combine(root,"module-cache")}};
            var control=Run(root,"$ProgressPreference='SilentlyContinue'; Remove-Module PSReadLine -Force -ErrorAction SilentlyContinue; Get-Command Set-PSReadLineOption -ErrorAction Stop | Out-Null",environment);
            Check(control.ExitCode==0&&File.Exists(marker),"fixture reproduces an unrelated slow module loaded by exact-name command discovery");File.Delete(marker);
            var output=new StringBuilder();object gate=new object();var native=new WindowsCommandRunner.TerminalProcess();
            native.Output+=text=>{lock(gate)output.Append(text);};var clock=Stopwatch.StartNew();
            try
            {
                native.Start(root,false,null,AgentCommandExecutor.PowerShellPath(),environment);
                native.Input("[Console]::WriteLine(('STARTUP-'+'READY'))\r");bool ready=false;
                while(clock.Elapsed.TotalSeconds<60&&native.IsRunning&&!File.Exists(marker))
                {
                    lock(gate)ready=Regex.Replace(output.ToString(),@"\x1b\[[0-?]*[ -/]*[@-~]|\x1b\][^\a]*(?:\a|\x1b\\)","").Contains("STARTUP-READY");
                    if(ready)break;Thread.Sleep(20);
                }
                string diagnostic;lock(gate)diagnostic=output.ToString();Console.WriteLine("CONPTY STARTUP: elapsedMs="+clock.ElapsedMilliseconds+" ready="+ready+" shadowImported="+File.Exists(marker)+" output="+diagnostic);
                if(File.Exists(marker))Console.WriteLine("SHADOW IMPORT STACK: "+File.ReadAllText(marker));
                Check(!File.Exists(marker),"terminal startup never discovers or imports a shadow PSReadLine module");
                Check(ready,"real ConPTY reaches command readiness with the original startup deadline");
                native.Input("[Console]::WriteLine(('HISTORY-OFF:'+ (Get-PSReadLineOption).HistorySaveStyle))\r");
                clock.Restart();bool historyOff=false;while(clock.Elapsed.TotalSeconds<12){lock(gate)historyOff=output.ToString().Contains("HISTORY-OFF:SaveNothing");if(historyOff)break;Thread.Sleep(20);}
                Check(historyOff,"the explicitly loaded line editor still disables persistent command history");
                native.Input("[Console]::WriteLine(('MODULEPATH-'+'RESTORED:'+ [string]($env:PSModulePath -eq "+Q(environment["PSModulePath"])+"))); [Console]::WriteLine((Get-StartupFixtureText))\r");
                clock.Restart();bool restored=false,thirdParty=false;while(clock.Elapsed.TotalSeconds<12){lock(gate){string text=output.ToString();restored=text.Contains("MODULEPATH-RESTORED:True");thirdParty=text.Contains("THIRD-PARTY-OK");}if(restored&&thirdParty)break;Thread.Sleep(20);}
                Check(restored&&thirdParty,"normal module lookup is restored and third-party commands still autoload after startup");
            }
            finally{native.Dispose();}
        }
        public static int Main()
        {
            string root=Path.Combine(Directory.GetCurrentDirectory(),"work","startup-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            Case("ordinary-no-discovery",()=>WithoutDiscovery(root,false));
            Case("sandbox-no-discovery",()=>WithoutDiscovery(root,true));
            Case("shadow-readline",()=>ShadowReadLine(root));
            Console.WriteLine("PowerShell startup tests: "+checks+" passed; "+failures+" failed");return failures==0?0:1;
        }
    }
}
