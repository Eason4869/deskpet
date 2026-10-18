using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class ConversationTitleTests
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        public static int Main()
        {
            try
            {
                var api=new TitleApi{Reply="标题：整理桌面文件夹。"};var session=new ConversationSession(api,new Silent(),()=>null);session.History.Commit("在桌面新建一个文件夹","已经建好了。");
                var cfg=new PetSettings{Text=new ConnectionSettings{BaseUrl="http://127.0.0.1:1",Model="fixture"}};string title=session.GenerateTitleAsync(cfg,CancellationToken.None).GetAwaiter().GetResult();
                Check(title=="整理桌面文件夹" && api.RequestCount==1 && api.LastPrompt.Contains("只输出标题"),"AI conversation title is generated and normalized: "+title+" / "+api.RequestCount+" / "+api.LastPrompt);
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"titles-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var store=new ProjectCatalogStore(Path.Combine(root,"projects.json"));var catalog=ProjectCatalogStore.MigrateLegacy(new PetSettings{AgentWorkspace=root});var project=ProjectCatalogStore.CreateProject(catalog,"新项目",root);project.Name="我的项目";var chat=project.Conversations[0];chat.Name="手工标题";chat.NameEdited=true;store.Save(catalog);var loaded=store.Load();
                Check(loaded.Projects[0].Name=="我的项目" && loaded.Projects[0].Conversations[0].Name=="手工标题" && loaded.Projects[0].Conversations[0].NameEdited,"manual project and conversation names persist");
                Console.WriteLine("Conversation title tests: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
        sealed class TitleApi:IConversationApi
        {
            public string Reply="",LastPrompt="";public int RequestCount;
            public Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,CancellationToken token){RequestCount++;LastPrompt=messages[messages.Count-1].Content;return Task.FromResult(Reply);}
            public Task<string> TranscribeAsync(ConnectionSettings settings,byte[] wav,CancellationToken token){return Task.FromResult("");}
            public Task<byte[]> SpeechAsync(ConnectionSettings settings,string voice,string text,CancellationToken token){return Task.FromResult(new byte[0]);}
            public void Dispose(){}
        }
        sealed class Silent:IReplySpeech{public Task SpeakAsync(string text,PetSettings settings,CancellationToken token){return Task.FromResult(0);}public void SetVolume(int volume){}public void Stop(){}public void Dispose(){}}
    }
}
