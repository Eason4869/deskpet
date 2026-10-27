using System.Text.Json.Nodes;
using System.Reflection;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Dafeiyu;
using DeskPet.Desktop;
internal static class CompleteCompanionLocalUiChecks {
private sealed class Source:IOfficialCompanionSource {
long selection;public long BeginSelection()=>++selection;public bool OwnsSelection(long token)=>token==selection;public OfficialCompanionInstallation? Installed(string id)=>null;
public Task<OfficialCompanionResult> EnsureAsync(string id,OfficialDownloadRouting routing,OfficialCompanionCheckMode mode,IProgress<OfficialCompanionProgress>? progress,CancellationToken cancel)=>Task.FromCanceled<OfficialCompanionResult>(new CancellationToken(true));
public Task<IList<OfficialCompanionResult>> EnsureAllAsync(OfficialDownloadRouting routing,IProgress<OfficialCompanionProgress>? progress,CancellationToken cancel)=>throw new Exception("No network in local source fixture");public void Dispose(){}
}
static void Copy(string source,string target){foreach(string file in Directory.GetFiles(source,"*",SearchOption.AllDirectories)){string destination=Path.Combine(target,Path.GetRelativePath(source,file));Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(file,destination,true);}}
internal static int Run(string root){int count=0;void Check(bool ok,string label){if(!ok)throw new Exception("Local wardrobe: "+label);count++;}void Until(Func<bool> ready){var timeout=System.Diagnostics.Stopwatch.StartNew();while(!ready()){if(timeout.ElapsedMilliseconds>30000)throw new TimeoutException("Local wardrobe restart");Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}Dispatcher.UIThread.RunJobs();}void Complete(Task task){Until(()=>task.IsCompleted);task.GetAwaiter().GetResult();}
string source=Path.Combine(root,"source"),data=Path.Combine(root,"data");Copy(Path.GetFullPath("assets/complete-defaults/e-bao"),source);
var catalog=JsonNode.Parse(File.ReadAllText("assets/holidays/catalog.json"))!.AsObject();var entries=catalog["outfits"]!.AsArray().Where(x=>x!["character_id"]!.GetValue<string>()=="e-bao").ToArray();
foreach(var entry in entries){string holiday=entry!["holiday_id"]!.GetValue<string>();Copy(Path.GetFullPath("assets/holidays/"+holiday+"/e-bao"),Path.Combine(source,"holidays",holiday,"e-bao"));}
catalog["outfits"]=new JsonArray(entries.Select(x=>x!.DeepClone()).ToArray());File.WriteAllText(Path.Combine(source,"holidays","catalog.json"),catalog.ToJsonString());
var inventory=CompanionPackageInventory.Read(source,CancellationToken.None);Check(inventory.HolidayCatalog.Outfits.Count==10,"complete local source inventory");CompanionPackStorage.Install(source,data,"e-bao",inventory.AllReferences,destination=>CompanionPackageInventory.Read(destination,CancellationToken.None));
SecretProtection.Configure(new TestProtector());var settings=new PetSettings{SelectedCompanionKey="custom-pack:e-bao",OnboardingState="completed",LastOnboardingSeries="0.11",OnboardingVersion=2,AutoUpdate=false,GreetingEnabled=false,ReadReplies=false,HideOnFullscreen=false};new SettingsStore(Path.Combine(data,"settings.json")).Save(settings);
using(var owner=new DesktopController(new ClassicDesktopStyleApplicationLifetime(),new TestRuntime(data),officialSourceFactory:_=>new Source())){
Check(owner.Skin.PickerKey=="custom-pack:e-bao"&&owner.Skin.OfficialCharacterId==null,"restart retains local identity");owner.Pet.HolidayLocalNow=()=>new DateTime(2026,12,24,12,0,0);
owner.Start();
Until(()=>owner.Skin.PackageHolidayCatalog?.Outfits.Count==10);Check(owner.Holidays.Catalog.Outfits.Count(o=>o.CharacterId=="e-bao")==10,"startup publishes complete local wardrobe");
typeof(PetWindow).GetMethod("RefreshHoliday",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(owner.Pet,new object[]{true});Check(owner.Holidays.EffectiveOutfit?.HolidayId=="christmas","local Christmas activates from actual calendar context");Check(owner.Holidays.OwnedOutfits.Count==0,"calendar appearance grants no permanent unlock");
var local=owner.Skin;Complete(owner.SelectSkinAsync(owner.Skins.First(s=>s.OfficialCharacterId=="e-bao")));Check(owner.Skin.OfficialCharacterId=="e-bao"&&owner.Holidays.Catalog.Outfits.Count(o=>o.CharacterId=="e-bao")==1,"official selection restores its own catalog");Check(!owner.Holidays.Catalog.Outfits.Any(o=>o.CharacterId=="e-bao"&&o.HolidayId=="christmas"),"local outfits cannot leak into official source");
Complete(owner.SelectSkinAsync(local));Check(owner.Skin.PickerKey=="custom-pack:e-bao"&&owner.Holidays.Catalog.Outfits.Count(o=>o.CharacterId=="e-bao")==10,"local wardrobe restores after source switching");owner.Pet.Close();owner.Dispose();Complete(owner.ImageServicesShutdown);}
return count;}
}
