using System;using System.Collections.Generic;using System.IO;using System.Linq;
namespace Dafeiyu {
 public sealed partial class ProjectCatalogStore {
  public static IEnumerable<ConversationDefinition> AllConversations(ProjectCatalog catalog){return catalog.Projects.SelectMany(p=>p.Conversations).Concat(catalog.UnassignedConversations).Concat(catalog.QuickConversations);}
  public static bool VisibleForPet(ConversationDefinition conversation,string petId){return conversation.Mode=="work" || String.IsNullOrEmpty(petId) || conversation.PetId==petId;}
  // Preserve original encrypted files before splitting historical mixed-mode turns.
  public static ProjectCatalog UpgradeOwnership(ProjectCatalog original,string dataRoot,IEnumerable<string> petIds,string currentPet){
   if(original.OwnershipVersion>=1 && original.LegacyRootImported)return original;var catalog=original.Copy();NormalizeDefaults(catalog);var owners=(petIds??new string[0]).Concat(new[]{currentPet??""}).Distinct().ToArray();
   if(!catalog.LegacyRootImported){
    string oldRoot=Path.Combine(dataRoot,"history");var files=Directory.Exists(oldRoot)?Directory.GetFiles(oldRoot,"*.history"):new string[0];
    if(files.Length>0){
     var imported=new ConversationDefinition{Id=Guid.NewGuid().ToString("N"),Name=TextResources.Get("conversationOwnership.upgradeOwnership.name"),Mode=null};
     string target=Path.Combine(ConversationWorkspace.ResolveDataDirectory(dataRoot,imported.Id),"history");Directory.CreateDirectory(target);
     foreach(string file in files)File.Copy(file,Path.Combine(target,Path.GetFileName(file)),false);
     catalog.UnassignedConversations.Add(imported);
    }
    catalog.LegacyRootImported=true;
   }
   foreach(var definition in AllConversations(catalog).ToArray()){
    if(definition.Mode=="work" || definition.Mode=="chat" || definition.Mode=="quick")continue;
    var sourceOwner=catalog.Projects.FirstOrDefault(p=>p.Conversations.Contains(definition));bool wasQuick=catalog.QuickConversations.Contains(definition);var source=ConversationWorkspace.ResolveDataDirectory(dataRoot,definition.Id);var history=new HistoryStore(Path.Combine(source,"history"));var batches=owners.Select(id=>{var messages=history.Load(id);if(history.Warning.Length>0)throw new IOException(TextResources.Get("conversationOwnership.upgradeOwnership.ownersSelect"));return new{Pet=id,Messages=messages};}).Where(b=>b.Messages.Count>0).ToArray();
    string backup=Path.Combine(dataRoot,"ownership-backups",definition.Id);if(Directory.Exists(history.DirectoryPath))foreach(string file in Directory.GetFiles(history.DirectoryPath,"*.history")){Directory.CreateDirectory(backup);string copy=Path.Combine(backup,Path.GetFileName(file));if(!File.Exists(copy))File.Copy(file,copy);}
    var work=new List<ChatMessage>();var privateBatches=new List<Tuple<string,List<ChatMessage>>>();foreach(var batch in batches){var workTurns=new HashSet<string>(batch.Messages.Where(m=>m.Role=="assistant"&&m.WorkMode).Select(m=>m.TurnId));work.AddRange(batch.Messages.Where(m=>workTurns.Contains(m.TurnId)));var personal=batch.Messages.Where(m=>!workTurns.Contains(m.TurnId)).ToList();if(personal.Count>0)privateBatches.Add(Tuple.Create(batch.Pet,personal));}
    bool keepWork=work.Count>0 || (!wasQuick&&privateBatches.Count==0);if(keepWork){definition.Mode="work";definition.PetId="";if(wasQuick){catalog.QuickConversations.Remove(definition);catalog.UnassignedConversations.Add(definition);}if(work.Count>0)WriteHistory(history,"work",work.GroupBy(m=>m.Id).Select(g=>g.First()).OrderBy(m=>m.Timestamp).ToList());}
    if(privateBatches.Count==0 && !keepWork){definition.Mode="quick";definition.PetId=currentPet??"";}
    for(int index=0;index<privateBatches.Count;index++){
     var batch=privateBatches[index];var personal=!keepWork&&index==0?definition:definition.Copy();if(!Object.ReferenceEquals(personal,definition))personal.Id=Guid.NewGuid().ToString("N");personal.Mode=wasQuick?"quick":"chat";personal.PetId=batch.Item1;
     if(sourceOwner!=null)sourceOwner.Conversations.Remove(personal);catalog.UnassignedConversations.Remove(personal);if(personal.Mode=="quick"){if(!catalog.QuickConversations.Contains(personal))catalog.QuickConversations.Add(personal);}else{personal.SectionId=DefaultChatSectionId;catalog.UnassignedConversations.Add(personal);}
     var target=new HistoryStore(Path.Combine(ConversationWorkspace.ResolveDataDirectory(dataRoot,personal.Id),"history"));WriteHistory(target,personal.PetId,batch.Item2);
    }
   }
   catalog.OwnershipVersion=1;return catalog;
  }
  static void WriteHistory(HistoryStore history,string persona,IList<ChatMessage> messages){history.SaveAsync(persona,messages).GetAwaiter().GetResult();history.FlushAsync().GetAwaiter().GetResult();if(history.Warning.Length>0)throw new IOException(TextResources.Get("conversationOwnership.writeHistory.text"));}
 }
}
