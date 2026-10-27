using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
namespace Dafeiyu
{
    // Both desktop surfaces use the same immutable owner filters and ordering.
    public static class AgentJobViewState
    {
        public static IList<AgentJobSnapshot> Select(IEnumerable<AgentJobSnapshot> jobs,string projectId,string conversationId,string replyId,string filter,bool latest)
        {
            if(String.IsNullOrWhiteSpace(projectId))return new AgentJobSnapshot[0];
            var selected=jobs.Where(job=>job.Owner.ProjectId==projectId&&(conversationId==null||job.Owner.ConversationId==conversationId)&&(replyId==null||job.SourceReplyId==replyId));
            if(filter=="active")selected=selected.Where(job=>!AgentJobStore.IsTerminal(job.State));
            else if(filter=="finished")selected=selected.Where(job=>AgentJobStore.IsTerminal(job.State));
            return (latest?selected.OrderByDescending(job=>job.CreatedUtc):selected.OrderBy(job=>job.CreatedUtc)).ThenBy(job=>job.Id,StringComparer.Ordinal).ToList().AsReadOnly();
        }
    }
    public sealed class AgentJobPreviewCandidates
    {
        static readonly Regex address=new Regex(@"https?://[^\s<>""']+",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50));
        readonly List<Uri> candidates=new List<Uri>();
        public IList<Uri> Candidates {get{return candidates.ToArray();}}
        public void Add(string text)
        {
            if(candidates.Count==5||String.IsNullOrEmpty(text))return;
            // Log pages are bounded; regex input remains bounded even for direct callers.
            if(text.Length>65536)text=text.Substring(0,65536);
            foreach(Match match in address.Matches(text))
            {
                Uri parsed;if(!Uri.TryCreate(match.Value.TrimEnd('.',',',';',')','}'),UriKind.Absolute,out parsed)||!parsed.IsLoopback||(parsed.Scheme!="http"&&parsed.Scheme!="https"))continue;
                var safe=new UriBuilder(parsed){UserName="",Password="",Query="",Fragment=""}.Uri;
                if(candidates.Any(existing=>existing.AbsoluteUri==safe.AbsoluteUri))continue;
                candidates.Add(safe);if(candidates.Count==5)return;
            }
        }
    }
    public sealed class AgentJobCompletionTracker
    {
        readonly object gate=new object();
        readonly HashSet<string> completed=new HashSet<string>(StringComparer.Ordinal);
        public AgentJobCompletionTracker(IEnumerable<AgentJobSnapshot> recovered)
        {foreach(var job in recovered)if(AgentJobStore.IsTerminal(job.State))completed.Add(job.Id);}
        public IList<AgentJobSnapshot> Observe(IEnumerable<AgentJobSnapshot> jobs)
        {
            var result=new List<AgentJobSnapshot>();
            lock(gate)foreach(var job in jobs)if(AgentJobStore.IsTerminal(job.State)&&completed.Add(job.Id))result.Add(job);
            return result.AsReadOnly();
        }
    }
}
