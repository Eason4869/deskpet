using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class ConversationWorkspaceManagerTests
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        sealed class HeldApi:IConversationApi
        {
            public readonly TaskCompletionSource<bool> Release=new TaskCompletionSource<bool>(),Entered=new TaskCompletionSource<bool>();
            internal bool HoldAfterCancellation;
            public async Task<string> ChatAsync(ConnectionSettings s,IList<ChatMessage> m,CancellationToken c){Entered.TrySetResult(true);if(HoldAfterCancellation)await Release.Task;else await Task.WhenAny(Release.Task,Task.Delay(5000,c));return "done";}
            public Task<string> TranscribeAsync(ConnectionSettings s,byte[] w,CancellationToken c){return Task.FromResult("");}
            public Task<byte[]> SpeechAsync(ConnectionSettings s,string v,string t,CancellationToken c){return Task.FromResult(new byte[0]);}
            public void Dispose(){}
        }
        sealed class Speech:IReplySpeech{public Task SpeakAsync(string t,PetSettings s,CancellationToken c){return Task.FromResult(0);}public void SetVolume(int n){}public void Stop(){}public void Dispose(){}}
        static PetSettings Settings(){var s=new PetSettings{ReadReplies=false};s.Text.BaseUrl="http://127.0.0.1:1";s.Text.Model="fixture";return s;}
        static bool Rejected(Func<Task<ConversationWorkspace>> open){try{open().GetAwaiter().GetResult();return false;}catch(InvalidOperationException){return true;}}
        static void DraftOwnership(string root)
        {
            var catalog=new ProjectCatalog();var settings=Settings();var project=ProjectCatalogStore.QuickProject(catalog,settings);var api=new HeldApi{HoldAfterCancellation=true};
            using(var manager=new ConversationWorkspaceManager(Path.Combine(root,"draft"),"pet",()=>api,()=>new Speech(),()=>null,()=>settings,()=>false))
            {
                var draft=manager.CreateWorkspace(project,new ConversationDefinition{Id="unsaved-quick",Name="Unsent",Mode="quick",PetId="pet"});draft.IsDraft=true;
                Check(Object.ReferenceEquals(manager.OpenCatalogWorkspaceAsync(catalog,draft.Id).GetAwaiter().GetResult(),draft),"UI opening returns canonical open draft without catalog membership");
                Check(!ProjectCatalogStore.AllConversations(catalog).Any()&&manager.OpenCount==1,"UI draft opening neither persists nor duplicates owner");
                bool repairRejected=false;try{manager.GetExistingImageOwnerAsync(catalog,new ImageTaskOrigin{ConversationId=draft.Id,PersonaId="pet"}).GetAwaiter().GetResult();}catch(InvalidOperationException){repairRejected=true;}
                Check(repairRejected,"image repair cannot bypass catalog using an open draft");
                var sending=draft.Session.SendTextAsync("hold close",settings,"pet");api.Entered.Task.GetAwaiter().GetResult();var close=manager.CloseWorkspaceAsync(draft.Id);
                try{Check(!close.IsCompleted&&Rejected(()=>manager.OpenCatalogWorkspaceAsync(catalog,draft.Id)),"UI cannot return or recreate a currently closing unregistered draft");}
                finally{api.Release.TrySetResult(true);close.GetAwaiter().GetResult();try{sending.GetAwaiter().GetResult();}catch(OperationCanceledException){}}
                Check(Rejected(()=>manager.OpenCatalogWorkspaceAsync(catalog,draft.Id))&&manager.OpenCount==0,"UI cannot recreate closed unregistered draft");
                Check(!ProjectCatalogStore.AllConversations(catalog).Any(),"draft lifecycle never writes an empty catalog row");
            }
        }
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
                DraftOwnership(root);
                Console.WriteLine("Conversation workspace manager tests: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
    }
}
