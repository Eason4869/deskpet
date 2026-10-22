using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    public sealed partial class AgentWorkspace
    {
        IAgentQuestionProvider questionProvider;
        public IAgentQuestionProvider QuestionProvider {get{return questionProvider;}set{questionProvider=value;BuildSchemas();}}
        Func<string,string,CancellationToken,Task<string>> imageGenerator;
        public Func<string,string,CancellationToken,Task<string>> ImageGenerator {get{return imageGenerator;}set{imageGenerator=value;BuildSchemas();}}
        readonly Dictionary<string,AgentImageInspection> inspectionResults=new Dictionary<string,AgentImageInspection>();
        readonly CancellationTokenSource projectToolsLifetime=new CancellationTokenSource();
        internal AgentImageInspection TakeInspectionResult(string callId){lock(inspectionResults){AgentImageInspection result;if(!inspectionResults.TryGetValue(callId,out result))return null;inspectionResults.Remove(callId);return result;}}
        async Task<string> AskUserAsync(Dictionary<string,object> args,string callId,CancellationToken cancel)
        {
            if(QuestionProvider==null)return "错误：当前入口没有用户交互能力，无法等待任务回答。";
            string question=GetString(args,"question","");if(String.IsNullOrWhiteSpace(question)||question.Length>4000)return "错误：问题必须为 1～4000 字符。";
            object raw;var options=new List<string>();if(args.TryGetValue("options",out raw)){var list=raw as IEnumerable;if(list==null||raw is string)return "错误：选项必须是 2～6 条文字的数组。";foreach(object value in list){if(!(value is string)||String.IsNullOrWhiteSpace((string)value)||((string)value).Length>500)return "错误：每条选项应为 1～500 字符。";options.Add((string)value);if(options.Count>6)return "错误：选项最多 6 条。";}if(options.Count<2||options.Distinct(StringComparer.Ordinal).Count()!=options.Count)return "错误：请提供 2～6 条不同的选项。";}
            bool free=true;if(args.TryGetValue("allow_free_input",out raw)){if(!(raw is bool))return "错误：allow_free_input 必须是布尔值。";free=(bool)raw;}if(options.Count==0&&!free)return "错误：无选项时必须允许自由回答。";
            string session=kernelSessionModel==null?"":kernelSessionModel.SessionId;string task=kernelTaskId??(TaskState==null?null:TaskState.Id);
            if(String.IsNullOrWhiteSpace(session)||String.IsNullOrWhiteSpace(task))return "错误：此交互入口尚未绑定会话和任务。";
            using(var lifetime=CancellationTokenSource.CreateLinkedTokenSource(cancel,projectToolsLifetime.Token)){var answer=await AgentAuthorizationService.AwaitCancellation(QuestionProvider.AskAsync(new AgentQuestionRequest{SessionId=session,TaskId=task,CallId=callId,Question=question,Options=options.ToArray(),AllowFreeInput=free},lifetime.Token),lifetime.Token).ConfigureAwait(false);lifetime.Token.ThrowIfCancellationRequested();if(answer==null||String.IsNullOrWhiteSpace(answer.Text))return "错误：未收到用户回答。";return new JavaScriptSerializer().Serialize(new{answer=answer.Text,option_index=answer.OptionIndex});}
        }
        string InspectImage(string path,Dictionary<string,object> args,string callId,CancellationToken cancel)
        {
            if(providerCapabilities==null||!providerCapabilities.SupportsImages)return "错误：当前模型不支持图片理解；请选择支持图片输入的模型。";
            string question=GetString(args,"question","");if(question.Length>4000)throw new ArgumentException("图片问题最多 4000 字符。");
            AgentImageInspection image;using(LockDirectories(Path.GetDirectoryName(path),false)){image=AgentImageInspection.Parse(path,ReadBytes(path,cancel,AgentImageInspection.MaximumBytes),question);}
            cancel.ThrowIfCancellationRequested();lock(inspectionResults){if(inspectionResults.Count>=4)throw new ArgumentException("尚有未发送的图片输入，请先完成当前模型调用。");inspectionResults[callId]=image;}return image.Summary();
        }
        string ReadGit(string name,string path,Dictionary<string,object> args,CancellationToken cancel,string authorizedResource)
        {
            string repo=AgentGitTools.FindRoot(path,Root,RestrictToRoot,cancel);if(!String.Equals(repo,authorizedResource,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("等待期间 Git 仓库已变化，请重新读取。");using(LockDirectories(Directory.Exists(path)?path:Path.GetDirectoryName(path),false))using(LockDirectories(repo,false))return AgentGitTools.Read(name,path,repo,GetString(args,"scope","unstaged"),GetInt(args,"limit",10,1,50),15,cancel);
        }
        void DisposeProjectTools(){projectToolsLifetime.Cancel();lock(inspectionResults)inspectionResults.Clear();}
    }
}
