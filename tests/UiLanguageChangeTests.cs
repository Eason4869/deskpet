using System;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class UiLanguageChangeTests
    {
        static int checks;
        static void Check(bool yes,string why){if(!yes)throw new Exception(why);checks++;}
        public static int Main()
        {
            try{
                string root=Path.Combine(Path.GetFullPath("work/live-language"),"settings-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                string path=Path.Combine(root,"settings.json");var store=new SettingsStore(path);var live=new PetSettings();
                File.WriteAllText(path,"{\"UiLanguage\":\"zh_CN\",\"SystemPrompt\":\"saved\",\"FutureField\":{\"retain\":true}}");live.UiLanguage="zh_CN";live.SystemPrompt="unsaved draft";TextResources.ConfigureStartupLanguage("zh_CN");
                UiLanguageChange.Apply(store,live,"en_US");var json=new JavaScriptSerializer();var disk=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));
                Check(TextResources.CurrentLanguage=="en_US"&&live.UiLanguage=="en_US","saved language becomes effective");Check(live.SystemPrompt=="unsaved draft"&&(string)disk["SystemPrompt"]=="saved"&&disk.ContainsKey("FutureField"),"only language saved, drafts and unknown fields retained");
                string previous=File.ReadAllText(path);using(var locked=new FileStream(path+".tmp",FileMode.Create,FileAccess.ReadWrite,FileShare.None)){bool failed=false;try{UiLanguageChange.Apply(store,live,"ja_JP");}catch(IOException){failed=true;}Check(failed&&TextResources.CurrentLanguage=="en_US"&&live.UiLanguage=="en_US"&&File.ReadAllText(path)==previous,"failed write changes no state");}
                File.WriteAllText(path,"broken");bool bad=false;try{UiLanguageChange.Apply(store,live,"ja_JP");}catch(InvalidDataException){bad=true;}Check(bad&&File.ReadAllText(path)=="broken"&&TextResources.CurrentLanguage=="en_US","strict read never overwrites corrupt settings");
                File.WriteAllText(path,previous);UiLanguageChange.ApplyAndContinueGuide(store,live,"ja_JP");disk=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));Check(live.OnboardingLessonId=="welcome"&&(string)disk["OnboardingLessonId"]=="welcome"&&TextResources.CurrentLanguage=="ja_JP","language and guide progress commit together");
                var stale=SettingsStore.Snapshot(live);stale.UiLanguage="zh_CN";store.Save(stale);Check(store.Load().UiLanguage=="ja_JP","stale normal snapshot cannot undo committed language");
                File.Delete(path);File.Delete(path+".tmp");Directory.Delete(root);Console.WriteLine("UiLanguageChangeTests: "+checks+" passed");return 0;
            }catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
    }
}
