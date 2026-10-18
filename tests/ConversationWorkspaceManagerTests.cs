using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class ConversationWorkspaceManagerTests
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        sealed class HeldApi:IConversationApi
        {
            public readonly TaskCompletionSource<bool> Release=new TaskCompletionSource<bool>();
            public async Task<string> ChatAsync(ConnectionSettings s,IList<ChatMessage> m,CancellationToken c){await Task.WhenAny(Release.Task,Task.Delay(5000,c));return "done";}
            public Task<string> TranscribeAsync(ConnectionSettings s,byte[] w,CancellationToken c){return Task.FromResult("");}
            public Task<byte[]> SpeechAsync(ConnectionSettings s,string v,string t,CancellationToken c){return Task.FromResult(new byte[0]);}
            public void Dispose(){}
        }
        sealed class Speech:IReplySpeech{public Task SpeakAsync(string t,PetSettings s,CancellationToken c){return Task.FromResult(0);}public void SetVolume(int n){}public void Stop(){}public void Dispose(){}}
        static PetSettings Settings(){var s=new PetSettings{ReadReplies=false};s.Text.BaseUrl="http://127.0.0.1:1";s.Text.Model="fixture";return s;}
        public static int Main()
        {
            try
            {
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"manager-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var project=new ProjectDefinition{Id="p",Name="p",Workspace=root};
                var apiA=new HeldApi();
                using(var manager=new ConversationWorkspaceManager(root,"pet",delegate{return apiA;},()=>new Speech(),()=>null,Settings,()=>true))
                {
                    var a=manager.CreateWorkspace(project,new ConversationDefinition{Id="a",Name="A"});var b=manager.CreateWorkspace(project,new ConversationDefinition{Id="b",Name="B"});
                    var send=a.Session.SendTextAsync("work",Settings(),"pet");Check(manager.Busy,"manager reports active workspace work");
                    manager.CloseWorkspaceAsync(b.Id).GetAwaiter().GetResult();Check(manager.OpenCount==1 && manager.Busy,"ClosingOneWorkspaceLeavesOthersRunning");
                    var save=manager.SaveAllAsync();Task.Delay(100).ContinueWith(delegate{apiA.Release.SetResult(true);});send.GetAwaiter().GetResult();save.GetAwaiter().GetResult();Check(!manager.Busy,"SaveAllAsync waits for active work to finish");
                }
                Console.WriteLine("Conversation workspace manager tests: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
    }
}
