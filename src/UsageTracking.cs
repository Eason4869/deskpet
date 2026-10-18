using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
namespace Dafeiyu
{
    public static class UsageTracking
    {
        sealed class Context { internal string Feature,ConversationId,ProjectId; }
        static readonly AsyncLocal<Context> scope=new AsyncLocal<Context>();
        static readonly AsyncLocal<UsageRequest> current=new AsyncLocal<UsageRequest>();
        static UsageStore store;
        public static void Initialize(UsageStore value){Interlocked.Exchange(ref store,value);}
        public static IDisposable BeginScope(string feature,string conversationId=null,string projectId=null)
        {
            var prior=scope.Value;scope.Value=new Context{Feature=feature,ConversationId=conversationId??(prior==null?null:prior.ConversationId),ProjectId=projectId??(prior==null?null:prior.ProjectId)};
            return new Restore(()=>scope.Value=prior);
        }
        internal static UsageRequest BeginRequest(ConnectionSettings settings,CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            var context=scope.Value;var request=new UsageRequest(store,new UsageRecord{RequestId=Guid.NewGuid().ToString("N"),TimestampUtc=DateTime.UtcNow,Provider=ProviderIdentity.Name(settings),Model=settings.Model,Feature=context==null?"chat":context.Feature,ConversationId=context==null?null:context.ConversationId,ProjectId=context==null?null:context.ProjectId,Status="pending"},cancel,current.Value);
            current.Value=request;return request;
        }
        internal static void Observe(Dictionary<string,object> root,bool anthropic){var request=current.Value;if(request!=null)request.Observe(root,anthropic);}
        sealed class Restore:IDisposable { Action action;internal Restore(Action action){this.action=action;}public void Dispose(){var value=Interlocked.Exchange(ref action,null);if(value!=null)value();} }
        internal sealed class UsageRequest:IDisposable
        {
            readonly UsageStore target;readonly UsageRecord record;readonly CancellationToken cancel;readonly UsageRequest prior;bool completed,disposed;
            long? anthropicInput,cacheCreation;
            internal UsageRequest(UsageStore target,UsageRecord record,CancellationToken cancel,UsageRequest prior){this.target=target;this.record=record;this.cancel=cancel;this.prior=prior;Save();}
            internal void Complete(){completed=true;}
            void Save(){if(target==null)return;try{target.AddOrUpdate(record);}catch(Exception e){if(!(e is IOException || e is UnauthorizedAccessException))throw;}}
            static long? Number(Dictionary<string,object> data,string key)
            {object raw;if(!data.TryGetValue(key,out raw)||raw==null || raw is bool || raw is string)return null;try{decimal value=Convert.ToDecimal(raw,System.Globalization.CultureInfo.InvariantCulture);return value>=0 && value<=Int64.MaxValue && Decimal.Truncate(value)==value?(long?)value:null;}catch(Exception e){if(!(e is InvalidCastException || e is FormatException || e is OverflowException))throw;return null;}}
            static Dictionary<string,object> Object(Dictionary<string,object> root,string key){object raw;return root.TryGetValue(key,out raw)?raw as Dictionary<string,object>:null;}
            internal void Observe(Dictionary<string,object> root,bool anthropic)
            {
                if(root==null || disposed)return;var usage=Object(root,"usage");var message=Object(root,"message");if(usage==null && message!=null)usage=Object(message,"usage");if(usage==null)return;
                long? input=Number(usage,anthropic?"input_tokens":"prompt_tokens"),output=Number(usage,anthropic?"output_tokens":"completion_tokens");
                if(anthropic)
                {
                    // Anthropic raw input excludes cache reads and cache creation. Keep raw
                    // counters separate so partial/cumulative snapshots cannot add them twice.
                    anthropicInput=UsageRecord.Max(anthropicInput,input);
                    record.CachedInputTokens=UsageRecord.Max(record.CachedInputTokens,Number(usage,"cache_read_input_tokens"));
                    cacheCreation=UsageRecord.Max(cacheCreation,Number(usage,"cache_creation_input_tokens"));
                    input=UsageRecord.Sum(anthropicInput,UsageRecord.Sum(record.CachedInputTokens??0,cacheCreation??0));
                }
                else {var details=Object(usage,"prompt_tokens_details");if(details!=null)record.CachedInputTokens=UsageRecord.Max(record.CachedInputTokens,Number(details,"cached_tokens"));details=Object(usage,"completion_tokens_details");if(details!=null)record.ReasoningTokens=UsageRecord.Max(record.ReasoningTokens,Number(details,"reasoning_tokens"));}
                record.InputTokens=UsageRecord.Max(record.InputTokens,input);record.OutputTokens=UsageRecord.Max(record.OutputTokens,output);
                record.TotalTokens=UsageRecord.Max(record.TotalTokens,Number(usage,"total_tokens"));record.TotalTokens=UsageRecord.Max(record.TotalTokens,UsageRecord.Sum(record.InputTokens,record.OutputTokens));Save();
            }
            public void Dispose(){if(disposed)return;disposed=true;record.Status=cancel.IsCancellationRequested?"cancelled":completed?"complete":"error";Save();current.Value=prior;}
        }
    }
}
