using System;
using System.IO;

namespace Dafeiyu.Tests
{
    public static class OnboardingUpgradeTests
    {
        static int passed;
        public static int Main()
        {
            passed=0;
            try { Run();Console.WriteLine("Onboarding upgrade tests: "+passed+" passed");return 0; }
            catch(Exception error){Console.WriteLine("FAIL onboarding upgrade: "+error.Message);return 1;}
        }
        static void Run()
        {
            var completedCurrentSeries=new PetSettings{LastOnboardingSeries="0.10",OnboardingState="completed",OnboardingVersion=2};
            Check(!Prepare(completedCurrentSeries,"0.10.5-dev.2").ShowGuide&&completedCurrentSeries.OnboardingState=="completed","O08 preparation content does not force current-series users to repeat completed guidance");
            Check(typeof(PetSettings).GetProperty("LastOnboardingSeries")!=null,"settings expose persisted onboarding series");
            var fresh=new PetSettings();var result=Prepare(fresh,"0.5.5");
            Check(result.ShowGuide && result.Changed && Series(fresh)=="0.5" && fresh.OnboardingStep==0,"fresh installation records current series before its only automatic guide");
            Check(!Prepare(fresh,"0.5.5").ShowGuide,"same process cannot automatically open a second first-run guide");
            foreach(string state in new[]{"new","deferred","completed","skipped"}) {
                fresh.OnboardingState=state;fresh.OnboardingStep=7;
                Check(!Prepare(fresh,"0.5.11").ShowGuide && fresh.OnboardingStep==7,"same-series restart preserves guide state after "+state);
            }
            string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"onboarding-series-tests",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);var path=Path.Combine(folder,"settings.json");
            File.WriteAllText(path,"{\"OnboardingState\":\"completed\",\"OnboardingStep\":8,\"OnboardingVersion\":1}");
            var store=new SettingsStore(path);var old=store.Load();
            Check(Series(old)=="0.5","legacy config without field migrates to baseline 0.5");
            result=Prepare(old,"0.5.11");
            Check(!result.ShowGuide && !result.Changed && old.OnboardingStep==8,"0.5.3 to 0.5.11 never repeats onboarding");
            result=Prepare(old,"0.6.0");
            Check(result.ShowGuide && result.Changed && Series(old)=="0.6" && old.OnboardingStep==0 && old.OnboardingState=="new","legacy config first entering 0.6 resets guide from step zero");
            store.Save(old);var persisted=store.Load();
            Check(Series(persisted)=="0.6" && !Prepare(persisted,"0.6.0").ShowGuide,"series is persisted before any guide completion or close");
            result=Prepare(persisted,"0.6.1");
            Check(result.ShowGuide && result.Changed && persisted.OnboardingStep==OnboardingUpgrade.WorkGuideStep && persisted.OnboardingVersion==OnboardingUpgrade.WorkGuideVersion,"0.6.0 upgrade opens the new feature lessons once without repeating setup");
            store.Save(persisted);persisted=store.Load();
            Check(!Prepare(persisted,"0.6.1").ShowGuide && persisted.OnboardingStep==14,"feature revision is persisted before guide completion or close");
            foreach(string state in new[]{"new","deferred","completed","skipped"}) {
                persisted.OnboardingState=state;persisted.OnboardingStep=15;
                Check(!Prepare(persisted,"0.6.2").ShowGuide && persisted.OnboardingStep==15,"feature guidance does not repeat after "+state);
            }
            Check(!Prepare(persisted,"0.6.0").ShowGuide && !Prepare(persisted,"0.6.1").ShowGuide,"patch downgrade and re-upgrade retain the claimed feature revision");
            var currentFresh=new PetSettings();result=Prepare(currentFresh,"0.6.1");
            Check(result.ShowGuide && currentFresh.OnboardingStep==0 && currentFresh.OnboardingVersion==2 && !Prepare(currentFresh,"0.6.2").ShowGuide,"fresh 0.6.1 installation sees the full guide only once");
            var newerSeries=Recorded("0.7");Check(!Prepare(newerSeries,"0.6.1").ShowGuide && Series(newerSeries)=="0.7","feature revision cannot reopen a guide after a series downgrade");
            Check(Series(persisted.Copy())=="0.6","settings copy preserves last onboarding series");
            foreach(var pair in new[]{new[]{"0.8","1.0.0"},new[]{"0.9","0.10.0"},new[]{"2.8","10.0.0"},new[]{"0.5","8.7.4"}}) {
                var settings=Recorded(pair[0]);result=Prepare(settings,pair[1]);
                Check(result.ShowGuide && result.Changed && settings.OnboardingStep==0,"numeric series advance triggers across "+pair[0]+" to "+pair[1]);
            }
            var previewFresh=new PetSettings();result=Prepare(previewFresh,"0.10.0-dev.1");
            Check(result.ShowGuide && Series(previewFresh)=="0.10" && previewFresh.OnboardingVersion==OnboardingUpgrade.WorkGuideVersion,"fresh preview installation records its numeric series and current lessons");
            var preview=Recorded("0.9");result=Prepare(preview,"0.10.0-dev.1");
            Check(result.ShowGuide && Series(preview)=="0.10" && preview.OnboardingStep==0,"stable to higher preview series triggers the upgrade guide");
            preview.OnboardingState="deferred";preview.OnboardingStep=6;
            Check(!Prepare(preview,"0.10.0-dev.2").ShowGuide && !Prepare(preview,"0.10.0").ShowGuide && preview.OnboardingStep==6,"preview revisions and same-series stable release preserve the claimed guide");
            Check(!Prepare(preview,"0.9.9-dev.1").ShowGuide && Series(preview)=="0.10","lower preview retains the highest claimed series");
            var invalidPreview=Recorded("0.9");result=Prepare(invalidPreview,"0.10.0-dev.01");
            Check(!result.ShowGuide && !result.Changed && Series(invalidPreview)=="0.9","invalid preview cannot consume an onboarding series");
            var downgrade=Recorded("1.3");
            Check(!Prepare(downgrade,"0.8.3").ShowGuide && Series(downgrade)=="1.3","downgrading keeps the highest triggered series");
            Check(!Prepare(downgrade,"1.3.9").ShowGuide && Series(downgrade)=="1.3","returning to a previously triggered series stays silent");
            Check(Prepare(downgrade,"1.4.0").ShowGuide && Series(downgrade)=="1.4","a newer series still triggers after downgrade and re-upgrade");
            foreach(string invalid in new[]{null,"","bad","-1.2",new string('9',10000)}) {
                var damaged=Recorded(invalid);result=Prepare(damaged,"0.5.5");
                Check(!result.ShowGuide && result.Changed && Series(damaged)=="0.5","damaged recorded series recovers to migration baseline");
            }
            foreach(string invalid in new[]{null,"","bad","1","1.-2","1.2.3.4.5",new string('9',10000)+".0","2147483648.0"}) {
                var settings=Recorded("0.5");result=Prepare(settings,invalid);
                Check(!result.ShowGuide && !result.Changed && Series(settings)=="0.5","invalid running version cannot reset guide or consume the series");
            }
            var canonical=Recorded("00.05");result=Prepare(canonical,"0.5.99");
            Check(!result.ShowGuide && result.Changed && Series(canonical)=="0.5","equivalent numeric series canonicalizes without retriggering");
            var maximum=Recorded("2147483647.2147483647");result=Prepare(maximum,"2147483647.2147483647.1");
            Check(!result.ShowGuide && !result.Changed,"maximum supported numeric series does not overflow");
            var manual=Recorded("0.6");manual.OnboardingVersion=OnboardingUpgrade.WorkGuideVersion;manual.OnboardingState="deferred";manual.OnboardingStep=4;
            Check(!Prepare(manual,"0.6.2").ShowGuide && manual.OnboardingState=="deferred" && manual.OnboardingStep==4,"manual guide state remains available without automatic reopening");
        }
        static PetSettings Recorded(string series){return new PetSettings{OnboardingState="completed",OnboardingStep=9,LastOnboardingSeries=series};}
        static string Series(PetSettings settings){return settings.LastOnboardingSeries;}
        static OnboardingUpgradeResult Prepare(PetSettings settings,string version){return OnboardingUpgrade.Prepare(settings,version);}
        static void Check(bool condition,string message){if(!condition)throw new Exception(message);passed++;Console.WriteLine("PASS "+message);}
    }
}
