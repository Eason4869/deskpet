using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Dafeiyu.Tests
{
    // Runs against the delivered artwork, separately from synthetic fault fixtures.
    public static class HolidayProductionSmoke
    {
        static int checks;
        static readonly BindingFlags PrivateInstance=BindingFlags.Instance|BindingFlags.NonPublic;
        static void Check(bool condition,string description)
        {if(!condition)throw new Exception(description);checks++;}
        static void Collect(){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
        static object Memory(string stage)
        {
            Collect();using(var process=Process.GetCurrentProcess()) {
                process.Refresh();return new{stage=stage,managedBytes=GC.GetTotalMemory(false),privateBytes=process.PrivateMemorySize64,workingSetBytes=process.WorkingSet64};
            }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        static List<WeakReference> VisitAll(HolidayVisualAssets assets,HolidayCatalog catalog,List<object> samples,int round)
        {
            var references=new List<WeakReference>();
            foreach(var outfit in catalog.Outfits) {
                assets.Clear();
                foreach(var definition in outfit.Clips.Concat(new[]{outfit.Gaze,outfit.EasterEgg})) {
                    var clip=assets.Get(definition);references.Add(new WeakReference(clip));
                    Check(clip.Count==definition.FrameCount && clip.Width==480 && clip.Height==520,"delivered clip geometry: "+definition.Path);
                    Check(clip.Loop==definition.Loop && Math.Abs(clip.Duration-definition.FrameCount/definition.Fps)<1e-8,"delivered clip timing: "+definition.Path);
                    for(int frame=0;frame<clip.Count;frame++) {
                        var source=new FormatConvertedBitmap(clip.Frame(frame),PixelFormats.Pbgra32,null,0);
                        var pixels=new byte[480*520*4];source.CopyPixels(pixels,480*4,0);
                        bool opaque=false,transparent=false,matching=true;
                        for(int y=0;y<520;y+=8)for(int x=0;x<480;x+=8) {
                            byte alpha=pixels[(y*480+x)*4+3];
                            matching&=alpha==clip.AlphaAt(frame,x,y);
                            opaque|=alpha>16;transparent|=alpha==0;
                        }
                        Check(matching,"display and hit mask diverged: "+definition.Path+"/"+frame);
                        Check(opaque && transparent,"delivered frame needs a character and transparent margin: "+definition.Path+"/"+frame);
                    }
                }
            }
            samples.Add(Memory("all-actions-loaded-round-"+round));assets.Clear();return references;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void WarmIdle(HolidayVisualAssets assets,HolidayCatalog catalog)
        {assets.Get(catalog.Outfits[0].FindClip("idle","default"));}
        static void Pump(double seconds)
        {
            var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(seconds)};
            timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
        }
        static object Field(PetWindow pet,string name){return typeof(PetWindow).GetField(name,PrivateInstance).GetValue(pet);}
        static void Render(PetWindow pet){typeof(PetWindow).GetMethod("UpdateFrame",PrivateInstance).Invoke(pet,new object[]{0d});}
        sealed class ZeroRandom:Random {public override double NextDouble(){return 0;}}
        static void VerifyLifecycle(HolidayCatalog catalog,string root)
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};PetWindow pet=null;
            try {
                var settings=new PetSettings{SkinId="e-bao",FollowCursor=false,Wander=false,GreetingEnabled=false,AutoUpdate=false,SaveChatHistory=false};
                foreach(string key in settings.Hotkeys.Keys.ToArray())settings.Hotkeys[key]="";
                pet=new PetWindow(null,settings,new SettingsStore(Path.Combine(root,"settings.json")),null);
                var stateStore=new HolidayStateStore(Path.Combine(root,"holiday-state.json"));
                var controller=new HolidayController(new HolidayCalendar(),catalog,stateStore);
                DateTime date=new DateTime(2026,10,30,12,0,0);pet.HolidayLocalTime=()=>date;pet.InitializeHoliday(controller);
                typeof(PetWindow).GetField("random",PrivateInstance).SetValue(pet,new ZeroRandom());
                pet.Show();Pump(.15);
                foreach(var outfit in catalog.Outfits) {
                    settings.QuietMode=true;date=new DateTime(2026,10,30,12,0,0);
                    var skin=pet.Catalog.Skins.Single(s=>s.BuiltinCharacterId==outfit.CharacterId);
                    Check(pet.SelectSkin(skin),"select real builtin character: "+outfit.CharacterId);
                    pet.PlayAction("idle",0);pet.RefreshHoliday();
                    Check(controller.OwnedOutfits.Count==0,"each character starts with its own empty collection");
                    string persona=pet.Skin.Id,prompt=pet.Settings.SystemPrompt;
                    date=new DateTime(2026,10,31,12,0,0);pet.RefreshHoliday();Render(pet);
                    Check(controller.EffectiveOutfit==outfit && Field(pet,"holidayCommitted")==outfit,"actual event costume commits: "+outfit.CharacterId);
                    Check(controller.OwnedOutfits.Count==0,"temporary event wear never grants ownership");
                    var shown=(MotionClip)Field(pet,"shownMotion");int sample=(int)Field(pet,"shownSample");
                    Check(shown!=null && Object.ReferenceEquals(((Image)pet.Content).Source,shown.Frame(sample)),"actual pet publishes matching costume and alpha frame");
                    settings.QuietMode=false;controller.LeaveHover();pet.RefreshHoliday();
                    Check((bool)typeof(PetWindow).GetMethod("TryHolidayHover",PrivateInstance).Invoke(pet,null),"real outfit can begin through hover gate: "+outfit.CharacterId);
                    Pump(2.8);
                    Check(!pet.HolidayPlaying && controller.WardrobeVisible && controller.OwnedOutfits.Count==1,"complete rendered egg unlocks current real outfit");
                    var saved=stateStore.Load();
                    Check(saved.WardrobeDiscovered && saved.ForCharacter(outfit.CharacterId).UnlockedOutfits.Contains(outfit.OutfitId),"actual unlock and discovery persist together");
                    date=new DateTime(2026,11,1,12,0,0);settings.QuietMode=true;pet.RefreshHoliday();
                    Check(controller.EffectiveOutfit==null,"event end restores original without a preference");
                    Check(controller.SelectOwnedOutfit(outfit.OutfitId),"owned costume can be selected after the event");pet.RefreshHoliday();Render(pet);
                    Check(controller.EffectiveOutfit==outfit && Field(pet,"holidayCommitted")==outfit,"owned selection commits after event");
                    Check(pet.Skin.Id==persona && pet.Settings.SystemPrompt==prompt,"wearing and unlocking preserve persona and prompt");
                    Check(stateStore.Load().ForCharacter(outfit.CharacterId).PreferredOutfitId==outfit.OutfitId,"each character preference is durable");
                    Console.WriteLine("PASS production lifecycle "+outfit.CharacterId);
                }
            }finally {if(pet!=null)pet.Close();Pump(.15);app.Shutdown();}
        }
        [STAThread]public static int Main()
        {
            try {
                var samples=new List<object>{Memory("before-catalog")};var catalog=new HolidayCatalog();
                Check(catalog.Outfits.Count==5 && catalog.Outfits.All(o=>o.Ready),"five approved production outfits must be embedded and Ready");
                samples.Add(Memory("before-first-outfit"));var assets=new HolidayVisualAssets(null);
                WarmIdle(assets,catalog);samples.Add(Memory("first-outfit-idle"));
                for(int round=1;round<=3;round++) {
                    var retired=VisitAll(assets,catalog,samples,round);Collect();
                    Check(retired.All(item=>!item.IsAlive),"switching away releases all visited costume clips, round "+round);
                    samples.Add(Memory("all-actions-and-five-roles-round-"+round));
                }
                string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"holiday-production-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root,"memory.json"),new JavaScriptSerializer().Serialize(samples));
                Console.WriteLine("Production artwork memory observations: "+Path.Combine(root,"memory.json"));
                VerifyLifecycle(catalog,root);
                Console.WriteLine("Holiday production smoke: "+checks+" checks passed; all five real outfits, three complete decode/cache rounds.");return 0;
            }catch(Exception error){Console.Error.WriteLine("FAIL production holiday: "+error);return 1;}
        }
    }
}
