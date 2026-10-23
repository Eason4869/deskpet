using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class AgentJobToolTests
    {
        static int checks;
        static Type Tools {get{var type=typeof(AgentJobStore).Assembly.GetType("Dafeiyu.AgentJobTools");if(type==null)throw new Exception("AgentJobTools contract adapter is missing");return type;}}
        static object Call(string method,params object[] args)
        {try{return Tools.GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public).Invoke(null,args);}catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}}
        static void Check(bool value,string why){if(!value)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        static Dictionary<string,object> Normalize(string name,Dictionary<string,object> args,string root,int maximum=1800)
        {return (Dictionary<string,object>)Call("Normalize",name,args,root,maximum);}
        static void StrictStartArguments()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var args=Normalize("start_job",new Dictionary<string,object>{{"command","echo fixture"}},temporary.Path,300);
                Check(args.Count==4&&(int)args["timeout_seconds"]==300&&(string)args["working_directory"]==temporary.Path&&(string)args["title"]=="echo fixture","start normalization freezes all four effective arguments and the project timeout ceiling");
                Check((int)Normalize("start_job",new Dictionary<string,object>{{"command","echo fixture"}},temporary.Path,28800)["timeout_seconds"]==1800,"background default remains 1800 even with a larger project ceiling");
                foreach(var bad in new[]{
                    new Dictionary<string,object>{{"command","echo x"},{"owner","forged"}},
                    new Dictionary<string,object>{{"command","echo x"},{"title",null}},
                    new Dictionary<string,object>{{"command","echo x"},{"working_directory",false}},
                    new Dictionary<string,object>{{"command","echo x"},{"timeout_seconds",true}},
                    new Dictionary<string,object>{{"command","echo x"},{"timeout_seconds",2.5m}},
                    new Dictionary<string,object>{{"command","echo x"},{"timeout_seconds",301}},
                    new Dictionary<string,object>{{"command",new string('x',16385)}}})
                {bool denied=false;try{Normalize("start_job",bad,temporary.Path,300);}catch(ArgumentException){denied=true;}Check(denied,"strict start rejects unknown/null/type/limit argument case "+checks);}
            }
        }
        static void StrictReadAndStopArguments()
        {
            string id=new string('a',32);
            var read=Normalize("get_job",new Dictionary<string,object>{{"id",id},{"limit_bytes",65536}},"unused");
            Check((int)read["limit_bytes"]==65536&&(string)read["cursor"]=="","read accepts the complete 64-KiB body page and defaults its opaque cursor");
            foreach(var bad in new[]{new Dictionary<string,object>{{"id",id},{"reason","owner_deleted"}},new Dictionary<string,object>{{"id","../metadata"}},new Dictionary<string,object>{{"id",id},{"limit_bytes","1024"}},new Dictionary<string,object>{{"id",id},{"limit_bytes",65537}}})
            {bool denied=false;try{Normalize("get_job",bad,"unused");}catch(ArgumentException){denied=true;}Check(denied,"strict read rejects paths, forged controls and invalid page limits "+checks);}
            bool reasonDenied=false;try{Normalize("stop_job",new Dictionary<string,object>{{"id",id},{"reason","timeout"}},"unused");}catch(ArgumentException){reasonDenied=true;}
            Check(reasonDenied,"stop cannot accept a model-selected stop reason");
        }
        static void CompleteWireJsonAndResourceErrors()
        {
            using(var temporary=new AgentJobTestSupport.TemporaryDirectory())
            {
                var clock=new AgentJobTestSupport.Clock();var job=AgentJobTestSupport.Snapshot(temporary.Path,new string('b',32),AgentJobState.Interrupted,clock);
                var json=(string)Call("Success",job,false);var root=new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string,object>;var wire=(Dictionary<string,object>)root["job"];
                Check(wire.Count==19&&(string)wire["state"]=="interrupted"&&(string)wire["agent_id"]=="primary"&&!wire.ContainsKey("pid"),"wire snapshot has only the approved complete fields and stable owner");
                Check(((Dictionary<string,object>)wire["timings_ms"]).Count==7&&((string)wire["created_utc"]).EndsWith("Z",StringComparison.Ordinal),"wire timings expose all seven nullable phases and timestamps use ISO UTC");
                var huge=new string('\n',30000);var page=new AgentJobPage(job,new[]{new AgentJobLogRecord(1,"stdout",clock.UtcNow,huge,0)},"cursor",false,null);
                string full=(string)Call("Page",page);var decoded=(Dictionary<string,object>)new JavaScriptSerializer{MaxJsonLength=1024*1024}.DeserializeObject(full);
                Check(full.Length>24000&&((object[])decoded["records"]).Length==1,"large escaped log pages remain a complete JSON object");
                var error=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject((string)Call("Error","job_not_found"));var detail=(Dictionary<string,object>)error["error"];
                Check(!(bool)error["ok"]&&!(bool)detail["retryable"]&&!((string)detail["message"]).StartsWith("[",StringComparison.Ordinal),"protocol errors use an embedded UI resource instead of a missing-key marker");
                Check((bool)Call("IsFailure",Call("Error","permission_denied"))&&!(bool)Call("IsFailure",json),"tool event status follows the job JSON ok field rather than a translated text prefix");
                Check((bool)Call("IsFailure","{broken"),"an invalid job protocol response cannot be recorded as a successful tool call");
            }
        }
        static void UnsupportedPlatformNeverExposesJobTools()
        {
            if(Environment.OSVersion.Platform==PlatformID.Win32NT)return;
            using(var workspace=AgentWorkspace.ForContextBudget(32768))
            {
                Check(!workspace.AvailableToolNames.Contains("start_job")&&!workspace.AvailableToolNames.Contains("get_job")&&!workspace.AvailableToolNames.Contains("stop_job"),"unsupported platform exposes no background job schemas");
                foreach(string name in new[]{"start_job","get_job","stop_job"})
                {
                    var response=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(workspace.ExecuteAsync(name,"{}","forged-"+name,System.Threading.CancellationToken.None).GetAwaiter().GetResult());
                    Check(!(bool)response["ok"]&&(string)((Dictionary<string,object>)response["error"])["code"]=="unsupported_platform","unsupported direct "+name+" returns a platform denial before any native factory");
                }
            }
        }
        public static int Main()
        {
            checks=0;int failures=0;foreach(var test in new Action[]{StrictStartArguments,StrictReadAndStopArguments,CompleteWireJsonAndResourceErrors,UnsupportedPlatformNeverExposesJobTools})try{test();}catch(Exception error){failures++;Console.WriteLine("FAIL "+test.Method.Name+": "+error);}
            Console.WriteLine("AgentJobToolTests: "+checks+" passed");return failures==0?0:1;
        }
    }
}
