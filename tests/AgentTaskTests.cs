using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class AgentTaskTests
    {
        static int checks;static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        static string Call(AgentWorkspace w,string name,object args){return w.ExecuteAsync(name,new JavaScriptSerializer().Serialize(args),CancellationToken.None).GetAwaiter().GetResult();}
        public static int Main(){try{
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tasks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var w=new AgentWorkspace(root);
            string result=Call(w,"update_plan",new{steps=new[]{new{text="Read project",status="complete"},new{text="Implement",status="in_progress"}}});Check(!result.StartsWith("错误："),"plan is recorded through the real tool dispatcher");
            var state=typeof(AgentWorkspace).GetProperty("TaskState");Check(state!=null,"workspace exposes actual task state");object task=state.GetValue(w,null);Type taskType=task.GetType();
            Check(new JavaScriptSerializer().Serialize(task).Contains("Read project"),"task snapshot includes plan steps");
            Type storeType=typeof(AgentWorkspace).Assembly.GetType("Dafeiyu.AgentTaskStore");Check(storeType!=null,"encrypted task store is available");
            string directory=Path.Combine(root,"task-store");object store=Activator.CreateInstance(storeType,new object[]{directory});
            storeType.GetMethod("SaveAsync").Invoke(store,new[]{task});((System.Threading.Tasks.Task)storeType.GetMethod("FlushAsync").Invoke(store,null)).GetAwaiter().GetResult();
            var file=Directory.GetFiles(directory).Single();Check(!Encoding.UTF8.GetString(File.ReadAllBytes(file)).Contains("Read project"),"task checkpoint is encrypted on disk");
            object saved=storeType.GetMethod("Load").Invoke(store,new object[]{"",root});Check(saved!=null && new JavaScriptSerializer().Serialize(saved).Contains("Read project"),"checkpoint restores the real plan");
            Check(storeType.GetMethod("Load").Invoke(store,new object[]{"other-persona",root})==null,"persona cannot load another persona checkpoint");
            var contextState=(AgentTaskState)task;contextState.ContextSummary="Preserve API compatibility";contextState.ContextCompactions=3;contextState.EstimatedContextTokens=8123;contextState.Guidance.Add(new AgentGuidance{Id="guide-1",Text="Keep existing UI"});
            ((AgentTaskStore)store).SaveAsync(contextState).GetAwaiter().GetResult();var contextRestored=((AgentTaskStore)store).Load("",root);
            Check(contextRestored.ContextCompactions==3 && contextRestored.ContextSummary=="Preserve API compatibility" && contextRestored.Guidance.Single().Text=="Keep existing UI" && contextRestored.CheckpointText().Contains("Keep existing UI"),"encrypted restart preserves compressed context and applied guidance");
            contextState.ContextStatus="compacting";((AgentTaskStore)store).SaveAsync(contextState).GetAwaiter().GetResult();Check(((AgentTaskStore)store).Load("",root).ContextStatus!="compacting","restart does not leave an interrupted summary permanently in progress");
            w.ReadOnly=true;Check(!Call(w,"update_plan",new{steps=new[]{new{text="Read only",status="pending"}}}).StartsWith("错误："),"planning can record task progress without modifying project files");
            Check(Call(w,"update_plan",new{steps=new[]{new{text="bad",status="invented"}}}).StartsWith("错误："),"invalid plan statuses are rejected");
            File.WriteAllText(Path.Combine(root,"resume.txt"),"original");var original=new AgentWorkspace(root);original.BeginTask(new AgentTaskState{Workspace=root});Call(original,"read_file",new{path="resume.txt"});Call(original,"edit_file",new{path="resume.txt",old_text="original",new_text="changed"});
            original.FinishTask("interrupted","stopped");var restartStore=new AgentTaskStore(Path.Combine(root,"restart"));restartStore.SaveAsync(original.TaskState).GetAwaiter().GetResult();var loaded=restartStore.Load("",root);
            Check(File.ReadAllText(Path.Combine(root,"resume.txt"))=="changed" && loaded.Changes.Count==1,"loading checkpoint preserves changes without replaying operations");
            var resumed=new AgentWorkspace(root);resumed.BeginTask(loaded);Check(!Call(resumed,"restore_file",new{path="resume.txt"}).StartsWith("错误：") && File.ReadAllText(Path.Combine(root,"resume.txt"))=="original","explicit restore works after checkpoint save and restart");
            var boundaryStore=new AgentTaskStore(Path.Combine(root,"boundary"));boundaryStore.SaveAsync(new AgentTaskState{Workspace=root,Goal="valid"}).GetAwaiter().GetResult();var oversized=new AgentTaskState{Workspace=root};for(int i=0;i<257;i++)oversized.Changes.Add(new AgentChange{Path=Path.Combine(root,"x"+i),AfterHash=new string('a',64)});
            boundaryStore.SaveAsync(oversized).GetAwaiter().GetResult();var stillValid=new AgentTaskStore(Path.Combine(root,"boundary")).Load("",root);Check(boundaryStore.Warning.Length>0 && stillValid!=null && stillValid.Goal=="valid","store refuses self-unreadable oversized checkpoint while preserving previous valid data");
            var failedState=new AgentTaskState{Workspace=root,Goal="newest-unsaved"};var failedStore=new AgentTaskStore(Path.Combine(root,"save-failure"));
            failedStore.SaveAsync(new AgentTaskState{Workspace=root,Goal="old-on-disk"}).GetAwaiter().GetResult();string failedPath=Directory.GetFiles(Path.Combine(root,"save-failure")).Single();
            using(var held=new FileStream(failedPath,FileMode.Open,FileAccess.Read,FileShare.Read)){failedStore.SaveAsync(failedState).GetAwaiter().GetResult();Check(failedStore.Warning.Length>0,"real file-sharing failure records a checkpoint save warning");}
            var latest=failedStore.Load("",root);Check(latest!=null && latest.Goal=="newest-unsaved" && failedStore.Warning.Length>0,"reload preserves unsaved latest checkpoint and warning instead of reverting to disk");
            failedStore.SaveAsync(latest).GetAwaiter().GetResult();Check(failedStore.Warning.Length==0 && new AgentTaskStore(Path.Combine(root,"save-failure")).Load("",root).Goal=="newest-unsaved","successful retry persists newest state and clears its warning");
            var malformed=new AgentTaskState{Workspace=root};malformed.Plan.Add(null);
            byte[] plain;using(var memory=new MemoryStream()){new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(AgentTaskState)).WriteObject(memory,malformed);plain=memory.ToArray();}File.WriteAllBytes(file,SecretProtection.Protect(plain));
            var typedStore=new AgentTaskStore(directory);Check(typedStore.Load("",root)==null && typedStore.Warning.Length>0,"malformed checkpoint elements are rejected safely");
            typedStore.ClearAsync("",root).GetAwaiter().GetResult();Check(!File.Exists(file) && typedStore.Warning.Length==0,"explicit clearing permits a fresh checkpoint");
            Console.WriteLine("Agent task tests: "+checks+" passed");return 0;
        }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}}
    }
}
