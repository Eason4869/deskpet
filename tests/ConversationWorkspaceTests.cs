using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class ConversationWorkspaceTests
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        sealed class Api:IConversationApi
        {
            public Task<string> ChatAsync(ConnectionSettings s,IList<ChatMessage> m,CancellationToken c){return Task.FromResult("reply-"+m[m.Count-1].Content);}
            public Task<string> TranscribeAsync(ConnectionSettings s,byte[] w,CancellationToken c){return Task.FromResult("");}
            public Task<byte[]> SpeechAsync(ConnectionSettings s,string v,string t,CancellationToken c){return Task.FromResult(new byte[0]);}
            public void Dispose(){}
        }
        sealed class Speech:IReplySpeech{public Task SpeakAsync(string t,PetSettings s,CancellationToken c){return Task.FromResult(0);}public void SetVolume(int n){}public void Stop(){}public void Dispose(){}}
        static PetSettings Settings(){var s=new PetSettings{ReadReplies=false,SaveChatHistory=true};s.Text.BaseUrl="http://127.0.0.1:1";s.Text.Model="fixture";return s;}
        public static int Main()
        {
            try
            {
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"workspaces-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                var project=new ProjectDefinition{Id="p",Name="project",Workspace=root};var chatA=new ConversationDefinition{Id="a",Name="A"};var chatB=new ConversationDefinition{Id="b",Name="B"};
                using(var a=ConversationWorkspace.Create(project,chatA,root,"pet",()=>new Api(),()=>new Speech(),()=>null,Settings,()=>true))
                using(var b=ConversationWorkspace.Create(project,chatB,root,"pet",()=>new Api(),()=>new Speech(),()=>null,Settings,()=>true))
                {
                    a.Session.SendTextAsync("alpha",Settings(),"pet").GetAwaiter().GetResult();b.Session.SendTextAsync("beta",Settings(),"pet").GetAwaiter().GetResult();
                    Check(a.Session.History.Messages.Count==2 && b.Session.History.Messages.Count==2,"two workspaces keep independent history");
                    Check(a.Session.History.Messages[0].Content=="alpha" && b.Session.History.Messages[0].Content=="beta","workspace history cannot leak across conversations");
                    Check(a.Queue.Items.Count==0 && b.Queue.Items.Count==0,"workspace queues are separate");
                    Check(a.DataDirectory!=b.DataDirectory && File.Exists(Path.Combine(a.DataDirectory,"history"))==false,"workspace storage is namespaced by conversation");
                }
                Console.WriteLine("Conversation workspace tests: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
    }
}
