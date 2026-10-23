using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu
{
    internal static class AgentGitTools
    {
        internal const int MaximumOutput=24000;
        internal static string FindRoot(string path,string authorizedRoot,bool restricted,CancellationToken cancel=default(CancellationToken))
        {
            string directory=File.Exists(path)?Path.GetDirectoryName(path):path;
            for(var current=new DirectoryInfo(directory);current!=null;current=current.Parent){cancel.ThrowIfCancellationRequested();if(restricted&&!Inside(current.FullName,authorizedRoot))break;
                if((current.Attributes&FileAttributes.ReparsePoint)!=0)throw new UnauthorizedAccessException(TextResources.Get("agentGitTools.findRoot.text"));
                string marker=Path.Combine(current.FullName,".git");if(Directory.Exists(marker)||File.Exists(marker)){ValidateMetadata(marker,authorizedRoot,restricted,cancel);return current.FullName;}}
            throw new ArgumentException(TextResources.Get("agentGitTools.findRoot.text.alternativeA"));
        }
        static bool Inside(string path,string root){return String.Equals(path.TrimEnd('\\','/'),root.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase)||path.StartsWith(root.TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
        static void ValidateMetadata(string marker,string root,bool restricted,CancellationToken cancel)
        {
            if((File.GetAttributes(marker)&FileAttributes.ReparsePoint)!=0)throw new UnauthorizedAccessException(TextResources.Get("agentGitTools.validateMetadata.text"));
            string permittedMetadata=root;
            if(File.Exists(marker)){string worktreeMarker=marker;string value=ReadPointer(marker);if(!value.StartsWith("gitdir: ",StringComparison.Ordinal))throw new ArgumentException(TextResources.Get("agentGitTools.validateMetadata.text.alternativeA"));marker=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(marker),value.Substring(8)));if(restricted&&!Inside(marker,root))permittedMetadata=ValidateLinkedWorktree(worktreeMarker,marker);}
            if(!Directory.Exists(marker))throw new ArgumentException(TextResources.Get("agentGitTools.validateMetadata.text.alternativeB"));
            AgentInstructions.RejectLinks(marker);
            int count=0;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var todo=new Stack<string>();todo.Push(marker);while(todo.Count>0){cancel.ThrowIfCancellationRequested();string path=todo.Pop();if(!seen.Add(path))continue;if(++count>100000)throw new ArgumentException(TextResources.Get("agentGitTools.validateMetadata.text.alternativeC"));if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new UnauthorizedAccessException(TextResources.Get("agentGitTools.validateMetadata.text"));if(Directory.Exists(path)){foreach(string entry in Directory.EnumerateFileSystemEntries(path)){cancel.ThrowIfCancellationRequested();todo.Push(entry);}}else if(Path.GetFileName(path)=="alternates")throw new UnauthorizedAccessException(TextResources.Get("agentGitTools.validateMetadata.text.alternativeD"));else if(Path.GetFileName(path)=="commondir"){string common=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path),ReadPointer(path)));AgentInstructions.RejectLinks(common);if(restricted&&!Inside(common,permittedMetadata))throw new UnauthorizedAccessException(TextResources.Get("agentGitTools.validateMetadata.text.alternativeE"));todo.Push(common);}}
        }
        static string ReadPointer(string path){AgentInstructions.RejectLinks(path);if(!File.Exists(path)||new FileInfo(path).Length>4096)throw new UnauthorizedAccessException(TextResources.Get("agentGitTools.readPointer.text"));return File.ReadAllText(path).Trim();}
        static string ValidateLinkedWorktree(string worktreeMarker,string metadata)
        {
            // A standard linked worktree explicitly delegates access to its shared repository
            // metadata. Require the reciprocal pointer and Git's canonical worktrees layout;
            // an arbitrary gitdir file cannot grant access to another ordinary repository.
            AgentInstructions.RejectLinks(metadata);string backlink=Path.GetFullPath(Path.Combine(metadata,ReadPointer(Path.Combine(metadata,"gitdir"))));
            if(!String.Equals(backlink,Path.GetFullPath(worktreeMarker),StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException(TextResources.Get("agentGitTools.validateLinkedWorktree.text"));
            string common=Path.GetFullPath(Path.Combine(metadata,ReadPointer(Path.Combine(metadata,"commondir"))));AgentInstructions.RejectLinks(common);
            string parent=Path.GetDirectoryName(metadata.TrimEnd('\\','/'));if(!String.Equals(parent,Path.Combine(common,"worktrees"),StringComparison.OrdinalIgnoreCase)||!Directory.Exists(Path.Combine(common,"objects"))||!File.Exists(Path.Combine(common,"config")))throw new UnauthorizedAccessException(TextResources.Get("agentGitTools.validateLinkedWorktree.text.alternativeA"));return common;
        }
        internal static string Read(string tool,string path,string repository,string scope,int limit,int timeout,CancellationToken cancel)
        {
            var common=new List<string>{"--no-pager","--no-optional-locks","--literal-pathspecs","-c","core.fsmonitor=false","-c","core.untrackedCache=false","-c","core.hooksPath=","-c","diff.external=","-c","core.quotePath=false","-c","color.ui=false","-c","submodule.recurse=false","-c","protocol.allow=never","-c","credential.helper=","-c","core.worktree="+repository,"-C",repository};
            if(tool=="git_status"||tool=="git_diff"){
                // Worktree reads can execute clean/process filters even with external diffs disabled.
                // Ask Git to resolve includes and worktree config; only an empty no-match result is safe.
                var filters=RunCommand(common.Concat(new[]{"config","--includes","--name-only","--get-regexp",@"^filter\..*\.(clean|process)$"}).ToArray(),repository,timeout,cancel);
                if(filters.ExitCode==0)throw new ArgumentException(TextResources.Get("agentGitTools.read.text"));
                if(filters.Truncated||filters.ExitCode!=1||!String.IsNullOrEmpty(filters.Output))throw new ArgumentException(TextResources.Get("agentGitTools.read.text.alternativeA"));
            }
            string relative=String.Equals(path,repository,StringComparison.OrdinalIgnoreCase)?".":path.Substring(repository.TrimEnd('\\','/').Length+1).Replace('\\','/');
            cancel.ThrowIfCancellationRequested();string branch=Run(common.Concat(new[]{"symbolic-ref","--short","-q","HEAD"}).ToArray(),repository,timeout,cancel,false);
            branch=(branch??"").Trim();if(branch.Length>256)branch=branch.Substring(0,256)+"…";
            string[] command;
            if(tool=="git_status")command=new[]{"status","--short","--branch","--untracked-files=normal","--ignore-submodules=all","--",relative};
            else if(tool=="git_log")command=new[]{"log","--no-show-signature","--no-decorate","--format=%h %ad %s","--date=short","-n",limit.ToString(System.Globalization.CultureInfo.InvariantCulture),"--",relative};
            else {if(scope!="unstaged"&&scope!="staged")throw new ArgumentException(TextResources.Get("agentGitTools.read.text.alternativeB"));command=(new[]{"diff","--no-ext-diff","--no-textconv","--no-color","--ignore-submodules=all","--no-renames"}).Concat(scope=="staged"?new[]{"--cached"}:new string[0]).Concat(new[]{"--",relative}).ToArray();}
            string output=Run(common.Concat(command).ToArray(),repository,timeout,cancel,true);
            if(output.Length>MaximumOutput)output=TextResources.Format("agentGitTools.read.outputOutputSubstring",output.Substring(0,MaximumOutput));
            return TextResources.Get("agentGitTools.read.text.alternativeC")+repository+TextResources.Get("agentGitTools.read.text.alternativeD")+(String.IsNullOrWhiteSpace(branch)?TextResources.Get("agentGitTools.read.text.alternativeE"):branch.Trim())+"\n"+(tool=="git_diff"?TextResources.Format("agentGitTools.read.template",scope):"")+output;
        }
        static string Run(string[] arguments,string directory,int timeout,CancellationToken cancel,bool requireSuccess)
        {
            var result=RunCommand(arguments,directory,timeout,cancel);
            if(requireSuccess&&result.ExitCode!=0){string diagnostic=result.Output??"";if(diagnostic.Length>1000)diagnostic=diagnostic.Substring(0,1000);throw new ArgumentException(TextResources.Format("agentGitTools.run.template",diagnostic));}
            string text=result.Output??"";return text+(result.Truncated?TextResources.Get("agentGitTools.run.text"):"");
        }
        static CommandResult RunCommand(string[] arguments,string directory,int timeout,CancellationToken cancel)
        {
            string executable=LocateGit();var environment=new Dictionary<string,string>{{"GIT_OPTIONAL_LOCKS","0"},{"GIT_NO_LAZY_FETCH","1"},{"GIT_TERMINAL_PROMPT","0"},{"GIT_CONFIG_NOSYSTEM","1"},{"GIT_CONFIG_GLOBAL",Environment.OSVersion.Platform==PlatformID.Win32NT?"NUL":"/dev/null"},{"GIT_PAGER",""},{"GIT_ATTR_NOSYSTEM","1"}};
            var command=new AgentCommand{Id=Guid.NewGuid().ToString("N"),Executable=executable,Workspace=directory,Arguments=arguments};
            CommandResult result;
            if(Environment.OSVersion.Platform==PlatformID.Win32NT)result=WindowsCommandRunner.Run(command,timeout,cancel,true,null,environment,false);
            else result=RunPortable(command,timeout,cancel,environment);
            cancel.ThrowIfCancellationRequested();if(result.TimedOut)throw new ArgumentException(TextResources.Get("agentGitTools.runCommand.text"));
            return result;
        }
        static CommandResult RunPortable(AgentCommand command,int timeout,CancellationToken cancel,IDictionary<string,string> environment)
        {
#if PORTABLE
            var start=new ProcessStartInfo(command.Executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=command.Workspace};foreach(string arg in command.Arguments)start.ArgumentList.Add(arg);
            foreach(string key in start.Environment.Keys.Where(k=>k.StartsWith("GIT_",StringComparison.OrdinalIgnoreCase)).ToArray())start.Environment.Remove(key);foreach(var pair in environment)start.Environment[pair.Key]=pair.Value;
            using(var process=new Process{StartInfo=start}){cancel.ThrowIfCancellationRequested();process.Start();var text=new StringBuilder();object gate=new object();bool truncated=false;
                Func<StreamReader,Task> drain=async reader=>{char[] block=new char[4096];int n;while((n=await reader.ReadAsync(block,0,block.Length).ConfigureAwait(false))>0){lock(gate){int take=Math.Min(n,256*1024-text.Length);if(take<n)truncated=true;if(take>0)text.Append(block,0,take);}}};
                var stdout=drain(process.StandardOutput);var stderr=drain(process.StandardError);var watch=Stopwatch.StartNew();bool timedOut=false;
                try{while(!process.WaitForExit(50)){if(cancel.IsCancellationRequested||watch.Elapsed.TotalSeconds>=timeout){timedOut=!cancel.IsCancellationRequested;process.Kill(true);break;}}if(!process.WaitForExit(2000))throw new IOException(TextResources.Get("agentGitTools.runPortable.text.alternativeA"));if(!Task.WaitAll(new[]{stdout,stderr},2000))throw new IOException(TextResources.Get("agentGitTools.runPortable.text.alternativeB"));return new CommandResult{Output=text.ToString(),ExitCode=process.ExitCode,TimedOut=timedOut,Cancelled=cancel.IsCancellationRequested,Truncated=truncated};}
                finally{if(!process.HasExited)process.Kill(true);}
            }
#else
            throw new PlatformNotSupportedException(TextResources.Get("agentGitTools.runPortable.text"));
#endif
        }
        static string LocateGit(){foreach(string dir in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)){if(String.IsNullOrWhiteSpace(dir))continue;try{string path=Path.Combine(dir.Trim('"'),Environment.OSVersion.Platform==PlatformID.Win32NT?"git.exe":"git");if(File.Exists(path))return Path.GetFullPath(path);}catch(ArgumentException){}}throw new ArgumentException(TextResources.Get("agentGitTools.locateGit.text"));}
    }
}
