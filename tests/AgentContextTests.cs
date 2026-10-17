using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Dafeiyu.Tests
{
    public static class AgentContextTests
    {
        static int checks;
        static void Check(bool value,string reason){if(!value)throw new Exception(reason);checks++;Console.WriteLine("PASS "+reason);}
        public static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("Agent context tests: "+checks+" passed");return 0;}catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}}
        static async Task Run()
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"context-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            foreach(bool anthropic in new[]{false,true}){
                var workspace=new AgentWorkspace(root){ContextTokenLimit=8192};workspace.BeginTask(new AgentTaskState{Workspace=root,Goal="keep original goal"});
                var history=new List<object>{new{role="system",content="original authority"},new{role="user",content="initial goal"}};
                for(int i=0;i<9;i++){
                    if(anthropic){history.Add(new{role="assistant",content=new object[]{new{type="thinking",thinking=new string('思',3000),signature="sig-"+i},new{type="tool_use",id="call-"+i,name="read_file",input=new{path="file-"+i}}}});history.Add(new{role="user",content=new[]{new{type="tool_result",tool_use_id="call-"+i,content=new string('文',4000)}}});}
                    else{history.Add(new{role="assistant",content=new string('思',3000),tool_calls=new[]{new{id="call-"+i,type="function",function=new{name="read_file",arguments=new JavaScriptSerializer().Serialize(new{path="file-"+i})}}}});history.Add(new{role="tool",tool_call_id="call-"+i,content=new string('文',4000)});}
                }
                history.Add(new{role="user",content="latest instruction stays exact"});int summaries=0;
                bool compacted=await AgentContextBudget.PrepareAsync(history,anthropic,workspace,(text,token)=>{summaries++;Check(text.Contains("read_file") && text.Contains("file-0") && text.Contains("call-0"),"summary input preserves tool name, source path and linked call identity: "+anthropic);return Task.FromResult("Remember decisions and pending validation.");},CancellationToken.None);
                string json=new JavaScriptSerializer().Serialize(history);
                Check(compacted && summaries==1 && AgentContextBudget.EstimateTokens(history)<8192,"large native history compacts before exceeding context budget: "+anthropic);
                Check(json.Contains("original authority") && json.Contains("initial goal") && json.Contains("latest instruction stays exact"),"authority original goal and latest instruction remain exact: "+anthropic);
                Check(!json.Contains("call-0") && json.Contains("call-8") && (!anthropic || json.Contains("sig-8")),"old native groups fold together and recent identities survive: "+anthropic);
                Check(workspace.TaskState.ContextCompactions==1 && workspace.TaskState.ContextSummary.Contains("Remember decisions"),"actual compaction summary is checkpointed: "+anthropic);
            }
            var small=new List<object>{new{role="user",content="small"}};int calls=0;
            Check(!await AgentContextBudget.PrepareAsync(small,false,new AgentWorkspace(root),(text,token)=>{calls++;return Task.FromResult("unused");},CancellationToken.None) && calls==0,"small context does not issue an extra summary request");
            var image=new List<object>{new{role="user",content=new object[]{new{type="text",text="look"},new{type="image_url",image_url=new{url="data:image/png;base64,"+new string('a',2000000)}}}}};
            Check(AgentContextBudget.EstimateTokens(image)<5000,"image base64 is not counted as millions of text tokens");
            var huge=new List<object>{new{role="user",content=new string('字',20000)}};bool rejected=false;
            try{await AgentContextBudget.PrepareAsync(huge,false,new AgentWorkspace(root){ContextTokenLimit=8192},(text,token)=>Task.FromResult("unused"),CancellationToken.None);}catch(ApiException){rejected=true;}
            Check(rejected && new JavaScriptSerializer().Serialize(huge).Contains(new string('字',20000)),"oversized newest instruction is preserved and fails safely");
            var fallback=new List<object>{new{role="user",content="original"}};for(int i=0;i<12;i++)fallback.Add(new{role=i%2==0?"assistant":"user",content=new string('旧',3000)});fallback.Add(new{role="user",content="latest"});
            var w=new AgentWorkspace(root){ContextTokenLimit=8192};w.BeginTask(new AgentTaskState{Workspace=root,Goal="real project facts"});
            Check(await AgentContextBudget.PrepareAsync(fallback,false,w,(text,token)=>{throw new ApiException("summary unavailable");},CancellationToken.None) && w.TaskState.ContextSummary.Contains("摘要生成失败"),"summary failure falls back to marked task facts without inventing a summary");
            foreach(bool anthropic in new[]{false,true}){
                var data=new byte[4*1024*1024];data[0]=137;data[1]=80;data[2]=78;data[3]=71;var attachment=ChatAttachment.CreateImage("large.png",data);
                var old=new ChatMessage("user","old attachment");old.SetAttachments(new[]{attachment});var latest=new ChatMessage("user","latest attachment");latest.SetAttachments(new[]{attachment});
                var bytes=new List<object>{new{role="user",content=(object)"original"},new{role="user",content=ChatAttachment.Content(old,anthropic)},new{role="user",content=ChatAttachment.Content(latest,anthropic)}};
                var budget=new AgentWorkspace(root){ContextTokenLimit=32768};budget.BeginTask(new AgentTaskState{Workspace=root,Goal="original"});int summaries=0;
                Check(AgentContextBudget.EstimateTokens(bytes)<10000 && !AgentContextBudget.FitsWireBytes(bytes),"image history can exceed wire bytes below token threshold: "+anthropic);
                Check(await AgentContextBudget.PrepareAsync(bytes,anthropic,budget,(text,token)=>{summaries++;Check(!text.Contains(attachment.Data.Substring(0,1000)),"old image base64 is excluded from summary input: "+anthropic);return Task.FromResult("old attachment inspected");},CancellationToken.None,items=>AgentContextBudget.FitsWireBytes(items)) && summaries==1 && AgentContextBudget.FitsWireBytes(bytes),"byte-pressure compaction frees an old attachment and keeps latest: "+anthropic);
                var protectedHistory=new List<object>{new{role="user",content=ChatAttachment.Content(old,anthropic)},new{role="user",content=ChatAttachment.Content(latest,anthropic)}};bool limited=false;
                try{await AgentContextBudget.PrepareAsync(protectedHistory,anthropic,budget,(text,token)=>Task.FromResult("unused"),CancellationToken.None,items=>AgentContextBudget.FitsWireBytes(items));}catch(ApiException){limited=true;}
                Check(limited && protectedHistory.Count==2 && budget.TaskState.ContextStatus=="limited","protected oversized attachments fail clearly without silently dropping input: "+anthropic);
            }
            foreach(bool cancelled in new[]{false,true}){
                var pending=new List<object>{new{role="user",content="original"},new{role="assistant",content=new string('旧',20000)},new{role="user",content="latest"}};var budget=new AgentWorkspace(root){ContextTokenLimit=8192};budget.BeginTask(new AgentTaskState{Workspace=root,Goal="original"});bool failed=false;
                try{await AgentContextBudget.PrepareAsync(pending,false,budget,(text,token)=>{if(cancelled)throw new OperationCanceledException();throw new InvalidOperationException("unexpected summary failure");},CancellationToken.None);}catch(Exception){failed=true;}
                Check(failed && budget.TaskState.ContextStatus!="compacting","summary exception clears in-progress compression status: "+cancelled);
            }
            foreach(bool anthropic in new[]{false,true}){
                var goal=new ChatMessage("user","current goal with attachment");goal.SetAttachments(new[]{ChatAttachment.FromText("brief.txt","exact constraints")});var history=new List<object>{new{role="user",content=(object)"older conversation"},new{role="user",content=ChatAttachment.Content(goal,anthropic)},new{role="assistant",content=(object)new string('旧',20000)},new{role="user",content=(object)"latest guide"}};var budget=new AgentWorkspace(root){ContextTokenLimit=8192};budget.BeginTask(new AgentTaskState{Workspace=root,Goal=goal.Content});
                await AgentContextBudget.PrepareAsync(history,anthropic,budget,(text,token)=>Task.FromResult("old narrative summarized"),CancellationToken.None);string actual=new JavaScriptSerializer().Serialize(history);
                Check(actual.Contains("exact constraints"),"the original task and its attachment remain exact even after earlier conversation: "+anthropic);
            }
            foreach(bool anthropic in new[]{false,true}){
                var patch=new{edits=new[]{new{path="large-patch.txt",content=new string('补',20000)}}};var history=new List<object>{new{role="user",content="original"}};
                if(anthropic){history.Add(new{role="assistant",content=new[]{new{type="tool_use",id="patch-id",name="apply_patch",input=(object)patch}}});history.Add(new{role="user",content=new[]{new{type="tool_result",tool_use_id="patch-id",content="applied"}}});history.Add(new{role="assistant",content=new[]{new{type="tool_use",id="recent-id",name="read_file",input=(object)new{path="recent.txt"}}}});history.Add(new{role="user",content=new[]{new{type="tool_result",tool_use_id="recent-id",content="recent"}}});}
                else{history.Add(new{role="assistant",tool_calls=new[]{new{id="patch-id",type="function",function=new{name="apply_patch",arguments=new JavaScriptSerializer().Serialize(patch)}}}});history.Add(new{role="tool",tool_call_id="patch-id",content="applied"});history.Add(new{role="assistant",tool_calls=new[]{new{id="recent-id",type="function",function=new{name="read_file",arguments="{}"}}}});history.Add(new{role="tool",tool_call_id="recent-id",content="recent"});}
                history.Add(new{role="user",content="latest"});var budget=new AgentWorkspace(root){ContextTokenLimit=8192};
                await AgentContextBudget.PrepareAsync(history,anthropic,budget,(text,token)=>{Check(text.Contains("large-patch.txt") && text.Contains("patch-id") && text.Contains("apply_patch") && text.Contains("工具参数已截断") && text.Length<5000,"large patch summary metadata is bounded while preserving its source: "+anthropic);return Task.FromResult("patch applied");},CancellationToken.None);
            }
        }
    }
}
