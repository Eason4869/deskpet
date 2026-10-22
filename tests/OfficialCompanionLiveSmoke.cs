using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Dafeiyu.Tests
{
    // Explicit post-publication acceptance, never included in offline CI loops.
    // Uses a new owned data directory and the actual updater transport/routing.
    public static class OfficialCompanionLiveSmoke
    {
        static object Memory(string stage)
        {
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            using(var process=Process.GetCurrentProcess()) {
                process.Refresh();return new{stage=stage,managedBytes=GC.GetTotalMemory(false),privateBytes=process.PrivateMemorySize64,workingSetBytes=process.WorkingSet64};
            }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        static List<WeakReference> Visit(HolidayVisualAssets visuals,HolidayCatalog catalog)
        {
            var retired=new List<WeakReference>();
            foreach(var outfit in catalog.Outfits)foreach(var definition in outfit.Clips.Concat(new[]{outfit.Gaze,outfit.EasterEgg})) {
                var clip=visuals.Get(definition);retired.Add(new WeakReference(clip));
                if(clip.Count!=definition.FrameCount)throw new InvalidDataException("Published clip frame count changed.");
                for(int i=0;i<clip.Count;i++)clip.Frame(i);
            }
            return retired;
        }
        static object Measure(OfficialCompanionInstallation installed)
        {
            var samples=new List<object>{Memory("before-downloaded-outfit")};
            var catalog=new HolidayCatalog(installed.HolidayRootDirectory);var visuals=new HolidayVisualAssets(null);
            visuals.Get(catalog.Outfits[0].FindClip("idle","default"));samples.Add(Memory("downloaded-idle"));
            for(int round=1;round<=3;round++) {
                var retired=Visit(visuals,catalog);samples.Add(Memory("downloaded-all-actions-"+round));
                visuals.Clear();samples.Add(Memory("downloaded-cleared-"+round));
                if(retired.Any(x=>x.IsAlive))throw new InvalidDataException("Retired downloaded costume clips remain retained.");
            }
            return samples;
        }
        [STAThread] public static int Main(string[] args)
        {
            try {
                if(args.Length!=4)throw new ArgumentException("Supply new data directory, role ID, expected companions-v tag, and --mirror or --direct.");
                string root=Path.GetFullPath(args[0]),id=args[1],tag=args[2];OfficialCompanionRules.Id(id);
                if(tag!="companions-v"+ReleaseInfo.DisplayVersion||(args[3]!="--mirror"&&args[3]!="--direct"))throw new ArgumentException("Unexpected release or routing mode.");
                if(Directory.Exists(root)||File.Exists(root))throw new IOException("Live acceptance requires a new owned directory.");
                Directory.CreateDirectory(root);
                var routing=new OfficialDownloadRouting(args[3]=="--mirror",PetSettings.DefaultUpdateMirror,false,"");
                var routes=new HashSet<string>(StringComparer.Ordinal);string lastStage="";object gate=new object();
                var progress=new OfficialProgress<OfficialCompanionProgress>(p=>{lock(gate){if(!String.IsNullOrEmpty(p.Route))routes.Add(p.Route);if(lastStage!=p.Stage){lastStage=p.Stage;Console.WriteLine("Live companion stage: "+p.Stage);}}});
                var native=new WpfOfficialNativeValidator();
                using(var cancel=new CancellationTokenSource(TimeSpan.FromMinutes(10)))
                using(var transport=new GitHubUpdates())
                using(var downloads=new OfficialCompanionDownloads(root,ReleaseInfo.DisplayVersion,ReleaseVersion.Parse(ReleaseInfo.DisplayVersion).IsPreview?UpdateChannel.Preview:UpdateChannel.Stable,transport,native.ValidateAsync)) {
                    var result=downloads.EnsureAsync(id,routing,OfficialCompanionCheckMode.Force,progress,cancel.Token).GetAwaiter().GetResult();
                    var asset=result.Installation.Asset;
                    if(asset.Id!=id||asset.Tag!=tag||asset.Version!=ReleaseInfo.DisplayVersion||result.UsedOfflineFallback||!result.MetadataChecked||!result.InstalledNow)throw new InvalidDataException("Fresh live download did not install the expected published resource.");
                    var reopened=downloads.Installed(id);
                    if(reopened==null||reopened.RootDirectory!=result.Installation.RootDirectory)throw new InvalidDataException("Installed receipt could not be reopened.");
                    new WpfOfficialNativeValidator().ValidateAsync(reopened,cancel.Token).GetAwaiter().GetResult();
                    var repeat=downloads.EnsureAsync(id,routing,OfficialCompanionCheckMode.Cached,progress,cancel.Token).GetAwaiter().GetResult();
                    if(repeat.InstalledNow||repeat.Installation.RootDirectory!=reopened.RootDirectory)throw new InvalidDataException("Verified installed package was not reused.");
                    var memory=Measure(reopened);
                    File.WriteAllBytes(Path.Combine(root,"live-acceptance.json"),OfficialJson.Bytes(new{status="passed",id=id,tag=tag,version=asset.Version,size=asset.Size,sha256=asset.Sha256,mirrorRequested=routing.UseMirror,routes=routes,nativeFinalRoot=true,installedReuse=true,downloadedMemory=memory}));
                    Console.WriteLine("PASS actual published companion download, native final-root decoding and installed reuse: "+id);return 0;
                }
            }catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
    }
}
