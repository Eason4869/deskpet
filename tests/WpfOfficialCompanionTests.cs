using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu.Tests
{
    public static class WpfOfficialCompanionTests
    {
        static int passed;
        static void Check(bool value,string message){if(!value)throw new Exception(message);passed++;}
        static void Hold(IllustratedAnimation clip,double hold)
        {var property=typeof(IllustratedAnimation).GetProperty("HoldLastSeconds");Check(property!=null,"illustrated jump hold metadata is available");property.SetValue(clip,hold,null);}
        [STAThread] public static int Main(string[] args)
        {
            try {JumpHold();NativePixels();CatalogTrust();if(args.Contains("--thin-ui"))ThinStartupAndGuide(args.FirstOrDefault(a=>a.StartsWith("--legacy-assets=",StringComparison.Ordinal)));Console.WriteLine("WPF official companion tests: "+passed+" passed");return 0;}
            catch(Exception error){Console.WriteLine("FAIL WPF official companion: "+error);return 1;}
        }
        static void JumpHold()
        {
            foreach(double hold in new[]{.05,.35}) {
                var manifest=new SkinManifest{Version=2,Animations=new Dictionary<string,IllustratedAnimation>()};
                foreach(string action in AnimationPlayer.States)manifest.Animations[action]=new IllustratedAnimation{Frames=action=="jumping"?5:8,Fps=action=="jumping"?1/.13:8,Loop=action!="jumping"};
                Hold(manifest.Animations["jumping"],hold);
                var skin=new SkinDefinition("skin-0123456789abcdef0123456789abcdef","unused",manifest);
                var clock=skin.CreateAnimation();clock.Play("jumping",.7);clock.Advance(.65+hold-.0001);
                Check(clock.State=="jumping","manifest completion includes final hold for persona alias");
                clock.Advance(.0002);Check(clock.State=="idle","jump completes once after final hold");
                var image=BitmapSource.Create(3840,520,96,96,PixelFormats.Bgra32,null,new byte[3840*520*4],3840*4);
                var clip=new MotionClip(image,5,.65,false,480,520,8);
                Check(clip.Sample(.129)==0 && clip.Sample(.13)==1 && clip.Sample(.519)==3 && clip.Sample(.52)==4 && clip.Sample(.99)==4,"pose sampler preserves .13 cadence and final pose");
            }
        }
        static void NativePixels()
        {
            var type=typeof(SkinCatalog).Assembly.GetType("Dafeiyu.WpfOfficialNativeValidator");Check(type!=null,"official installation has a native validation adapter");
            var method=type.GetMethod("ValidatePng",BindingFlags.Static|BindingFlags.NonPublic);
            Check(method!=null,"native adapter exposes bounded raster materialization");
            string path=Path.Combine(Path.GetTempPath(),"deskpet-native-"+Guid.NewGuid().ToString("N")+".png");
            try {
                foreach(bool visible in new[]{false,true}) {
                    var pixels=new byte[480*520*4];if(visible){pixels[3]=255;pixels[0]=100;}
                    var image=BitmapSource.Create(480,520,96,96,PixelFormats.Bgra32,null,pixels,480*4);
                    var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var output=File.Create(path))png.Save(output);
                    bool rejected=false;
                    try{method.Invoke(null,new object[]{path,480,520,1,1,true,System.Threading.CancellationToken.None});}
                    catch(TargetInvocationException e){if(!(e.InnerException is InvalidDataException))throw;rejected=true;}
                    Check(rejected!=visible,visible?"native visible transparent frame is usable":"native fully transparent frame is rejected");
                }
                var broken=File.ReadAllBytes(path);int position=8;
                while(position<broken.Length){int size=broken[position]<<24|broken[position+1]<<16|broken[position+2]<<8|broken[position+3];if(Encoding.ASCII.GetString(broken,position+4,4)=="IDAT"){broken[position+8]=0;uint crc=0xffffffff;for(int i=position+4;i<position+8+size;i++){crc^=broken[i];for(int bit=0;bit<8;bit++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}crc^=0xffffffff;for(int i=0;i<4;i++)broken[position+8+size+i]=(byte)(crc>>(24-i*8));break;}position+=size+12;}
                File.WriteAllBytes(path,broken);bool corruptRejected=false;
                try{method.Invoke(null,new object[]{path,480,520,1,1,true,CancellationToken.None});}catch(TargetInvocationException error){if(!(error.InnerException is InvalidDataException))throw;corruptRejected=true;}
                Check(corruptRejected,"native decoder rejects corrupt IDAT even with repaired chunk CRC");
            }finally{if(File.Exists(path))File.Delete(path);}
        }
        static void CatalogTrust()
        {
            string data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"deskpet-official-test-"+Guid.NewGuid().ToString("N"));
            try {
                var catalog=new SkinCatalog(data);Check(catalog.Skins.Count==5,"thin catalog keeps all five official entries without optional full resources");
                bool fallbackReserved=false;try{catalog.Remove(catalog.FindOfficial("e-bao"));}catch(InvalidOperationException){fallbackReserved=true;}Check(fallbackReserved,"official Ebao fallback cannot be hidden or deleted");
                var key=typeof(SkinDefinition).GetProperty("PickerKey");Check(key!=null,"catalog separates picker provenance from persona identity");
                foreach(var skin in catalog.Skins)Check((string)key.GetValue(skin,null)=="official:"+skin.BuiltinCharacterId,"official picker uses fixed provenance identity");
                string source=Path.Combine(data,"fixture");Directory.CreateDirectory(source);
                WriteNative(Path.Combine(source,"action.png"),8,1,8);WriteNative(Path.Combine(source,"gaze.png"),4,4,16);WriteNative(Path.Combine(source,"preview.png"),1,1,1);
                var manifest=new SkinManifest{Version=2,PackageId="dafeiyu",Name="自定义",Format="illustrated-atlases-v1",CellWidth=480,CellHeight=520,Preview="preview.png",Gaze=new IllustratedGaze{File="gaze.png",Columns=4,Frames=16,Interpolation="none",Order="clockwise-from-up"},Animations=new Dictionary<string,IllustratedAnimation>()};
                foreach(string action in IllustratedPack.Actions)manifest.Animations[action]=new IllustratedAnimation{File="action.png",Columns=8,Frames=8,Fps=8,Loop=action!="jumping"};
                string json=Path.Combine(source,"pet.json");using(var output=File.Create(json))SkinCatalog.Serializer().WriteObject(output,manifest);
                var custom=catalog.Import(json);Check(custom.BuiltinCharacterId==null,"self-declared official ID does not confer provenance");
                Check((string)key.GetValue(custom,null)=="custom-pack:dafeiyu","custom package has its own picker identity");
                var official=catalog.Skins[1];Check(official.BuiltinCharacterId=="dafeiyu" && official.Id!=custom.Id,"new custom collision retains official entry and allocates distinct persona");
                Check(new SkinCatalog(data).Skins.Count==6,"custom and official survive reload independently");
                string oldData=Path.Combine(data,"old");Directory.CreateDirectory(oldData);string personal=Path.Combine(oldData,"settings.json");File.WriteAllText(personal,"preserved personal settings");
                CompanionPackStorage.Install(source,oldData,"dafeiyu",IllustratedPack.Files(manifest),folder=>IllustratedPack.Validate(folder,manifest,true));
                var previous=new SkinCatalog(oldData);var retained=previous.Skins.Single(s=>!s.IsOfficial);
                Check(retained.Id=="dafeiyu"&&previous.FindOfficial("dafeiyu").Id.StartsWith("skin-",StringComparison.Ordinal),"preexisting untrusted custom preserves exact persona while official gets separate alias");
                Check(previous.Find("dafeiyu")==retained&&File.ReadAllText(personal)=="preserved personal settings","legacy selected custom ID resolves without rewriting user settings");
                previous.Remove(retained);Check(new SkinCatalog(oldData).FindOfficial("dafeiyu")!=null,"hiding a spoofed custom package does not hide official placeholder");
                var inventory=OfficialJson.Object(OfficialJson.Parse(Encoding.UTF8.GetBytes(OfficialCompanionLegacyData.Json),65536));
                var record=OfficialJson.Array(inventory["roles"]).Select(OfficialJson.Object).Single(r=>(string)r["id"]=="xiaobei");
                string corrupt=Path.Combine(data,"bundled-skins","xiaobei-"+((string)record["cache_version"]).Substring(0,24));Directory.CreateDirectory(corrupt);File.WriteAllText(Path.Combine(corrupt,"pet.json"),new string('x',65537));
                Check(new SkinCatalog(data).FindOfficial("e-bao")!=null,"oversized corrupt canonical legacy metadata cannot abort catalog fallback");
            }finally{if(Directory.Exists(data))Directory.Delete(data,true);}
        }
        static void WriteNative(string path,int columns,int rows,int count)
        {
            int width=columns*480,height=rows*520;var pixels=new byte[width*height*4];
            for(int i=0;i<count;i++){int p=(i/columns*520*width+i%columns*480)*4;pixels[p]=100;pixels[p+3]=255;}
            var image=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var output=File.Create(path))encoder.Save(output);
        }
        static void ThinStartupAndGuide(string legacyAssets)
        {
            Check(typeof(PetWindow).Assembly.GetManifestResourceStream("Dafeiyu.Atlas")==null,"thin acceptance really excludes Dafeiyu full default artwork");
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            string data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tu",Guid.NewGuid().ToString("N").Substring(0,8));
            try {
                var settings=new PetSettings{SkinId="dafeiyu",SystemPrompt="保留原伙伴人格",PersonaProfileVersion=1,AutoUpdate=false,GreetingEnabled=false,Wander=false,FollowCursor=false,HideOnFullscreen=false,SaveChatHistory=false};
                foreach(string key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
                pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(data,"settings.json")),null);
                Check(pet.Skin.BuiltinCharacterId=="e-bao"&&settings.PendingOfficialCompanionId=="dafeiyu","thin startup uses embedded Ebao and persists missing selected role");
                Check(settings.PersonaOverrides["dafeiyu"]=="保留原伙伴人格","startup fallback preserves previous persona before activation");
                Check(pet.Catalog.Skins.Count(s=>s.IsPlaceholder)==4,"thin startup has exactly four download placeholders");
                foreach(var skin in pet.Catalog.Skins)Check(SkinCatalog.GetAvatar(skin)!=null,"every thin official avatar decodes without default role artwork");
                settings.OnboardingLessonId="pet.select";pet.ShowOnboarding();Pump();
                var guide=(OnboardingWindow)typeof(PetWindow).GetField("guide",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet);
                Check(guide!=null&&guide.IsVisible,"first guide opens on the companion lesson");
                string before=settings.SkinId,picker=settings.SelectedCompanionKey,pending=settings.PendingOfficialCompanionId,prompt=settings.SystemPrompt;
                Check(!pet.SelectSkin(pet.Catalog.FindOfficial("marfu")),"guide companion attempt is notice-only");
                pet.DownloadAllOfficialAsync(guide).GetAwaiter().GetResult();
                Check(settings.SkinId==before&&settings.SelectedCompanionKey==picker&&settings.PendingOfficialCompanionId==pending&&settings.SystemPrompt==prompt,"guide attempts preserve selection persona and pending restore");
                Check(typeof(PetWindow).GetField("officialDownloads",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet)==null,"guide attempts never create a download service");
                Check(Walk(guide).OfType<Button>().Single(b=>b.Name=="GuideNext").IsEnabled,"notice-only companion lesson remains advanceable");
                guide.Close();Pump();Check(pet.SelectSkin(pet.Skin),"explicit already-visible Ebao selection succeeds");
                Check(settings.PendingOfficialCompanionId=="","explicit Ebao selection clears older automatic restore");
                NativeSelectionRaces(pet);
                if(legacyAssets!=null)LegacyCacheWithoutEmbeddedPngs(legacyAssets.Substring("--legacy-assets=".Length));
            }finally {
                if(pet!=null){pet.Close();Pump();}foreach(var window in app.Windows.Cast<Window>().ToArray())window.Close();Pump();app.Shutdown();
                if(Directory.Exists(data))Directory.Delete(data,true);
            }
        }
        static System.Collections.Generic.IEnumerable<DependencyObject> Walk(DependencyObject root)
        {yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var item in Walk(VisualTreeHelper.GetChild(root,i)))yield return item;}
        static void Pump()
        {var until=DateTime.UtcNow.AddMilliseconds(350);while(DateTime.UtcNow<until){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);System.Threading.Thread.Sleep(5);}}
        static void Wait(Task task)
        {var end=DateTime.UtcNow.AddSeconds(60);while(!task.IsCompleted&&DateTime.UtcNow<end)Pump();Check(task.IsCompleted,"controlled asynchronous operation completes within bounded test time");task.GetAwaiter().GetResult();}
        static FieldInfo Field(string name){return typeof(PetWindow).GetField(name,BindingFlags.NonPublic|BindingFlags.Instance);}
        static void NativeSelectionRaces(PetWindow pet)
        {
            using(var fixture=new OfficialCompanionFixture())using(var transport=fixture.Transport()) {
                foreach(string id in OfficialCompanionFixture.Ids)fixture.Files[OfficialCompanionFixture.PackageName(id)]=NativePackage(id,100);
                var old=(OfficialCompanionDownloads)Field("officialDownloads").GetValue(pet);if(old!=null)old.Dispose();
                var native=(WpfOfficialNativeValidator)Field("officialNative").GetValue(pet);
                using(var service=new OfficialCompanionDownloads(pet.Store.DirectoryPath,ReleaseInfo.DisplayVersion,UpdateChannel.Preview,transport,native.ValidateAsync)) {
                    Field("officialDownloads").SetValue(pet,service);Field("officialRequest").SetValue(pet,0L);Field("officialStartupStarted").SetValue(pet,true);
                    var started=new TaskCompletionSource<bool>();var release=new TaskCompletionSource<bool>();bool hold=true,failMarfu=false;
                    fixture.Intercept=async (request,cancel)=>{
                        if(request.RequestUri.AbsolutePath.EndsWith(OfficialCompanionFixture.PackageName("dafeiyu"),StringComparison.Ordinal)&&hold){started.TrySetResult(true);await release.Task.ConfigureAwait(false);}
                        if(request.RequestUri.AbsolutePath.EndsWith(OfficialCompanionFixture.PackageName("marfu"),StringComparison.Ordinal)&&failMarfu)return OfficialCompanionFixture.Response(new byte[0],HttpStatusCode.NotFound);
                        return null;
                    };
                    var selection=pet.RequestSkinAsync(pet.Catalog.FindOfficial("dafeiyu"),CompanionSelectionOrigin.StartupRestore,null);Wait(started.Task);
                    Check(pet.Skin.BuiltinCharacterId=="e-bao"&&pet.Settings.PendingOfficialCompanionId=="dafeiyu","missing request retains old visible source while downloading");
                    pet.Catalog.Remove(pet.Catalog.FindOfficial("dafeiyu"));Check(pet.Settings.PendingOfficialCompanionId==""&&!pet.Catalog.Skins.Any(s=>s.BuiltinCharacterId=="dafeiyu"),"hiding pending noncurrent role invalidates restore intent without cancelling shared installation");
                    Check(pet.SelectSkin(pet.Skin),"already-visible Ebao supersedes in-flight requested role");hold=false;release.TrySetResult(true);Wait(selection);
                    Check(service.Installed("dafeiyu")!=null&&pet.Skin.BuiltinCharacterId=="e-bao"&&pet.Settings.PendingOfficialCompanionId=="","late completed package installs without taking over latest explicit choice");
                    Check(!pet.Catalog.Skins.Any(s=>s.BuiltinCharacterId=="dafeiyu")&&new SettingsStore(Path.Combine(pet.Store.DirectoryPath,"settings.json")).Load().PendingOfficialCompanionId=="","late hidden-role completion cannot unhide or resurrect pending selection after restart");
                    pet.Catalog.RestoreRemoved();pet.Catalog.PreserveActive(pet.Skin);
                    Wait(pet.RequestSkinAsync(pet.Catalog.FindOfficial("dafeiyu"),CompanionSelectionOrigin.StartupRestore,null));
                    Check(pet.Skin.BuiltinCharacterId=="dafeiyu"&&!pet.Skin.Embedded,"native installed full package activates through manifest path");
                    var selected=pet.Skin;string persona=pet.Settings.SkinId,key=pet.Settings.SelectedCompanionKey;long token=(long)Field("officialRequest").GetValue(pet);
                    var unavailable=pet.Holidays.Catalog.Find("dafeiyu","halloween-2026");pet.Holidays.Catalog.ReportUnavailable(unavailable);
                    failMarfu=true;Wait(pet.DownloadAllOfficialAsync(null));
                    Check(pet.Skin.Source==selected.Source&&pet.Settings.SkinId==persona&&pet.Settings.SelectedCompanionKey==key&&(long)Field("officialRequest").GetValue(pet)==token,"partial bulk preserves current root selection persona and ownership token");
                    Check(pet.Holidays.Catalog.Find("dafeiyu","halloween-2026").Ready&&!ReferenceEquals(pet.Holidays.Catalog.Find("dafeiyu","halloween-2026"),unavailable)&&pet.Skin.NativeSourceKey!=selected.NativeSourceKey,"partial bulk same-root verified repair replaces rejected catalog and native source generation");
                    Check(!pet.Catalog.FindOfficial("gugugaga").IsPlaceholder&&pet.Catalog.FindOfficial("marfu").IsPlaceholder,"partial bulk publishes successful roles and leaves missing role retryable");
                    failMarfu=false;Wait(pet.DownloadAllOfficialAsync(null));Check(pet.Catalog.Skins.Count(s=>s.IsPlaceholder)==0,"bulk retry completes missing role without losing finished packages");
                    string oldSource=pet.Skin.Source;var oldVisual=pet.Skin;var oldInstallation=service.Installed("dafeiyu");var oldOutfit=pet.Holidays.Catalog.Find("dafeiyu","halloween-2026");
                    PersonaProfiles.Save(pet.Settings,pet.Skin,"保留当前自定义人格");pet.Settings.VoiceName="private-test-voice";PersonaProfiles.SaveVoices(pet.Settings);
                    var preferences=(SettingsPage)Field("preferences").GetValue(pet);if(preferences!=null)typeof(SettingsPage).GetMethod("RefreshAvatar",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(preferences,null);
                    fixture.Files[OfficialCompanionFixture.PackageName("dafeiyu")]=NativePackage("dafeiyu",190);
                    Field("completionUntil").SetValue(pet,Double.MaxValue);Wait(pet.DownloadAllOfficialAsync(null));
                    Check(pet.Skin.Source==oldSource&&ReferenceEquals(pet.Holidays.Catalog.Find("dafeiyu","halloween-2026"),oldOutfit),"busy bulk source update retains old immutable current default and holiday objects");
                    typeof(PetWindow).GetMethod("QueueOfficial",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,new object[]{oldVisual,token,false});
                    Field("completionUntil").SetValue(pet,0d);typeof(PetWindow).GetMethod("TryApplyDeferredOfficial",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,null);
                    var commitDeadline=DateTime.UtcNow.AddSeconds(30);while(pet.Skin.Source==oldSource&&DateTime.UtcNow<commitDeadline)Pump();
                    Check(pet.Skin.Source!=oldSource&&pet.Settings.SkinId==persona&&pet.Settings.SelectedCompanionKey==key&&pet.Settings.SystemPrompt=="保留当前自定义人格"&&pet.Settings.VoiceName=="private-test-voice","safe same-role update refreshes source without persona voice or history activation");
                    var shown=(MotionClip)Field("shownMotion").GetValue(pet);int sample=(int)Field("shownSample").GetValue(pet);
                    Check(shown!=null&&shown.AlphaAt(sample,11,0)>16&&shown.AlphaAt(sample,0,0)==0,"source refresh publishes new frame and matching hit mask rather than stale same-persona cache");
                    Check(pet.Holidays.Catalog.Find("dafeiyu","halloween-2026").SourceKey!=oldOutfit.SourceKey,"same outfit version uses refreshed immutable source identity");
                    var delayed=(Task<SkinDefinition>)typeof(PetWindow).GetMethod("PrepareOfficialAsync",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pet,new object[]{oldInstallation,false});Wait(delayed);
                    Check(delayed.Result.Source==pet.Skin.Source,"delayed older background result cannot replace newer bulk source cache for unchanged selection token");
                    var cache=(System.Collections.IDictionary)Field("officialCatalogs").GetValue(pet);Check(cache.Count<=6,"catalog cache bounds retired sources across updates");
                    Wait(pet.RequestSkinAsync(pet.Catalog.FindOfficial("e-bao"),CompanionSelectionOrigin.StartupRestore,null));
                    string eSource=pet.Skin.Source,hiddenPersona=pet.Catalog.FindOfficial("dafeiyu").Id;
                    Check(!pet.Skin.Embedded&&pet.Skin.OfficialInstallation!=null,"bulk-installed newer Ebao is a valid selected external source");
                    pet.Catalog.Remove(pet.Catalog.FindOfficial("dafeiyu"));Wait(pet.DownloadAllOfficialAsync(null));
                    Check(!pet.Catalog.Skins.Any(s=>s.BuiltinCharacterId=="dafeiyu")&&pet.Catalog.FindOfficial("dafeiyu").Id==hiddenPersona,"bulk refreshes hidden official registry without unhiding role or losing persona");
                    var reloaded=new SkinCatalog(pet.Store.DirectoryPath);Check(!reloaded.Skins.Any(s=>s.BuiltinCharacterId=="dafeiyu")&&reloaded.FindOfficial("dafeiyu").Id==hiddenPersona,"hidden official identity persists across catalog restart");
                    if(preferences!=null){preferences.RefreshSkinChoices();((Button)preferences.FindControl("RestoreCompanions")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
                    Check(pet.Skin.Source==eSource&&pet.Catalog.FindOfficial("e-bao").Source==eSource&&pet.Catalog.Skins.Any(s=>s.BuiltinCharacterId=="dafeiyu"),"explicit restore unhides role while retaining selected installed Ebao source");
                    // Even a legacy/manual hidden flag cannot remove the required fallback.
                    CompanionPackStorage.SetHidden(pet.Store.DirectoryPath,"official:e-bao",true);Check(new SkinCatalog(pet.Store.DirectoryPath).Skins.Any(s=>s.BuiltinCharacterId=="e-bao"),"persisted Ebao hidden flag cannot remove offline fallback");CompanionPackStorage.SetHidden(pet.Store.DirectoryPath,"official:e-bao",false);
                    var fallback=pet.Catalog.EmbeddedFallback();Wait(pet.RequestSkinAsync(fallback,CompanionSelectionOrigin.StartupRestore,null));Check(pet.Skin.Source==eSource&&!pet.Skin.Embedded,"cached embedded Ebao option prefers receipt-verified installed source without metadata delay");
                    foreach(var window in Application.Current.Windows.OfType<OfficialCompanionProgressWindow>().ToArray())window.Close();
                }
                Field("officialDownloads").SetValue(pet,null);
            }
        }
        static byte[] NativePackage(string id,byte color)
        {
            return OfficialCompanionFixture.Package(id,files=>{
                foreach(string path in files.Keys.Where(k=>k.EndsWith(".png",StringComparison.Ordinal)).ToArray()) {
                    int columns=path.EndsWith("gaze.png",StringComparison.Ordinal)?4:path.EndsWith("preview.png",StringComparison.Ordinal)?1:8;
                    files[path]=NativePng(columns,columns==4?4:1,columns==4?16:columns,color);
                }
                var manifest=OfficialJson.Object(OfficialJson.Parse(files["pet.json"],65536));manifest["avatar"]="avatar.png";files["pet.json"]=OfficialCompanionFixture.Json(manifest);
                var pixels=new byte[64*64*4];pixels[0]=color;pixels[3]=255;var image=BitmapSource.Create(64,64,96,96,PixelFormats.Bgra32,null,pixels,64*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var output=new MemoryStream()){encoder.Save(output);files["avatar.png"]=output.ToArray();}
            });
        }
        static byte[] NativePng(int columns,int rows,int count,byte color)
        {
            int width=480*columns,height=520*rows;var pixels=new byte[width*height*4];for(int i=0;i<count;i++){int p=(i/columns*520*width+i%columns*480+(color==190?11:0))*4;pixels[p]=color;pixels[p+3]=255;}
            var image=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using(var output=new MemoryStream()){encoder.Save(output);return output.ToArray();}
        }
        static void CanonicalStoreOwnership(string assets)
        {
            string data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"co",Guid.NewGuid().ToString("N").Substring(0,8));
            try {
                string source=Path.Combine(assets,"gugugaga");SkinManifest canonical;using(var input=File.OpenRead(Path.Combine(source,"pet.json")))canonical=SkinCatalog.ReadManifest(input);
                CompanionPackStorage.Install(source,data,"gugugaga",IllustratedPack.Files(canonical),folder=>IllustratedPack.Validate(folder,canonical,true));
                var catalog=new SkinCatalog(data);var official=catalog.FindOfficial("gugugaga");
                Check(official.IsOfficial&&official.Id=="gugugaga"&&!catalog.Skins.Any(s=>s.PickerKey=="custom-pack:gugugaga"),"pre-upgrade canonical stored package is trusted ownership rather than custom ownership");
                var personal=new PetSettings();PersonaProfiles.Activate(personal,official);PersonaProfiles.Save(personal,official,"保留官方旧包人格");personal.VoiceName="old-official-private-voice";PersonaProfiles.SaveVoices(personal);
                string replacement=Path.Combine(data,"replacement");foreach(string path in IllustratedPack.Files(canonical)){string destination=Path.Combine(replacement,path);Directory.CreateDirectory(Path.GetDirectoryName(destination));File.Copy(Path.Combine(source,path),destination);}
                canonical.SystemPrompt+="这是新导入的非官方自定义设计。";string json=Path.Combine(replacement,"pet.json");using(var output=File.Create(json))SkinCatalog.Serializer().WriteObject(output,canonical);
                var custom=catalog.Import(json);
                Check(custom.PickerKey=="custom-pack:gugugaga"&&custom.Id!=official.Id&&!custom.IsOfficial,"new custom replacement of canonical stored package receives a separate persisted persona");
                PersonaProfiles.Activate(personal,custom);PersonaProfiles.Save(personal,custom,"保留自定义人格");PersonaProfiles.Activate(personal,official);
                Check(personal.SystemPrompt=="保留官方旧包人格"&&personal.VoiceName=="old-official-private-voice","custom replacement cannot take over prior official persona or voice profile");
                catalog.Remove(custom);catalog=new SkinCatalog(data);var updated=catalog.Import(json);var reopened=new SkinCatalog(data);
                Check(updated.Id==custom.Id&&reopened.FindPicker(updated.PickerKey).Id==custom.Id&&reopened.FindOfficial("gugugaga").Id==official.Id,"hidden custom update and restart retain separate official and custom identities");
            }finally{if(Directory.Exists(data))Directory.Delete(data,true);}
        }
        static void LegacyCacheWithoutEmbeddedPngs(string assets)
        {
            CanonicalStoreOwnership(assets);
            var inventory=OfficialJson.Object(OfficialJson.Parse(Encoding.UTF8.GetBytes(OfficialCompanionLegacyData.Json),65536));
            foreach(string id in new[]{"xiaobei","gugugaga"}) {
                string data=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"lg",Guid.NewGuid().ToString("N").Substring(0,8));PetWindow previous=null;
                try {
                    var record=OfficialJson.Array(inventory["roles"]).Select(OfficialJson.Object).Single(r=>(string)r["id"]==id);
                    string cache=Path.Combine(data,"bundled-skins",id+"-"+((string)record["cache_version"]).Substring(0,24));
                    string source=Path.Combine(assets,id);foreach(string path in Directory.GetFiles(source,"*",SearchOption.AllDirectories)) {
                        string relative=path.Substring(source.Length+1);if(Path.GetExtension(relative)!=".png"&&relative!="pet.json")continue;
                        string destination=Path.Combine(cache,relative);Directory.CreateDirectory(Path.GetDirectoryName(destination));File.Copy(path,destination);
                    }
                    string alias="skin-"+Guid.NewGuid().ToString("N");CompanionPackStorage.RetainIdentity(data,id,alias);
                    var settings=new PetSettings{SkinId=alias,AutoUpdate=false,GreetingEnabled=false,Wander=false,SaveChatHistory=false};
                    foreach(string key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
                    previous=new PetWindow(null,settings,new SettingsStore(Path.Combine(data,"settings.json")),null);
                    Check(previous.Skin.BuiltinCharacterId==id&&previous.Skin.Id==alias&&previous.Skin.Source==cache,"thin upgrade retains native-validated canonical old cache and persona alias for "+id);
                    Check(!previous.Skin.Embedded&&!previous.Skin.Bundled&&!previous.Skin.IsPlaceholder&&String.IsNullOrEmpty(settings.PendingOfficialCompanionId),"old default cache stays manifest-owned usable source without premature fallback for "+id);
                    Check(previous.Skin.Load().HasGaze&&SkinCatalog.GetPreview(previous.Skin).PixelWidth==480,"retained old cache native gaze and preview remain readable for "+id);
                    Check(Field("officialDownloads").GetValue(previous)==null,"loading canonical cache needs no metadata or download for "+id);
                }finally{if(previous!=null){previous.Close();Pump();}if(Directory.Exists(data))Directory.Delete(data,true);}
            }
        }
    }
}
