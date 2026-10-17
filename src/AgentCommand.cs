using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    [DataContract] public sealed class AgentCommand
    {
        [DataMember]public string Id,Executable,Workspace;
        [DataMember]public string[] Arguments;
    }
    [DataContract]public sealed class CommandResult
    {
        [DataMember]public string CommandId,Executable,Workspace,Output;
        [DataMember]public string[] Arguments;
        [DataMember]public int ExitCode;
        [DataMember]public bool Truncated,TimedOut,Cancelled;
        [DataMember]public long ElapsedMilliseconds;
        public CommandResult Copy(){var c=(CommandResult)MemberwiseClone();c.Arguments=Arguments==null?null:(string[])Arguments.Clone();return c;}
    }
    public sealed partial class AgentWorkspace
    {
        readonly List<AgentCommand> commands=new List<AgentCommand>();readonly List<CommandResult> commandResults=new List<CommandResult>();
        public bool CommandsEnabled {get;private set;}
        public int CommandTimeoutSeconds {get;set;}
        public IList<CommandResult> CommandResults{get{lock(commandResults)return commandResults.Select(r=>r.Copy()).ToList().AsReadOnly();}}
        public void ConfigureCommands(bool enabled,string json)
        {
            commands.Clear();CommandsEnabled=false;if(!enabled){BuildSchemas();return;}
            var recipes=new JavaScriptSerializer{MaxJsonLength=65536,RecursionLimit=12}.Deserialize<AgentCommand[]>(String.IsNullOrWhiteSpace(json)?"[]":json);
            if(recipes==null || recipes.Length>32)throw new ArgumentException("最多配置 32 条项目命令。");var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var recipe in recipes){
                if(recipe==null || String.IsNullOrWhiteSpace(recipe.Id) || recipe.Id.Length>80 || recipe.Id.Any(Char.IsControl) || !names.Add(recipe.Id) ||
                    String.IsNullOrWhiteSpace(recipe.Executable) || !Path.IsPathRooted(recipe.Executable) || !File.Exists(recipe.Executable) || recipe.Arguments==null || recipe.Arguments.Length>100 || recipe.Arguments.Any(a=>a==null || a.Length>8000 || a.IndexOf('\0')>=0) ||
                    String.IsNullOrWhiteSpace(recipe.Workspace) || !Path.IsPathRooted(recipe.Workspace))throw new ArgumentException("项目命令必须具有唯一 ID、已存在的可执行文件绝对路径、参数数组和明确的项目目录。");
                string bound=Path.GetFullPath(recipe.Workspace).TrimEnd('\\','/');if(!String.Equals(bound,Root.TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase))continue;
                commands.Add(new AgentCommand{Id=recipe.Id,Executable=Path.GetFullPath(recipe.Executable),Arguments=(string[])recipe.Arguments.Clone(),Workspace=Root});
            }
            CommandsEnabled=true;if(CommandTimeoutSeconds<=0)CommandTimeoutSeconds=120;BuildSchemas();
        }
        string ListCommands(){return new JavaScriptSerializer().Serialize(commands.Select(c=>new{command_id=c.Id,executable=c.Executable,arguments=c.Arguments,workspace=c.Workspace}).ToArray());}
        string RunCommand(string id,CancellationToken token)
        {
            if(!CommandsEnabled || readOnly)throw new ArgumentException("此项目尚未启用命令执行，或当前为计划模式。");
            var command=commands.SingleOrDefault(c=>c.Id==id);if(command==null)throw new ArgumentException("未知或未授权的项目命令 ID，请先查看 list_commands。");
            using(LockDirectories(Root,false)){
                var result=WindowsCommandRunner.Run(command,Math.Max(1,Math.Min(600,CommandTimeoutSeconds)),token);lock(commandResults){commandResults.Add(result);if(commandResults.Count>8)commandResults.RemoveAt(0);}PublishTask();token.ThrowIfCancellationRequested();
                string output=result.Output??"";bool previewCut=output.Length>12000;if(previewCut)output=output.Substring(output.Length-12000);
                string json=new JavaScriptSerializer().Serialize(new{result.CommandId,result.ExitCode,result.TimedOut,result.ElapsedMilliseconds,Output=output,Truncated=result.Truncated || previewCut});
                return (result.TimedOut?"错误：命令超时。":result.ExitCode!=0?"错误：命令返回非零退出码。":"")+json;
            }
        }
        string ReadOutput(string id,int offset,int limit)
        {
            CommandResult result;lock(commandResults)result=commandResults.LastOrDefault(r=>r.CommandId==id);if(result==null)throw new ArgumentException("本任务尚无此命令的输出。");
            string output=result.Output??"";offset=Math.Min(offset,output.Length);int count=Math.Min(limit,output.Length-offset);
            return new JavaScriptSerializer().Serialize(new{result.CommandId,result.ExitCode,result.TimedOut,result.Truncated,Output=output.Substring(offset,count),next_offset=offset+count<output.Length?(int?)(offset+count):null});
        }
    }
}
