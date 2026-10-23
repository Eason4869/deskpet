using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
namespace Dafeiyu.Tests
{
    public static class AgentJobViewStateTests
    {
        static int checks;
        static void Check(bool condition,string description){if(!condition)throw new Exception(description);checks++;Console.WriteLine("PASS "+description);}
        static Type Require(string name){var type=typeof(AgentJobManager).Assembly.GetType("Dafeiyu."+name);if(type==null)throw new Exception(name+" is missing");return type;}
        static AgentJobSnapshot Snapshot(string directory,string id,AgentJobState state,string project,string conversation,string reply,DateTime created)
        {
            var snapshot=AgentJobTestSupport.Snapshot(directory,id,state,new AgentJobTestSupport.Clock());
            var record=typeof(AgentJobSnapshot).GetMethod("ToRecord",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(snapshot,null);var type=record.GetType();
            type.GetField("Owner").SetValue(record,new AgentJobOwner(project,conversation,"primary"));type.GetField("CreatedUtc").SetValue(record,created);
            var source=type.GetField("SourceReplyId");if(source==null)throw new Exception("Persistent reply origin is missing");source.SetValue(record,reply);
            type.GetField("SourceCallId").SetValue(record,"call-"+id);type.GetField("SourceTaskId").SetValue(record,"task-"+id);
            return (AgentJobSnapshot)Activator.CreateInstance(typeof(AgentJobSnapshot),BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{record},null);
        }
        static void OriginFiltersAndPersistence()
        {
            var select=Require("AgentJobViewState").GetMethod("Select");
            using(var root=new AgentJobTestSupport.TemporaryDirectory())
            {
                var now=DateTime.UtcNow;var jobs=new[]{Snapshot(root.Path,new string('a',32),AgentJobState.Running,"a","one","reply-one",now),Snapshot(root.Path,new string('b',32),AgentJobState.CleanupFailed,"a","two","reply-two",now.AddSeconds(1)),Snapshot(root.Path,new string('c',32),AgentJobState.Succeeded,"b","one","reply-one",now.AddSeconds(2)),Snapshot(root.Path,new string('d',32),AgentJobState.Stopped,"a","one","reply-old",now.AddSeconds(-1))};
                Func<string,string,string,string,bool,IList<AgentJobSnapshot>> run=(project,conversation,reply,filter,latest)=>(IList<AgentJobSnapshot>)select.Invoke(null,new object[]{jobs,project,conversation,reply,filter,latest});
                Check(run("a",null,null,"all",true).Select(job=>job.Id).SequenceEqual(new[]{jobs[1].Id,jobs[0].Id,jobs[3].Id}),"project tasks include unopened and archived owners in stable latest-first order");
                Check(run("a","one","reply-one","all",true).Single().Id==jobs[0].Id,"a reply card cannot attach another project's, conversation's or reply's task");
                Check(run("a",null,null,"active",false).Select(job=>job.Id).SequenceEqual(new[]{jobs[0].Id,jobs[1].Id}),"cleanup failure remains active and earliest-first ordering is stable");
                Check(run("a",null,null,"finished",true).Single().Id==jobs[3].Id,"finished filter excludes unconfirmed cleanup");
                var clock=new AgentJobTestSupport.Clock();using(var store=new AgentJobTestSupport.Store(root.Path,clock))store.Save(jobs[3]);
                using(var store=new AgentJobTestSupport.Store(root.Path,clock))
                {var restored=store.Load().Single();Check((string)typeof(AgentJobSnapshot).GetProperty("SourceReplyId").GetValue(restored,null)=="reply-old"&&(string)typeof(AgentJobSnapshot).GetProperty("SourceCallId").GetValue(restored,null)=="call-"+jobs[3].Id,"persistent task metadata retains the original reply and tool call after reopening");}
            }
        }
        static void PreviewCandidatesAreLocalBoundedAndSanitized()
        {
            var type=Require("AgentJobPreviewCandidates");var candidates=Activator.CreateInstance(type);var add=type.GetMethod("Add");
            add.Invoke(candidates,new object[]{"<script>open http://example.com:1234/evil</script> file:///secret ftp://127.0.0.1/a http://localhost.evil/a https://127.0.0.1.evil/a http://user:password@localhost:1234/path?token=secret#fragment https://[::1]:4321/ http://127.0.0.2:4567/a"});
            var values=(IList<Uri>)type.GetProperty("Candidates").GetValue(candidates,null);
            Check(values.Count==3&&values.All(uri=>uri.IsLoopback&&uri.UserInfo==""&&uri.Query==""&&uri.Fragment==""),"preview candidates reject public hosts and remove credentials, query and fragments");
            Console.WriteLine("Preview diagnostics: "+String.Join(" | ",values.Select(uri=>uri.AbsoluteUri+" host="+uri.Host)));
            Check(values.Any(uri=>uri.AbsoluteUri=="http://localhost:1234/path")&&values.Any(uri=>{System.Net.IPAddress ip;return System.Net.IPAddress.TryParse(uri.Host.Trim('[',']'),out ip)&&ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetworkV6&&System.Net.IPAddress.IsLoopback(ip);}),"loopback paths and IPv6 survive sanitization");
            add.Invoke(candidates,new object[]{"http://localhost:1234/path?another=secret http://127.0.0.1:8001/ http://localhost:8002/ http://localhost:8003/"});
            values=(IList<Uri>)type.GetProperty("Candidates").GetValue(candidates,null);
            Check(values.Count==5&&values.Select(uri=>uri.AbsoluteUri).Distinct().Count()==5,"each task offers at most five unique sanitized preview addresses");
        }
        static void CompletionClaimsBelongToTheApplication()
        {
            var type=Require("AgentJobCompletionTracker");var method=type.GetMethod("Observe");
            using(var root=new AgentJobTestSupport.TemporaryDirectory())
            {
                string id=new string('f',32);var now=DateTime.UtcNow;
                var running=Snapshot(root.Path,id,AgentJobState.Running,"a","one","reply",now);var finished=Snapshot(root.Path,id,AgentJobState.Succeeded,"a","one","reply",now);
                var recovered=Snapshot(root.Path,new string('e',32),AgentJobState.Interrupted,"a","old","old",now);
                var tracker=Activator.CreateInstance(type,new object[]{new[]{recovered,running}});
                Func<AgentJobSnapshot[],IList<AgentJobSnapshot>> observe=jobs=>(IList<AgentJobSnapshot>)method.Invoke(tracker,new object[]{jobs});
                Check(observe(new[]{recovered,running}).Count==0,"recovered terminal tasks and unchanged running tasks do not notify");
                Check(observe(new[]{recovered,finished}).Single().Id==id,"one terminal transition yields one owner-preserving notification");
                Check(observe(new[]{finished}).Count==0&&observe(new[]{running,finished}).Count==0,"repeated panel refreshes and stale snapshots never duplicate completion notifications");
            }
        }
        public static int Main()
        {
            checks=0;int failures=0;foreach(var test in new Action[]{OriginFiltersAndPersistence,PreviewCandidatesAreLocalBoundedAndSanitized,CompletionClaimsBelongToTheApplication})try{test();}catch(Exception error){failures++;Console.WriteLine("FAIL "+test.Method.Name+": "+error);}
            Console.WriteLine("AgentJobViewStateTests: "+checks+" passed");return failures==0?0:1;
        }
    }
}
