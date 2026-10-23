using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    public sealed class AgentProjectFact
    {
        public string Id=Guid.NewGuid().ToString("N"),Text,TaskId,SourceFile;public DateTime UpdatedUtc=DateTime.UtcNow;
        public override string ToString(){return Text;}
        public AgentProjectFact Copy(){return (AgentProjectFact)MemberwiseClone();}
    }
    public sealed class AgentTaskMemory
    {
        readonly string path;
        public AgentTaskMemory(string workspace){path=Path.Combine(Path.GetFullPath(workspace),".deskpet-agent-kernel","project-facts.bin");}
        public IList<AgentProjectFact> List()
        {
            if(!File.Exists(path))return new AgentProjectFact[0];AgentInstructions.RejectLinks(path);byte[] bytes=File.ReadAllBytes(path);if(bytes.Length>256*1024)throw new IOException(TextResources.Get("agentTaskMemory.list.text"));var facts=new JavaScriptSerializer{MaxJsonLength=256*1024}.Deserialize<List<AgentProjectFact>>(Encoding.UTF8.GetString(SecretProtection.Unprotect(bytes)));
            if(facts==null||facts.Count>64||facts.Any(f=>f==null||String.IsNullOrWhiteSpace(f.Id)||String.IsNullOrWhiteSpace(f.TaskId)||String.IsNullOrWhiteSpace(f.Text)||f.Text.Length>2000))throw new IOException(TextResources.Get("agentTaskMemory.list.text.alternativeA"));return facts.Select(f=>f.Copy()).ToArray();
        }
        public void Save(AgentProjectFact fact)
        {
            if(fact==null||String.IsNullOrWhiteSpace(fact.TaskId)||String.IsNullOrWhiteSpace(fact.Text)||fact.Text.Length>2000)throw new ArgumentException(TextResources.Get("agentTaskMemory.save.text"));var facts=List().Where(f=>f.Id!=fact.Id).ToList();if(facts.Count>=64)throw new InvalidOperationException(TextResources.Get("agentTaskMemory.save.text.alternativeA"));fact=fact.Copy();fact.UpdatedUtc=DateTime.UtcNow;facts.Add(fact);Write(facts);
        }
        public void Delete(string id){Write(List().Where(f=>f.Id!=id).ToList());}
        void Write(IList<AgentProjectFact> facts)
        {
            string directory=Path.GetDirectoryName(path);AgentInstructions.RejectLinks(directory);Directory.CreateDirectory(directory);string temporary=Path.Combine(directory,Guid.NewGuid().ToString("N")+".tmp");try{File.WriteAllBytes(temporary,SecretProtection.Protect(Encoding.UTF8.GetBytes(new JavaScriptSerializer{MaxJsonLength=256*1024}.Serialize(facts))));AtomicFile.Commit(temporary,path);}finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        public string ForPrompt(){var facts=List();if(facts.Count==0)return "";return TextResources.Format("agentTaskMemory.forPrompt.template",new JavaScriptSerializer{MaxJsonLength=256*1024}.Serialize(facts));}
    }
}
