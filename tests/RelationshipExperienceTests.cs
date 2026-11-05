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
        static void Check(
#if NET8_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)]
#endif
            bool ok,string reason){if(!ok)throw new Exception(reason);checks++;Console.WriteLine("PASS "+reason);}
#if !NET8_0_OR_GREATER
        public static int Main(){return RunChecks();}
#endif
        public static int RunChecks(){try{Run().GetAwaiter().GetResult();Console.WriteLine("RelationshipExperienceTests: "+checks+" passed");return 0;}catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}}
        static async Task Run()
        {
            var type=typeof(RelationshipRuntime).Assembly.GetType("Dafeiyu.RelationshipExperience");Check(type!=null,"saved-stage experience reader exists");
            var fallback=typeof(GreetingService).GetMethods().SingleOrDefault(m=>m.Name=="Fallback"&&m.GetParameters().Length==7);Check(fallback!=null,"greeting fallback accepts saved-stage candidate without replacing existing callers");
            var fallbackArgs=new object[7];Array.Copy(new object[]{"skin","Name",DateTime.Now,0,new string[0]},fallbackArgs,5);fallbackArgs[6]="Stage welcome";
            Check("Stage welcome".Equals(fallback.Invoke(null,fallbackArgs)),"normal local welcome slot uses saved stage candidate");
            var holiday=new HolidayGreetingContext(new HolidayContext{PersonaId="owned",CharacterId="e-bao",LocalTime=new DateTime(2026,10,31)},new HolidayOccurrence{Id="halloween",InstanceKey="halloween:20261031",Stage="day"},null,false,2,true);
            Check(HolidayGreetingPhrases.ForCharacter("e-bao","halloween","day").Contains(GreetingService.Fallback("owned","Name",holiday.Date,0,null,holiday,"Stage welcome")),"holiday local wording wins over unlocked stage candidate");
            var api=new GreetingApi();var settings=new PetSettings{AiGreetingEnabled=true,SystemPrompt="Keep my explicitly chosen persona.",Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="fixture"}};var greeting=new GreetingService(()=>api,TimeSpan.FromSeconds(1));
            Check(await greeting.GenerateAsync(settings,"owned","Name",null,DateTime.Now,0,null,CancellationToken.None,null,"Stage welcome","Saved factual stage brief")=="A fresh AI greeting."&&api.Calls==1,"AI-enabled greeting retains exactly one generation and successful AI priority");
            Check(api.Messages[0].Content.Contains("Keep my explicitly chosen persona.")&&api.Messages[1].Content.Contains("Saved factual stage brief")&&!api.Messages[0].Content.Contains("Saved factual stage brief"),"factual stage context remains subordinate to explicit persona");
            await greeting.GenerateAsync(settings,"owned","Name",null,DateTime.Now,0,null,CancellationToken.None,null,"Stage welcome","");Check(!api.Messages.Any(m=>m.Content.Contains("Saved factual stage brief")),"disabled AI context contributes no stage information");api.Fail=true;Check(await greeting.GenerateAsync(settings,"owned","Name",null,DateTime.Now,0,null,CancellationToken.None,null,"Stage welcome","")=="Stage welcome","AI failure retains existing local slot with saved stage fallback");
            var settingsGate=new DataMaintenanceGate();using(var f=new RelationshipFixture())using(var runtime=new RelationshipRuntime(f.Root,null,settingsGate)){
                var experience=Activator.CreateInstance(type,new object[]{runtime});var method=type.GetMethod("ReadAsync");if(method==null)throw new Exception("Missing experience read contract");
                var pack=RelationshipContent.ParseJson("{\"fallback_locale\":\"en_US\",\"stages\":{\"first_meeting\":{\"welcome\":{\"en_US\":\"Owned welcome\"},\"interaction\":{\"en_US\":\"Owned response\"}}}}");
                Func<string,Task<object>> read=async id=>{var invoked=method.Invoke(experience,new object[]{id,pack,"en_US"});if(invoked==null)throw new Exception("Missing experience read task");var task=(Task)invoked;await task;return Value(task,"Result");};
                var empty=await read("official:e-bao");Check(!(bool)Value(empty,"HasSavedState")&&!(bool)Value(empty,"ReadFailed")&&String.IsNullOrEmpty((string)Value(empty,"Interaction")),"absent saved state does not fabricate first-meeting text");
                runtime.RecordVisit("official:e-bao");var saved=await read("official:e-bao");Check((bool)Value(saved,"HasSavedState")&&(string)Value(saved,"Welcome")=="Owned welcome"&&(string)Value(saved,"Interaction")=="Owned response","saved stage resolves actual content and locale fallback");
                var model=new RelationshipHomeModel(runtime);var optOut=await SaveForFixture(model,runtime,settingsGate,f.Root,true,true,false);Check(optOut.Success,"actual AI-context setting save succeeds: "+optOut.Error);Check((string)Value(await read("official:e-bao"),"AiContext")=="","saved AI-context opt-out provides no context");
                var pausedSave=await SaveForFixture(model,runtime,settingsGate,f.Root,false,true,false);Check(pausedSave.Success,"actual growth pause save succeeds: "+pausedSave.Error);var paused=await read("official:e-bao");Check(CanRespond(paused,"growth-paused-no-receipt"),"paused growth still permits saved-stage response to an admitted actual action");
                string path=Path.Combine(f.Root,"relationships","state.json");File.WriteAllText(path,"{broken");var failed=await read("official:e-bao");Check((bool)Value(failed,"ReadFailed")&&!(bool)Value(failed,"HasSavedState")&&String.IsNullOrEmpty((string)Value(failed,"Welcome")),"corrupt saved state does not invent first meeting");Check(File.ReadAllText(path)=="{broken","reader preserves corrupt original");
            }
            using(var f=new RelationshipFixture()){
                var gate=new DataMaintenanceGate();using(var runtime=new RelationshipRuntime(f.Root,null,gate)){
                    var enqueue=typeof(RelationshipRuntime).GetMethod("TryEnqueueUserInteraction",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);Check(enqueue!=null,"interaction has explicit compatible admission signal");
                    object[] args=InteractionArguments();using(await gate.BeginAsync()){Check(false.Equals(enqueue.Invoke(runtime,args)),"real maintenance refuses user interaction admission");await (Task)args[3];}
                    Check(runtime.Service.GetSnapshot("official:e-bao").State==null,"refused interaction persists no stale success");
                    args=InteractionArguments();Check(true.Equals(enqueue.Invoke(runtime,args)),"accepted interaction owns admission");await (Task)args[3];Check(runtime.Service.GetSnapshot("official:e-bao").State.Score==1,"accepted interaction queues exactly once");
                    File.Delete(Path.Combine(f.Root,"relationships","state.json"));Directory.CreateDirectory(Path.Combine(f.Root,"relationships","state.json"));args=InteractionArguments();Check(true.Equals(enqueue.Invoke(runtime,args)),"save failure is distinct from admission refusal");await (Task)args[3];Check(runtime.PendingCount>0&&runtime.Warning.Length>0,"actual save failure retains pending warning instead of successful content");
                }
            }
        }
        static bool CanRespond(object value,string eventId){var method=value.GetType().GetMethod("CanRespondToEvent",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);if(method==null)throw new Exception("Missing response contract");return true.Equals(method.Invoke(value,new object[]{eventId}));}
        static object Value(object x,string name){var property=x.GetType().GetProperty(name);if(property==null)throw new Exception("Missing property "+name);var value=property.GetValue(x,null);if(value==null)throw new Exception("Missing value "+name);return value;}
        static object[] InteractionArguments(){var args=new object[5];args[0]="official:e-bao";args[1]=true;args[2]=true;return args;}
        static async Task<RelationshipResult> SaveForFixture(RelationshipHomeModel model,RelationshipRuntime runtime,DataMaintenanceGate gate,string root,bool growth,bool feedback,bool ai)
        {
            EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> observe=(sender,args)=>{var e=args.Exception;if(e is IOException||e is UnauthorizedAccessException||e is System.Runtime.Serialization.SerializationException)Console.WriteLine("STORAGE_FIRST_CHANCE fixture="+root+" exception="+e);};
            AppDomain.CurrentDomain.FirstChanceException+=observe;
            try{var result=await model.UpdateSettingsAsync(growth,feedback,ai).ConfigureAwait(false);Console.WriteLine("SETTINGS_RESULT path="+Path.Combine(root,"relationships","state.json")+" success="+result.Success+" error="+result.Error+" maintenance="+gate.IsMaintenanceActive+" worker="+runtime.FlushAsync().Status+" pending="+runtime.PendingCount+" restart="+runtime.RequiresRestart+" warning="+runtime.Warning+" settingsVersion="+runtime.SettingsVersion);return result;}
            finally{AppDomain.CurrentDomain.FirstChanceException-=observe;}
        }
        sealed class GreetingApi:IConversationApi{
            internal int Calls;internal bool Fail;internal IList<ChatMessage> Messages=new List<ChatMessage>();
            public Task<string> ChatAsync(ConnectionSettings cfg,IList<ChatMessage> messages,CancellationToken cancel){Calls++;Messages=messages;if(Fail)throw new ApiException("Owned fixture failure");return Task.FromResult("A fresh AI greeting.");}
            public Task<string> TranscribeAsync(ConnectionSettings cfg,byte[] wave,CancellationToken cancel){throw new Exception("No new transcription");}public Task<byte[]> SpeechAsync(ConnectionSettings cfg,string voice,string text,CancellationToken cancel){throw new Exception("No new speech");}public void Dispose(){}
        }
    }
}
