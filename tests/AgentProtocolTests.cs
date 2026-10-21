using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Dafeiyu.Tests
{
    public static class AgentProtocolTests
    {
        private static int passed,failed;
        public static int Main(string[] args)
        {
            if(args.Length>0 && args[0]=="--wait-command"){File.WriteAllText(args[1],"started");for(int i=0;i<1000;i++){if(File.Exists(args[2])){Console.WriteLine("finished naturally");return 0;}Thread.Sleep(10);}return 9;}
            if(args.Length>0 && args[0]=="--changing-error"){int count=File.Exists("count.txt")?Int32.Parse(File.ReadAllText("count.txt")):0;File.WriteAllText("count.txt",(++count).ToString());Console.Error.WriteLine("diagnostic-"+count);return 1;}
            if(args.Length>0 && args[0]=="--verify-fixture"){Console.WriteLine(File.ReadAllText("verified.txt"));return 0;}
            Run("inspect_image transmits visual blocks on both protocols without logging bytes",async delegate
            {
                foreach(bool anthropic in new[]{false,true})using(var folder=new WorkspaceFolder())
                using(var server=new Fixture(new[]{Json(ToolReply(anthropic,"vision","inspect_image",new{path="visual.png",question="Identify the color"})),Json(FinalReply(anthropic,"The supplied image is red."))}))using(var api=new ApiClient())using(var ws=new AgentWorkspace(folder.Path))
                {
                    string path=System.IO.Path.Combine(folder.Path,"visual.png");using(var image=new System.Drawing.Bitmap(2,2)){using(var g=System.Drawing.Graphics.FromImage(image))g.Clear(System.Drawing.Color.Red);image.Save(path,System.Drawing.Imaging.ImageFormat.Png);}
                    string data=Convert.ToBase64String(File.ReadAllBytes(path));ws.ConfigureProvider(anthropic?"anthropic":"openai",anthropic?"claude":"gpt-4o");ws.BeginTask(new AgentTaskState{Workspace=folder.Path,Goal="Inspect"});
                    var result=await api.ChatAgentAsync(Config(server,anthropic),Messages(),ws,null,null,null,false,CancellationToken.None);await Within(server.Done);
                    Assert(result.Contains("red"),"vision follow-up failed");string body=server.Bodies[1];Assert(body.Contains(data),"image bytes never reached model follow-up");Assert(body.Contains(anthropic?"\"type\":\"image\"":"\"type\":\"image_url\""),"image was serialized as ordinary text");
                    string saved=J(ws.TaskState);Assert(!saved.Contains(data)&&!saved.Contains("base64"),"image bytes entered saved task history");Assert(ws.TakeInspectionResult("vision")==null,"vision side channel retained consumed image");
                }
            });
            Run("guidance preserves the current tool and revises an unstarted tool batch",async delegate
            {
                foreach(bool anthropic in new[]{false,true})using(var folder=new WorkspaceFolder())
                {
                    var first=Object(ToolReply(anthropic,"first","write_file",new{path="kept.txt",content="current operation completed"}));
                    var later=Object(ToolReply(anthropic,"later","write_file",new{path="stale.txt",content="must not run"}));
                    if(anthropic)first["content"]=((object[])first["content"]).Concat((object[])later["content"]).ToArray();
                    else
                    {
                        var one=(Dictionary<string,object>)((Dictionary<string,object>)((object[])first["choices"])[0])["message"];
                        var two=(Dictionary<string,object>)((Dictionary<string,object>)((object[])later["choices"])[0])["message"];
                        one["tool_calls"]=((object[])one["tool_calls"]).Concat((object[])two["tool_calls"]).ToArray();
                    }
                    using(var server=new Fixture(new[]{Json(J(first)),Json(FinalReply(anthropic,"adjusted within original task"))}))using(var api=new ApiClient())
                    {
                        bool inject=false,admitted=false;var workspace=new AgentWorkspace(folder.Path);var task=new AgentTaskState{Workspace=folder.Path,Goal="original task"};workspace.BeginTask(task);var events=new List<AgentToolEvent>();
                        workspace.TakeGuidanceAsync=ct=>{if(!inject||admitted)return Task.FromResult<IList<ChatMessage>>(new List<ChatMessage>());admitted=true;return Task.FromResult<IList<ChatMessage>>(new[]{new ChatMessage("user","CHANGE_DIRECTION_NOW")});};
                        var result=await api.ChatAgentAsync(Config(server,anthropic),Messages(),workspace,null,null,e=>{events.Add(e);if(e.Id=="first"&&e.Status=="running")inject=true;},false,CancellationToken.None);
                        await Within(server.Done);Equal("adjusted within original task",result);
                        Assert(File.Exists(Path.Combine(folder.Path,"kept.txt"))&&!File.Exists(Path.Combine(folder.Path,"stale.txt")),"guidance interrupted the current operation or launched the stale next operation");
                        Assert(events.Any(e=>e.Id=="first"&&e.Status=="complete")&&events.Any(e=>e.Id=="later"&&e.Status=="skipped")&&!events.Any(e=>e.Status=="error"),"guidance was reported as an interrupted or failed tool");
                        Assert(server.Bodies.Count==2&&server.Bodies[1].Contains("CHANGE_DIRECTION_NOW")&&server.Bodies[1].Contains("later"),"next decision did not include guidance and observations for all tool IDs");
                        Equal(task.Id,workspace.TaskState.Id);Equal("original task",workspace.TaskState.Goal);
                    }
                }
            });
            Run("both native protocols write then read with linked observations",async delegate
            {
                foreach(bool anthropic in new[]{false,true})using(var folder=new WorkspaceFolder())
                using(var server=new Fixture(new[]{Json(ToolReply(anthropic,"write","write_file",new {path="answer.md",content="# 完成🌙"})),Json(ToolReply(anthropic,"read","read_file",new {path="answer.md"})),Json(FinalReply(anthropic,"已写入并核对。"))}))
                using(var api=new ApiClient())
                {
                    var events=new List<AgentToolEvent>();string visible="";
                    string result=await api.ChatAgentAsync(Config(server,anthropic),Messages(),new AgentWorkspace(folder.Path),d=>visible+=d,null,e=>{if(e.Status=="running")visible="";events.Add(e);},false,CancellationToken.None);
                    Equal("已写入并核对。",result);Equal(result,visible);Equal("# 完成🌙",File.ReadAllText(System.IO.Path.Combine(folder.Path,"answer.md")));
                    await Within(server.Done);Equal(3,server.Bodies.Count);Equal(4,events.Count);Equal("running",events[0].Status);Equal("complete",events[1].Status);Equal("read",events[2].Id);
                    Assert(!events[1].Summary.Contains("完成🌙"),"tool summary leaked file content");Equal("已写入 answer.md",events[1].Summary);Equal("已读取 answer.md",events[3].Summary);
                    var first=Object(server.Bodies[0]);Assert(first.ContainsKey("tools"),"native schema absent");Equal(false,first["stream"]);
                    var third=Object(server.Bodies[2]);string wire=J(third["messages"]);
                    Assert(wire.Contains("完成🌙") || wire.Contains("完成"),"read observation omitted");
                    Assert(wire.Contains(anthropic?"tool_use_id":"tool_call_id"),"observations not linked to native calls");
                    Assert(server.Headers[0].Contains(anthropic?"x-api-key: fixture-key":"Authorization: Bearer fixture-key"),"auth not reused");
                    Assert(server.Headers[0].StartsWith(anthropic?"POST /v1/messages ":"POST /v1/chat/completions "),"wrong native endpoint");
                }
            });
            Run("streaming arguments assemble across chunks and provisional text moves before running",async delegate
            {
                foreach(bool anthropic in new[]{false,true})using(var folder=new WorkspaceFolder())
                {
                    using(var server=new Fixture(new[]{new Reply(ToolStream(anthropic,"# streaming🌙"),ToolTerminal(anthropic),true),new Reply(FinalStreamStart(anthropic),FinalTerminal(anthropic),true)}))using(var api=new ApiClient())
                    {
                        string answer="",moved="",thinking="";var events=new List<AgentToolEvent>();
                        var firstText=new TaskCompletionSource<bool>();var finalText=new TaskCompletionSource<bool>();
                        Task<string> pending=api.ChatAgentAsync(Config(server,anthropic),Messages(),new AgentWorkspace(folder.Path),d=>{answer+=d;if(d.Contains("检查"))firstText.TrySetResult(true);if(d.Contains("最终"))finalText.TrySetResult(true);},d=>thinking+=d,e=>{if(e.Status=="running"){moved+=answer;answer="";}events.Add(e);},true,CancellationToken.None);
                        await Within(firstText.Task);Assert(!pending.IsCompleted,"not streaming before tool terminal");Assert(!File.Exists(System.IO.Path.Combine(folder.Path,"stream.md")),"executed incomplete tool call");server.Release(0);
                        await Within(finalText.Task);Assert(!pending.IsCompleted,"final answer buffered until stream completed");Equal("先检查。",moved);Assert(answer.StartsWith("最终"),"final answer did not stream");Equal(2,events.Count);Equal("# streaming🌙",File.ReadAllText(System.IO.Path.Combine(folder.Path,"stream.md")));server.Release(1);
                        Equal("最终答复🌙",await pending);Equal("最终答复🌙",answer);Equal("先推理。",thinking);await Within(server.Done);
                        string history=server.Bodies[1];Assert(history.Contains("stream.md") && history.Contains(anthropic?"tool_result":"tool_call_id"),"stream native history lost");
                        Assert(history.Contains(anthropic?"signature":"reasoning_content"),"reasoning history lost");
                    }
                }
            });
            Run("stream JSON compatibility performs tools without a duplicate request",async delegate
            {
                foreach(bool anthropic in new[]{false,true})using(var folder=new WorkspaceFolder())using(var server=new Fixture(new[]{Json(ToolReply(anthropic,"a","write_file",new {path="json.txt",content="json"})),Json(FinalReply(anthropic,"JSON 完成"))}))using(var api=new ApiClient())
                {
                    Equal("JSON 完成",await api.ChatAgentAsync(Config(server,anthropic),Messages(),new AgentWorkspace(folder.Path),null,null,null,true,CancellationToken.None));await Within(server.Done);Equal(2,server.Bodies.Count);Equal("json",File.ReadAllText(System.IO.Path.Combine(folder.Path,"json.txt")));
                }
            });
            Run("disabled tools are never advertised or executed",async delegate
            {
                foreach(bool anthropic in new[]{false,true})using(var server=new Fixture(new[]{Json(ToolReply(anthropic,"a","write_file",new {path="forbidden.txt",content="no"}))}))using(var api=new ApiClient())
                {
                    int events=0;await Fails(()=>api.ChatAgentAsync(Config(server,anthropic),Messages(),null,null,null,e=>events++,false,CancellationToken.None),"未启用");Equal(0,events);await Within(server.Done);Assert(!Object(server.Bodies[0]).ContainsKey("tools"),"disabled schema advertised");
                }
            });
            Run("cancellation while arguments are incomplete never executes tools",async delegate
            {
                foreach(bool anthropic in new[]{false,true})using(var folder=new WorkspaceFolder())using(var server=new Fixture(new[]{new Reply(ToolStream(anthropic,"not-written"),ToolTerminal(anthropic),true)}))using(var api=new ApiClient())using(var cancellation=new CancellationTokenSource())
                {
                    var arrived=new TaskCompletionSource<bool>();int events=0;
                    Task<string> pending=api.ChatAgentAsync(Config(server,anthropic),Messages(),new AgentWorkspace(folder.Path),d=>arrived.TrySetResult(true),null,e=>events++,true,cancellation.Token);
                    await Within(arrived.Task);cancellation.Cancel();try{await Within(pending);throw new Exception("cancel ignored");}catch(OperationCanceledException){}
                    Equal(0,events);Assert(!File.Exists(System.IO.Path.Combine(folder.Path,"stream.md")),"cancelled tool executed");
                }
            });
            Run("caller cancellation from running event prevents execution",async delegate
            {
                using(var folder=new WorkspaceFolder())using(var server=new Fixture(new[]{Json(ToolReply(false,"a","write_file",new {path="cancel.txt",content="no"}))}))using(var api=new ApiClient())using(var cancellation=new CancellationTokenSource())
                {
                    try{await api.ChatAgentAsync(Config(server,false),Messages(),new AgentWorkspace(folder.Path),null,null,e=>{if(e.Status=="running")cancellation.Cancel();},false,cancellation.Token);throw new Exception("cancel ignored");}catch(OperationCanceledException){}
                    Assert(!File.Exists(System.IO.Path.Combine(folder.Path,"cancel.txt")),"running-event cancellation ignored");
                }
            });
            Run("stream truncation and token-limited calls are rejected before execution",async delegate
            {
                foreach(bool anthropic in new[]{false,true})foreach(bool limited in new[]{false,true})using(var folder=new WorkspaceFolder())
                {
                    string body=ToolReply(anthropic,"x","write_file",new {path="bad.txt",content="no"});body=body.Replace(anthropic?"\"stop_reason\":\"tool_use\"":"\"finish_reason\":\"tool_calls\"",anthropic?"\"stop_reason\":\"max_tokens\"":"\"finish_reason\":\"length\"");
                    Reply reply=limited?Json(body):new Reply(ToolStream(anthropic,"no"),"",false);
                    using(var server=new Fixture(new[]{reply}))using(var api=new ApiClient())
                    {await Fails(()=>api.ChatAgentAsync(Config(server,anthropic),Messages(),new AgentWorkspace(folder.Path),null,null,null,!limited,CancellationToken.None),limited?"限制":"中断");Assert(!File.Exists(System.IO.Path.Combine(folder.Path,limited?"bad.txt":"stream.md")),"incomplete tool executed");}
                }
            });
            Run("failed tools produce error events and native error observations",async delegate
            {
                foreach(bool anthropic in new[]{false,true})using(var folder=new WorkspaceFolder())using(var server=new Fixture(new[]{Json(ToolReply(anthropic,"bad","read_file",new {path="missing.txt"})),Json(FinalReply(anthropic,"文件不存在。"))}))using(var api=new ApiClient())
                {
                    var events=new List<AgentToolEvent>();Equal("文件不存在。",await api.ChatAgentAsync(Config(server,anthropic),Messages(),new AgentWorkspace(folder.Path),null,null,events.Add,false,CancellationToken.None));await Within(server.Done);Equal("error",events[1].Status);Assert(server.Bodies[1].Contains("错误："),"tool failure observation absent");if(anthropic)Assert(server.Bodies[1].Contains("\"is_error\":true"),"Anthropic is_error absent");
                }
            });
            Run("native task continues beyond the previous default turn cap",async delegate
            {
                using(var folder=new WorkspaceFolder()){
                    var replies=new List<Reply>();for(int i=0;i<16;i++)replies.Add(Json(ToolReply(false,"call-"+i,"list_files",new{path="."})));replies.Add(Json(FinalReply(false,"continued")));
                    using(var server=new Fixture(replies.ToArray()))using(var api=new ApiClient()){Equal("continued",await api.ChatAgentAsync(Config(server,false),Messages(),new AgentWorkspace(folder.Path),null,null,null,false,CancellationToken.None));await Within(server.Done);Equal(17,server.Bodies.Count);}
                }
            });
            Run("malformed tool JSON and duplicate IDs are never executed",async delegate
            {
                foreach(string body in new[]{"{\"choices\":[{\"finish_reason\":\"tool_calls\",\"message\":{\"content\":null,\"tool_calls\":[{\"id\":\"x\",\"type\":\"function\",\"function\":{\"name\":\"write_file\",\"arguments\":\"{broken\"}}]}}]}",DuplicateCalls()})
                    using(var folder=new WorkspaceFolder())using(var server=new Fixture(new[]{Json(body)}))using(var api=new ApiClient())
                    {int events=0;await Fails(()=>api.ChatAgentAsync(Config(server,false),Messages(),new AgentWorkspace(folder.Path),null,null,e=>events++,false,CancellationToken.None),"未执行");Equal(0,events);}
            });
            Run("usage budget stops tools and request timeout interrupts incomplete streams",async delegate
            {
                foreach(bool anthropic in new[]{false,true})using(var folder=new WorkspaceFolder())
                {
                    var root=Object(ToolReply(anthropic,"budget","write_file",new {path="budget.txt",content="no"}));root["usage"]=anthropic?(object)new {input_tokens=65500,output_tokens=100}:new {total_tokens=65600};
                    using(var server=new Fixture(new[]{Json(J(root))}))using(var api=new ApiClient())
                    {int events=0;await Fails(()=>api.ChatAgentAsync(Config(server,anthropic),Messages(),new AgentWorkspace(folder.Path),null,null,e=>events++,false,CancellationToken.None),"Token");Equal(0,events);Assert(!File.Exists(System.IO.Path.Combine(folder.Path,"budget.txt")),"budget exhausted call executed");}
                    using(var server=new Fixture(new[]{new Reply(ToolStream(anthropic,"no"),ToolTerminal(anthropic),true)}))using(var api=new ApiClient(TimeSpan.FromMilliseconds(500)))
                    {int events=0;await Fails(()=>api.ChatAgentAsync(Config(server,anthropic),Messages(),new AgentWorkspace(folder.Path),null,null,e=>events++,true,CancellationToken.None),"超时");Equal(0,events);Assert(!File.Exists(System.IO.Path.Combine(folder.Path,"stream.md")),"timed-out tool executed");}
                }
            });
            Run("native task continues past the former maximum of 48 model rounds",async delegate
            {
                using(var folder=new WorkspaceFolder()){
                    var replies=new List<Reply>();for(int i=0;i<50;i++)replies.Add(Json(ToolReply(false,"long-"+i,"list_files",new{path="."})));replies.Add(Json(FinalReply(false,"long task completed")));
                    using(var server=new Fixture(replies.ToArray()))using(var api=new ApiClient()){Equal("long task completed",await api.ChatAgentAsync(Config(server,false),Messages(),new AgentWorkspace(folder.Path),null,null,null,false,CancellationToken.None));await Within(server.Done);Equal(51,server.Bodies.Count);}
                }
            });
            Run("full access prompt contains real directories and absolute file tools have accurate summaries",async delegate
            {
                foreach(bool anthropic in new[]{false,true})using(var folder=new WorkspaceFolder())using(var outside=new WorkspaceFolder())
                {
                    string directory=System.IO.Path.Combine(outside.Path,"created"),file=System.IO.Path.Combine(directory,"result.md");
                    using(var server=new Fixture(new[]{Json(ToolReply(anthropic,"mkdir","create_directory",new {path=directory})),Json(ToolReply(anthropic,"write","write_file",new {path=file,content="private-content-must-not-be-a-summary"})),Json(ToolReply(anthropic,"open","open_directory",new {path=directory})),Json(FinalReply(anthropic,"完成"))}))using(var api=new ApiClient())
                    {
                        var workspace=new AgentWorkspace(folder.Path,false);var events=new List<AgentToolEvent>();string opened=null;workspace.DirectoryOpener=p=>opened=p;
                        Equal("完成",await api.ChatAgentAsync(Config(server,anthropic),Messages(),workspace,null,null,events.Add,false,CancellationToken.None));await Within(server.Done);
                        Equal("private-content-must-not-be-a-summary",File.ReadAllText(file));Equal("已创建目录 "+(directory.Length>110?directory.Substring(0,110)+"…":directory),events[1].Summary);Equal("已写入 "+(file.Length>110?file.Substring(0,110)+"…":file),events[3].Summary);Equal("已打开目录 "+(directory.Length>110?directory.Substring(0,110)+"…":directory),events[5].Summary);Equal(directory,opened);
                        Assert(!events[3].Summary.Contains("private-content"),"summary leaked write content");
                        var request=Object(server.Bodies[0]);string prompt=anthropic?(string)request["system"]:(string)((Dictionary<string,object>)((object[])request["messages"])[0])["content"];
                        Assert(prompt.Contains("完整访问") && prompt.Contains("相对路径"),"full access scope missing");
                        int start=prompt.IndexOf('{'),end=prompt.LastIndexOf('}');Assert(start>=0 && end>start,"real path JSON missing");var paths=Object(prompt.Substring(start,end-start+1));
                        Equal(folder.Path,paths["WorkspaceRoot"]);Equal(System.IO.Path.GetPathRoot(folder.Path),paths["WorkspaceDrive"]);
                        Equal(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),paths["DesktopDirectory"]);Equal(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),paths["MyDocuments"]);Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),paths["UserProfile"]);
                        Assert(anthropic?request.ContainsKey("max_tokens"):!request.ContainsKey("max_tokens"),"max_tokens compatibility regressed");
                    }
                }
            });
            Run("strict scope and plan instructions prohibit creation opening and writing",async delegate
            {
                foreach(bool anthropic in new[]{false,true})using(var folder=new WorkspaceFolder())using(var server=new Fixture(new[]{Json(FinalReply(anthropic,"只读计划"))}))using(var api=new ApiClient())
                {
                    var workspace=new AgentWorkspace(folder.Path){PlanMode=true};Equal("只读计划",await api.ChatAgentAsync(Config(server,anthropic),Messages(),workspace,null,null,null,false,CancellationToken.None));await Within(server.Done);
                    var request=Object(server.Bodies[0]);string prompt=anthropic?(string)request["system"]:J(request["messages"]);
                    Assert(prompt.Contains("计划模式") && prompt.Contains("工作区及其子目录"),"plan/scope instruction missing");
                    Assert(prompt.Contains("create_directory") && prompt.Contains("open_directory") && prompt.Contains("write_file") && prompt.Contains("edit_file"),"plan side-effect prohibitions missing");
                    string schemas=J(request["tools"]);Assert(!schemas.Contains("create_directory") && !schemas.Contains("open_directory") && !schemas.Contains("write_file") && !schemas.Contains("edit_file"),"plan advertised side-effect tools");
                }
            });
            Run("protocol rejects an oversized batch before any tool runs",async delegate
            {
                using(var folder=new WorkspaceFolder())
                {
                    var calls=new List<object>();for(int i=0;i<17;i++)calls.Add(new {id="batch-"+i,type="function",function=new {name="list_files",arguments="{}"}});
                    string body=J(new {choices=new[]{new {finish_reason="tool_calls",message=new {content=(string)null,tool_calls=calls}}}});
                    using(var server=new Fixture(new[]{Json(body)}))using(var api=new ApiClient())
                    {int events=0;var workspace=new AgentWorkspace(folder.Path);await Fails(()=>api.ChatAgentAsync(Config(server,false),Messages(),workspace,null,null,e=>events++,false,CancellationToken.None),"未执行");Equal(0,events);await Within(server.Done);Equal(1,server.Bodies.Count);}
                }
            });
            Run("both protocols patch actual files then execute an authorized verification",async delegate
            {
                foreach(bool anthropic in new[]{false,true})using(var folder=new WorkspaceFolder())using(var server=new Fixture(new[]{Json(ToolReply(anthropic,"patch","apply_patch",new{edits=new[]{new{path="verified.txt",content="verified-content"}}})),Json(ToolReply(anthropic,"command","exec_command",new{command="Get-Content verified.txt",working_directory=folder.Path,timeout_seconds=5})),Json(FinalReply(anthropic,"verified"))}))using(var api=new ApiClient())
                {
                    var workspace=new AgentWorkspace(folder.Path);Terminal(workspace);
                    Equal("verified",await api.ChatAgentAsync(Config(server,anthropic),Messages(),workspace,null,null,null,false,CancellationToken.None));await Within(server.Done);Equal(0,workspace.CommandResults[0].ExitCode);Assert(workspace.CommandResults[0].Output.Contains("verified-content"),"command did not read the patched file");Assert(server.Bodies[2].Contains("verified-content"),"real command observation absent");Equal(1,workspace.TaskState.Changes.Count);
                }
            });
            Run("successful edits reset the repeated-error streak",async delegate
            {
                foreach(bool anthropic in new[]{false,true})using(var folder=new WorkspaceFolder())using(var server=new Fixture(new[]{Json(ToolReply(anthropic,"a","read_file",new{path="missing"})),Json(ToolReply(anthropic,"fix1","write_file",new{path="fix.txt",content="one"})),Json(ToolReply(anthropic,"b","read_file",new{path="missing"})),Json(ToolReply(anthropic,"fix2","write_file",new{path="fix.txt",content="two"})),Json(ToolReply(anthropic,"c","read_file",new{path="missing"})),Json(FinalReply(anthropic,"progress preserved"))}))using(var api=new ApiClient())
                {Equal("progress preserved",await api.ChatAgentAsync(Config(server,anthropic),Messages(),new AgentWorkspace(folder.Path),null,null,null,false,CancellationToken.None));await Within(server.Done);Equal("two",File.ReadAllText(Path.Combine(folder.Path,"fix.txt")));}
            });
            Run("different real command diagnostics reset the error streak",async delegate
            {
                foreach(bool anthropic in new[]{false,true})using(var folder=new WorkspaceFolder())using(var server=new Fixture(new[]{Json(ToolReply(anthropic,"a","exec_command",new{command="& '"+System.Reflection.Assembly.GetExecutingAssembly().Location.Replace("'","''")+"' --changing-error",working_directory=folder.Path,timeout_seconds=5})),Json(ToolReply(anthropic,"b","exec_command",new{command="& '"+System.Reflection.Assembly.GetExecutingAssembly().Location.Replace("'","''")+"' --changing-error",working_directory=folder.Path,timeout_seconds=5})),Json(ToolReply(anthropic,"c","exec_command",new{command="& '"+System.Reflection.Assembly.GetExecutingAssembly().Location.Replace("'","''")+"' --changing-error",working_directory=folder.Path,timeout_seconds=5})),Json(FinalReply(anthropic,"new diagnostics retained"))}))using(var api=new ApiClient())
                {var workspace=new AgentWorkspace(folder.Path);Terminal(workspace);Equal("new diagnostics retained",await api.ChatAgentAsync(Config(server,anthropic),Messages(),workspace,null,null,null,false,CancellationToken.None));await Within(server.Done);Assert(workspace.CommandResults[2].Output.Contains("diagnostic-3"),"changing command errors lost");}
            });
            Run("repeated identical errors stop after three real protocol rounds",async delegate
            {
                foreach(bool anthropic in new[]{false,true})using(var folder=new WorkspaceFolder())using(var server=new Fixture(new[]{Json(ToolReply(anthropic,"a","read_file",new{path="missing"})),Json(ToolReply(anthropic,"b","read_file",new{path="missing"})),Json(ToolReply(anthropic,"c","read_file",new{path="missing"}))}))using(var api=new ApiClient())
                {await Fails(()=>api.ChatAgentAsync(Config(server,anthropic),Messages(),new AgentWorkspace(folder.Path),null,null,null,false,CancellationToken.None),"重复失败三次");await Within(server.Done);Equal(3,server.Bodies.Count);}
            });
            Run("context compaction retains native tool identities and thinking signatures",delegate
            {
                var history=new List<object>{new{role="assistant",content="call"},new{role="tool",tool_call_id="tool-1",content=new string('x',20000)},new{role="assistant",content="newer"},new{role="user",content="continue"}};
                AgentContextBudget.Compact(history,false);var reduced=Object(J(history[1]));Equal("tool-1",reduced["tool_call_id"]);Assert(((string)reduced["content"]).Length<1500,"old tool output not reduced");
                var signature=new{type="thinking",thinking="opaque",signature="signature-1"};var blocks=new List<object>{signature,new{type="tool_result",tool_use_id="anthropic-1",content=new string('y',20000)}};
                history=new List<object>{new{role="user",content=blocks},new{role="assistant",content="next"}};AgentContextBudget.Compact(history,true);string compact=J(history);Assert(compact.Contains("signature-1") && compact.Contains("anthropic-1") && compact.Contains("read_output"),"native content identity lost");return Task.FromResult(0);
            });
            Run("safe guidance joins the same task at a real file-tool boundary without cancellation",async delegate
            {
                foreach(bool anthropic in new[]{false,true})using(var folder=new WorkspaceFolder())using(var server=new Fixture(new[]{new Reply(ToolReply(anthropic,"files","list_files",new{path="."}),"",true,"application/json"),Json(ToolReply(anthropic,"edit","write_file",new{path="guided.txt",content="guided"})),Json(FinalReply(anthropic,"done after guidance"))}))using(var api=new ApiClient()){
                    var cfg=new PetSettings{AgentEnabled=true,AgentWorkspace=folder.Path,ReadReplies=false,Text=Config(server,anthropic)};cfg.Text.StreamReplies=false;
                    using(var session=new ConversationSession(api,new SilentSpeech(),()=>null,new HistoryStore(Path.Combine(folder.Path,"history"))))using(var queue=new ConversationQueue(session,()=>cfg,()=>"fixture",Path.Combine(folder.Path,"queue.json"))){
                        queue.Enqueue("original task");await WaitCondition(()=>server.Bodies.Count==1);string id=session.CurrentAgentTask.Id;queue.Hold();queue.Enqueue("later ordinary request");await queue.SteerAsync("only change guided.txt and retain original task");
                        Assert(session.IsBusy && session.CurrentAgentTask.Id==id,"guidance interrupted the active request");server.Release(0);await WaitCondition(()=>!session.IsBusy && !queue.IsRunning);await session.FlushHistoryAsync();await Within(server.Done);
                        Equal(id,session.CurrentAgentTask.Id);Equal(0,session.CurrentAgentTask.Commands.Count);Assert(server.Bodies[1].Contains("only change guided.txt"),"next native request lacked guidance");Assert(queue.Items.Count==1 && queue.Items[0].Text=="later ordinary request","guidance duplicated or consumed an unrelated queued request");Assert(session.History.Messages.All(m=>m.Status!="cancelled"),"guide marked work cancelled");Equal("guided",File.ReadAllText(Path.Combine(folder.Path,"guided.txt")));
                    }
                }
            });
            Run("oversized history is summarized before the native model request",async delegate
            {
                foreach(bool anthropic in new[]{false,true})using(var folder=new WorkspaceFolder())using(var server=new Fixture(new[]{Json(FinalReply(anthropic,"prior decisions summarized")),Json(FinalReply(anthropic,"task continued"))}))using(var api=new ApiClient()){
                    var messages=new List<ChatMessage>{new ChatMessage("user","initial goal")};for(int i=0;i<12;i++)messages.Add(new ChatMessage(i%2==0?"assistant":"user",new string('旧',2000)));messages.Add(new ChatMessage("user","latest exact request"));var w=new AgentWorkspace(folder.Path){ContextTokenLimit=8192};w.BeginTask(new AgentTaskState{Workspace=folder.Path,Goal="latest exact request"});Equal("task continued",await api.ChatAgentAsync(Config(server,anthropic),messages,w,null,null,null,false,CancellationToken.None));await Within(server.Done);Assert(!Object(server.Bodies[0]).ContainsKey("tools"),"summary request exposed tools");Assert(server.Bodies[1].Contains("prior decisions summarized") && server.Bodies[1].Contains("latest exact request"),"compacted request lost summary or current intent");Equal(1,w.TaskState.ContextCompactions);
                }
            });
            Run("oversized guidance is retained before native request or checkpoint overflow",async delegate
            {
                foreach(bool anthropic in new[]{false,true})foreach(bool checkpoint in new[]{false,true})using(var folder=new WorkspaceFolder())using(var server=new Fixture(new[]{new Reply(FinalReply(anthropic,"current round complete"),"",true,"application/json")}))using(var api=new ApiClient()){
                    var data=new byte[4*1024*1024];data[0]=137;data[1]=80;data[2]=78;data[3]=71;var image=ChatAttachment.CreateImage("large.png",data);var cfg=new PetSettings{AgentEnabled=true,AgentWorkspace=folder.Path,ReadReplies=false,Text=Config(server,anthropic)};cfg.Text.StreamReplies=false;
                    using(var session=new ConversationSession(api,new SilentSpeech(),()=>null,new HistoryStore(Path.Combine(folder.Path,"history"))))using(var queue=new ConversationQueue(session,()=>cfg,()=>"fixture",Path.Combine(folder.Path,"queue.json"))){
                        var work=session.SendTextAsync("original task",cfg,"fixture",attachments:checkpoint?null:new[]{image});await WaitCondition(()=>server.Bodies.Count==1);queue.Hold();await queue.SteerAsync("first large guide",attachments:new[]{image});if(checkpoint)await queue.SteerAsync("second large guide",attachments:new[]{image});server.Release(0);bool blocked=false;
                        try{await work;}catch(ApiException e){blocked=e.Message.Contains(checkpoint?"检查点容量":"单次请求预算");}await session.FlushHistoryAsync();await Within(server.Done);
                        Assert(blocked,"oversized guidance did not report its bounded storage/request limit");Equal(1,queue.Items.Count);Equal(checkpoint?"second large guide":"first large guide",queue.Items.Single().Text);Equal(image.Data,queue.Items.Single().Attachments.Single().Data);Equal(checkpoint?1:0,session.CurrentAgentTask.Guidance.Count);Equal(1,server.Bodies.Count);Assert(String.IsNullOrEmpty(session.AgentTaskWarning),"overflow produced an unsaved task instead of retaining queued inputs");
                        if(checkpoint){var task=new AgentTaskStore(Path.Combine(folder.Path,"history","agent-tasks")).Load("",folder.Path);Equal(image.Data,task.Guidance.Single().Attachments.Single().Data);}
                    }
                }
            });
            Run("failed link tools reach the model without returning the conversation to the queue",async delegate
            {
                foreach(bool anthropic in new[]{false,true})using(var folder=new WorkspaceFolder())
                using(var server=new Fixture(new[]{Json(ToolReply(anthropic,"web","fetch_url",new{url="https://example.test/page"})),Json(FinalReply(anthropic,"无法读取网页，请提供正文。"))}))
                using(var api=new LinkApi())
                {
                    var cfg=new PetSettings{AgentEnabled=true,AgentApprovalMode=AgentApprovalMode.Full,AgentWorkspace=folder.Path,ReadReplies=false,Text=Config(server,anthropic)};cfg.Text.StreamReplies=false;
                    using(var session=new ConversationSession(api,new SilentSpeech(),()=>null))
                    using(var queue=new ConversationQueue(session,()=>cfg,()=>"fixture",Path.Combine(folder.Path,"queue.json")))
                    {
                        queue.Enqueue("请分析 https://example.test/page");await WaitCondition(()=>!queue.IsRunning&&!session.IsBusy);
                        Assert(!queue.IsPaused&&queue.Items.Count==0,"failed link was forced back into the paused queue: "+queue.Notice);
                        var reply=session.History.Messages.Last();Equal("complete",reply.Status);Equal("无法读取网页，请提供正文。",reply.Content);
                        Assert(reply.ToolEvents.Any(e=>e.Id=="web"&&e.Status=="error"),"failed fetch remained a running tool");
                        await Within(server.Done);Equal(2,server.Bodies.Count);
                        Assert(server.Bodies[1].Contains("错误：")&&server.Bodies[1].Contains(anthropic?"tool_use_id":"tool_call_id"),"model did not receive a linked tool error");
                    }
                }
            });
            Console.WriteLine("AgentProtocolTests: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
        }

        sealed class LinkHandler:System.Net.Http.HttpMessageHandler
        {
            protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request,CancellationToken token)
            {return Task.FromResult(new System.Net.Http.HttpResponseMessage(HttpStatusCode.Forbidden){Content=new System.Net.Http.StringContent("private error body")});}
        }
        sealed class LinkApi:IConversationApi,IAgentConversationApi
        {
            readonly ApiClient native=new ApiClient();
            public Task<string> ChatAgentAsync(ConnectionSettings c,IList<ChatMessage> m,AgentWorkspace w,Action<string>d,Action<string>r,Action<AgentToolEvent>t,bool stream,CancellationToken token)
            {w.ConfigureGitHub(new GitHubClient(new Uri("https://api.github.com"),new LinkHandler(),"",""));return native.ChatAgentAsync(c,m,w,d,r,t,stream,token);}
            public Task<string> ChatAsync(ConnectionSettings c,IList<ChatMessage>m,CancellationToken t){return native.ChatAsync(c,m,t);}
            public Task<string> TranscribeAsync(ConnectionSettings c,byte[]b,CancellationToken t){throw new NotSupportedException();}
            public Task<byte[]> SpeechAsync(ConnectionSettings c,string v,string s,CancellationToken t){throw new NotSupportedException();}
            public void Dispose(){native.Dispose();}
        }

        sealed class TerminalApproval:IAgentApprovalProvider {public Task<bool> ApproveAsync(AgentApprovalRequest request){return Task.FromResult(true);}}
        static void Terminal(AgentWorkspace workspace){workspace.ConfigureKernel(AgentPermissionRuntime.DefaultWorkspacePolicy(),null,new TerminalApproval(),null,"protocol");workspace.ConfigureTerminal(AgentTerminalPolicy.ConfirmEach);workspace.ConfigureDesktopSession();}
        static async Task WaitCondition(Func<bool> done){for(int i=0;i<1500;i++){if(done())return;await Task.Delay(10);}throw new Exception("agent integration timed out");}
        sealed class SilentSpeech:IReplySpeech{public Task SpeakAsync(string text,PetSettings settings,CancellationToken cancel){return Task.FromResult(0);}public void SetVolume(int n){}public void Stop(){}public void Dispose(){}}
        private static string DuplicateCalls()
        {var function=new {name="list_files",arguments="{}"};return J(new {choices=new[]{new {finish_reason="tool_calls",message=new {content=(string)null,tool_calls=new[]{new {id="same",type="function",function=function},new {id="same",type="function",function=function}}}}}});}
        private static IList<ChatMessage> Messages(){return new[]{new ChatMessage("user","完成工作区任务")};}
        private static ConnectionSettings Config(Fixture server,bool anthropic){return new ConnectionSettings{BaseUrl="http://127.0.0.1:"+server.Port+"/v1",ApiKey="fixture-key",Model="fixture-model",Protocol=anthropic?"anthropic":"openai"};}
        private static string J(object value){return new JavaScriptSerializer().Serialize(value);}
        private static Dictionary<string,object> Object(string value){return (Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(value);}
        private static string ToolReply(bool anthropic,string id,string name,object args)
        {
            return anthropic?J(new {content=new[]{new {type="tool_use",id=id,name=name,input=args}},stop_reason="tool_use"}):J(new {choices=new[]{new {message=new {role="assistant",content=(string)null,tool_calls=new[]{new {id=id,type="function",function=new {name=name,arguments=J(args)}}}},finish_reason="tool_calls"}}});
        }
        private static string FinalReply(bool anthropic,string text){return anthropic?J(new {content=new[]{new {type="text",text=text}},stop_reason="end_turn"}):J(new {choices=new[]{new {message=new {content=text},finish_reason="stop"}}});}
        private static Reply Json(string value){return new Reply(value,"",false,"application/json");}
        private static string Event(object value){return "data: "+J(value)+"\r\n\r\n";}
        private static string ToolStream(bool anthropic,string content)
        {
            string arguments=J(new {path="stream.md",content=content});int split=arguments.Length/2;
            if(anthropic)return Event(new {type="content_block_start",index=0,content_block=new {type="thinking",thinking="先推理。",signature="sig"}})+Event(new {type="content_block_stop",index=0})+
                Event(new {type="content_block_start",index=1,content_block=new {type="text",text="先检查。"}})+Event(new {type="content_block_stop",index=1})+
                Event(new {type="content_block_start",index=2,content_block=new {type="tool_use",id="stream-call",name="write_file",input=new {}}})+
                Event(new {type="content_block_delta",index=2,delta=new {type="input_json_delta",partial_json=arguments.Substring(0,split)}})+Event(new {type="content_block_delta",index=2,delta=new {type="input_json_delta",partial_json=arguments.Substring(split)}});
            return Event(new {choices=new[]{new {delta=new {content="先检查。",reasoning_content="先推理。"}}}})+
                Event(new {choices=new[]{new {delta=new {tool_calls=new[]{new {index=0,id="stream-call",type="function",function=new {name="write_",arguments=arguments.Substring(0,split)}}}}}}})+
                Event(new {choices=new[]{new {delta=new {tool_calls=new[]{new {index=0,function=new {name="file",arguments=arguments.Substring(split)}}}}}}});
        }
        private static string ToolTerminal(bool anthropic){return anthropic?Event(new {type="content_block_stop",index=2})+Event(new {type="message_delta",delta=new {stop_reason="tool_use"},usage=new {output_tokens=30}})+Event(new {type="message_stop"}):Event(new {choices=new[]{new {delta=new {},finish_reason="tool_calls"}}})+"data: [DONE]\n\n";}
        private static string FinalStreamStart(bool anthropic){return anthropic?Event(new {type="content_block_start",index=0,content_block=new {type="text",text="最终"}})+Event(new {type="content_block_delta",index=0,delta=new {type="text_delta",text="答复🌙"}}):Event(new {choices=new[]{new {delta=new {content="最终答复🌙"}}}});}
        private static string FinalTerminal(bool anthropic){return anthropic?Event(new {type="content_block_stop",index=0})+Event(new {type="message_delta",delta=new {stop_reason="end_turn"}})+Event(new {type="message_stop"}):Event(new {choices=new[]{new {delta=new {},finish_reason="stop"}}})+"data: [DONE]\n\n";}
        private static void Run(string name,Func<Task> test){try{test().GetAwaiter().GetResult();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e);}}
        private static void Assert(bool value,string message){if(!value)throw new Exception(message);}
        private static void Equal(object expected,object actual){if(!System.Object.Equals(expected,actual))throw new Exception("expected "+expected+", actual "+actual);}
        private static async Task Fails(Func<Task<string>> action,string text){try{await action();throw new Exception("expected failure");}catch(ApiException e){Assert(e.Message.Contains(text),"wrong failure: "+e.Message);}}
        private static async Task<T> Within<T>(Task<T> task){if(await Task.WhenAny(task,Task.Delay(6000))!=task)throw new Exception("fixture timeout");return await task;}
        private static async Task Within(Task task){if(await Task.WhenAny(task,Task.Delay(6000))!=task)throw new Exception("fixture timeout");await task;}

        private sealed class WorkspaceFolder : IDisposable
        {
            internal readonly string Path=System.IO.Path.GetFullPath(System.IO.Path.Combine("work","agent-protocol-"+Guid.NewGuid().ToString("N")));
            internal WorkspaceFolder(){Directory.CreateDirectory(Path);}
            public void Dispose(){Directory.Delete(Path,true);}
        }
        private sealed class Reply
        {
            internal string First,Tail,Type;internal bool Hold;internal TaskCompletionSource<bool> Released=new TaskCompletionSource<bool>();
            internal Reply(string first,string tail,bool hold,string type="text/event-stream"){First=first;Tail=tail;Hold=hold;Type=type;}
        }
        private sealed class Fixture : IDisposable
        {
            private readonly TcpListener listener;private readonly Reply[] replies;
            internal readonly List<string> Bodies=new List<string>(),Headers=new List<string>();internal readonly Task Done;internal int Port;
            internal Fixture(Reply[] replies){this.replies=replies;listener=new TcpListener(IPAddress.Loopback,0);listener.Start();Port=((IPEndPoint)listener.LocalEndpoint).Port;Done=Serve();}
            internal void Release(int index){replies[index].Released.TrySetResult(true);}
            private async Task Serve()
            {
                try
                {
                    foreach(Reply reply in replies)using(TcpClient client=await listener.AcceptTcpClientAsync())using(NetworkStream stream=client.GetStream())
                    {
                        var header=new StringBuilder();byte[] one=new byte[1];
                        while(!header.ToString().EndsWith("\r\n\r\n",StringComparison.Ordinal)){if(await stream.ReadAsync(one,0,1)==0)throw new IOException();header.Append((char)one[0]);if(header.Length>65536)throw new IOException();}
                        int length=0;foreach(string line in header.ToString().Split(new[]{"\r\n"},StringSplitOptions.None))if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))length=Int32.Parse(line.Substring(15).Trim());
                        byte[] body=new byte[length];int offset=0;while(offset<length){int read=await stream.ReadAsync(body,offset,length-offset);if(read==0)throw new IOException();offset+=read;}
                        Headers.Add(header.ToString());Bodies.Add(Encoding.UTF8.GetString(body));
                        await Write(stream,Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: "+reply.Type+"\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n"));
                        byte[] first=Encoding.UTF8.GetBytes(reply.First);for(int i=0;i<first.Length;i+=7)await Chunk(stream,first,i,Math.Min(7,first.Length-i));
                        if(reply.Hold)await reply.Released.Task;
                        byte[] tail=Encoding.UTF8.GetBytes(reply.Tail);if(tail.Length>0)await Chunk(stream,tail,0,tail.Length);
                        await Write(stream,Encoding.ASCII.GetBytes("0\r\n\r\n"));
                    }
                }
                catch(IOException){}catch(SocketException){}catch(ObjectDisposedException){}
            }
            private static async Task Chunk(NetworkStream stream,byte[] bytes,int offset,int count){await Write(stream,Encoding.ASCII.GetBytes(count.ToString("x")+"\r\n"));await stream.WriteAsync(bytes,offset,count);await Write(stream,Encoding.ASCII.GetBytes("\r\n"));}
            private static Task Write(NetworkStream stream,byte[] bytes){return stream.WriteAsync(bytes,0,bytes.Length);}
            public void Dispose(){foreach(Reply reply in replies)reply.Released.TrySetResult(true);listener.Stop();}
        }
    }
}
