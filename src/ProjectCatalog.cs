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
        [DataMember] public string Id="",Name="新对话",Status="idle";[DataMember] public bool NameEdited,Archived,HasUnreadCompletion,Pinned;
        [DataMember] public string Mode="work",PetId="";
        [DataMember] public string SectionId="";
        [DataMember] public bool HasPermissionOverride,SandboxEnabled;[DataMember] public AgentApprovalMode ApprovalMode;
        [DataMember] public long CreatedUtcTicks,LastOpenedUtcTicks;
        public ConversationDefinition Copy(){return (ConversationDefinition)MemberwiseClone();}
    }
    [DataContract] public sealed class ProjectDefinition
    {
        [DataMember] public string Id="",Name="",Workspace="",Goal="",SectionId="projects";
        [DataMember] public bool IsQuickChatDefault,Pinned;
        [DataMember] public bool FullAccess=true,BackupFiles=true,PlanMode=false,CommandsEnabled=false;
        [DataMember] public string CommandRecipes="[]";
        [DataMember] public AgentTerminalPolicy TerminalPolicy;
        [DataMember] public AgentApprovalMode ApprovalMode=AgentApprovalMode.Auto;[DataMember] public bool SandboxEnabled=true;[DataMember] public string SandboxReadDirectories;
        [DataMember] public bool TrustProjectInstructions;
        [DataMember] public string GlobalInstructions="",SkillsDirectory="";
        [DataMember] public int CommandTimeoutSeconds=120,ContextTokenLimit=32768;
        [DataMember] public long LastOpenedUtcTicks;[DataMember] public bool Archived;
        [DataMember] public List<ConversationDefinition> Conversations=new List<ConversationDefinition>();
        [OnDeserializing] void BeforeDeserialize(StreamingContext context){ApprovalMode=AgentApprovalMode.Legacy;SandboxEnabled=false;}
        public ProjectDefinition Copy(){var copy=(ProjectDefinition)MemberwiseClone();copy.Conversations=(Conversations??new List<ConversationDefinition>()).Select(c=>c.Copy()).ToList();return copy;}
    }
    [DataContract] public sealed class SidebarSection
    {
        [DataMember] public string Mode="work";
        [DataMember] public string Id="",Name="";
        public SidebarSection Copy(){return (SidebarSection)MemberwiseClone();}
    }
    [DataContract] public sealed class ProjectCatalog
    {
        Dictionary<string,bool> sidebarExpansion,sidebarSectionExpansion;
        internal Dictionary<string,bool> SidebarSectionExpansion {get{return sidebarSectionExpansion??(sidebarSectionExpansion=new Dictionary<string,bool>());}}
        internal Dictionary<string,bool> SidebarExpansion {get{return sidebarExpansion??(sidebarExpansion=new Dictionary<string,bool>());}}
        [DataMember] public int Version=1,OwnershipVersion=1;
        [DataMember] public bool LegacyRootImported;
        [DataMember] public string ActiveProjectId="";
        [DataMember] public List<ProjectDefinition> Projects=new List<ProjectDefinition>();
        [DataMember] public List<ConversationDefinition> UnassignedConversations=new List<ConversationDefinition>();
        [DataMember] public List<ConversationDefinition> QuickConversations=new List<ConversationDefinition>();
        [DataMember] public Dictionary<string,List<string>> SidebarConversationOrder=new Dictionary<string,List<string>>();
        [DataMember] public Dictionary<string,string> SidebarSectionLabels=new Dictionary<string,string>();
        [DataMember] public bool RecentIncludeProjects=true;
        [DataMember] public List<SidebarSection> Sections=new List<SidebarSection>{new SidebarSection{Id="projects",Name="项目"}};
        public ProjectCatalog Copy(){var copy=new ProjectCatalog{Version=Version,OwnershipVersion=OwnershipVersion,LegacyRootImported=LegacyRootImported,ActiveProjectId=ActiveProjectId,RecentIncludeProjects=RecentIncludeProjects};copy.Projects=(Projects??new List<ProjectDefinition>()).Select(p=>p.Copy()).ToList();copy.Sections=(Sections??new List<SidebarSection>()).Select(s=>s.Copy()).ToList();copy.UnassignedConversations=(UnassignedConversations??new List<ConversationDefinition>()).Select(c=>c.Copy()).ToList();copy.QuickConversations=(QuickConversations??new List<ConversationDefinition>()).Select(c=>c.Copy()).ToList();copy.SidebarConversationOrder=(SidebarConversationOrder??new Dictionary<string,List<string>>()).ToDictionary(x=>x.Key,x=>new List<string>(x.Value??new List<string>()));copy.SidebarSectionLabels=new Dictionary<string,string>(SidebarSectionLabels??new Dictionary<string,string>());return copy;}
    }
    public sealed partial class ProjectCatalogStore
    {
        public const string UnassignedProjectId="unassigned",QuickProjectId="quick",DefaultChatSectionId="default_chat";
        public static ProjectDefinition QuickProject(ProjectCatalog catalog,PetSettings settings=null){var result=UnassignedProject(catalog,settings);result.Id=QuickProjectId;result.Name="快速对话";result.Conversations=catalog.QuickConversations;return result;}
        public static ProjectDefinition UnassignedProject(ProjectCatalog catalog,PetSettings settings=null)
        {
            return new ProjectDefinition{Id=UnassignedProjectId,Name="无项目",Workspace=settings==null?AppDomain.CurrentDomain.BaseDirectory:settings.AgentWorkspace,FullAccess=settings!=null&&settings.AgentFullAccess,BackupFiles=settings==null||settings.AgentBackupFiles,PlanMode=settings!=null&&settings.AgentPlanMode,Goal=settings==null?"":settings.AgentGoal,TerminalPolicy=settings==null?AgentTerminalPolicy.Disabled:settings.AgentTerminalPolicy,ApprovalMode=settings==null?AgentApprovalMode.Legacy:settings.AgentApprovalMode,SandboxEnabled=settings!=null&&settings.AgentSandboxEnabled,SandboxReadDirectories=settings==null?"":settings.AgentSandboxReadDirectories,TrustProjectInstructions=settings!=null&&settings.AgentTrustProjectInstructions,GlobalInstructions=settings==null?"":settings.AgentGlobalInstructions,SkillsDirectory=settings==null?"":settings.AgentSkillsDirectory,CommandTimeoutSeconds=settings==null?120:settings.AgentCommandTimeoutSeconds,ContextTokenLimit=settings==null?32768:settings.AgentContextTokenLimit,Conversations=catalog.UnassignedConversations};
        }
        public static ProjectDefinition FindProject(ProjectCatalog catalog,string id,PetSettings settings=null){return id==QuickProjectId?QuickProject(catalog,settings):id==UnassignedProjectId?UnassignedProject(catalog,settings):catalog.Projects.SingleOrDefault(p=>p.Id==id);}
        readonly string path;readonly object gate=new object();bool readFailed;
        internal void PreserveAfterReadFailure(){lock(gate)readFailed=true;}
        public ProjectCatalogStore(string filePath){path=Path.GetFullPath(filePath);}
        public ProjectCatalog Load()
        {
            lock(gate)
            {
                try{if(!File.Exists(path)){readFailed=false;return new ProjectCatalog();}
                ProjectCatalog catalog;using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))catalog=(ProjectCatalog)new DataContractJsonSerializer(typeof(ProjectCatalog)).ReadObject(stream);
                NormalizeDefaults(catalog);Validate(catalog);foreach(var conversation in catalog.Projects.SelectMany(p=>p.Conversations).Concat(catalog.UnassignedConversations).Concat(catalog.QuickConversations))if(conversation.Status=="running")conversation.Status="idle";readFailed=false;return catalog;}catch{readFailed=true;throw;}
            }
        }
        public void Save(ProjectCatalog catalog)
        {
            if(catalog==null)throw new ArgumentNullException("catalog");NormalizeDefaults(catalog);Validate(catalog);var copy=catalog.Copy();string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            lock(gate)
            {
                if(readFailed)throw new IOException("原项目记录未能完整读取，已保留原文件；修复读取问题并重新打开后才能保存。");Directory.CreateDirectory(Path.GetDirectoryName(path));
                using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){new DataContractJsonSerializer(typeof(ProjectCatalog)).WriteObject(stream,copy);}
                AtomicFile.Commit(temp,path);
            }
        }
        public static ProjectDefinition CreateProject(ProjectCatalog catalog,string name,string workspace,string sectionId="projects")
        {
            if(catalog==null)throw new ArgumentNullException("catalog");name=(name??"").Trim();if(name.Length<1 || name.Length>80)throw new ArgumentException("项目名称应为 1～80 个字符。");
            if(String.IsNullOrWhiteSpace(workspace) || !Path.IsPathRooted(workspace) || !Directory.Exists(workspace))throw new ArgumentException("项目工作区必须是已存在的绝对目录。");
            NormalizeDefaults(catalog);if(!catalog.Sections.Any(s=>s.Id==sectionId&&s.Mode=="work"))throw new ArgumentException("分区不存在。");
            var project=new ProjectDefinition{Id=Guid.NewGuid().ToString("N"),Name=name,SectionId=sectionId,Workspace=Path.GetFullPath(workspace).TrimEnd('\\','/'),LastOpenedUtcTicks=DateTime.UtcNow.Ticks};
            project.Conversations.Add(new ConversationDefinition{Id=Guid.NewGuid().ToString("N"),Name="新对话",Status="active",CreatedUtcTicks=DateTime.UtcNow.Ticks,LastOpenedUtcTicks=DateTime.UtcNow.Ticks});
            catalog.Projects.Add(project);catalog.ActiveProjectId=project.Id;Validate(catalog);return project;
        }
        public static ConversationDefinition CreateConversation(ProjectCatalog catalog,string projectId,string name)
        {
            var project=catalog==null?null:FindProject(catalog,projectId);if(project==null)throw new ArgumentException("项目不存在。");
            name=String.IsNullOrWhiteSpace(name)?"新对话":name.Trim();if(name.Length>80)throw new ArgumentException("会话名称不能超过 80 个字符。");
            var conversation=new ConversationDefinition{Id=Guid.NewGuid().ToString("N"),Name=name,CreatedUtcTicks=DateTime.UtcNow.Ticks,LastOpenedUtcTicks=DateTime.UtcNow.Ticks};
            if(projectId==QuickProjectId)conversation.Mode="quick";project.Conversations.Add(conversation);project.LastOpenedUtcTicks=conversation.LastOpenedUtcTicks;catalog.ActiveProjectId=project.Id==QuickProjectId||project.Id==UnassignedProjectId?"":project.Id;return conversation;
        }
        // Moves a conversation to another project. The conversation keeps its Id so
        // its history and queue stay attached; only the owning project changes.
        public static ConversationDefinition MoveConversation(ProjectCatalog catalog,string fromProjectId,string toProjectId,string conversationId)
        {
            if(catalog==null)throw new ArgumentNullException("catalog");
            var projects=catalog.Projects??new List<ProjectDefinition>();
            var from=FindProject(catalog,fromProjectId);var to=FindProject(catalog,toProjectId);
            if(from==null || to==null)throw new ArgumentException("项目不存在。");
            if(ReferenceEquals(from,to))throw new ArgumentException("对话已属于该项目。");
            var conversation=(from.Conversations??new List<ConversationDefinition>()).SingleOrDefault(c=>c.Id==conversationId);
            if(conversation==null)throw new ArgumentException("对话不存在。");
            if((to.Conversations??new List<ConversationDefinition>()).Any(c=>c.Id==conversationId))throw new ArgumentException("该项目中已有同名对话记录。");
            if(conversation.Mode!="work" && to.Id!=QuickProjectId)throw new InvalidOperationException("对话模式只能保存在快速对话分区，不能选择项目。");if(conversation.Mode=="work" && to.Id==QuickProjectId)throw new InvalidOperationException("工作对话不能移入快速对话分区。");
            from.Conversations.Remove(conversation);
            if(to.Conversations==null)to.Conversations=new List<ConversationDefinition>();
            conversation.LastOpenedUtcTicks=DateTime.UtcNow.Ticks;
            to.Conversations.Add(conversation);to.LastOpenedUtcTicks=conversation.LastOpenedUtcTicks;
            catalog.ActiveProjectId=to.Id==UnassignedProjectId||to.Id==QuickProjectId?"":to.Id;Validate(catalog);return conversation;
        }
        public static ProjectCatalog MigrateLegacy(PetSettings settings)
        {
            return new ProjectCatalog();
        }
        public static SidebarSection CreateSection(ProjectCatalog catalog,string name,string mode="work")
        {
            NormalizeDefaults(catalog);if(mode!="chat"&&mode!="work")throw new ArgumentException("分区模式无效。");name=SectionName(name);var section=new SidebarSection{Id=Guid.NewGuid().ToString("N"),Name=name,Mode=mode};catalog.Sections.Add(section);return section;
        }
        public static void RenameSection(ProjectCatalog catalog,string id,string name)
        {
            NormalizeDefaults(catalog);var section=catalog.Sections.SingleOrDefault(s=>s.Id==id);if(section==null)throw new ArgumentException("分区不存在。");section.Name=SectionName(name);
        }
        static string SectionName(string name){name=(name??"").Trim();if(name.Length<1 || name.Length>40)throw new ArgumentException("分区名称应为 1～40 个字符。");return name;}
        public static ProjectDefinition EnsureQuickChatProject(ProjectCatalog catalog,PetSettings settings)
        {
            NormalizeDefaults(catalog);return QuickProject(catalog,settings);
        }
        internal static void NormalizeDefaults(ProjectCatalog catalog)
        {
            if(catalog==null || catalog.Projects==null)throw new SerializationException();
            if(catalog.Sections==null){catalog.Sections=new List<SidebarSection>();catalog.RecentIncludeProjects=true;}
            if(catalog.UnassignedConversations==null)catalog.UnassignedConversations=new List<ConversationDefinition>();
            if(catalog.QuickConversations==null)catalog.QuickConversations=new List<ConversationDefinition>();
            if(catalog.SidebarConversationOrder==null)catalog.SidebarConversationOrder=new Dictionary<string,List<string>>();
            if(catalog.SidebarSectionLabels==null)catalog.SidebarSectionLabels=new Dictionary<string,string>();
            foreach(var section in catalog.Sections)if(section!=null&&String.IsNullOrEmpty(section.Mode))section.Mode="work";
            if(!catalog.Sections.Any(s=>s!=null&&s.Id==DefaultChatSectionId))catalog.Sections.Add(new SidebarSection{Id=DefaultChatSectionId,Name="默认分区",Mode="chat"});
            // Full-window chats were previously stored beside quick-window chats.
            // Stable IDs keep history and queue files in their original locations.
            foreach(var chat in catalog.QuickConversations.Where(c=>c!=null&&c.Mode=="chat").ToArray()){catalog.QuickConversations.Remove(chat);if(!catalog.UnassignedConversations.Any(c=>c.Id==chat.Id))catalog.UnassignedConversations.Add(chat);}
            foreach(var chat in catalog.UnassignedConversations.Where(c=>c!=null&&c.Mode=="chat"))if(!catalog.Sections.Any(s=>s!=null&&s.Mode=="chat"&&s.Id==chat.SectionId))chat.SectionId=DefaultChatSectionId;
            catalog.Sections.RemoveAll(section=>section!=null&&section.Id=="chat");if(!catalog.Sections.Any(section=>section!=null&&section.Id=="projects"))catalog.Sections.Insert(0,new SidebarSection{Id="projects",Name="项目"});
            foreach(var project in catalog.Projects.ToArray())if(project!=null){if(project.IsQuickChatDefault){foreach(var conversation in project.Conversations??new List<ConversationDefinition>()){if(!catalog.QuickConversations.Any(c=>c.Id==conversation.Id)){conversation.Mode="legacy-quick";catalog.QuickConversations.Add(conversation);}}catalog.Projects.Remove(project);if(catalog.ActiveProjectId==project.Id)catalog.ActiveProjectId="";}else if(!catalog.Sections.Any(s=>s!=null&&s.Id==project.SectionId&&s.Mode=="work"))project.SectionId="projects";}
        }        static void Validate(ProjectCatalog catalog)
        {
            if(catalog==null || catalog.Version!=1 || catalog.Projects==null)throw new SerializationException();
            if(catalog.Sections==null || catalog.Sections.Any(s=>s==null || String.IsNullOrWhiteSpace(s.Id) || String.IsNullOrWhiteSpace(s.Name) || s.Name.Length>40) || catalog.Sections.Select(s=>s.Id).Distinct(StringComparer.Ordinal).Count()!=catalog.Sections.Count)throw new SerializationException();
            var ids=new HashSet<string>(StringComparer.Ordinal);var conversationIds=new HashSet<string>(StringComparer.Ordinal);
            foreach(var project in catalog.Projects)
            {
                if(project==null || String.IsNullOrWhiteSpace(project.Id) || !ids.Add(project.Id) || String.IsNullOrWhiteSpace(project.Name) || project.Name.Length>80 || String.IsNullOrWhiteSpace(project.Workspace) || !Path.IsPathRooted(project.Workspace))throw new SerializationException();
                if(project.Conversations==null || !catalog.Sections.Any(s=>s.Id==project.SectionId&&s.Mode=="work"))throw new SerializationException();
                foreach(var chat in project.Conversations)ValidateConversation(chat,conversationIds);
            }
            if(catalog.UnassignedConversations==null)throw new SerializationException();foreach(var chat in catalog.UnassignedConversations)ValidateConversation(chat,conversationIds);foreach(var chat in catalog.QuickConversations)ValidateConversation(chat,conversationIds);
            if(!String.IsNullOrEmpty(catalog.ActiveProjectId) && !catalog.Projects.Any(p=>p.Id==catalog.ActiveProjectId))throw new SerializationException();
        }
        static void ValidateConversation(ConversationDefinition chat,HashSet<string> ids){if(chat==null || String.IsNullOrWhiteSpace(chat.Id) || !ids.Add(chat.Id) || String.IsNullOrWhiteSpace(chat.Name) || chat.Name.Length>80)throw new SerializationException();}
    }
}
