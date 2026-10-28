using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu.Tests
{
    public static class ProjectBindingAdmissionTests
    {
        static int checks;
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        sealed class Api:IConversationApi
        {
            internal int Calls;internal TaskCompletionSource<string> Pending;
            public Task<string> ChatAsync(ConnectionSettings value,IList<ChatMessage> messages,CancellationToken token){Interlocked.Increment(ref Calls);return Pending==null?Task.FromResult("offline reply"):Pending.Task;}
            public Task<string> TranscribeAsync(ConnectionSettings value,byte[] wav,CancellationToken token){return Task.FromResult("offline transcription");}
            public Task<byte[]> SpeechAsync(ConnectionSettings value,string voice,string text,CancellationToken token){return Task.FromResult(new byte[0]);}
            public void Dispose(){}
        }
        sealed class Speech:IReplySpeech
        {public Task SpeakAsync(string value,PetSettings settings,CancellationToken token){return Task.FromResult(0);}public void SetVolume(int value){}public void Stop(){}public void Dispose(){}}
        public static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("Project binding admission: "+checks+" passed");return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
        static async Task Run()
        {
            string root=Path.GetFullPath(Path.Combine("work","project-binding-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);
            var settings=new PetSettings{AgentEnabled=false,ReadReplies=false,ShowPredictedMessages=false,Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1/v1",Model="offline-model"}};
            var api=new Api();var catalog=new ProjectCatalog();var project=ProjectCatalogStore.CreateProject(catalog,"binding-test",root);var conversation=ProjectCatalogStore.CreateConversation(catalog,project.Id,"chat");conversation.Mode="chat";
            using(var manager=new ConversationWorkspaceManager(root,"fixture",()=>api,()=>new Speech(),()=>null,()=>settings,()=>false)){
                var workspace=manager.CreateWorkspace(project,conversation);
                using(await manager.BeginProjectConfigurationAsync(project.Id)){
                    bool rejected=false;try{workspace.Queue.Enqueue("not accepted while rebinding");}catch(InvalidOperationException){rejected=true;}
                    Check(rejected&&!workspace.Queue.HasPending&&api.Calls==0,"project transaction blocks new queued messages without consuming a draft");
                    rejected=false;try{await workspace.Session.SendTextAsync("not started",workspace.EffectiveSettings(),"fixture");}catch(InvalidOperationException){rejected=true;}
                    Check(rejected&&!workspace.Session.IsBusy&&api.Calls==0,"project transaction blocks direct session starts");
                    rejected=false;try{manager.CreateWorkspace(project,ProjectCatalogStore.CreateConversation(catalog,project.Id,"new chat"));}catch(InvalidOperationException){rejected=true;}
                    Check(rejected,"project transaction blocks newly opening workspaces from bypassing admission");
                }
                api.Pending=new TaskCompletionSource<string>();var sending=workspace.Session.SendTextAsync("existing request",workspace.EffectiveSettings(),"fixture");
                bool busy=false;try{using(await manager.BeginProjectConfigurationAsync(project.Id)){} }catch(InvalidOperationException){busy=true;}
                Check(busy&&workspace.Session.IsBusy,"existing foreground request refuses project rebinding without cancellation");
                api.Pending.SetResult("finished reply");await sending;api.Pending=null;
                using(await manager.BeginProjectConfigurationAsync(project.Id))Check(!workspace.Session.IsBusy,"request completion releases project admission for configuration");
                await workspace.Session.SendTextAsync("after configuration",workspace.EffectiveSettings(),"fixture");Check(api.Calls==2,"normal sends resume after configuration publication");
            }
        }
    }
}
