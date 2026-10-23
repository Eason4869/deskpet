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
            if(QuestionProvider==null)return TextResources.Get("agentProjectTools.askUserAsync.text");
            string question=GetString(args,"question","");if(String.IsNullOrWhiteSpace(question)||question.Length>4000)return TextResources.Get("agentProjectTools.askUserAsync.text.alternativeA");
            object raw;var options=new List<string>();if(args.TryGetValue("options",out raw)){var list=raw as IEnumerable;if(list==null||raw is string)return TextResources.Get("agentProjectTools.askUserAsync.text.alternativeB");foreach(object value in list){if(!(value is string)||String.IsNullOrWhiteSpace((string)value)||((string)value).Length>500)return TextResources.Get("agentProjectTools.askUserAsync.text.alternativeC");options.Add((string)value);if(options.Count>6)return TextResources.Get("agentProjectTools.askUserAsync.text.alternativeD");}if(options.Count<2||options.Distinct(StringComparer.Ordinal).Count()!=options.Count)return TextResources.Get("agentProjectTools.askUserAsync.text.alternativeE");}
            bool free=true;if(args.TryGetValue("allow_free_input",out raw)){if(!(raw is bool))return TextResources.Get("agentProjectTools.askUserAsync.text.alternativeF");free=(bool)raw;}if(options.Count==0&&!free)return TextResources.Get("agentProjectTools.askUserAsync.text.alternativeG");
            string session=kernelSessionModel==null?"":kernelSessionModel.SessionId;string task=kernelTaskId??(TaskState==null?null:TaskState.Id);
            if(String.IsNullOrWhiteSpace(session)||String.IsNullOrWhiteSpace(task))return TextResources.Get("agentProjectTools.askUserAsync.text.alternativeH");
            using(var lifetime=CancellationTokenSource.CreateLinkedTokenSource(cancel,projectToolsLifetime.Token)){var answer=await AgentAuthorizationService.AwaitCancellation(QuestionProvider.AskAsync(new AgentQuestionRequest{SessionId=session,TaskId=task,CallId=callId,Question=question,Options=options.ToArray(),AllowFreeInput=free},lifetime.Token),lifetime.Token).ConfigureAwait(false);lifetime.Token.ThrowIfCancellationRequested();if(answer==null||String.IsNullOrWhiteSpace(answer.Text))return TextResources.Get("agentProjectTools.askUserAsync.text.alternativeI");return new JavaScriptSerializer().Serialize(new{answer=answer.Text,option_index=answer.OptionIndex});}
        }
        string InspectImage(string path,Dictionary<string,object> args,string callId,CancellationToken cancel)
        {
            if(providerCapabilities==null||!providerCapabilities.SupportsImages)return TextResources.Get("agentProjectTools.inspectImage.text");
            string question=GetString(args,"question","");if(question.Length>4000)throw new ArgumentException(TextResources.Get("agentProjectTools.inspectImage.text.alternativeA"));
            AgentImageInspection image;using(LockDirectories(Path.GetDirectoryName(path),false)){image=AgentImageInspection.Parse(path,ReadBytes(path,cancel,AgentImageInspection.MaximumBytes),question);}
            cancel.ThrowIfCancellationRequested();lock(inspectionResults){if(inspectionResults.Count>=4)throw new ArgumentException(TextResources.Get("agentProjectTools.inspectImage.text.alternativeB"));inspectionResults[callId]=image;}return image.Summary();
        }
        string ReadGit(string name,string path,Dictionary<string,object> args,CancellationToken cancel,string authorizedResource)
        {
            string repo=AgentGitTools.FindRoot(path,Root,RestrictToRoot,cancel);if(!String.Equals(repo,authorizedResource,StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException(TextResources.Get("agentProjectTools.readGit.text"));using(LockDirectories(Directory.Exists(path)?path:Path.GetDirectoryName(path),false))using(LockDirectories(repo,false))return AgentGitTools.Read(name,path,repo,GetString(args,"scope","unstaged"),GetInt(args,"limit",10,1,50),15,cancel);
        }
        void DisposeProjectTools(){projectToolsLifetime.Cancel();lock(inspectionResults)inspectionResults.Clear();}
    }
}
