using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class AgentSubagentExecutionTests
    {
        static int count,models;static void Check(bool ok,string why){if(!ok)throw new Exception(why);count++;Console.WriteLine("PASS "+why);}
        sealed class Model:IAgentConversationApi,IDisposable
        {
            readonly int id=Interlocked.Increment(ref models);
            public async Task<string> ChatAgentAsync(ConnectionSettings s,IList<ChatMessage> messages,AgentWorkspace w,Action<string> delta,Action<string> reasoning,Action<AgentToolEvent> tool,bool stream,CancellationToken cancel){await Task.Delay(60,cancel);Check(messages.Count==2,"worker gets independent minimal history");string path=messages.Last().Content;Check(!w.AvailableToolNames.Contains("exec_command")&&!w.AvailableToolNames.Contains("spawn_subagent")&&!w.AvailableToolNames.Contains("write_file"),"worker tool set remains read-only with no recursive delegation");string text=await w.ExecuteAsync("read_file",new JavaScriptSerializer().Serialize(new{path}),"read-"+id,cancel);if(tool!=null)tool(new AgentToolEvent{Id="read-"+id,Name="read_file",Status="running"});return "model-"+id+" "+text;}public void Dispose(){}
        }
        public static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("Agent subagent execution tests: "+count+" passed");return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
        static async Task Run(){string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"children-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"a.txt"),"first evidence");File.WriteAllText(Path.Combine(root,"b.txt"),"second evidence");using(var workspace=new AgentWorkspace(root)){
            workspace.ConfigureKernel(AgentPermissionRuntime.DefaultWorkspacePolicy(),null,null,new ExecutionClaimStore(root),"parent");workspace.BeginTask(new AgentTaskState{Id="parent",Workspace=root,Goal="explore"});workspace.ConfigureWorkers(()=>new Model(),new ConnectionSettings{Model="fixture"});workspace.ConfigureDesktopSession();
            var json=new JavaScriptSerializer();string a=await workspace.ExecuteAsync("spawn_subagent","{\"profile\":\"explore\",\"input\":\"a.txt\"}","spawn-a",CancellationToken.None),b=await workspace.ExecuteAsync("spawn_subagent","{\"profile\":\"explore\",\"input\":\"b.txt\"}","spawn-b",CancellationToken.None);var ids=new[]{(string)((Dictionary<string,object>)json.DeserializeObject(a))["Id"],(string)((Dictionary<string,object>)json.DeserializeObject(b))["Id"]};string result=await workspace.ExecuteAsync("wait_subagents",json.Serialize(new{ids,timeout_seconds=5}),"wait",CancellationToken.None);
            Check(models==2&&result.Contains("first evidence")&&result.Contains("second evidence")&&result.Contains("model-1")&&result.Contains("model-2"),"two independent worker model calls return different real file evidence");Check(workspace.TaskState.Children.Count==2&&workspace.TaskState.Children.All(c=>c.Status=="completed"&&c.ToolCalls==1),"parent task stores actual worker results and tool counts");Check((await workspace.ExecuteAsync("complete_subagent","{\"id\":\"fake\",\"result\":\"done\"}","forge",CancellationToken.None)).StartsWith("错误："),"parent cannot forge child completion");
        }}
    }
}
