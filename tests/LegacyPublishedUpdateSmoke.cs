using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Security.Cryptography;

// Loads the real published binaries in an isolated process. No real installation
// or network is used; only HTTP response transport is replaced.
class LegacyPublishedUpdateSmoke
{
    static string Hash(string path){using(var input=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();}
    static void Set(object job,string field,object value){job.GetType().GetField(field).SetValue(job,value);}
    static object Await(object value){var task=(Task)value;task.GetAwaiter().GetResult();return task.GetType().GetProperty("Result").GetValue(task,null);}
    sealed class Handler:HttpMessageHandler
    {
        internal string Feed,Commit,Package;internal int Downloads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();HttpContent content;
            if(request.RequestUri.AbsolutePath.EndsWith("/releases"))content=new StringContent(Feed);
            else if(request.RequestUri.AbsolutePath.Contains("/git/ref/"))content=new StringContent("{\"object\":{\"type\":\"commit\",\"sha\":\""+Commit+"\"}}");
            else if(Uri.UnescapeDataString(request.RequestUri.Segments.Last())==Path.GetFileName(Package)){Downloads++;content=new StreamContent(File.OpenRead(Package));}
            else throw new Exception("Unexpected local HTTP fixture route: "+request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});
        }
    }
    static void Refused(MethodInfo apply,object job,string exe,string originalHash,string label)
    {
        bool rejected=false;try{apply.Invoke(null,new object[]{job,null});}catch(TargetInvocationException error){if(!(error.InnerException is InvalidOperationException))throw;rejected=true;}
        if(!rejected||Hash(exe)!=originalHash)throw new Exception("Unsafe update accepted: "+label);
        Console.WriteLine("PASS genuine v1.0.3 helper rejects "+label+" before replacing installed app");
    }
    static int Main(string[] args)
    {
        try{
            string publishedApp=Path.GetFullPath(args[0]),publishedWorker=Path.GetFullPath(args[1]),package=Path.GetFullPath(args[2]);
            if(Hash(publishedApp)!="534bd0ca482eee280092d76812474371e00fcd661fb36d17104141b4548ca2a6"||Hash(publishedWorker)!="1d0b194660f3b3e1880e32bdd4a29d68fb405c2952a44b4a8558120c3f19b9b4")throw new Exception("Published v1.0.3 binary hashes differ");
            var old=Assembly.LoadFile(publishedApp);
            string install=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"owned install"),data=Path.Combine(install,"data"),work=Path.Combine(data,"updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);
            string exe=Path.Combine(install,"桌面伙伴.exe");File.Copy(publishedApp,exe);File.Copy(publishedWorker,Path.Combine(install,"DeskPet.Update.exe"));
            var sentinels=new[]{"settings.json","holiday-state.json","history/keep.history","skins/personal/pet.json"};
            foreach(string relative in sentinels){string path=Path.Combine(data,relative);Directory.CreateDirectory(Path.GetDirectoryName(path));if(relative!="settings.json"&&relative!="holiday-state.json")File.WriteAllText(path,"private sentinel "+relative);}
            var settings=Activator.CreateInstance(old.GetType("Dafeiyu.PetSettings",true));
            foreach(var pair in new[]{new[]{"SkinId","e-bao"},new[]{"SelectedCompanionKey","official:e-bao"},new[]{"SystemPrompt","private persona"}})settings.GetType().GetProperty(pair[0]).SetValue(settings,pair[1],null);
            var settingsStore=Activator.CreateInstance(old.GetType("Dafeiyu.SettingsStore",true),new object[]{Path.Combine(data,"settings.json")});settingsStore.GetType().GetMethod("Save").Invoke(settingsStore,new[]{settings});
            var wardrobe=Activator.CreateInstance(old.GetType("Dafeiyu.HolidayState",true));wardrobe.GetType().GetMethod("Unlock").Invoke(wardrobe,new object[]{"e-bao","halloween-2026"});var role=wardrobe.GetType().GetMethod("ForCharacter").Invoke(wardrobe,new object[]{"e-bao"});role.GetType().GetProperty("PreferredOutfitId").SetValue(role,"halloween-2026",null);
            var wardrobeStore=Activator.CreateInstance(old.GetType("Dafeiyu.HolidayStateStore",true),new object[]{Path.Combine(data,"holiday-state.json")});wardrobeStore.GetType().GetMethod("Save").Invoke(wardrobeStore,new[]{wardrobe});
            var preserved=sentinels.ToDictionary(relative=>relative,relative=>Hash(Path.Combine(data,relative)));
            string target=Path.Combine(work,"target.exe");using(var zip=new ZipArchive(File.OpenRead(package),ZipArchiveMode.Read))using(var input=zip.GetEntry("DafeiyuDeskPet/桌面伙伴.exe").Open())using(var output=File.Create(target))input.CopyTo(output);
            string build=System.Diagnostics.FileVersionInfo.GetVersionInfo(target).ProductVersion;
            string version=Path.GetFileName(package).Substring("DeskPet-Windows-".Length);version=version.Substring(0,version.Length-4);
            string commit=build.Contains("+")?build.Substring(build.IndexOf('+')+1):"";
            if(commit.Length!=40)throw new Exception("Actual preview package must carry its complete commit identity: "+build);
            var feedType=old.GetType("Dafeiyu.GitHubUpdates",true);var handler=new Handler{Commit=commit,Package=package};string repo="https://github.com/Eason4869/deskpet",name=Path.GetFileName(package),digest=Hash(package);
            handler.Feed=new JavaScriptSerializer().Serialize(new[]{new{draft=false,prerelease=true,tag_name="v"+version,html_url=repo+"/releases/tag/v"+version,assets=new[]{new{name=name,browser_download_url=repo+"/releases/download/v"+version+"/"+name,size=new FileInfo(package).Length,digest="sha256:"+digest}}}});
            var feed=feedType.GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(Uri),typeof(HttpMessageHandler)},null).Invoke(new object[]{new Uri("https://api.github.com/"),handler});object release;
            using((IDisposable)feed){var channel=old.GetType("Dafeiyu.UpdateChannel",true);release=Await(feedType.GetMethod("CheckAsync",new[]{typeof(string),typeof(bool),typeof(string),typeof(bool),channel,typeof(CancellationToken)}).Invoke(feed,new object[]{repo,false,"",true,Enum.Parse(channel,"Preview"),CancellationToken.None}));
                if(!(bool)release.GetType().GetMethod("IsNewerThan",new[]{typeof(string)}).Invoke(release,new object[]{"1.0.3"}))throw new Exception("Old client does not offer new preview");
                var download=feedType.GetMethods().Single(m=>m.Name=="DownloadAsync"&&m.GetParameters().Length==5);
                string downloaded=(string)Await(download.Invoke(feed,new object[]{release,work,false,null,CancellationToken.None}));if(Hash(downloaded)!=digest||handler.Downloads!=1)throw new Exception("Actual old download/digest check differs");package=downloaded;
            }
            Console.WriteLine("PASS genuine v1.0.3 client selects preview and downloads actual "+new FileInfo(package).Length+" byte package with verified SHA256");
            // Use the exact preview identity protocol emitted by the old coordinator,
            // then serialize with its writer and read with the genuine helper.
            var appEngine=old.GetType("Dafeiyu.UpdateInstall",true);var job=Activator.CreateInstance(old.GetType("Dafeiyu.UpdateJob",true));
            Set(job,"InstallationDirectory",install);Set(job,"WorkDirectory",work);Set(job,"PackagePath",package);Set(job,"Mode","portable");Set(job,"InstalledVersion","1.0.3");Set(job,"TargetVersion",version);Set(job,"Size",new FileInfo(package).Length);Set(job,"Digest",digest);Set(job,"AssetUrl",repo+"/releases/download/v"+version+"/"+name);
            Set(job,"ProtocolVersion",2);Set(job,"InstalledAssemblyVersion",AssemblyName.GetAssemblyName(publishedApp).Version.ToString());Set(job,"InstalledBuildId",System.Diagnostics.FileVersionInfo.GetVersionInfo(publishedApp).ProductVersion);Set(job,"TargetAssemblyVersion",AssemblyName.GetAssemblyName(target).Version.ToString());Set(job,"TargetBuildId",build);
            string handoff=Path.Combine(work,"job.json");appEngine.GetMethod("Write").MakeGenericMethod(job.GetType()).Invoke(null,new object[]{handoff,job});
            var worker=Assembly.LoadFile(publishedWorker);var engine=worker.GetType("Dafeiyu.UpdateInstall",true);job=engine.GetMethod("Read").MakeGenericMethod(worker.GetType("Dafeiyu.UpdateJob",true)).Invoke(null,new object[]{handoff});var apply=engine.GetMethod("ApplyPortable");string originalHash=Hash(exe);
            Set(job,"Digest",new string('0',64));Refused(apply,job,exe,originalHash,"wrong package digest");Set(job,"Digest",digest);
            // Correctly hashed ZIP still cannot write outside the allowed runtime.
            string maliciousWork=Path.Combine(data,"updates",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(maliciousWork);string malicious=Path.Combine(maliciousWork,name);using(var output=File.Create(malicious))using(var zip=new ZipArchive(output,ZipArchiveMode.Create))using(var writer=new StreamWriter(zip.CreateEntry("DafeiyuDeskPet/../escape.txt").Open()))writer.Write("malicious");
            Set(job,"WorkDirectory",maliciousWork);Set(job,"PackagePath",malicious);Set(job,"Size",new FileInfo(malicious).Length);Set(job,"Digest",Hash(malicious));Refused(apply,job,exe,originalHash,"traversal package");if(Directory.GetFiles(install,"escape.txt",SearchOption.AllDirectories).Length!=0)throw new Exception("Traversal escaped");
            Set(job,"WorkDirectory",work);Set(job,"PackagePath",package);Set(job,"Size",new FileInfo(package).Length);Set(job,"Digest",digest);apply.Invoke(null,new object[]{job,null});
            if(Hash(exe)!=Hash(target)||Hash(Path.Combine(work,"backup","桌面伙伴.exe"))!=originalHash)throw new Exception("Updated app or original backup differs");
            foreach(string relative in sentinels)if(Hash(Path.Combine(data,relative))!=preserved[relative])throw new Exception("Personal data changed: "+relative);
            var reopened=settingsStore.GetType().GetMethod("Load").Invoke(settingsStore,null);var reopenedWardrobe=wardrobeStore.GetType().GetMethod("Load").Invoke(wardrobeStore,null);var reopenedRole=reopenedWardrobe.GetType().GetMethod("ForCharacter").Invoke(reopenedWardrobe,new object[]{"e-bao"});
            if((string)reopened.GetType().GetProperty("SelectedCompanionKey").GetValue(reopened,null)!="official:e-bao"||(string)reopenedRole.GetType().GetProperty("PreferredOutfitId").GetValue(reopenedRole,null)!="halloween-2026")throw new Exception("Real persisted selection or wardrobe changed");
            Console.WriteLine("PASS genuine published v1.0.3 helper applies real preview; original backup and byte-identical selected E/settings/owned Halloween/history/custom skin preserved; owned fixture="+install);
            return 0;
        }catch(Exception error){Console.WriteLine("FAIL "+(error.InnerException??error));return 1;}
    }
}
