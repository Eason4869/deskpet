using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
namespace Dafeiyu.Tests
{
    public static class CompleteCompanionTests
    {
        static int checks;
        static void Check(bool value,string message){if(!value)throw new Exception("FAIL: "+message);checks++;}
        static void Reject(Action action,string message){try{action();}catch(InvalidDataException){checks++;return;}throw new Exception("FAIL accepted: "+message);}
        public static int Main(string[] args)
        {
            try {
                CompanionPackagePolicy.CheckArchive(536870912,1024);
                CompanionPackagePolicy.CheckExpanded(536870912);
                Reject(()=>CompanionPackagePolicy.CheckArchive(536870913,1),"ZIP byte boundary");
                Reject(()=>CompanionPackagePolicy.CheckArchive(1,1025),"ZIP entry boundary");
                Reject(()=>CompanionPackagePolicy.CheckExpanded(536870913),"expanded byte boundary");
                Check(CompanionPackagePolicy.MaximumPngBytes==26214400,"PNG boundary retained");
                Check(CompanionPackagePolicy.MaximumAppearanceBytes==268435456,"appearance boundary retained");
                Check(CompanionPackagePolicy.MaximumManifestBytes==65536,"manifest boundary retained");
                CompanionPackStorage.CheckSpace(100,100+64L*1024*1024);
                Reject(()=>CompanionPackStorage.CheckSpace(100,99+64L*1024*1024),"free space includes reserved margin");
                string root=Path.Combine(Environment.CurrentDirectory,"work","complete-companion-fixtures",Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                try {
                    Directory.CreateDirectory(Path.Combine(root,"animations"));
                    File.WriteAllText(Path.Combine(root,"pet.json"),"{\"id\":\"fixture-pet\",\"animations\":{\"idle\":{\"file\":\"animations/idle.png\"}},\"gaze\":{\"file\":\"gaze.png\"},\"preview\":\"preview.png\"}",Encoding.UTF8);
                    foreach(string file in new[]{"animations/idle.png","gaze.png","preview.png"})File.WriteAllBytes(Path.Combine(root,file),new byte[]{137,80,78,71,13,10,26,10});
                    var first=CompanionPackageInventory.Read(root,CancellationToken.None);
                    Check(first.PackageId=="fixture-pet"&&first.AllReferences.Count==4,"legacy default-only full package inventory");
                    Check(first.HolidayCatalog==null,"no synthetic holiday collection");
                    string hash=first.ContentSha256;
                    File.AppendAllText(Path.Combine(root,"preview.png"),"new");
                    Check(hash!=CompanionPackageInventory.Read(root,CancellationToken.None).ContentSha256,"same version new payload changes identity");
                    File.WriteAllBytes(Path.Combine(root,"unreferenced.png"),new byte[]{1});
                    Reject(()=>CompanionPackageInventory.Read(root,CancellationToken.None),"unreferenced PNG cannot silently disappear");
                    File.Delete(Path.Combine(root,"unreferenced.png"));
                    File.Delete(Path.Combine(root,"gaze.png"));
                    Reject(()=>CompanionPackageInventory.Read(root,CancellationToken.None),"missing referenced file");
                    string zip=Path.Combine(root,"legacy.zip");
                    using(var output=File.Create(zip))using(var archive=new System.IO.Compression.ZipArchive(output,System.IO.Compression.ZipArchiveMode.Create)) {
                        using(var writer=new StreamWriter(archive.CreateEntry("pet.json").Open()))writer.Write("{}");
                        using(var writer=new StreamWriter(archive.CreateEntry("animations\\idle.png").Open()))writer.Write("legacy");
                    }
                    Check(CompanionPackStorage.ReadZip(zip,Path.Combine(root,"data"),path=>File.Exists(Path.Combine(Path.GetDirectoryName(path),"animations","idle.png"))),"Windows Framework ZIP separators remain compatible");
                    var probe=CompanionPackStorage.FreeSpaceProbe;
                    try{CompanionPackStorage.FreeSpaceProbe=_=>0;Reject(()=>CompanionPackStorage.ReadZip(zip,Path.Combine(root,"low-space"),path=>true),"low disk preflight rejects before extraction");Check(!Directory.GetDirectories(Path.Combine(root,"low-space","companion-packs")).Any(),"low space leaves no extraction directory");}finally{CompanionPackStorage.FreeSpaceProbe=probe;}
                    string storage=Path.Combine(root,"transaction");var installed=CompanionPackStorage.Install(root,storage,"fixture-pet",new[]{"preview.png"},_=>{});
                    File.AppendAllText(Path.Combine(root,"preview.png"),"changed");
                    using(var cancellation=new CancellationTokenSource()) {
                        bool cancelled=false;try{CompanionPackStorage.Install(root,storage,"fixture-pet",new[]{"preview.png"},_=>cancellation.Cancel(),cancellation.Token);}catch(OperationCanceledException){cancelled=true;}
                        Check(cancelled&&CompanionPackStorage.Installed(storage).Single().Value==installed,"cancel before pointer publication preserves prior installation");
                    }
                    string replacement=CompanionPackStorage.Install(root,storage,"fixture-pet",new[]{"preview.png","animations/idle.png"},_=>{});
                    string trimmed=CompanionPackStorage.Install(root,storage,"fixture-pet",new[]{"preview.png"},_=>{});
                    Check(trimmed!=replacement&&!File.Exists(Path.Combine(trimmed,"animations/idle.png")),"removed resources publish an exact replacement rather than retaining old extras");
                    File.WriteAllText(Path.Combine(trimmed,"corrupt-extra.txt"),"bad");
                    string repaired=CompanionPackStorage.Install(root,storage,"fixture-pet",new[]{"preview.png"},path=>{if(File.Exists(Path.Combine(path,"corrupt-extra.txt")))throw new InvalidDataException();});
                    Check(repaired!=trimmed&&!File.Exists(Path.Combine(repaired,"corrupt-extra.txt")),"valid source repairs damaged previous installation");
                    string inventoryRoot=Path.Combine(root,"official-inventory");Directory.CreateDirectory(inventoryRoot);
                    for(int n=0;n<600;n++)File.WriteAllText(Path.Combine(inventoryRoot,"file"+n+".json"),"{}");
                    Check(OfficialCompanionStorage.Inventory(inventoryRoot).Count==600,"official installed inventory uses the shared entry limit above512");
                    string receiptData=Path.Combine(Environment.CurrentDirectory,"work","receipt-"+Guid.NewGuid().ToString("N").Substring(0,8));
                    try {
                        var official=new OfficialCompanionStorage(receiptData);string roleRoot=official.CharacterRoot("dafeiyu");Directory.CreateDirectory(roleRoot);
                        string stage=Path.Combine(roleRoot,".stage");Directory.Move(inventoryRoot,stage);
                        foreach(string required in new[]{"pet.json","companions.json","holidays/catalog.json"}){string path=OfficialCompanionStorage.SafePath(stage,required);Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,"{}");}
                        var asset=new OfficialCompanionAsset("dafeiyu","fixture","0.11.0-dev.3","0.11.0-dev.3","companions-v0.11.0-dev.3","DeskPet-Companion-dafeiyu-0.11.0-dev.3.zip",1,new string('a',64),new[]{new OfficialCompanionOutfit("halloween-classic","1.0.0")});
                        official.WriteReceipt(stage,asset,CancellationToken.None);official.Publish(stage,asset,CancellationToken.None,()=>{},action=>action());
                        Check(official.Installed("dafeiyu")!=null,"official603-file receipt publishes and reads back under the shared entry limit");
                    }finally{if(Directory.Exists(receiptData))Directory.Delete(receiptData,true);}
                    probe=CompanionPackStorage.FreeSpaceProbe;
                    try {CompanionPackStorage.FreeSpaceProbe=_=>64L*1024*1024+150;using(CompanionPackStorage.ReserveDiskSpace(root,100)){Reject(()=>{using(CompanionPackStorage.ReserveDiskSpace(root,60)){}},"concurrent downloads cannot reserve the same remaining disk bytes");}using(CompanionPackStorage.ReserveDiskSpace(root,150)){}Check(true,"finished downloads release volume reservations");}finally{CompanionPackStorage.FreeSpaceProbe=probe;}
                    string aliases=Path.Combine(root,"aliases.zip");
                    using(var output=File.Create(aliases))using(var archive=new System.IO.Compression.ZipArchive(output,System.IO.Compression.ZipArchiveMode.Create)) {
                        archive.CreateEntry("a\\b.png");archive.CreateEntry("a/b.png");
                    }
                    Reject(()=>CompanionPackStorage.ReadZip(aliases,Path.Combine(root,"data"),path=>true),"normalized ZIP aliases still rejected");
                } finally {Directory.Delete(root,true);}
                if(args.Length==2&&args[0]=="--materials")foreach(string id in new[]{"e-bao","dafeiyu","marfu","xiaobei","gugugaga"}) {
                    var inventory=CompanionPackageInventory.Read(Path.Combine(args[1],id),CancellationToken.None);
                    Check(inventory.HolidayCatalog!=null&&inventory.HolidayCatalog.Outfits.Count==10,"ten holiday outfits: "+id);
                    Check(inventory.AllReferences.Count>=343,"complete actual payload: "+id);
                    Check(inventory.HolidayCatalog.Outfits.All(o=>o.Ready),"all outfits validated: "+id);
                    Console.WriteLine("PASS actual full inventory "+id+": "+inventory.AllReferences.Count+" files / "+inventory.ExpandedBytes+" bytes");
                }
                Console.WriteLine("Complete companion tests: "+checks+" passed");return 0;
            }catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
    }
}
