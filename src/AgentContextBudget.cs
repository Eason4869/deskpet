using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    internal static class AgentContextBudget
    {
        internal static int EstimateTokens(IEnumerable<object> messages)
        {
            long size=0;foreach(object message in messages)size+=Estimate(message)+12;
            return (int)Math.Min(Int32.MaxValue,size);
        }
        internal static bool FitsWireBytes(object body)
        {
            try{return Encoding.UTF8.GetByteCount(new JavaScriptSerializer{MaxJsonLength=32*1024*1024}.Serialize(body))<=8*1024*1024;}
            catch(InvalidOperationException){return false;}
        }
        static long Estimate(object value)
        {
            if(value==null)return 0;var text=value as string;if(text!=null)return (Encoding.UTF8.GetByteCount(text)+2)/3;
            if(value is bool || value is ValueType)return 2;
            var map=value as Dictionary<string,object>;
            if(map!=null){string type=Text(map,"type");if(type=="image" || type=="image_url")return 2048;if(type=="document" || type=="file")return 4096;return map.Sum(p=>Estimate(p.Value)+4);}
            var list=value as System.Collections.IEnumerable;if(list!=null){long sum=0;foreach(object item in list)sum+=Estimate(item);return sum;}
            return Estimate(Object(value));
        }
        // Runs only at a complete protocol boundary. A tool-call message and all
        // its observations are one indivisible unit, including thinking signatures.
        internal static async Task<bool> PrepareAsync(List<object> history,bool anthropic,AgentWorkspace workspace,Func<string,CancellationToken,Task<string>> summarize,CancellationToken cancel,Func<IList<object>,bool> fitsBytes=null,int overheadTokens=0)
        {
            int limit=workspace.ContextTokenLimit<=0?32768:Math.Max(1024,Math.Min(ModelContextCapacity.Maximum,workspace.ContextTokenLimit));
            var policy=AgentContextPolicy.Resolve(limit,overheadTokens);int inputLimit=policy.InputBudget;Func<IList<object>,bool> fits=items=>EstimateTokens(items)<=inputLimit && (fitsBytes==null || fitsBytes(items));bool required=!fits(history);if(!required && EstimateTokens(history)<policy.CompactAt)return false;
            try{
            workspace.ContextProgress("compacting");cancel.ThrowIfCancellationRequested();for(int i=0;i<history.Count;i++)history[i]=Object(history[i]);Compact(history,anthropic);
            // Every result here is complete and its bounded original is archived.
            foreach(object item in history){var m=Object(item);if(!anthropic && Text(m,"role")=="tool" && Text(m,"content").Length>1500)m["content"]=Preview(Text(m,"content"),Text(m,"tool_call_id"));
                if(anthropic && Text(m,"role")=="user"){object content;if(m.TryGetValue("content",out content) && !(content is string)){var blocks=content as System.Collections.IEnumerable;if(blocks!=null){var converted=new List<object>();foreach(object b in blocks){var block=Object(b);if(Text(block,"type")=="tool_result" && Text(block,"content").Length>1500)block["content"]=Preview(Text(block,"content"),Text(block,"tool_use_id"));converted.Add(block);}m["content"]=converted;}}}}
            // Normalize anonymous messages before folding, without serializing
            // large base64 attachments into a text summary.
            for(int i=0;i<history.Count;i++)history[i]=Object(history[i]);
            if(required && fits(history)){var previous=workspace.TaskState;workspace.RecordCompaction(EstimateTokens(history)+overheadTokens,previous!=null && !String.IsNullOrEmpty(previous.ContextSummary)?previous.ContextSummary:TextResources.Get("agentContextBudget.prepareAsync.workspaceRecordCompaction"));return true;}
            var units=new List<List<object>>();for(int i=0;i<history.Count;i++){
                var unit=new List<object>{history[i]};var m=Object(history[i]);
                if(Text(m,"role")=="assistant" && HasCalls(m,anthropic))while(i+1<history.Count && IsResult(Object(history[i+1]),anthropic))unit.Add(history[++i]);units.Add(unit);
            }
            var users=history.Where(m=>Text(Object(m),"role")=="user" && !IsResult(Object(m),anthropic)).ToList();
            var snapshot=workspace.TaskState;object originalGoal=users.FirstOrDefault(m=>snapshot!=null && ContainsGoal(Object(m),snapshot.Goal));
            object first=users.FirstOrDefault(),last=users.LastOrDefault();var recent=units.LastOrDefault(u=>u.Any(m=>HasCalls(Object(m),anthropic)));
            var kept=new List<object>();var removed=new List<object>();
            foreach(var unit in units){bool protect=unit.Any(m=>Text(Object(m),"role")=="system" || System.Object.ReferenceEquals(m,first) || System.Object.ReferenceEquals(m,last) || System.Object.ReferenceEquals(m,originalGoal)) || unit==recent;(protect?kept:removed).AddRange(unit);}
            if(removed.Count==0 && !required){workspace.ContextProgress("idle");return false;}
            if(removed.Count==0 || EstimateTokens(kept)>=inputLimit-128 || (fitsBytes!=null && !fitsBytes(kept))){workspace.ContextProgress("limited");throw new ApiException(TextResources.Get("agentContextBudget.prepareAsync.text"));}
            var digest=new StringBuilder();foreach(object item in removed){cancel.ThrowIfCancellationRequested();var m=Object(item);digest.Append(Text(m,"role")).Append(": ");DigestToolMetadata(m,digest);object content;if(m.TryGetValue("content",out content)){if(content is string)digest.Append((string)content);else DigestContent(content,digest);}digest.AppendLine();if(digest.Length>48000){digest.Length=48000;digest.AppendLine(TextResources.Get("agentContextBudget.prepareAsync.digestAppendLine"));break;}}
            string summary;try{summary=await summarize(digest.ToString(),cancel).ConfigureAwait(false);if(String.IsNullOrWhiteSpace(summary))throw new ApiException(TextResources.Get("agentContextBudget.prepareAsync.text.alternativeA"));}
            catch(ApiException){workspace.ContextProgress("failed");throw new ApiException(TextResources.Get("agentContextBudget.prepareAsync.text.alternativeB"));}
            cancel.ThrowIfCancellationRequested();if(summary.Length>6000)summary=summary.Substring(0,6000);
            var task=workspace.TaskState;if(task!=null)task.ContextSummary="";string facts=task==null?"":task.CheckpointText();if(facts.Length>6000)facts=facts.Substring(0,6000);
            string checkpoint=TextResources.Format("agentContextBudget.prepareAsync.checkpointSummaryFacts",summary,facts);
            var note=new Dictionary<string,object>{{"role","assistant"},{"content",checkpoint}};int insertion=first==null?0:kept.IndexOf(first)+1;kept.Insert(insertion,note);
            while(!fits(kept) && checkpoint.Length>256){checkpoint=TextResources.Format("agentContextBudget.prepareAsync.checkpointCheckpointSubstring",checkpoint.Substring(0,checkpoint.Length/2));note["content"]=checkpoint;}
            if(!fits(kept)){workspace.ContextProgress("limited");throw new ApiException(TextResources.Get("agentContextBudget.prepareAsync.text.alternativeC"));}
            history.Clear();history.AddRange(kept);workspace.RecordCompaction(EstimateTokens(history)+overheadTokens,checkpoint);return true;
            }catch(OperationCanceledException){workspace.ContextProgress("cancelled");throw;}
            catch{if(workspace.TaskState.ContextStatus=="compacting")workspace.ContextProgress("limited");throw;}
        }
        static void DigestToolMetadata(Dictionary<string,object> message,StringBuilder digest)
        {
            string resultId=Text(message,"tool_call_id");if(resultId.Length>0)digest.Append("tool_result id=").Append(resultId).Append(": ");object value;
            if(!message.TryGetValue("tool_calls",out value))return;var calls=value as System.Collections.IEnumerable;if(calls==null)return;
            foreach(object raw in calls){var call=Object(raw);object function;if(!call.TryGetValue("function",out function))continue;var data=Object(function);DigestCall(Text(call,"id"),Text(data,"name"),Text(data,"arguments"),digest);if(digest.Length>=48000)break;}
        }
        static void DigestCall(string id,string name,object input,StringBuilder digest)
        {
            string arguments=input as string??new JavaScriptSerializer{MaxJsonLength=1024*1024}.Serialize(input);if(arguments.Length>2048)arguments=TextResources.Format("agentContextBudget.digestCall.argumentsArgumentsSubstring",arguments.Substring(0,2048));
            digest.Append("tool_call id=").Append(id).Append(" name=").Append(name).Append(" arguments=").Append(arguments).AppendLine();
        }
        static bool ContainsGoal(Dictionary<string,object> message,string goal){object content;if(!message.TryGetValue("content",out content))return false;if(content is string)return (string)content==goal;var blocks=content as System.Collections.IEnumerable;return blocks!=null && blocks.Cast<object>().Any(b=>Text(Object(b),"type")=="text" && Text(Object(b),"text")==goal);}
        static void DigestContent(object value,StringBuilder text){if(text.Length>=48000 || value==null)return;if(value is string){string s=(string)value;text.Append(s,0,Math.Min(s.Length,48000-text.Length));return;}var list=value as System.Collections.IEnumerable;if(!(value is Dictionary<string,object>) && list!=null){foreach(object item in list)DigestContent(item,text);return;}var block=Object(value);string type=Text(block,"type");if(type=="tool_use"){DigestCall(Text(block,"id"),Text(block,"name"),block.ContainsKey("input")?block["input"]:null,text);return;}if(type=="tool_result")text.Append("tool_result id=").Append(Text(block,"tool_use_id")).Append(": ");if(type=="image" || type=="image_url" || type=="file" || type=="document"){text.Append(TextResources.Get("agentContextBudget.digestContent.textAppend"));return;}foreach(string key in new[]{"text","thinking","content"}){object part;if(block.TryGetValue(key,out part))DigestContent(part,text);}}
        static bool HasCalls(Dictionary<string,object> message,bool anthropic){object value;if(!anthropic)return message.ContainsKey("tool_calls");if(!message.TryGetValue("content",out value) || value is string)return false;var blocks=value as System.Collections.IEnumerable;return blocks!=null && blocks.Cast<object>().Any(b=>Text(Object(b),"type")=="tool_use");}
        static bool IsResult(Dictionary<string,object> message,bool anthropic){if(!anthropic)return Text(message,"role")=="tool";object value;if(Text(message,"role")!="user" || !message.TryGetValue("content",out value) || value is string)return false;var blocks=value as System.Collections.IEnumerable;return blocks!=null && blocks.Cast<object>().Any(b=>Text(Object(b),"type")=="tool_result");}
        // Replace only completed tool result payloads. IDs, tool calls and all
        // thinking/signature blocks stay intact in their original protocol order.
        internal static int Compact(List<object> history,bool anthropic)
        {
            int retained=0;
            for(int i=0;i<history.Count;i++){
                var message=Object(history[i]);string role=Text(message,"role");
                if(!anthropic && role=="tool"){
                    string output=Text(message,"content");if(i<history.Count-2 && output.Length>1500){message["content"]=Preview(output,Text(message,"tool_call_id"));history[i]=message;}
                    retained+=Text(message,"content").Length;
                }
                if(anthropic && role=="user"){
                    object raw;if(!message.TryGetValue("content",out raw) || raw is string)continue;var array=raw as System.Collections.IEnumerable;if(array==null)continue;var blocks=new List<object>();bool changed=false;
                    foreach(object item in array){var block=Object(item);if(Text(block,"type")=="tool_result"){
                        string output=Text(block,"content");if(i<history.Count-1 && output.Length>1500){block["content"]=Preview(output,Text(block,"tool_use_id"));changed=true;}retained+=Text(block,"content").Length;
                    }blocks.Add(block);}if(changed){message["content"]=blocks;history[i]=message;}
                }
            }
            return retained;
        }
        static string Preview(string text,string id){return TextResources.Format("agentContextBudget.preview.template",text.Substring(0,1000),id);}
        static string Text(Dictionary<string,object> obj,string key){object value;return obj.TryGetValue(key,out value)?value as string??"":"";}
        static Dictionary<string,object> Object(object value)
        {
            var map=value as Dictionary<string,object>;if(map!=null)return map;map=new Dictionary<string,object>();if(value==null)return map;foreach(var p in value.GetType().GetProperties())map[p.Name]=p.GetValue(value,null);return map;
        }
    }
}
