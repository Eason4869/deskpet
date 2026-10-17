using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
namespace Dafeiyu
{
    [DataContract] public sealed class ConversationDefinition
    {
        [DataMember] public string Id="",Name="新对话",Status="idle";[DataMember] public bool NameEdited,Archived;
        [DataMember] public long CreatedUtcTicks,LastOpenedUtcTicks;
        public ConversationDefinition Copy(){return (ConversationDefinition)MemberwiseClone();}
    }
    [DataContract] public sealed class ProjectDefinition
    {
        [DataMember] public string Id="",Name="",Workspace="",Goal="";
        [DataMember] public bool FullAccess=true,BackupFiles=true,PlanMode=false,CommandsEnabled=false;
        [DataMember] public string CommandRecipes="[]";
        [DataMember] public int CommandTimeoutSeconds=120,ContextTokenLimit=32768;
        [DataMember] public long LastOpenedUtcTicks;[DataMember] public bool Archived;
        [DataMember] public List<ConversationDefinition> Conversations=new List<ConversationDefinition>();
        public ProjectDefinition Copy(){var copy=(ProjectDefinition)MemberwiseClone();copy.Conversations=(Conversations??new List<ConversationDefinition>()).Select(c=>c.Copy()).ToList();return copy;}
    }
    [DataContract] public sealed class ProjectCatalog
    {
        [DataMember] public int Version=1;
        [DataMember] public string ActiveProjectId="";
        [DataMember] public List<ProjectDefinition> Projects=new List<ProjectDefinition>();
        public ProjectCatalog Copy(){var copy=new ProjectCatalog{Version=Version,ActiveProjectId=ActiveProjectId};copy.Projects=(Projects??new List<ProjectDefinition>()).Select(p=>p.Copy()).ToList();return copy;}
    }
    public sealed class ProjectCatalogStore
    {
        readonly string path;readonly object gate=new object();
        public ProjectCatalogStore(string filePath){path=Path.GetFullPath(filePath);}
        public ProjectCatalog Load()
        {
            lock(gate)
            {
                if(!File.Exists(path))return new ProjectCatalog();
                ProjectCatalog catalog;using(var stream=File.OpenRead(path))catalog=(ProjectCatalog)new DataContractJsonSerializer(typeof(ProjectCatalog)).ReadObject(stream);
                NormalizeDefaults(catalog);Validate(catalog);return catalog;
            }
        }
        public void Save(ProjectCatalog catalog)
        {
            if(catalog==null)throw new ArgumentNullException("catalog");Validate(catalog);var copy=catalog.Copy();string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            lock(gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){new DataContractJsonSerializer(typeof(ProjectCatalog)).WriteObject(stream,copy);}
                if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
            }
        }
        public static ProjectDefinition CreateProject(ProjectCatalog catalog,string name,string workspace)
        {
            if(catalog==null)throw new ArgumentNullException("catalog");name=(name??"").Trim();if(name.Length<1 || name.Length>80)throw new ArgumentException("项目名称应为 1～80 个字符。");
            if(String.IsNullOrWhiteSpace(workspace) || !Path.IsPathRooted(workspace) || !Directory.Exists(workspace))throw new ArgumentException("项目工作区必须是已存在的绝对目录。");
            var project=new ProjectDefinition{Id=Guid.NewGuid().ToString("N"),Name=name,Workspace=Path.GetFullPath(workspace).TrimEnd('\\','/'),LastOpenedUtcTicks=DateTime.UtcNow.Ticks};
            project.Conversations.Add(new ConversationDefinition{Id=Guid.NewGuid().ToString("N"),Name="新对话",Status="active",CreatedUtcTicks=DateTime.UtcNow.Ticks,LastOpenedUtcTicks=DateTime.UtcNow.Ticks});
            catalog.Projects.Add(project);catalog.ActiveProjectId=project.Id;Validate(catalog);return project;
        }
        public static ConversationDefinition CreateConversation(ProjectCatalog catalog,string projectId,string name)
        {
            var project=(catalog==null?new List<ProjectDefinition>():catalog.Projects).SingleOrDefault(p=>p.Id==projectId);if(project==null)throw new ArgumentException("项目不存在。");
            name=String.IsNullOrWhiteSpace(name)?"新对话":name.Trim();if(name.Length>80)throw new ArgumentException("会话名称不能超过 80 个字符。");
            var conversation=new ConversationDefinition{Id=Guid.NewGuid().ToString("N"),Name=name,CreatedUtcTicks=DateTime.UtcNow.Ticks,LastOpenedUtcTicks=DateTime.UtcNow.Ticks};
            project.Conversations.Add(conversation);project.LastOpenedUtcTicks=conversation.LastOpenedUtcTicks;catalog.ActiveProjectId=project.Id;return conversation;
        }
        public static ProjectCatalog MigrateLegacy(PetSettings settings)
        {
            var catalog=new ProjectCatalog();if(settings==null)return catalog;
            string workspace=String.IsNullOrWhiteSpace(settings.AgentWorkspace)?Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data","agent-workspace"):settings.AgentWorkspace;
            var project=new ProjectDefinition{Id=Guid.NewGuid().ToString("N"),Name="默认项目",Workspace=workspace,Goal=settings.AgentGoal??"",FullAccess=settings.AgentFullAccess,BackupFiles=settings.AgentBackupFiles,PlanMode=settings.AgentPlanMode,CommandsEnabled=settings.AgentCommandsEnabled,CommandRecipes=settings.AgentCommandRecipes??"[]",CommandTimeoutSeconds=settings.AgentCommandTimeoutSeconds<=0?120:settings.AgentCommandTimeoutSeconds,ContextTokenLimit=settings.AgentContextTokenLimit<=0?32768:settings.AgentContextTokenLimit,LastOpenedUtcTicks=DateTime.UtcNow.Ticks};
            project.Conversations.Add(new ConversationDefinition{Id=Guid.NewGuid().ToString("N"),Name="新对话",Status="active",CreatedUtcTicks=DateTime.UtcNow.Ticks,LastOpenedUtcTicks=DateTime.UtcNow.Ticks});catalog.Projects.Add(project);catalog.ActiveProjectId=project.Id;return catalog;
        }
        static void NormalizeDefaults(ProjectCatalog catalog)
        {
            foreach(var project in catalog.Projects)if(project!=null && project.Name=="agent-workspace")project.Name="默认项目";
        }        static void Validate(ProjectCatalog catalog)
        {
            if(catalog==null || catalog.Version!=1 || catalog.Projects==null)throw new SerializationException();
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var project in catalog.Projects)
            {
                if(project==null || String.IsNullOrWhiteSpace(project.Id) || !ids.Add(project.Id) || String.IsNullOrWhiteSpace(project.Name) || project.Name.Length>80 || String.IsNullOrWhiteSpace(project.Workspace) || !Path.IsPathRooted(project.Workspace))throw new SerializationException();
                if(project.Conversations==null)throw new SerializationException();
                var chats=new HashSet<string>(StringComparer.Ordinal);
                foreach(var chat in project.Conversations)if(chat==null || String.IsNullOrWhiteSpace(chat.Id) || !chats.Add(chat.Id) || String.IsNullOrWhiteSpace(chat.Name) || chat.Name.Length>80)throw new SerializationException();
            }
            if(!String.IsNullOrEmpty(catalog.ActiveProjectId) && !catalog.Projects.Any(p=>p.Id==catalog.ActiveProjectId))throw new SerializationException();
        }
    }
}
