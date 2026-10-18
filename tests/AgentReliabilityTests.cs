using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class AgentReliabilityTests
    {
        static int failed,passed;
        public static int Main()
        {
            Run("another persona cannot clear an outstanding save failure",()=>{
                var protector=new Protection{Fail=true};SecretProtection.Configure(protector);
                var store=new HistoryStore(Folder());var log=new ConversationLog();log.Commit("question","answer");
                store.SaveAsync("a",log.Messages).GetAwaiter().GetResult();Check(store.Warning.Length>0,"A failure not visible");
                protector.Fail=false;store.SaveAsync("b",log.Messages).GetAwaiter().GetResult();
                Check(store.Warning.Length>0,"B successful save cleared A failure");store.Load("b");Check(store.Warning.Length>0,"B load cleared A failure");
                store.SaveAsync("a",log.Messages).GetAwaiter().GetResult();Check(store.Warning.Length==0,"repairing A did not clear its warning");
            });
            Run("two image turns within the total byte budget retain their actual attachments",()=>{
                SecretProtection.Configure(new Protection());var api=new Model();var settings=new PetSettings{ReadReplies=false};settings.Text.BaseUrl="http://127.0.0.1:1";settings.Text.Model="fixture";
                var bytes=new byte[2*1024*1024];bytes[0]=137;bytes[1]=80;bytes[2]=78;bytes[3]=71;var file=ChatAttachment.CreateImage("large.png",bytes);
                using(var session=new ConversationSession(api,new Speech(),()=>null)){
                    session.SendTextAsync("first",settings,"test",attachments:new[]{file}).GetAwaiter().GetResult();
                    session.SendTextAsync("second",settings,"test",attachments:new[]{file}).GetAwaiter().GetResult();
                    Check(api.Messages.Last().Attachments.Count==1,"current attachment dropped");
                    Check(api.Messages.Sum(m=>ChatAttachment.StoredSize(m.Attachments))<8*1024*1024,"previous media was not budgeted");
                }
            });
            Run("failed save does not deadlock persona switch and queue",()=>{
                var protector=new Protection{Fail=true,Block=true};SecretProtection.Configure(protector);var store=new HistoryStore(Folder());
                var session=new ConversationSession(new Model(),new Speech(),()=>null,store);session.SelectPersona("a");
                var queue=new ConversationQueue(session,()=>new PetSettings(),()=>"test",Path.Combine(Folder(),"queue.json"));
                session.AddLiveTurn("a","question","answer");Check(protector.Entered.WaitOne(2000),"save did not start");
                var switching=Task.Run(()=>session.SelectPersona("b"));Thread.Sleep(100);protector.Release.Set();
                Check(switching.Wait(2000),"persona switch deadlocked while save warning reentered queue");
                Check(store.FlushAsync().Wait(2000),"history worker deadlocked");queue.Dispose();session.Dispose();
            });
            Run("long tasks keep showing recent tool progress",()=>{
                SecretProtection.Configure(new Protection());var settings=new PetSettings{AgentEnabled=true,AgentWorkspace=Folder(),ReadReplies=false};settings.Text.BaseUrl="http://127.0.0.1:1";settings.Text.Model="fixture";
                using(var session=new ConversationSession(new ToolModel(),new Speech(),()=>null)){session.SendTextAsync("long task",settings,"fixture").GetAwaiter().GetResult();var tools=session.History.Messages.Last().ToolEvents;Check(tools.Count==100 && tools.First().Id=="tool-5" && tools.Last().Id=="tool-104" && tools.Last().Status=="complete","chat tool progress froze after the first 100 events");}
            });
            Console.WriteLine("Agent reliability tests: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
        }
        static string Folder(){string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"reliability-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(path);return path;}
        static void Check(bool value,string why){if(!value)throw new Exception(why);}
        static void Run(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.Message);}}
        sealed class Protection:ISecretProtector
        {
            public bool Fail,Block;public readonly ManualResetEvent Entered=new ManualResetEvent(false),Release=new ManualResetEvent(false);
            public byte[] Protect(byte[] value){if(Block){Entered.Set();Release.WaitOne();}if(Fail)throw new CryptographicException("injected failure");return value;}
            public byte[] Unprotect(byte[] value){return value;}
        }
        sealed class ToolModel:Model,IAgentConversationApi
        {
            public Task<string> ChatAgentAsync(ConnectionSettings c,IList<ChatMessage> m,AgentWorkspace w,Action<string>d,Action<string>r,Action<AgentToolEvent>t,bool stream,CancellationToken cancel){for(int i=0;i<105;i++){t(new AgentToolEvent{Id="tool-"+i,Name="read_file",Summary="reading "+i,Status="running"});t(new AgentToolEvent{Id="tool-"+i,Name="read_file",Summary="read "+i,Status="complete"});}return Task.FromResult("done");}
        }
        class Model:IConversationApi
        {
            public IList<ChatMessage> Messages;
            public Task<string> ChatAsync(ConnectionSettings c,IList<ChatMessage> m,CancellationToken t){Messages=m;return Task.FromResult("answer");}
            public Task<string> TranscribeAsync(ConnectionSettings c,byte[] w,CancellationToken t){throw new NotSupportedException();}
            public Task<byte[]> SpeechAsync(ConnectionSettings c,string v,string x,CancellationToken t){throw new NotSupportedException();}public void Dispose(){}
        }
        sealed class Speech:IReplySpeech{public Task SpeakAsync(string x,PetSettings p,CancellationToken t){return Task.FromResult(0);}public void Stop(){}public void SetVolume(int v){}public void Dispose(){}}
    }
}
