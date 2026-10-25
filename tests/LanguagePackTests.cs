using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class LanguagePackTests
    {
        static int checks;
        static readonly string[] Codes={"zh_CN","zh_TW","en_US","ja_JP","ko_KR","fr_FR","de_DE","hi_IN","id_ID","it_IT","pt_BR","es_419","es_ES"};
        static readonly string[] Names={"简体中文","繁體中文","English","日本語","한국어","Français (France)","Deutsch (Deutschland)","हिन्दी (भारत)","Bahasa Indonesia","Italiano (Italia)","Português (Brasil)","Español (Latinoamérica)","Español (España)"};
        static readonly string[] MenuLabels={"语言","語言","Language","言語","언어","Langue","Sprache","भाषा","Bahasa","Lingua","Idioma","Idioma","Idioma"};
        static readonly string[] CompanionLabels={"桌面伙伴","桌面夥伴","Desktop Companion","デスクトップコンパニオン","데스크톱 동반자","Compagnon de bureau","Desktopbegleiter","डेस्कटॉप साथी","Teman desktop","Compagno desktop","Companhia de desktop","Compañía de escritorio","Compañero de escritorio"};
        static readonly string[] Hashes={"38436fc7fc5fec01eecdb82f8f6983839217770bd815ad821675379c3a0ece0e","afcc5ac1d6e991706b4c35cc364425b8dd479370123dd330c1816eb1940312ab","9b2e6a56d2d64948966805c7d512226f5b1f878b7ca34124266697ec7e0c1bbc","01eb901a8178d4624001b42c0c3a508778c71019a54c8a7fed5dfd2577b39f21","4cea55afca180b972f08b68c30b3ddfede7d513bf3713dfc77bc4a772ed50006","ed42ecfef2e05423010089475c441ee37227c55d801fd0da67a2cb1faf831ea9","2b21525f8a28d28b43e13eb5cd7288819e67da05d426e265a228ccb139396f7b","d96ff6b75cc6ea25ce334da0c4621a4e25c1f85e676f70a474340283859b040f","3dd7ca358f7ae7c5a18a000d797b42a5420633d7d2de9f700294458276109e76","af377edd7ba72c865216a7aeca4ba6fa7014be336e3a4a782ec22cfdb086427e","756b45904f942f8465dc01ec0e9af81eece70b6588b16b484f136ec808073236","8a26719f7375f97a0b40b80a18ff6511e8fc1f9390aed9883772d874a52c3973","89b5f532c0f2a1466096798d0f15e9c1fa46dd055558b3890a6c6469582fe14d"};
        static void Check(bool yes,string why){if(!yes)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        static object Call(Type type,string method,params object[] args)
        {
            var target=type.GetMethod(method,BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);if(target==null)throw new Exception(type.Name+"."+method+" is missing");
            try{return target.Invoke(null,args);}catch(TargetInvocationException error){System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
        }
        static TextCatalog Catalog(string code){return (TextCatalog)Call(typeof(TextResources),"CatalogFor",code);}
        static PropertyInfo LanguageSetting {get{var property=typeof(PetSettings).GetProperty("UiLanguage");if(property==null)throw new Exception("Persisted UiLanguage setting is missing");return property;}}
        static void EmbeddedPacksMatchReviewedSupplement()
        {
            string[] canonicalKeys;
            using(var canonical=typeof(TextResources).Assembly.GetManifestResourceStream("Dafeiyu.Locales.zh_CN.json"))using(var reader=new StreamReader(canonical))
                canonicalKeys=new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.Deserialize<Dictionary<string,string>>(reader.ReadToEnd()).Keys.OrderBy(key=>key,StringComparer.Ordinal).ToArray();
            for(int i=0;i<Codes.Length;i++)using(var stream=typeof(TextResources).Assembly.GetManifestResourceStream("Dafeiyu.Locales."+Codes[i]+".json"))
            {
                Check(stream!=null,"embedded language exists: "+Codes[i]);
                if(Hashes[i]!=null)using(var hash=SHA256.Create())Check(BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant()==Hashes[i],"embedded bytes match versioned translation provenance: "+Codes[i]);
                stream.Position=0;using(var reader=new StreamReader(stream))
                {
                    var entries=new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.Deserialize<Dictionary<string,string>>(reader.ReadToEnd());
                    Check(entries.Keys.OrderBy(key=>key,StringComparer.Ordinal).SequenceEqual(canonicalKeys),"raw locale has every canonical key without relying on fallback: "+Codes[i]);
                }
            }
        }
        static void ExplicitCatalogReadsPreserveRequestedLocale()
        {
            string global=TextResources.CurrentLanguage,requested=global=="en_US"?"ja_JP":"en_US";var catalog=Catalog(requested);
            var get=typeof(TextResources).GetMethod("Get",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(string),typeof(TextCatalog)},null);
            var format=typeof(TextResources).GetMethod("FormatForCatalog",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(TextCatalog),typeof(string),typeof(object[])},null);
            Check(get!=null&&format!=null,"literal resource calls support an explicit typed catalog");
            Check((string)get.Invoke(null,new object[]{"relationship.stage.firstMeeting",catalog})==catalog.Get("relationship.stage.firstMeeting"),"typed Get reads requested catalog");
            string stage=RelationshipContent.StageName("first_meeting");
            Check((string)format.Invoke(null,new object[]{catalog,"relationship.aiContext",new object[]{stage}})==catalog.Format("relationship.aiContext",stage),"typed Format reads requested catalog");
            Check(RelationshipContent.Welcome("first_meeting",null,requested)==catalog.Get("relationship.welcome.firstMeeting")&&RelationshipContent.Interaction("best_friend",null,requested)==catalog.Get("relationship.interaction.bestFriend"),"generic lines preserve explicit locale distinct from process locale");
            Check(RelationshipContent.StageContext("first_meeting",requested)==catalog.Format("relationship.aiContext",catalog.Get("relationship.stage.firstMeeting")),"stage context uses explicit title and template catalog");
            Check(TextResources.CurrentLanguage==global,"explicit resource reads do not mutate frozen process language");
            Check(TextResources.Format(null,"relationship.home.today",12)==String.Format(null,TextResources.Get("relationship.home.today"),12),"original null-provider overload remains unambiguous and preserves formatting");
        }
        static void IndependentFeatureTextAndFormats()
        {
            using(var stream=typeof(LanguagePackTests).Assembly.GetManifestResourceStream("Dafeiyu.Tests.FeatureLocalizationBaseline"))
            {
                Check(stream!=null,"independent feature text/format baseline is embedded");
                string json;using(var reader=new StreamReader(stream))json=reader.ReadToEnd();
                var fixture=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(json);
                var rows=((object[])fixture["resources"]).Cast<Dictionary<string,object>>().ToArray();
                foreach(var row in rows)Check(Catalog("zh_CN").Get((string)row["key"])==(string)row["expected"],"independent feature text "+row["key"]);
                foreach(string code in Codes){foreach(var row in rows.Where(x=>(bool)x["format"]))Catalog(code).Format((string)row["key"],Enumerable.Range(0,(int)row["argument_count"]).Select(i=>(object)3).ToArray());Check(true,"all independent new feature formats render: "+code);}
            }
        }
        static void ChoicesAndSavedSelection()
        {
            var type=typeof(TextResources).Assembly.GetType("Dafeiyu.UiLanguages");if(type==null)throw new Exception("Language choice policy is missing");
            var choices=((IEnumerable)type.GetProperty("Choices").GetValue(null,null)).Cast<object>().Where(choice=>(string)choice.GetType().GetProperty("Code").GetValue(choice,null)!="system").ToArray();
            Check(choices.Select(choice=>(string)choice.GetType().GetProperty("Code").GetValue(choice,null)).SequenceEqual(Codes),"thirteen language options have the requested stable order");
            Check(choices.Select(choice=>(string)choice.GetType().GetProperty("NativeName").GetValue(choice,null)).SequenceEqual(Names),"language choices use exactly the requested native names");
            Check((string)Call(type,"Normalize","en-US")=="en_US"&&(string)Call(type,"Normalize","../../other.json")=="system","legacy locale aliases normalize and unknown codes follow the system without loading paths");
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"language-pack-fixtures",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                string path=Path.Combine(root,"settings.json");var store=new SettingsStore(path);var settings=new PetSettings();Check((string)LanguageSetting.GetValue(settings,null)=="system","new settings follow the system UI language");
                LanguageSetting.SetValue(settings,"en_US",null);store.Save(settings);Check((string)LanguageSetting.GetValue(store.Load(),null)=="en_US","selected language survives settings save and reload");
                Check((string)Call(type,"ReadSavedLanguage",path)=="en_US","standalone updater reads only the saved UI language");
                var copy=settings.Copy();Check((string)LanguageSetting.GetValue(copy,null)=="en_US","settings snapshots retain the selected language");
                store.Save(copy);Check((string)LanguageSetting.GetValue(store.Load(),null)=="en_US","saving a shutdown/settings snapshot cannot reset the selected language");
                LanguageSetting.SetValue(copy,"ja_JP",null);Check((string)LanguageSetting.GetValue(settings,null)=="en_US","language selection copies independently with settings");
                var saveLanguage=typeof(SettingsStore).GetMethod("SaveUiLanguage");if(saveLanguage==null)throw new Exception("Atomic language selection persistence is missing");
                File.Delete(path);Directory.CreateDirectory(path);bool failed=false;
                try{saveLanguage.Invoke(store,new object[]{settings,"ja_JP"});}catch(TargetInvocationException error){failed=error.InnerException is IOException||error.InnerException is UnauthorizedAccessException;}
                Check(failed&&(string)LanguageSetting.GetValue(settings,null)=="en_US","failed language persistence restores the previously selected language");Directory.Delete(path);
                File.WriteAllText(path,"{}");Check((string)LanguageSetting.GetValue(store.Load(),null)=="system","legacy settings without a language field follow the system");
                File.WriteAllText(path,"{\"UiLanguage\":\"../../other.json\"}");Check((string)LanguageSetting.GetValue(store.Load(),null)=="system","invalid persisted language codes normalize to the system default");
                Check((string)Call(type,"ReadSavedLanguage",path)=="system","standalone updater rejects path-like language identifiers");
            }
            finally{DeleteFixture(root);}
        }
        static void MissingKeysAndFormatsUseTheCanonicalFallback()
        {
            var assembly=typeof(LanguagePackTests).Assembly;
            using(var baselineStream=assembly.GetManifestResourceStream("Dafeiyu.Tests.LocalizationBaseline"))
            {
                if(baselineStream==null)throw new Exception("Independent localization format baseline is missing");
                string json;using(var reader=new StreamReader(baselineStream))json=reader.ReadToEnd();
                var baseline=(Dictionary<string,object>)new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.DeserializeObject(json);
                var formats=((object[])baseline["resources"]).Cast<Dictionary<string,object>>().Where(item=>(bool)item["format"]).ToArray();
                foreach(string code in Codes)
                {
                    var catalog=Catalog(code);foreach(var item in formats)
                    {
                        string key=(string)item["key"];var args=Enumerable.Range(0,(int)item["argument_count"]).Select(index=>(object)("value"+index)).ToArray();
                        try{catalog.Format(CultureInfo.GetCultureInfo(code.Replace('_','-')),key,args);}catch(FormatException error){throw new Exception(code+" invalid format at "+key,error);}
                    }
                    Check(true,"all independent format templates remain valid: "+code);
                    Check(code=="zh_CN"||catalog.Get("agent.jobs.error.jobNotFound")!=Catalog("zh_CN").Get("agent.jobs.error.jobNotFound"),"new Agent error text is translated: "+code);
                    int localeIndex=Array.IndexOf(Codes,code);
                    Check(catalog.Get("menus.language")==MenuLabels[localeIndex],"language-entry label is translated: "+code);
                    Check(catalog.Get("chatAppShell.openProfileMenu.companionLabel")==CompanionLabels[localeIndex],"avatar companion subtitle is translated: "+code);
                }
            }
        }
        static void TranslatedErrorsRetainTheirFailureMeaning()
        {
            string generic=Catalog("zh_CN").Format("agentWorkspace.executeAsync.taskFromResult","");
            using(var stream=typeof(TextResources).Assembly.GetManifestResourceStream("Dafeiyu.Locales.zh_CN.json"))using(var reader=new StreamReader(stream))
            {
                var source=(Dictionary<string,object>)new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.DeserializeObject(reader.ReadToEnd());var keys=source.Where(item=>((string)item.Value).StartsWith(generic,StringComparison.Ordinal)).Select(item=>item.Key).ToArray();
                foreach(string code in Codes){var catalog=Catalog(code);foreach(string key in keys)if(!(bool)Call(typeof(TextResources),"IsToolError",catalog.Get(key)))throw new Exception(code+" translated error was treated as success: "+key);Check(true,"legacy error detection survives translation and colon variants: "+code);}
                Check(!(bool)Call(typeof(TextResources),"IsToolError","normal output")&&!(bool)Call(typeof(TextResources),"IsToolError","{\"ok\":true}"),"ordinary output is not misclassified by translated error detection");
            }
        }
        static void SavedSelectionAppliesInANewProcess()
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"language-pack-fixtures",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                string path=Path.Combine(root,"settings.json");var store=new SettingsStore(path);
                foreach(string code in Codes)
                {
                    var settings=new PetSettings();LanguageSetting.SetValue(settings,code,null);store.Save(settings);
                    string assembly=typeof(LanguagePackTests).Assembly.Location;
                    var start=new ProcessStartInfo{UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=new UTF8Encoding(false),StandardErrorEncoding=new UTF8Encoding(false)};
                    if(assembly.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)){using(var current=Process.GetCurrentProcess())start.FileName=current.MainModule.FileName;start.Arguments="\""+assembly+"\" ";}else start.FileName=assembly;
                    start.Arguments+="--language-startup-fixture "+code+" \""+path+"\"";
                    using(var process=Process.Start(start))
                    {
                        try
                        {
                            if(!process.WaitForExit(10000))throw new Exception("Language startup fixture exceeded its deadline");string output=process.StandardOutput.ReadToEnd(),error=process.StandardError.ReadToEnd();
                            if(process.ExitCode!=0)throw new Exception("Language startup fixture failed: "+code+" "+error+" "+output);
                            var result=(Dictionary<string,object>)new JavaScriptSerializer().DeserializeObject(output);
                            Check((string)result["language"]==code&&(string)result["cancel"]==Catalog(code).Get("common.cancel")&&((object[])result["native_names"]).Cast<string>().SequenceEqual(Names),"new process restores the selected language and native option names before looking up UI text: "+code);
                            Check((string)result["shortcut_quick"]==Catalog(code).Get("shortcutDefinitions.all.allShortcutDefinition"),"shortcut titles initialized while loading settings use the final startup language: "+code);
                        }
                        finally{if(!process.HasExited){process.Kill();process.WaitForExit(5000);}}
                    }
                }
            }
            finally{DeleteFixture(root);}
        }
        public static int RunStartupFixture(string code,string path)
        {
            try
            {
                string full=Path.GetFullPath(path),parent=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"language-pack-fixtures"))+Path.DirectorySeparatorChar;
                var comparison=Environment.OSVersion.Platform==PlatformID.Win32NT?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
                if(!full.StartsWith(parent,comparison)||Path.GetFileName(full)!="settings.json"||!File.Exists(full)||!Codes.Contains(code))return 2;AgentInstructions.RejectLinks(full);
                var settings=new SettingsStore(full).Load();Call(typeof(TextResources),"ConfigureStartupLanguage",LanguageSetting.GetValue(settings,null));
                string language=(string)typeof(TextResources).GetProperty("CurrentLanguage").GetValue(null,null);
                if(language!=code)return 1;
                var shortcuts=typeof(TextResources).Assembly.GetType("Dafeiyu.ShortcutCatalog",true);var first=((Array)shortcuts.GetField("All",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null)).GetValue(0);
                var title=first.GetType().GetProperty("Title",BindingFlags.Instance|BindingFlags.NonPublic);string quick=title!=null?(string)title.GetValue(first,null):(string)first.GetType().GetField("Title",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(first);
                Console.OutputEncoding=new UTF8Encoding(false);Console.Write(new JavaScriptSerializer().Serialize(new{language=language,cancel=TextResources.Get("common.cancel"),shortcut_quick=quick,native_names=UiLanguages.Choices.Where(choice=>choice.Code!=UiLanguages.FollowSystem).Select(choice=>choice.NativeName).ToArray()}));return 0;
            }
            catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
        static void DeleteFixture(string path)
        {
            string parent=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"language-pack-fixtures"))+Path.DirectorySeparatorChar;
            string full=Path.GetFullPath(path);if(!full.StartsWith(parent,StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected language fixture path");if(Directory.Exists(full)){AgentInstructions.RejectLinks(full);Directory.Delete(full,true);}
        }
        public static int Main(string[] args)
        {return args.Length==0?RunAll():args.Length==3&&args[0]=="--language-startup-fixture"?RunStartupFixture(args[1],args[2]):2;}
        public static int RunAll()
        {
            checks=0;int failures=0;foreach(var test in new Action[]{ExplicitCatalogReadsPreserveRequestedLocale,IndependentFeatureTextAndFormats,EmbeddedPacksMatchReviewedSupplement,ChoicesAndSavedSelection,MissingKeysAndFormatsUseTheCanonicalFallback,TranslatedErrorsRetainTheirFailureMeaning,SavedSelectionAppliesInANewProcess})try{test();}catch(Exception error){failures++;Console.WriteLine("FAIL "+test.Method.Name+": "+error);}
            Console.WriteLine("LanguagePackTests: "+checks+" passed");return failures==0?0:1;
        }
    }
}
