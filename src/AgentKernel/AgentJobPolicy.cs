using System;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    public static class AgentJobPolicy
    {
        public const int DefaultTimeoutSeconds=1800,MaximumTimeoutSeconds=28800;
        public const int MaximumActiveJobs=4,MaximumProjectJobs=2;
        public const int MinimumPageBytes=1024,DefaultPageBytes=16384,MaximumPageBytes=65536;
        public const int MaximumCommandBytes=16384,MaximumTitleBytes=512;
        public const long MaximumJobLogBytes=8L*1024*1024,MaximumApplicationLogBytes=128L*1024*1024;
        public const int MaximumPendingLogBytes=256*1024,MaximumLogRecordBytes=8*1024,MaximumMetadataBytes=64*1024;
        public const int MaximumHistory=200,HistoryDays=7;
        public static int ResolveTimeout(int? requested,int projectMaximum)
        {
            if(projectMaximum<1||projectMaximum>MaximumTimeoutSeconds)throw new ArgumentOutOfRangeException("projectMaximum");
            if(!requested.HasValue)return Math.Min(DefaultTimeoutSeconds,projectMaximum);
            if(requested.Value<1||requested.Value>MaximumTimeoutSeconds||requested.Value>projectMaximum)throw new ArgumentOutOfRangeException("timeout_seconds");
            return requested.Value;
        }
        public static string IdempotencyKey(AgentJobOwner owner,AgentToolInvocation invocation)
        {
            if(owner==null||invocation==null)throw new ArgumentNullException("invocation");
            if(String.IsNullOrWhiteSpace(invocation.TaskId)||owner.ProjectId!=invocation.ProjectId||owner.AgentId!=invocation.AgentId)throw new ArgumentException("invocation_owner");
            string value=new JavaScriptSerializer().Serialize(new[]{"agent-job-v1",owner.ProjectId,owner.ConversationId,owner.AgentId,invocation.TaskId,invocation.CallId});
            using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","").ToLowerInvariant();
        }
    }
}
