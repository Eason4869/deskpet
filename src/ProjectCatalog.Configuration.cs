using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;

namespace Dafeiyu
{
    public sealed partial class ProjectCatalogStore
    {
        static readonly FieldInfo[] configurationFields=("Name Workspace Goal FullAccess BackupFiles PlanMode ApprovalMode SandboxEnabled SandboxReadDirectories TrustProjectInstructions GlobalInstructions SkillsDirectory TerminalPolicy CommandTimeoutSeconds BackgroundJobTimeoutSeconds ContextTokenLimit").Split(' ').Select(name=>typeof(ProjectDefinition).GetField(name)).ToArray();
        static readonly object configurationGate=new object();
        static readonly Dictionary<string,List<ProjectConfigurationPublication>> configurationPublications=new Dictionary<string,List<ProjectConfigurationPublication>>(Path.DirectorySeparatorChar=='\\'?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
        internal Action BeforeConfigurationWrite,BeforeConfigurationCommit,BeforeConfigurationPublication;

        internal sealed class ConfigurationChange
        {internal FieldInfo Field;internal object Original,Current;}
        internal sealed class ProjectConfigurationPublication:IDisposable
        {
            readonly string path;internal readonly string ProjectId;internal readonly ConfigurationChange[] Changes;
            bool removed,disposed;
            internal ProjectConfigurationPublication(string path,string id,ConfigurationChange[] changes){this.path=path;ProjectId=id;Changes=changes;}
            internal void Merge(ProjectCatalog candidate)
            {
                var project=candidate.Projects.FirstOrDefault(p=>p.Id==ProjectId);if(project==null)return;
                foreach(var change in Changes)if(Object.Equals(change.Field.GetValue(project),change.Original))change.Field.SetValue(project,change.Current);
            }
            internal void Observe(ProjectCatalog committed)
            {
                var project=committed.Projects.FirstOrDefault(p=>p.Id==ProjectId);removed=project==null;
                if(project!=null)foreach(var change in Changes)change.Current=change.Field.GetValue(project);
            }
            internal void Publish(ProjectDefinition target)
            {
                lock(configurationGate){if(disposed)throw new ObjectDisposedException("ProjectConfigurationPublication");if(target.Id!=ProjectId)throw new ArgumentException("Project identity changed.");if(!removed)foreach(var change in Changes)change.Field.SetValue(target,change.Current);}
            }
            public void Dispose()
            {
                lock(configurationGate){if(disposed)return;disposed=true;List<ProjectConfigurationPublication> list;if(configurationPublications.TryGetValue(path,out list)){list.Remove(this);if(list.Count==0)configurationPublications.Remove(path);}}
            }
        }
        internal static ProjectDefinition ConfigurationSnapshot(ProjectDefinition source)
        {var result=new ProjectDefinition{Id=source.Id};foreach(var field in configurationFields)field.SetValue(result,field.GetValue(source));return result;}
        // Only scalar configuration intent crosses the worker boundary. Never queue
        // a whole catalog captured before concurrent chats/renames/sidebar edits.
        internal Task<ProjectConfigurationPublication> SaveProjectConfigurationAsync(ProjectDefinition original,ProjectDefinition edited)
        {
            if(original==null||edited==null||original.Id!=edited.Id)throw new ArgumentException("Project identity changed.");
            var changes=configurationFields.Where(field=>!Object.Equals(field.GetValue(original),field.GetValue(edited))).Select(field=>new ConfigurationChange{Field=field,Original=field.GetValue(original),Current=field.GetValue(edited)}).ToArray();
            var publication=new ProjectConfigurationPublication(path,original.Id,changes);
            return Task.Run(delegate{
                if(BeforeConfigurationWrite!=null)BeforeConfigurationWrite();
                string temporary=path+".configuration-"+Guid.NewGuid().ToString("N")+".tmp";bool committed=false;
                try{
                    lock(gate){
                        if(readFailed)throw new IOException(TextResources.Get("projectCatalog.save.text"));
                        using(var writeLease=new FileStream(path+".write.lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
                            lock(configurationGate){List<ProjectConfigurationPublication> active;if(configurationPublications.TryGetValue(path,out active)&&active.Any(p=>p.ProjectId==publication.ProjectId))throw new IOException(TextResources.Get("dataMaintenance.busy"));}
                            ProjectCatalog latest;
                            try{using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))latest=(ProjectCatalog)new DataContractJsonSerializer(typeof(ProjectCatalog)).ReadObject(stream);NormalizeDefaults(latest);Validate(latest);}
                            catch{readFailed=true;throw;}
                            var project=latest.Projects.FirstOrDefault(p=>p.Id==publication.ProjectId);if(project==null)throw new InvalidOperationException("Project no longer exists.");
                            foreach(var change in changes)change.Field.SetValue(project,change.Current);
                            NormalizeDefaults(latest);Validate(latest);if(BeforeSave!=null)BeforeSave(latest);
                            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){new DataContractJsonSerializer(typeof(ProjectCatalog)).WriteObject(stream,latest);stream.Flush(true);}
                            if(BeforeConfigurationCommit!=null)BeforeConfigurationCommit();AtomicFile.Commit(temporary,path);
                            lock(configurationGate){List<ProjectConfigurationPublication> list;if(!configurationPublications.TryGetValue(path,out list))configurationPublications[path]=list=new List<ProjectConfigurationPublication>();publication.Observe(latest);list.Add(publication);}
                            committed=true;if(AfterSave!=null)AfterSave(latest);
                        }
                    }
                    if(BeforeConfigurationPublication!=null)BeforeConfigurationPublication();return publication;
                }catch{publication.Dispose();throw;}
                finally{if(!committed&&File.Exists(temporary))File.Delete(temporary);}
            });
        }
        void MergeUnpublishedConfigurations(ProjectCatalog candidate)
        {lock(configurationGate){List<ProjectConfigurationPublication> list;if(configurationPublications.TryGetValue(path,out list))foreach(var publication in list)publication.Merge(candidate);}}
        void ObservePublishedConfigurations(ProjectCatalog committed)
        {lock(configurationGate){List<ProjectConfigurationPublication> list;if(configurationPublications.TryGetValue(path,out list))foreach(var publication in list)publication.Observe(committed);}}
    }
}
