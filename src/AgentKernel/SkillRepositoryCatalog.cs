using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Dafeiyu
{
    public sealed class SkillOrigin
    {
        public string Kind {get;set;}
        public string Repository {get;set;}
        public string Branch {get;set;}
        public string Commit {get;set;}
        public string Path {get;set;}
    }

    public sealed class SkillRepositorySource
    {
        public string Id {get;set;}
        public string Repository {get;set;}
        public string Branch {get;set;}
        public bool Enabled {get;set;}
        public static string NormalizeRepository(string input)
        {
            string value=(input??"").Trim();
            if(value.StartsWith("https://github.com/",StringComparison.OrdinalIgnoreCase))value=value.Substring(19).TrimEnd('/');
            if(value.EndsWith(".git",StringComparison.OrdinalIgnoreCase))value=value.Substring(0,value.Length-4);
            if(value.Length>200||!Regex.IsMatch(value,@"\A[a-zA-Z0-9][a-zA-Z0-9-]{0,38}/[a-zA-Z0-9_.-]+\z")||value.Split('/')[1].Trim('.').Length==0)
                throw new ArgumentException(TextResources.Get("skillRepositoryCatalog.normalizeRepository.text"));
            return value;
        }
        public static string ValidateBranch(string branch)
        {
            string value=(branch??"").Trim();
            if(value.Length>256||value.Any(c=>Char.IsControl(c)||Char.IsWhiteSpace(c)||"~^:?*[\\".IndexOf(c)>=0)||value.Contains("..")||value.Contains("@{")||value.StartsWith("-")||value.StartsWith("/")||value.EndsWith("/")||value.EndsWith(".")||value.Split('/').Any(p=>p.StartsWith(".")||p.EndsWith(".lock"))||value.Contains("//"))
                throw new ArgumentException(TextResources.Get("skillRepositoryCatalog.validateBranch.text"));
            return value;
        }
    }
    public sealed class RepositorySkill
    {
        public string Name {get;set;}
        public string Description {get;set;}
        public string Path {get;set;}
        public string Content {get;set;}
    }
    public sealed class SkillRepositoryFile
    {
        public string Path {get;set;}
        public string Mode {get;set;}
        public string Sha {get;set;}
        public long Size {get;set;}
    }
    public sealed class SkillRepositorySnapshot
    {
        public string Repository {get;set;}
        public string RequestedBranch {get;set;}
        public string Branch {get;set;}
        public string Commit {get;set;}
        public string ScannedUtc {get;set;}
        public int InvalidCount {get;set;}
        public List<RepositorySkill> Skills {get;set;}
        public List<SkillRepositoryFile> Files {get;set;}
    }

    // Discovery data is separate from the managed runtime library. Opening it never makes a request.
    public sealed class SkillRepositoryCatalog
    {
        public sealed class State {public int Version {get;set;} public List<SkillRepositorySource> Sources {get;set;}}
        readonly object gate=new object();
        public string DirectoryPath {get;private set;}
        public SkillRepositoryCatalog(string directory){DirectoryPath=System.IO.Path.GetFullPath(directory);AgentInstructions.RejectLinks(DirectoryPath);}
        string ConfigPath {get{return System.IO.Path.Combine(DirectoryPath,"sources.json");}}
        public IList<SkillRepositorySource> Sources {get{lock(gate)return Read().Sources;}}
        State Read()
        {
            if(!File.Exists(ConfigPath))return new State{Version=1,Sources=new List<SkillRepositorySource>()};
            var state=SkillStorage.ReadJson<State>(ConfigPath,65536);
            if(state==null||state.Version!=1||state.Sources==null||state.Sources.Count>32)throw new InvalidOperationException(TextResources.Get("skillRepositoryCatalog.read.text"));
            var keys=new HashSet<string>(StringComparer.Ordinal);var ids=new HashSet<string>();
            foreach(var s in state.Sources){if(s==null||!ValidId(s.Id)||!ids.Add(s.Id)||Normalize(s.Repository)!=s.Repository||!keys.Add(s.Repository.ToLowerInvariant()+"\n"+SkillRepositorySource.ValidateBranch(s.Branch)))throw new InvalidOperationException(TextResources.Get("skillRepositoryCatalog.read.text.alternativeA"));}
            return state;
        }
        static string Normalize(string repository){return SkillRepositorySource.NormalizeRepository(repository);}
        static bool ValidId(string id){return id!=null&&Regex.IsMatch(id,@"\A[a-f0-9]{32}\z");}
        string CachePath(string id){if(!ValidId(id))throw new ArgumentException(TextResources.Get("skillRepositoryCatalog.cachePath.text"));return System.IO.Path.Combine(DirectoryPath,id+".json");}
        public SkillRepositorySource Add(string repository,string branch)
        {
            lock(gate){var state=Read();repository=Normalize(repository);branch=SkillRepositorySource.ValidateBranch(branch);
                if(state.Sources.Count>=32)throw new InvalidOperationException(TextResources.Get("skillRepositoryCatalog.add.text"));
                if(state.Sources.Any(s=>String.Equals(s.Repository,repository,StringComparison.OrdinalIgnoreCase)&&s.Branch==branch))throw new InvalidOperationException(TextResources.Get("skillRepositoryCatalog.add.text.alternativeA"));
                var item=new SkillRepositorySource{Id=Guid.NewGuid().ToString("N"),Repository=repository,Branch=branch,Enabled=true};state.Sources.Add(item);SkillStorage.WriteJson(ConfigPath,state,65536);return item;}
        }
        public void SetEnabled(string id,bool enabled){lock(gate){var state=Read();Find(state,id).Enabled=enabled;SkillStorage.WriteJson(ConfigPath,state,65536);}}
        public void SetBranch(string id,string branch)
        {
            lock(gate){var state=Read();var item=Find(state,id);branch=SkillRepositorySource.ValidateBranch(branch);
                if(state.Sources.Any(s=>s.Id!=id&&String.Equals(s.Repository,item.Repository,StringComparison.OrdinalIgnoreCase)&&s.Branch==branch))throw new InvalidOperationException(TextResources.Get("skillRepositoryCatalog.setBranch.text"));
                item.Branch=branch;SkillStorage.WriteJson(ConfigPath,state,65536);}
        }
        public void Remove(string id){lock(gate){var state=Read();state.Sources.Remove(Find(state,id));SkillStorage.WriteJson(ConfigPath,state,65536);}}
        static SkillRepositorySource Find(State state,string id){var s=state.Sources.SingleOrDefault(r=>r.Id==id);if(s==null)throw new InvalidOperationException(TextResources.Get("skillRepositoryCatalog.find.text"));return s;}
        public SkillRepositorySnapshot ReadSnapshot(string id)
        {
            lock(gate){var source=Find(Read(),id);string file=CachePath(id);if(!File.Exists(file))return null;
                var snapshot=SkillStorage.ReadJson<SkillRepositorySnapshot>(file,16*1024*1024);SkillRepositoryClient.ValidateSnapshot(snapshot);
                return snapshot.Repository==source.Repository&&(snapshot.RequestedBranch??"")==source.Branch?snapshot:null;}
        }
        public void SaveSnapshot(string id,SkillRepositorySnapshot snapshot)
        {
            lock(gate){var source=Find(Read(),id);SkillRepositoryClient.ValidateSnapshot(snapshot);
                if(snapshot.Repository!=source.Repository||(snapshot.RequestedBranch??"")!=source.Branch)throw new InvalidOperationException(TextResources.Get("skillRepositoryCatalog.saveSnapshot.text"));
                SkillStorage.WriteJson(CachePath(id),snapshot,16*1024*1024);}
        }
    }

    internal static class SkillStorage
    {
        internal static T ReadJson<T>(string path,int limit)
        {
            AgentInstructions.RejectLinks(path);if(new FileInfo(path).Length>limit)throw new InvalidOperationException(TextResources.Get("skillRepositoryCatalog.readJson.text"));
            try{return new JavaScriptSerializer{MaxJsonLength=limit}.Deserialize<T>(File.ReadAllText(path,Encoding.UTF8));}
            catch(ArgumentException){throw new InvalidOperationException(TextResources.Get("skillRepositoryCatalog.readJson.text.alternativeA"));}
        }
        internal static void WriteJson(string path,object value,int limit)
        {
            byte[] bytes=Encoding.UTF8.GetBytes(new JavaScriptSerializer{MaxJsonLength=limit}.Serialize(value));
            if(bytes.Length>limit)throw new InvalidOperationException(TextResources.Get("skillRepositoryCatalog.writeJson.text"));
            string directory=System.IO.Path.GetDirectoryName(path);AgentInstructions.RejectLinks(directory);Directory.CreateDirectory(directory);AgentInstructions.RejectLinks(path);
            string temp=System.IO.Path.Combine(directory,".write-"+Guid.NewGuid().ToString("N"));
            try{using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes,0,bytes.Length);file.Flush(true);}
                AgentInstructions.RejectLinks(path);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
