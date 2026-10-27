using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Dafeiyu.Maintenance;
namespace Dafeiyu.Tests
{
    public static class RelationshipExperienceTests
    {
        static int checks;
        static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);checks++;Console.WriteLine("PASS "+reason);}
        public static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("RelationshipExperienceTests: "+checks+" passed");return 0;}catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}}
        static async Task Run()
        {
            var type=typeof(RelationshipRuntime).Assembly.GetType("Dafeiyu.RelationshipExperience");Check(type!=null,"saved-stage experience reader exists");
            var fallback=typeof(GreetingService).GetMethods().SingleOrDefault(m=>m.Name=="Fallback"&&m.GetParameters().Length==7);Check(fallback!=null,"greeting fallback accepts saved-stage candidate without replacing existing callers");
            Check((string)fallback.Invoke(null,new object[]{"skin","Name",DateTime.Now,0,new string[0],null,"Stage welcome"})=="Stage welcome","normal local welcome slot uses saved stage candidate");
            var holiday=new HolidayGreetingContext(new HolidayContext{PersonaId="owned",CharacterId="e-bao",LocalTime=new DateTime(2026,10,31)},new HolidayOccurrence{Id="halloween",InstanceKey="halloween:20261031",Stage="day"},null,false,2,true);
            Check(HolidayGreetingPhrases.ForCharacter("e-bao","halloween","day").Contains(GreetingService.Fallback("owned","Name",holiday.Date,0,null,holiday,"Stage welcome")),"holiday local wording wins over unlocked stage candidate");
            var api=new GreetingApi();var settings=new PetSettings{AiGreetingEnabled=true,SystemPrompt="Keep my explicitly chosen persona.",Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="fixture"}};var greeting=new GreetingService(()=>api,TimeSpan.FromSeconds(1));
            Check(await greeting.GenerateAsync(settings,"owned","Name",null,DateTime.Now,0,null,CancellationToken.None,null,"Stage welcome","Saved factual stage brief")=="A fresh AI greeting."&&api.Calls==1,"AI-enabled greeting retains exactly one generation and successful AI priority");
            Check(api.Messages[0].Content.Contains("Keep my explicitly chosen persona.")&&api.Messages[1].Content.Contains("Saved factual stage brief")&&!api.Messages[0].Content.Contains("Saved factual stage brief"),"factual stage context remains subordinate to explicit persona");
            await greeting.GenerateAsync(settings,"owned","Name",null,DateTime.Now,0,null,CancellationToken.None,null,"Stage welcome","");Check(!api.Messages.Any(m=>m.Content.Contains("Saved factual stage brief")),"disabled AI context contributes no stage information");api.Fail=true;Check(await greeting.GenerateAsync(settings,"owned","Name",null,DateTime.Now,0,null,CancellationToken.None,null,"Stage welcome","")=="Stage welcome","AI failure retains existing local slot with saved stage fallback");
            var settingsGate=new DataMaintenanceGate();using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root,null,settingsGate)){
                var experience=Activator.CreateInstance(type,new object[]{runtime});var method=type.GetMethod("ReadAsync");
                var pack=RelationshipContent.ParseJson("{\"fallback_locale\":\"en_US\",\"stages\":{\"first_meeting\":{\"welcome\":{\"en_US\":\"Owned welcome\"},\"interaction\":{\"en_US\":\"Owned response\"}}}}");
                Func<string,Task<object>> read=async id=>{var task=(Task)method.Invoke(experience,new object[]{id,pack,"en_US"});await task;return task.GetType().GetProperty("Result").GetValue(task,null);};
                var empty=await read("official:e-bao");Check(!(bool)Value(empty,"HasSavedState")&&!(bool)Value(empty,"ReadFailed")&&String.IsNullOrEmpty((string)Value(empty,"Interaction")),"absent saved state does not fabricate first-meeting text");
                runtime.RecordVisit("official:e-bao");var saved=await read("official:e-bao");Check((bool)Value(saved,"HasSavedState")&&(string)Value(saved,"Welcome")=="Owned welcome"&&(string)Value(saved,"Interaction")=="Owned response","saved stage resolves actual content and locale fallback");
                var model=new RelationshipHomeModel(runtime);var optOut=await SaveForFixture(model,runtime,settingsGate,f.Root,true,true,false);Check(optOut.Success,"actual AI-context setting save succeeds: "+optOut.Error);Check((string)Value(await read("official:e-bao"),"AiContext")=="","saved AI-context opt-out provides no context");
                var pausedSave=await SaveForFixture(model,runtime,settingsGate,f.Root,false,true,false);Check(pausedSave.Success,"actual growth pause save succeeds: "+pausedSave.Error);var paused=await read("official:e-bao");Check((bool)paused.GetType().GetMethod("CanRespondToEvent",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).Invoke(paused,new object[]{"growth-paused-no-receipt"}),"paused growth still permits saved-stage response to an admitted actual action");
                string path=Path.Combine(f.Root,"relationships","state.json");File.WriteAllText(path,"{broken");var failed=await read("official:e-bao");Check((bool)Value(failed,"ReadFailed")&&!(bool)Value(failed,"HasSavedState")&&String.IsNullOrEmpty((string)Value(failed,"Welcome")),"corrupt saved state does not invent first meeting");Check(File.ReadAllText(path)=="{broken","reader preserves corrupt original");
            }
            using(var f=new RelationshipFixture()){
                var gate=new DataMaintenanceGate();using(var runtime=new RelationshipRuntime(f.Root,null,gate)){
                    var enqueue=typeof(RelationshipRuntime).GetMethod("TryEnqueueUserInteraction",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);Check(enqueue!=null,"interaction has explicit compatible admission signal");
                    object[] args={"official:e-bao",true,true,null,null};using(await gate.BeginAsync()){Check(!(bool)enqueue.Invoke(runtime,args),"real maintenance refuses user interaction admission");await (Task)args[3];}
                    Check(runtime.Service.GetSnapshot("official:e-bao").State==null,"refused interaction persists no stale success");
                    args=new object[]{"official:e-bao",true,true,null,null};Check((bool)enqueue.Invoke(runtime,args),"accepted interaction owns admission");await (Task)args[3];Check(runtime.Service.GetSnapshot("official:e-bao").State.Score==1,"accepted interaction queues exactly once");
                    File.Delete(Path.Combine(f.Root,"relationships","state.json"));Directory.CreateDirectory(Path.Combine(f.Root,"relationships","state.json"));args=new object[]{"official:e-bao",true,true,null,null};Check((bool)enqueue.Invoke(runtime,args),"save failure is distinct from admission refusal");await (Task)args[3];Check(runtime.PendingCount>0&&runtime.Warning.Length>0,"actual save failure retains pending warning instead of successful content");
                }
            }
        }
        static object Value(object x,string name){return x.GetType().GetProperty(name).GetValue(x,null);}
        static async Task<RelationshipResult> SaveForFixture(RelationshipHomeModel model,RelationshipRuntime runtime,DataMaintenanceGate gate,string root,bool growth,bool feedback,bool ai)
        {
            EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> observe=delegate(object sender,System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs args){var e=args.Exception;if(e is IOException||e is UnauthorizedAccessException||e is System.Runtime.Serialization.SerializationException)Console.WriteLine("STORAGE_FIRST_CHANCE fixture="+root+" exception="+e);};
            AppDomain.CurrentDomain.FirstChanceException+=observe;
            try{var result=await model.UpdateSettingsAsync(growth,feedback,ai).ConfigureAwait(false);Console.WriteLine("SETTINGS_RESULT path="+Path.Combine(root,"relationships","state.json")+" success="+result.Success+" error="+result.Error+" maintenance="+gate.IsMaintenanceActive+" worker="+runtime.FlushAsync().Status+" pending="+runtime.PendingCount+" restart="+runtime.RequiresRestart+" warning="+runtime.Warning+" settingsVersion="+runtime.SettingsVersion);return result;}
            finally{AppDomain.CurrentDomain.FirstChanceException-=observe;}
        }
        sealed class GreetingApi:IConversationApi{
            internal int Calls;internal bool Fail;internal IList<ChatMessage> Messages;
            public Task<string> ChatAsync(ConnectionSettings cfg,IList<ChatMessage> messages,CancellationToken cancel){Calls++;Messages=messages;if(Fail)throw new ApiException("Owned fixture failure");return Task.FromResult("A fresh AI greeting.");}
            public Task<string> TranscribeAsync(ConnectionSettings cfg,byte[] wave,CancellationToken cancel){throw new Exception("No new transcription");}public Task<byte[]> SpeechAsync(ConnectionSettings cfg,string voice,string text,CancellationToken cancel){throw new Exception("No new speech");}public void Dispose(){}
        }
    }
}
