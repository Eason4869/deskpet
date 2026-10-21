using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Dafeiyu
{
    // All network locations are constructed here; remote metadata cannot supply request URLs.
    public sealed class SkillRepositoryClient : IDisposable
    {
        public sealed class RepoResponse {public string default_branch {get;set;} public string sha {get;set;}}
        public sealed class TreeEntry {public string path {get;set;} public string mode {get;set;} public string type {get;set;} public string sha {get;set;} public long size {get;set;}}
        public sealed class TreeResponse {public bool truncated {get;set;} public List<TreeEntry> tree {get;set;}}
        readonly HttpClient http;
        public SkillRepositoryClient(HttpMessageHandler handler=null){http=new HttpClient(handler??new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false}){Timeout=Timeout.InfiniteTimeSpan};http.DefaultRequestHeaders.UserAgent.ParseAdd("DeskPet-SkillDiscovery/1.0");}
        public async Task<SkillRepositorySnapshot> ScanAsync(SkillRepositorySource source,CancellationToken token,IProgress<string> progress=null)
        {
            string repository=SkillRepositorySource.NormalizeRepository(source.Repository),branch=SkillRepositorySource.ValidateBranch(source.Branch);
            string prefix="https://api.github.com/repos/"+repository;
            if(branch.Length==0)branch=SkillRepositorySource.ValidateBranch((await Json<RepoResponse>(prefix,token).ConfigureAwait(false)).default_branch);
            if(branch.Length==0)throw new InvalidOperationException("仓库没有可读取的默认分支。");
            Report(progress,"正在读取 "+repository+" · "+branch);
            string commit=(await Json<RepoResponse>(prefix+"/commits/"+Uri.EscapeDataString(branch),token).ConfigureAwait(false)).sha;RequireHash(commit);
            var tree=await Json<TreeResponse>(prefix+"/git/trees/"+commit+"?recursive=1",token).ConfigureAwait(false);
            if(tree==null||tree.truncated||tree.tree==null||tree.tree.Count>50000)throw new InvalidOperationException("仓库目录过大或结果不完整，请选择较小的技能仓库。");
            var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var files=new List<SkillRepositoryFile>();
            foreach(var entry in tree.tree){if(entry==null)throw new InvalidOperationException("仓库目录数据无效，请重试。");SafePath(entry.path);if(!paths.Add(entry.path))throw new InvalidOperationException("仓库包含本机无法区分的同名路径。");
                if(entry.type=="tree")continue;RequireHash(entry.sha);if(entry.size<0)throw new InvalidOperationException("仓库文件大小无效。");
                files.Add(new SkillRepositoryFile{Path=entry.path,Mode=entry.mode,Sha=entry.sha,Size=entry.size});}
            var candidates=files.Where(f=>f.Path=="SKILL.md"||f.Path.EndsWith("/SKILL.md",StringComparison.Ordinal)).ToArray();
            if(candidates.Length>2048)throw new InvalidOperationException("单个仓库最多发现 2048 个技能。");
            var skills=new RepositorySkill[candidates.Length];int invalid=0,finished=0;long total=0;
            await Workers(candidates.Length,async index=>{
                var file=candidates[index];if(file.Size>65536||!Regular(file)){Interlocked.Increment(ref invalid);return;}
                byte[] data=await Bytes(Raw(repository,commit,file.Path),65536,token).ConfigureAwait(false);VerifyBlob(file,data);
                if(Interlocked.Add(ref total,data.Length)>12*1024*1024)throw new InvalidOperationException("仓库技能正文缓存超过 12 MiB，请选择较小的仓库。");
                try{string body=new UTF8Encoding(false,true).GetString(data);var info=AgentSkills.ParseInfo(body,SkillFolder(file.Path));skills[index]=new RepositorySkill{Name=info.Name,Description=info.Description,Path=SkillFolder(file.Path),Content=body};}
                catch(InvalidOperationException){Interlocked.Increment(ref invalid);}catch(DecoderFallbackException){Interlocked.Increment(ref invalid);}
                Report(progress,"已读取 "+Interlocked.Increment(ref finished)+" / "+candidates.Length+" 个技能");
            },token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return new SkillRepositorySnapshot{Repository=repository,RequestedBranch=source.Branch??"",Branch=branch,Commit=commit,ScannedUtc=DateTime.UtcNow.ToString("o"),InvalidCount=invalid,Files=files,Skills=skills.Where(s=>s!=null).OrderBy(s=>s.Name,StringComparer.Ordinal).ThenBy(s=>s.Path,StringComparer.Ordinal).ToList()};
        }
        public async Task<ManagedSkillInfo> InstallAsync(SkillRepositorySnapshot snapshot,RepositorySkill selected,ManagedSkillLibrary library,CancellationToken token,IProgress<string> progress=null)
        {
            ValidateSnapshot(snapshot);var skill=snapshot.Skills.SingleOrDefault(s=>s.Path==selected.Path&&s.Name==selected.Name);
            if(skill==null||skill.Content!=selected.Content)throw new InvalidOperationException("预览已变化，请重新选择技能。");
            if(library.List().Any(s=>s.Name==skill.Name))throw new InvalidOperationException("已存在同名技能："+skill.Name+"。请先移除已有副本。");
            string prefix=skill.Path.Length==0?"":skill.Path+"/";
            var files=snapshot.Files.Where(f=>f.Path.StartsWith(prefix,StringComparison.Ordinal)).ToArray();
            long totalBytes=0;foreach(var file in files){if(file.Size>64L*1024*1024-totalBytes)throw new InvalidOperationException("技能资源超过 64 MiB。");totalBytes+=file.Size;}
            if(files.Length==0||files.Length>2048)throw new InvalidOperationException("技能资源超过 2048 个文件。");
            foreach(var file in files)if(!Regular(file))throw new InvalidOperationException("技能资源含链接或子模块，无法安装。");
            string staging=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"deskpet-skill-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(staging);
            try{
                int completed=0;await Workers(files.Length,async index=>{var file=files[index];byte[] bytes=await Bytes(Raw(snapshot.Repository,snapshot.Commit,file.Path),(int)file.Size,token).ConfigureAwait(false);VerifyBlob(file,bytes);
                    string relative=file.Path.Substring(prefix.Length);SafePath(relative);string target=System.IO.Path.Combine(staging,relative.Replace('/',System.IO.Path.DirectorySeparatorChar));AgentInstructions.RequireWithin(staging,target);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
                    using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))await output.WriteAsync(bytes,0,bytes.Length,token).ConfigureAwait(false);
                    Report(progress,"正在下载 "+Interlocked.Increment(ref completed)+" / "+files.Length+" 个文件");
                },token).ConfigureAwait(false);
                if(Encoding.UTF8.GetString(File.ReadAllBytes(System.IO.Path.Combine(staging,"SKILL.md")))!=skill.Content)throw new InvalidOperationException("安装正文与预览不一致，请刷新后重试。");
                token.ThrowIfCancellationRequested();
                return library.Import(staging,new SkillOrigin{Kind="github",Repository=snapshot.Repository,Branch=snapshot.Branch,Commit=snapshot.Commit,Path=skill.Path});
            }finally{Directory.Delete(staging,true);}
        }
        async Task<T> Json<T>(string url,CancellationToken token) where T:class
        {
            byte[] bytes=await Bytes(url,16*1024*1024,token).ConfigureAwait(false);
            try{var result=new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.Deserialize<T>(Encoding.UTF8.GetString(bytes));if(result==null)throw new InvalidOperationException("GitHub 返回空仓库数据，请重试。");return result;}
            catch(ArgumentException){throw new InvalidOperationException("GitHub 返回的仓库数据无法读取。");}
        }
        async Task<byte[]> Bytes(string url,int limit,CancellationToken token)
        {
            using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token)){timeout.CancelAfter(TimeSpan.FromSeconds(40));
                try{using(var request=new HttpRequestMessage(HttpMethod.Get,url)){
                    if(request.RequestUri.Host=="api.github.com"){request.Headers.Accept.ParseAdd("application/vnd.github+json");request.Headers.Add("X-GitHub-Api-Version","2022-11-28");}
                    using(var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token).ConfigureAwait(false)){
                        int status=(int)response.StatusCode;
                        if(status==403||status==429)throw new InvalidOperationException("GitHub 限流或拒绝访问，请稍后重试；上次成功缓存会保留。");
                        if(status==404)throw new InvalidOperationException("仓库、分支或提交不存在，或仓库并非公开；请检查来源。");
                        if(!response.IsSuccessStatusCode)throw new InvalidOperationException("GitHub 读取失败（HTTP "+status+"），请稍后重试。");
                        if(response.Content.Headers.ContentLength>limit)throw new InvalidOperationException("技能下载超过大小限制。");
                        using(var input=await response.Content.ReadAsStreamAsync().ConfigureAwait(false))using(var output=new MemoryStream()){
                            var buffer=new byte[81920];int count;while((count=await input.ReadAsync(buffer,0,buffer.Length,timeout.Token).ConfigureAwait(false))>0){if(output.Length+count>limit)throw new InvalidOperationException("技能下载超过大小限制。");output.Write(buffer,0,count);}return output.ToArray();}
                    }}
                }catch(OperationCanceledException){if(token.IsCancellationRequested)throw;throw new InvalidOperationException("GitHub 请求超时，请检查网络后重试。");}
                catch(HttpRequestException){throw new InvalidOperationException("无法连接 GitHub，请检查网络后重试。");}}
        }
        static async Task Workers(int count,Func<int,Task> operation,CancellationToken token)
        {
            int next=-1,failed=0;var workers=Enumerable.Range(0,Math.Min(4,count)).Select(async worker=>{while(true){token.ThrowIfCancellationRequested();if(Volatile.Read(ref failed)!=0)return;int index=Interlocked.Increment(ref next);if(index>=count)return;try{await operation(index).ConfigureAwait(false);}catch{Interlocked.Exchange(ref failed,1);throw;}}}).ToArray();await Task.WhenAll(workers).ConfigureAwait(false);
        }
        internal static void SafePath(string path)
        {
            if(String.IsNullOrEmpty(path)||path.Length>1024||path.Split('/').Length>64||path.Any(c=>Char.IsControl(c)||"\\:*?\"<>|".IndexOf(c)>=0)||path.Split('/').Any(p=>p.Length==0||p=="."||p==".."||p.EndsWith(".")||p.EndsWith(" ")||Regex.IsMatch(p,@"\A(?:CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|$)",RegexOptions.IgnoreCase)))throw new InvalidOperationException("技能含无法安全保存的文件路径。");
        }
        internal static void ValidateSnapshot(SkillRepositorySnapshot snapshot)
        {
            if(snapshot==null||snapshot.Skills==null||snapshot.Files==null||snapshot.Skills.Count>2048||snapshot.Files.Count>50000)throw new InvalidOperationException("仓库缓存无效，请重新刷新。");
            if(SkillRepositorySource.NormalizeRepository(snapshot.Repository)!=snapshot.Repository)throw new InvalidOperationException("仓库缓存来源无效。");SkillRepositorySource.ValidateBranch(snapshot.Branch);RequireHash(snapshot.Commit);
            var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var f in snapshot.Files){if(f==null)throw new InvalidOperationException("仓库缓存包含空文件项，请重新刷新。");SafePath(f.Path);RequireHash(f.Sha);if(f.Size<0||!paths.Add(f.Path))throw new InvalidOperationException("仓库缓存包含无效文件。");}
            var skillPaths=new HashSet<string>(StringComparer.Ordinal);foreach(var s in snapshot.Skills){if(s==null)throw new InvalidOperationException("仓库缓存包含空技能项，请重新刷新。");if(s.Path!=null&&s.Path.Length>0)SafePath(s.Path);if(s.Path==null||!skillPaths.Add(s.Path))throw new InvalidOperationException("仓库缓存技能路径无效。");
                var info=AgentSkills.ParseInfo(s.Content,s.Path);if(info.Name!=s.Name||info.Description!=s.Description)throw new InvalidOperationException("技能缓存元数据不一致，请重新刷新。");
                var file=snapshot.Files.SingleOrDefault(f=>f.Path==(s.Path.Length==0?"":s.Path+"/")+"SKILL.md");if(file==null)throw new InvalidOperationException("技能缓存缺少正文文件。");VerifyBlob(file,Encoding.UTF8.GetBytes(s.Content));}
        }
        static string SkillFolder(string file){int at=file.LastIndexOf('/');return at<0?"":file.Substring(0,at);}
        static bool Regular(SkillRepositoryFile file){return file.Mode=="100644"||file.Mode=="100755";}
        static void RequireHash(string hash){if(hash==null||!Regex.IsMatch(hash,@"\A[a-f0-9]{40}\z"))throw new InvalidOperationException("仓库提交或文件校验值无效。");}
        static string Raw(string repository,string commit,string path){return "https://raw.githubusercontent.com/"+repository+"/"+commit+"/"+String.Join("/",path.Split('/').Select(Uri.EscapeDataString));}
        static void VerifyBlob(SkillRepositoryFile file,byte[] bytes)
        {
            using(var sha=SHA1.Create()){byte[] prefix=Encoding.UTF8.GetBytes("blob "+bytes.Length+"\0");sha.TransformBlock(prefix,0,prefix.Length,prefix,0);sha.TransformFinalBlock(bytes,0,bytes.Length);string digest=BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();if(bytes.LongLength!=file.Size||digest!=file.Sha)throw new InvalidOperationException("下载的技能文件校验不符，请重新刷新后重试。");}
        }
        static void Report(IProgress<string> progress,string value){if(progress!=null)progress.Report(value);}
        public void Dispose(){http.Dispose();}
    }
}
