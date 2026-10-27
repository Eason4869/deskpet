using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Collections.Generic;
namespace Dafeiyu.Tests
{
    public static class AgentJobContractTests
    {
        static int checks;
        static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        static int ReadLimit(object value,string name)
        {
            var property=value.GetType().GetProperty(name);var field=value.GetType().GetField(name);
            Check(property!=null||field!=null,"background limit exists independently on "+value.GetType().Name);
            return (int)(property!=null?property.GetValue(value,null):field.GetValue(value));
        }
        static void SetLimit(object value,string name,int limit)
        {
            var property=value.GetType().GetProperty(name);var field=value.GetType().GetField(name);
            Check(property!=null||field!=null,"background limit can be saved on "+value.GetType().Name);
            if(property!=null)property.SetValue(value,limit,null);else field.SetValue(value,limit);
        }
        static T Deserialize<T>(string json){using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(json)))return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);}
        static void LegacySettingsKeepExecTimeout()
        {
            var legacy=Deserialize<PetSettings>("{\"AgentCommandTimeoutSeconds\":120}");legacy.Normalize();
            Check(legacy.AgentCommandTimeoutSeconds==120,"legacy synchronous command keeps 120 seconds");
            Check(ReadLimit(legacy,"AgentBackgroundJobTimeoutSeconds")==1800,"legacy settings independently default background limit to 1800");
            legacy.AgentCommandTimeoutSeconds=900;SetLimit(legacy,"AgentBackgroundJobTimeoutSeconds",7200);legacy.Normalize();
            Check(legacy.AgentCommandTimeoutSeconds==600&&ReadLimit(legacy,"AgentBackgroundJobTimeoutSeconds")==7200,"background duration does not change synchronous 600-second cap");
        }
        static void LegacyProjectDefaultsTo1800()
        {
            var legacy=Deserialize<ProjectDefinition>("{\"Id\":\"legacy\",\"Name\":\"legacy\",\"CommandTimeoutSeconds\":120}");
            Check(legacy.CommandTimeoutSeconds==120&&ReadLimit(legacy,"BackgroundJobTimeoutSeconds")==1800,"legacy project initializes background duration before deserialization");
        }
        static void CopyAndEffectiveSettingsKeepBackgroundLimit()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var settings=new PetSettings{ReadReplies=false};SetLimit(settings,"AgentBackgroundJobTimeoutSeconds",7200);
                Check(ReadLimit(settings.Copy(),"AgentBackgroundJobTimeoutSeconds")==7200,"settings snapshot retains independent job duration");
                var settingsStore=new SettingsStore(Path.Combine(temporary.Path,"settings.json"));settingsStore.Save(settings);
                Check(ReadLimit(settingsStore.Load(),"AgentBackgroundJobTimeoutSeconds")==7200,"real settings save and reopen retains job duration");
                var catalog=new ProjectCatalog();var project=ProjectCatalogStore.CreateProject(catalog,"fixture",temporary.Path);SetLimit(project,"BackgroundJobTimeoutSeconds",3600);
                Check(ReadLimit(project.Copy(),"BackgroundJobTimeoutSeconds")==3600,"project snapshot retains job duration");
                var store=new ProjectCatalogStore(Path.Combine(temporary.Path,"projects.json"));store.Save(catalog);project=store.Load().Projects[0];
                Check(ReadLimit(project,"BackgroundJobTimeoutSeconds")==3600,"real catalog save and reopen retains job duration");
                using(var manager=new ConversationWorkspaceManager(temporary.Path,"pet",()=>new AgentJobTestSupport.OfflineApi(),()=>new AgentJobTestSupport.SilentSpeech(),()=>null,()=>settings,()=>false))
                {
                    var workspace=manager.CreateWorkspace(project,project.Conversations[0]);
                    Check(ReadLimit(workspace.EffectiveSettings(),"AgentBackgroundJobTimeoutSeconds")==3600,"persistent conversation uses project job duration instead of global duration");
                }
            }
        }
        static void ValidateExactArgumentBounds()
        {
            var policy=typeof(PetSettings).Assembly.GetType("Dafeiyu.AgentJobPolicy");Check(policy!=null,"job policy exists before tools can accept new durations");
            var resolve=policy.GetMethod("ResolveTimeout",BindingFlags.Public|BindingFlags.Static);Check(resolve!=null,"job timeout policy has one shared validation entry");
            Check((int)resolve.Invoke(null,new object[]{null,28800})==1800,"omitted duration defaults to 1800");
            Check((int)resolve.Invoke(null,new object[]{null,900})==900,"omitted duration respects smaller project ceiling");
            Check((int)resolve.Invoke(null,new object[]{1,28800})==1,"one-second request remains valid");
            Check((int)resolve.Invoke(null,new object[]{28800,28800})==28800,"eight-hour request remains valid");
            foreach(int value in new[]{0,28801,901})
            {
                bool rejected=false;try{resolve.Invoke(null,new object[]{value,value==901?900:28800});}catch(TargetInvocationException e){rejected=e.InnerException is ArgumentOutOfRangeException;}
                Check(rejected,"out-of-range or over-project duration rejected: "+value);
            }
        }
        static Type RequiredType(string name)
        {
            var type=typeof(PetSettings).Assembly.GetType("Dafeiyu."+name);Check(type!=null,"shared job contract exists: "+name);return type;
        }
        static void SpecsValidateCommandsAndKeepUnicode()
        {
            var type=RequiredType("AgentJobSpec");var constructor=type.GetConstructor(new[]{typeof(string),typeof(string),typeof(string),typeof(int)});
            Check(constructor!=null,"job spec accepts only frozen command/title/directory/duration");
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var spec=constructor.Invoke(new object[]{"echo "+new string('中',300),null,temporary.Path,1});
                var title=(string)type.GetProperty("Title").GetValue(spec,null);
                Check(Encoding.UTF8.GetByteCount(title)<=512&&!title.Contains("\uFFFD"),"inferred title is bounded without corrupting UTF-8");
                Check((int)type.GetProperty("TimeoutSeconds").GetValue(spec,null)==1,"spec retains explicit one-second duration");
                var bad=new[]{new object[]{" ",null,temporary.Path,1},new object[]{"echo\0bad",null,temporary.Path,1},new object[]{new string('a',16385),null,temporary.Path,1},new object[]{"echo ok",new string('a',513),temporary.Path,1},new object[]{"echo ok",null,"relative",1},new object[]{"echo ok",null,temporary.Path,0},new object[]{"echo ok",null,temporary.Path,28801}};
                foreach(var arguments in bad)
                {
                    bool rejected=false;try{constructor.Invoke(arguments);}catch(TargetInvocationException error){rejected=error.InnerException is ArgumentException;}
                    Check(rejected,"invalid command/title/path/duration rejected before any process exists");
                }
            }
        }
        static void StableOwnerScopesIdempotency()
        {
            var ownerType=RequiredType("AgentJobOwner");var owner=Activator.CreateInstance(ownerType,new object[]{"project","conversation","primary"});
            var policy=RequiredType("AgentJobPolicy");var key=policy.GetMethod("IdempotencyKey");Check(key!=null,"job invocation has stable owner-based idempotency");
            var args=new Dictionary<string,object>{{"command","echo ok"}};
            var a=new AgentToolInvocation("start_job",args,new AgentToolExecutionContext{SessionId="temporary-a",ProjectId="project",AgentId="primary",TaskId="persisted-task",CallId="call"});
            var b=new AgentToolInvocation("start_job",args,new AgentToolExecutionContext{SessionId="temporary-b",ProjectId="project",AgentId="primary",TaskId="persisted-task",CallId="call"});
            string first=(string)key.Invoke(null,new[]{owner,(object)a});
            Check(first==(string)key.Invoke(null,new[]{owner,(object)b}),"recreating a workspace cannot turn a repeated call into a new execution");
            var other=Activator.CreateInstance(ownerType,new object[]{"project","other-conversation","primary"});
            Check(first!=(string)key.Invoke(null,new[]{other,(object)b}),"different persistent conversations never share an invocation key");
            var changed=new AgentToolInvocation("start_job",new Dictionary<string,object>{{"command","echo changed"}},new AgentToolExecutionContext{ProjectId="project",AgentId="primary",TaskId="persisted-task",CallId="call"});
            Check(first==(string)key.Invoke(null,new[]{owner,(object)changed}),"changed payload keeps the same key so the manager can reject a conflict");
        }
        static void OutputChunksDoNotExposeMutableBuffers()
        {
            var type=RequiredType("AgentJobOutputChunk");var source=new byte[]{0xE4,0xB8,0xAD};
            var chunk=Activator.CreateInstance(type,new object[]{"stdout",source,source.Length,DateTime.UtcNow});source[0]=0;
            var bytes=(byte[])type.GetProperty("Bytes").GetValue(chunk,null);
            Check(bytes[0]==0xE4,"output chunk owns its bytes independently of the pipe read buffer");bytes[0]=0;
            Check(((byte[])type.GetProperty("Bytes").GetValue(chunk,null))[0]==0xE4,"a consumer cannot mutate a retained output chunk");
        }
        public static int Main()
        {
            checks=0;int failures=0;
            foreach(var test in new Action[]{LegacySettingsKeepExecTimeout,LegacyProjectDefaultsTo1800,CopyAndEffectiveSettingsKeepBackgroundLimit,ValidateExactArgumentBounds,SpecsValidateCommandsAndKeepUnicode,StableOwnerScopesIdempotency,OutputChunksDoNotExposeMutableBuffers})
                try{test();}catch(Exception error){failures++;Console.WriteLine("FAIL "+test.Method.Name+": "+error);}
            Console.WriteLine("AgentJobContractTests: "+checks+" passed");return failures==0?0:1;
        }
    }
}
