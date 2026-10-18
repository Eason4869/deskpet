using System;
using System.IO;
using System.Linq;
namespace Dafeiyu.Tests
{
    public static class ProjectCatalogTests
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        public static int Main()
        {
            try
            {
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"projects-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string workspaceA=Path.Combine(root,"a"),workspaceB=Path.Combine(root,"b");Directory.CreateDirectory(workspaceA);Directory.CreateDirectory(workspaceB);
                var store=new ProjectCatalogStore(Path.Combine(root,"projects.json"));var catalog=new ProjectCatalog();
                var a=ProjectCatalogStore.CreateProject(catalog,"alpha",workspaceA);a.Conversations[0].Name="review";ProjectCatalogStore.CreateConversation(catalog,a.Id,"follow-up");
                var b=ProjectCatalogStore.CreateProject(catalog,"beta",workspaceB);store.Save(catalog);var loaded=store.Load();
                Check(loaded.Projects.Count==2 && loaded.Projects.Any(p=>p.Name=="alpha") && loaded.Projects.Any(p=>p.Name=="beta"),"multiple projects persist independently");
                Check(loaded.Projects.Single(p=>p.Id==a.Id).Conversations.Any(c=>c.Name=="review") && loaded.Projects.Single(p=>p.Id==a.Id).Conversations.Count==2,"project conversations persist with their project");
                bool rejected=false;try{ProjectCatalogStore.CreateProject(catalog,"bad",Path.Combine(root,"missing"));}catch(ArgumentException){rejected=true;}Check(rejected,"new project requires an existing workspace");
                var legacy=new PetSettings{AgentWorkspace=workspaceA,AgentGoal="legacy goal"};var migrated=ProjectCatalogStore.MigrateLegacy(legacy);
                Check(migrated.Projects.Count==0&&!migrated.Sections.Any(section=>section.Id=="chat")&&ProjectCatalogStore.UnassignedProject(migrated,legacy).Workspace==workspaceA&&ProjectCatalogStore.UnassignedProject(migrated,legacy).Goal=="legacy goal","legacy work defaults remain available without recreating the removed default project");
                a.Conversations[0].Archived=true;b.Archived=true;store.Save(catalog);var archived=store.Load();Check(archived.Projects.Single(p=>p.Id==a.Id).Conversations[0].Archived && archived.Projects.Single(p=>p.Id==b.Id).Archived,"project and conversation archive state persists");Console.WriteLine("Project catalog tests: "+checks+" passed");return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
    }
}
