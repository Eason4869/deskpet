using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Dafeiyu.Tests
{
    public static class SkillRepositoryTests
    {
        static int checks;
        static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        static bool Reject(Action action){try{action();return false;}catch(Exception e){Console.WriteLine("  rejected: "+e.GetBaseException().Message);return true;}}
        internal static string Body(string name){return "---\nname: \""+name+"\"\ndescription: >-\n  Review a local project\n  with evidence.\n---\n# "+name+"\nNever execute bundled scripts.\n";}
        internal static string Blob(byte[] bytes){using(var sha=SHA1.Create()){byte[] prefix=Encoding.UTF8.GetBytes("blob "+bytes.Length+"\0");return BitConverter.ToString(sha.ComputeHash(prefix.Concat(bytes).ToArray())).Replace("-","").ToLowerInvariant();}}
        internal sealed class Fixture : HttpMessageHandler
        {
            public const string Commit="1111111111111111111111111111111111111111";
            public readonly Dictionary<string,byte[]> Files=new Dictionary<string,byte[]>();
            public readonly List<string> Requests=new List<string>();
            public bool Fail,Truncated,Tamper,Link,Block;
            public Fixture(){Files["SKILL.md"]=Encoding.UTF8.GetBytes(Body("root-helper"));Files["skills/review/SKILL.md"]=Encoding.UTF8.GetBytes(Body("review-helper"));Files["skills/review/references/guide.txt"]=Encoding.UTF8.GetBytes("evidence reference");Files["skills/review/never-run.cmd"]=Encoding.UTF8.GetBytes("@echo no");Files["category/nested/helper/SKILL.md"]=Encoding.UTF8.GetBytes(Body("nested-helper"));Files["invalid/SKILL.md"]=Encoding.UTF8.GetBytes("# no metadata");}
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            {
                lock(Requests)Requests.Add(request.RequestUri.AbsoluteUri);
                if(Block)await Task.Delay(Timeout.Infinite,token);
                if(Fail)return new HttpResponseMessage((HttpStatusCode)429){Content=new StringContent("sensitive response must not be shown")};
                string path=Uri.UnescapeDataString(request.RequestUri.AbsolutePath),json=null;
                if(request.RequestUri.Host=="api.github.com"){
                    if(path.EndsWith("/git/trees/"+Commit))json=new JavaScriptSerializer().Serialize(new{truncated=Truncated,tree=Files.Select(f=>new{path=f.Key,mode=Link&&f.Key.EndsWith("guide.txt")?"120000":"100644",type="blob",size=f.Value.Length,sha=Blob(f.Value)}).ToArray()});
                    else if(path.Contains("/commits/"))json="{\"sha\":\""+Commit+"\"}";
                    else json="{\"default_branch\":\"develop\"}";
                    return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)};
                }
                string key=path.Substring(("/demo/skills/"+Commit+"/").Length);
                byte[] content=Files[key];if(Tamper&&key.EndsWith("guide.txt"))content=Encoding.UTF8.GetBytes("changed");
                return new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(content)};
            }
        }
        static void Zip(string path,params string[] entries){using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))foreach(string entry in entries){using(var writer=new StreamWriter(zip.CreateEntry(entry).Open()))writer.Write(entry.EndsWith("SKILL.md")?Body("zip-helper"):"resource");}}
        public static int Main(string[] args)
        {
            string root=Path.Combine(Directory.GetCurrentDirectory(),"work","repository-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try{
                Check(SkillRepositorySource.NormalizeRepository("https://github.com/demo/skills.git/")=="demo/skills","GitHub URL normalizes to owner/repo");
                foreach(string bad in new[]{"https://evil.test/demo/skills","https://github.com/demo/skills/tree/main","../escape","demo/skills?token=secret","demo/.."})Check(Reject(delegate{SkillRepositorySource.NormalizeRepository(bad);}),"ambiguous or non GitHub repository rejected");
                var catalog=new SkillRepositoryCatalog(Path.Combine(root,"catalog"));var source=catalog.Add("demo/skills","");Check(new SkillRepositoryCatalog(catalog.DirectoryPath).Sources.Single().Enabled,"repository choice persists without scanning");
                Check(Reject(delegate{catalog.Add("DEMO/skills","");}),"duplicate sources are rejected ignoring case");
                var branchCatalog=new SkillRepositoryCatalog(Path.Combine(root,"case-branches"));branchCatalog.Add("demo/skills","Feature");branchCatalog.Add("demo/skills","feature");Check(branchCatalog.Sources.Count==2,"case-distinct Git branches remain readable and removable");
                var handler=new Fixture();using(var client=new SkillRepositoryClient(handler)){
                    var snapshot=client.ScanAsync(source,CancellationToken.None).GetAwaiter().GetResult();
                    Check(snapshot.Skills.Count==3&&snapshot.InvalidCount==1,"root standard and nested skills discovered; invalid metadata counted");
                    Check(snapshot.Branch=="develop"&&snapshot.Commit==Fixture.Commit,"default branch resolved to immutable commit");
                    Check(snapshot.Skills.All(s=>s.Description=="Review a local project with evidence."),"quoted names and folded descriptions parse through shared metadata reader");
                    Check(handler.Requests.Where(s=>s.Contains("raw.githubusercontent")).All(s=>s.Contains(Fixture.Commit)),"every preview reads the resolved commit");
                    byte[] originalBody=handler.Files["skills/review/SKILL.md"];handler.Files["skills/review/SKILL.md"]=new byte[]{239,187,191}.Concat(originalBody).ToArray();
                    var bomSnapshot=client.ScanAsync(source,CancellationToken.None).GetAwaiter().GetResult();Check(client.InstallAsync(bomSnapshot,bomSnapshot.Skills.Single(s=>s.Name=="review-helper"),new ManagedSkillLibrary(Path.Combine(root,"bom-library")),CancellationToken.None).GetAwaiter().GetResult().Name=="review-helper","UTF-8 BOM skill preserves preview and installation agreement");handler.Files["skills/review/SKILL.md"]=originalBody;
                    catalog.SaveSnapshot(source.Id,snapshot);catalog.SetEnabled(source.Id,false);Check(!new SkillRepositoryCatalog(catalog.DirectoryPath).Sources.Single().Enabled,"repository search toggle persists");catalog.SetEnabled(source.Id,true);
                    var cached=new SkillRepositoryCatalog(catalog.DirectoryPath).ReadSnapshot(source.Id);Check(cached.Skills.Count==3&&cached.Skills[0].Content.Length>0,"successful scan cached with offline preview content");
                    string cacheFile=Path.Combine(catalog.DirectoryPath,source.Id+".json");string validCache=File.ReadAllText(cacheFile);cached.Files.Insert(0,null);File.WriteAllText(cacheFile,new JavaScriptSerializer().Serialize(cached));
                    bool invalidCache=false;try{catalog.ReadSnapshot(source.Id);}catch(InvalidOperationException){invalidCache=true;}Check(invalidCache,"null file cache entry produces recoverable validation error");File.WriteAllText(cacheFile,validCache);
                    cached=catalog.ReadSnapshot(source.Id);cached.Skills.Insert(0,null);File.WriteAllText(cacheFile,new JavaScriptSerializer().Serialize(cached));invalidCache=false;try{catalog.ReadSnapshot(source.Id);}catch(InvalidOperationException){invalidCache=true;}Check(invalidCache,"null skill cache entry produces recoverable validation error");File.WriteAllText(cacheFile,validCache);
                    catalog.SetBranch(source.Id,"feature/skills");Check(catalog.ReadSnapshot(source.Id)==null,"changing branch hides the previous branch cache");catalog.SetBranch(source.Id,"");Check(catalog.ReadSnapshot(source.Id)!=null,"restoring branch recovers its last successful cache");
                    Check(Reject(delegate{catalog.SetBranch(source.Id,"bad..branch");})&&catalog.Sources.Single().Branch=="","invalid branch does not modify saved source");
                    var library=new ManagedSkillLibrary(Path.Combine(root,"library"));var review=snapshot.Skills.Single(s=>s.Name=="review-helper");
                    var installed=client.InstallAsync(snapshot,review,library,CancellationToken.None).GetAwaiter().GetResult();
                    Check(File.ReadAllText(Path.Combine(installed.Directory,"references","guide.txt"))=="evidence reference","install includes the selected directory resources");
                    Check(library.List().Single().Origin.Commit==Fixture.Commit&&library.List().Single().Origin.Path=="skills/review","origin commits atomically and survives reopening");
                    Check(Reject(delegate{client.InstallAsync(snapshot,review,library,CancellationToken.None).GetAwaiter().GetResult();})&&library.List().Count==1,"duplicate install never overwrites");
                    catalog.Remove(source.Id);Check(library.List().Single().Name=="review-helper","removing a repository retains installed skills");
                    ManagedSkillLibrary.ActiveDirectory=library.DirectoryPath;using(var workspace=new AgentWorkspace(root)){workspace.ConfigureInstructions(false);Check(workspace.ProjectInstructionText.Contains("review-helper"),"installed skill appears on the next request without project trust");Check(workspace.ExecuteAsync("load_skill","{\"name\":\"review-helper\"}",CancellationToken.None).Result.Contains("# review-helper"),"real load_skill reads repository installation");}ManagedSkillLibrary.ActiveDirectory=null;
                    handler.Tamper=true;Check(Reject(delegate{client.InstallAsync(snapshot,review,new ManagedSkillLibrary(Path.Combine(root,"tampered")),CancellationToken.None).GetAwaiter().GetResult();}),"resource hash mismatch aborts before registration");handler.Tamper=false;
                    handler.Truncated=true;Check(Reject(delegate{client.ScanAsync(source,CancellationToken.None).GetAwaiter().GetResult();}),"truncated tree never becomes partial success");handler.Truncated=false;
                    handler.Link=true;var linked=client.ScanAsync(source,CancellationToken.None).GetAwaiter().GetResult();Check(Reject(delegate{client.InstallAsync(linked,linked.Skills.Single(s=>s.Name=="review-helper"),new ManagedSkillLibrary(Path.Combine(root,"linked")),CancellationToken.None).GetAwaiter().GetResult();}),"linked resource blocks installation");handler.Link=false;
                    handler.Fail=true;try{client.ScanAsync(source,CancellationToken.None).GetAwaiter().GetResult();throw new Exception("expected limit error");}catch(InvalidOperationException e){Check(e.Message.Contains("限流")&&!e.Message.Contains("sensitive"),"rate limit is actionable and does not expose response body");}handler.Fail=false;
                    var cancel=new CancellationTokenSource();handler.Block=true;var pending=client.ScanAsync(source,cancel.Token);cancel.Cancel();Check(Reject(delegate{pending.GetAwaiter().GetResult();}),"cancellation reaches in-flight request");handler.Block=false;
                }
                string archive=Path.Combine(root,"skills.zip");Zip(archive,"wrapper/skills/a/SKILL.md","wrapper/skills/a/references/guide.txt");
                using(var zip=new SkillArchive(archive)){Check(zip.Skills.Count==1&&zip.Skills[0].Name=="zip-helper","wrapped ZIP discovers real skills");var item=zip.Install(zip.Skills[0],new ManagedSkillLibrary(Path.Combine(root,"zip-library")));Check(item.Origin.Kind=="zip"&&item.Origin.Commit.Length==64&&File.Exists(Path.Combine(item.Directory,"references","guide.txt")),"ZIP installs resources and archive digest");}
                foreach(string entry in new[]{"../escape","a/../../escape","C:/escape","a/file:stream","a/CON.txt","a/trailing.","a\\escape"}){string bad=Path.Combine(root,Guid.NewGuid()+".zip");Zip(bad,entry,"skill/SKILL.md");Check(Reject(delegate{using(var z=new SkillArchive(bad)){};}),"unsafe ZIP path rejected: "+entry);}
                string collision=Path.Combine(root,"collision.zip");Zip(collision,"a/SKILL.md","a/skill.md");Check(Reject(delegate{using(var z=new SkillArchive(collision)){};}),"case-colliding ZIP entries rejected");
                Console.WriteLine("Skill repository tests: "+checks+" passed. Fixtures: "+root);return 0;
            }catch(Exception e){Console.Error.WriteLine(e);return 1;}finally{ManagedSkillLibrary.ActiveDirectory=null;}
        }
    }
}
