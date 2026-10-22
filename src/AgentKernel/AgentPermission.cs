using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
namespace Dafeiyu
{
    public enum AgentPermissionEffect { Allow,Deny,Ask }
    public sealed class AgentPermissionRule { public string Action,Resource;public AgentPermissionEffect Effect; }
    public sealed class AgentPermissionDecision { public string Action,Resource;public AgentPermissionEffect Effect;public string Reason; }
    public sealed class AgentApprovalRequest { public string Id,Action,Resource,Risk,PayloadHash,SessionId,ProjectId,TaskId,CallId,ToolName,Arguments,UserIntent,IsolationDescription,ReviewReason; }
    public interface IAgentApprovalProvider { Task<bool> ApproveAsync(AgentApprovalRequest request); }
    public sealed class AgentPermissionRuntime
    {
        readonly object gate=new object();readonly List<AgentPermissionRule> rules=new List<AgentPermissionRule>();
        internal AgentAccessPolicy AccessPolicy;
        int version;public int Version {get{lock(gate)return version;}}
        public AgentPermissionRuntime Copy(){var copy=new AgentPermissionRuntime();copy.AccessPolicy=AccessPolicy;lock(gate)foreach(var rule in rules)copy.Add(rule);return copy;}
        public void Add(AgentPermissionRule rule){if(rule==null || String.IsNullOrWhiteSpace(rule.Action) || String.IsNullOrWhiteSpace(rule.Resource))throw new ArgumentException("权限规则不完整。");lock(gate){rules.Add(new AgentPermissionRule{Action=rule.Action,Resource=rule.Resource,Effect=rule.Effect});version++;}}
        public AgentPermissionDecision Decide(string action,string resource)
        {
            action=action??"";resource=resource??"";List<AgentPermissionRule> snapshot;lock(gate)snapshot=rules.ToList();
            var matches=snapshot.Where(r=>String.Equals(r.Action,action,StringComparison.Ordinal) && Match(r.Resource,resource)).ToList();
            if(matches.Any(r=>r.Effect==AgentPermissionEffect.Deny))return Result(action,resource,AgentPermissionEffect.Deny,"deny rule");
            if(matches.Any(r=>r.Effect==AgentPermissionEffect.Ask))return Result(action,resource,AgentPermissionEffect.Ask,"approval required");
            if(matches.Any(r=>r.Effect==AgentPermissionEffect.Allow))return Result(action,resource,AgentPermissionEffect.Allow,"allow rule");
            return Result(action,resource,AgentPermissionEffect.Deny,"unknown action or resource");
        }
        public async Task<AgentPermissionDecision> AuthorizeAsync(string action,string resource,IAgentApprovalProvider approval)
        {
            var decision=Decide(action,resource);if(decision.Effect!=AgentPermissionEffect.Ask)return decision;
            if(approval==null)return Result(action,resource,AgentPermissionEffect.Deny,"approval provider unavailable");
            bool approved=await approval.ApproveAsync(new AgentApprovalRequest{Id=Guid.NewGuid().ToString("N"),Action=action,Resource=resource,Risk="manual",PayloadHash=""}).ConfigureAwait(false);
            return Result(action,resource,approved?AgentPermissionEffect.Allow:AgentPermissionEffect.Deny,approved?"approved":"rejected");
        }
        static AgentPermissionDecision Result(string action,string resource,AgentPermissionEffect effect,string reason){return new AgentPermissionDecision{Action=action,Resource=resource,Effect=effect,Reason=reason};}
        public static AgentPermissionRuntime DefaultWorkspacePolicy(){var runtime=new AgentPermissionRuntime();runtime.Add(new AgentPermissionRule{Action="read",Resource="**",Effect=AgentPermissionEffect.Allow});runtime.Add(new AgentPermissionRule{Action="write",Resource="**",Effect=AgentPermissionEffect.Allow});runtime.Add(new AgentPermissionRule{Action="shell",Resource="**",Effect=AgentPermissionEffect.Allow});runtime.Add(new AgentPermissionRule{Action="network",Resource="**",Effect=AgentPermissionEffect.Allow});runtime.Add(new AgentPermissionRule{Action="subagent",Resource="**",Effect=AgentPermissionEffect.Allow});runtime.Add(new AgentPermissionRule{Action="github_read",Resource="**",Effect=AgentPermissionEffect.Allow});return runtime;}
        public static string ActionForTool(string name){if(name=="exec_command")return "shell";if(name=="fetch_url"||name=="generate_image")return "network";if(name=="spawn_subagent"||name=="complete_subagent"||name=="interrupt_subagent")return "subagent";if(name.StartsWith("mcp_",StringComparison.Ordinal))return "mcp";if(name.StartsWith("github_",StringComparison.Ordinal))return name.Contains("read")||name.Contains("list")||name.Contains("search")?"github_read":"github_write";if(name=="write_file"||name=="edit_file"||name=="apply_patch"||name=="restore_file"||name=="create_directory")return "write";return "read";}
        public static string ResourceForTool(string name,Dictionary<string,object> args){object value;if(name=="exec_command"&&args!=null&&args.TryGetValue("working_directory",out value)&&value is string)return (string)value;if(args!=null&&args.TryGetValue("path",out value)&&value is string)return (string)value;if(args!=null&&args.TryGetValue("url",out value)&&value is string)return (string)value;return name;}
        static bool Match(string pattern,string value)
        {
            if(pattern=="**")return true;var regex="^"+Regex.Escape(pattern).Replace("\\*\\*",".*").Replace("\\*","[^/]*")+"$";
            return Regex.IsMatch(value,regex,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        }
    }
}
