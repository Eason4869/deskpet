using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class ContextUsageTests
    {
        static int count;
        static void Check(bool value,string reason){if(!value)throw new Exception(reason);count++;Console.WriteLine("PASS "+reason);}
        public static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("Context usage tests: "+count+" passed");return 0;}catch(Exception e){Console.WriteLine(e);return 1;}}
        static async Task Run()
        {
            var known=new ConnectionSettings{ContextWindowTokens=262144};
            Check(ModelContextCapacity.Budget(known,32768)==262144,"known model capacity controls compression without a stale 32K work cap");
            Check(ModelContextCapacity.Budget(new ConnectionSettings(),65536)==65536 && ModelContextCapacity.Budget(new ConnectionSettings(),0)==32768,"unknown capacity uses the configured fallback and a safe default");
            using(var server=new ApiTests.Fixture("{\"data\":[{\"id\":\"large\",\"context_length\":1048576},{\"id\":\"unknown\"}]}"))using(var client=new ApiClient()){
                var capacities=new Dictionary<string,int>();var models=await client.ListModelsAsync(new ConnectionSettings{BaseUrl=server.Url,ApiKey="fixture",UseProxy=false},CancellationToken.None,capacities);Check(models.Count==2&&capacities["large"]==1048576&&!capacities.ContainsKey("unknown"),"actual HTTP model catalog recognizes capacity and leaves absent values unknown");
            }
            foreach(bool fail in new[]{false,true})using(var api=new SummaryApi(fail))using(var session=new ConversationSession(api,new Silent(),()=>null)){
                var settings=new PetSettings{AgentEnabled=false,ReadReplies=false,AgentContextTokenLimit=8192,Text=new ConnectionSettings{Model="fixture",BaseUrl="http://127.0.0.1",ContextWindowTokens=16384}};
                for(int i=0;i<6;i++)session.AddLiveTurn("","old user "+i,new string('旧',5000));await session.SendTextAsync("latest exact input",settings,"pet");
                Check(api.Calls==2&&api.Last.Last().Content=="latest exact input"&&session.CurrentContextUsage.Compactions==1&&!session.IsBusy,"plain conversation automatically compresses before send, including summary failure fallback: "+fail);
                Check(session.CurrentContextUsage.Tokens<16384&&session.CurrentContextUsage.Capacity==16384,"context preview receives compressed usage and model capacity: "+fail);
            }
            Check(ModelContextCapacity.Read(new Dictionary<string,object>{{"context_length",1048576}})==1048576,"recognizes model catalog capacity above 128K");
            Check(ModelContextCapacity.Read(new Dictionary<string,object>{{"top_provider",new Dictionary<string,object>{{"context_length",200000}}}})==200000,"recognizes nested provider capacity");
            Check(ModelContextCapacity.Read(new Dictionary<string,object>{{"context_window",-1}})==0 && ModelContextCapacity.Read(new Dictionary<string,object>{{"context_length","bogus"}})==0,"invalid metadata stays unknown");
            var cfg=new PetSettings();cfg.EnsureTextProviders();var p=cfg.TextProviders[0];p.Models=new List<string>{"a","b"};p.Connection.Model="a";p.ModelContextWindows["a"]=200000;p.ModelContextOverrides["a"]=475000;p.Normalize();
            cfg.SelectTextModel(p.Id,"a");Check(cfg.Text.ContextWindowTokens==475000,"manual model capacity overrides discovered capacity in request snapshot");
            var snapshot=cfg.Copy();cfg.SelectTextModel(p.Id,"b");Check(cfg.Text.ContextWindowTokens==0 && snapshot.Text.ContextWindowTokens==475000,"model switch clears previous capacity and preserves in-flight snapshot");
            string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"capacity-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);var store=new SettingsStore(Path.Combine(folder,"settings.json"));store.Save(snapshot);var loaded=store.Load();Check(loaded.TextProviders[0].ModelContextOverrides["a"]==475000&&loaded.TextProviders[0].ModelContextWindows["a"]==200000,"manual and detected per-model capacity persist separately");
            string target=Path.Combine(folder,"atomic.json"),temporary=target+".tmp";File.WriteAllText(target,"previous");File.WriteAllText(temporary,"next");var hold=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read);var release=Task.Run(async delegate{await Task.Delay(65);hold.Dispose();});AtomicFile.Commit(temporary,target);await release;Check(File.ReadAllText(target)=="next"&&!File.Exists(temporary),"atomic save retries a transient destination lock without deleting the old record first");
            File.WriteAllText(temporary,"pending");bool refused=false;using(var locked=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read))try{AtomicFile.Commit(temporary,target);}catch(IOException){refused=true;}Check(refused&&File.ReadAllText(target)=="next"&&File.Exists(temporary),"persistent storage lock retains previous data and pending replacement");
            var budget=new AgentWorkspace(folder){ContextTokenLimit=1000000};var history=new List<object>{new{role="user",content="goal"},new{role="assistant",content=new string('旧',180000)},new{role="user",content="latest"}};int calls=0;
            Check(!await AgentContextBudget.PrepareAsync(history,false,budget,(s,t)=>{calls++;return Task.FromResult("summary");},CancellationToken.None)&&calls==0,"large model no longer compacts at obsolete 128K ceiling");
            budget.ContextTokenLimit=8192;budget.BeginTask(new AgentTaskState{Workspace=folder,Goal="goal"});Check(await AgentContextBudget.PrepareAsync(history,false,budget,(s,t)=>Task.FromResult("retained facts"),CancellationToken.None)&&budget.TaskState.ContextCompactions==1&&AgentContextBudget.EstimateTokens(history)<8192,"automatic compression activates at configured budget and preserves latest request");
            Check(history.Any(x=>new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(x).Contains("latest")),"compression retains latest user input");
        }
        sealed class SummaryApi:IConversationApi{
            readonly bool fail;internal int Calls;internal IList<ChatMessage> Last;internal SummaryApi(bool failure){fail=failure;}
            public Task<string> ChatAsync(ConnectionSettings s,IList<ChatMessage> m,CancellationToken c){Calls++;Last=m;if(Calls==1&&fail)throw new ApiException("summary unavailable");return Task.FromResult(Calls==1?"retained historical facts":"final reply");}
            public Task<string> TranscribeAsync(ConnectionSettings s,byte[] b,CancellationToken c){throw new NotSupportedException();}public Task<byte[]> SpeechAsync(ConnectionSettings s,string v,string t,CancellationToken c){throw new NotSupportedException();}public void Dispose(){}
        }
        sealed class Silent:IReplySpeech{public Task SpeakAsync(string s,PetSettings p,CancellationToken c){return Task.FromResult(0);}public void Stop(){}public void SetVolume(int v){}public void Dispose(){}}
    }
}
