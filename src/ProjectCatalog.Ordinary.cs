using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;

namespace Dafeiyu
{
    public sealed partial class ProjectCatalogStore
    {
        // Create-only ordinary admission under the catalog's existing writer lease.
        // Repeated admission preserves every current field of the durable record.
        internal ProjectCatalog SaveOrdinaryConversation(ConversationDefinition definition)
        {
            if(definition==null||definition.Mode!="chat"||String.IsNullOrWhiteSpace(definition.Id)||String.IsNullOrWhiteSpace(definition.PetId))throw new ArgumentException("Ordinary conversation identity required.");
            var submitted=definition.Copy();string temporary=path+".ordinary-"+Guid.NewGuid().ToString("N")+".tmp";
            lock(gate)
            {
                if(readFailed)throw new IOException(TextResources.Get("projectCatalog.save.text"));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using(var writer=new FileStream(path+".write.lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
                {
                    var latest=new ProjectCatalogStore(path).Load();MergeUnpublishedConfigurations(latest);
                    if(BeforeSave!=null)BeforeSave(latest);
                    var existing=latest.Projects.SelectMany(p=>p.Conversations).Concat(latest.UnassignedConversations).Concat(latest.QuickConversations).SingleOrDefault(c=>c.Id==submitted.Id);
                    if(existing!=null)
                    {
                        if(!latest.UnassignedConversations.Contains(existing)||existing.Mode!="chat"||existing.PetId!=submitted.PetId)throw new InvalidOperationException(TextResources.Get("agent.work.notAvailable"));
                        return latest;
                    }
                    latest.UnassignedConversations.Add(submitted);NormalizeDefaults(latest);Validate(latest);
                    try
                    {
                        using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){new DataContractJsonSerializer(typeof(ProjectCatalog)).WriteObject(stream,latest);stream.Flush(true);}
                        AtomicFile.Commit(temporary,path);ObservePublishedConfigurations(latest);if(AfterSave!=null)AfterSave(latest);return latest;
                    }
                    finally{if(File.Exists(temporary))File.Delete(temporary);}
                }
            }
        }
    }
}
