using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu.Tests
{
    public static class ProjectConfigurationSaveTests
    {
        static int checks;
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        public static int Main(){try{Run().GetAwaiter().GetResult();Console.WriteLine("Project configuration saves: "+checks+" passed");return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
        static async Task Run()
        {
            string root=Path.GetFullPath(Path.Combine("work","project-config-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(root);string path=Path.Combine(root,"projects.json");
            var store=new ProjectCatalogStore(path);var catalog=new ProjectCatalog();var project=ProjectCatalogStore.CreateProject(catalog,"original",root);store.Save(catalog);
            var staleHost=catalog.Copy();var changed=project.Copy();changed.Goal="saved-goal";changed.Name="saved-name";
            var entered=new ManualResetEventSlim();var release=new ManualResetEventSlim();
            store.BeforeConfigurationWrite=()=>{entered.Set();if(!release.Wait(10000))throw new IOException("fixture release timeout");};
            var saving=store.SaveProjectConfigurationAsync(project,changed);Check(entered.Wait(5000)&&!saving.IsCompleted,"project configuration I/O runs on a held worker");
            ProjectCatalogStore.CreateConversation(catalog,ProjectCatalogStore.QuickProjectId,"concurrent quick chat");store.Save(catalog);release.Set();
            using(var publication=await saving){
                Check(project.Goal!="saved-goal"&&store.Load().Projects.Single().Goal=="saved-goal","project configuration commits before UI publication");
                Check(store.Load().QuickConversations.Count==1,"scoped configuration commit preserves metadata written before it");
                ProjectCatalogStore.CreateConversation(staleHost,ProjectCatalogStore.UnassignedProjectId,"later ordinary chat");new ProjectCatalogStore(path).Save(staleHost);
                var persisted=store.Load();Check(persisted.Projects.Single().Goal=="saved-goal"&&persisted.Projects.Single().Name=="saved-name","stale synchronous host cannot undo an unpublished configuration");
                staleHost.Projects.Single().Name="newer-user-name";new ProjectCatalogStore(path).Save(staleHost);
                Check(store.Load().Projects.Single().Name=="newer-user-name","newer explicit metadata edit wins its changed field");
                publication.Publish(project);Check(project.Goal=="saved-goal"&&project.Name=="newer-user-name","publication resolves concurrent field edits without replacing project identity");
            }
            store.BeforeConfigurationWrite=null;var before=File.ReadAllBytes(path);changed=project.Copy();changed.Goal="failed-goal";
            store.BeforeConfigurationCommit=()=>{throw new IOException("fixture disk failure");};bool failed=false;
            try{using(await store.SaveProjectConfigurationAsync(project,changed)){} }catch(IOException){failed=true;}
            Check(failed&&File.ReadAllBytes(path).SequenceEqual(before)&&project.Goal=="saved-goal","failed project write preserves durable and live configuration");
            Check(!Directory.GetFiles(root,"*.configuration-*.tmp").Any(),"failed project transaction cleans only its owned temporary file");
            store.BeforeConfigurationCommit=null;var latest=store.Load();latest.Projects.Single().Goal="normal-after-failure";store.Save(latest);
            Check(store.Load().Projects.Single().Goal=="normal-after-failure","failed configuration leaves no publication fence behind");
            File.WriteAllText(path,"{invalid");bool invalid=false;try{using(await store.SaveProjectConfigurationAsync(project,changed)){} }catch(Exception){invalid=true;}
            Check(invalid&&File.ReadAllText(path)=="{invalid","unreadable project catalog is never replaced with defaults");
        }
    }
}
