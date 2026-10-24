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
        static readonly string[] Hashes={"017e5ccc65b7ebae33884acd63357fbce9c01be0bf57873c6d972190f0f670e6","6b02ce15644572d43868b358d874ea7850b19e8abf02bfc9a09f79f8075be73f","7832aad1ecde9c228f3d78979e33b822143033e0e428b02b59e2f71f3d3c3d6f","5d7b21941f1bc94a2dcf2b6b1dee5e503d654fe5dc76056f946e6aa4937721c4","2e8f55dd5d469e649c750cde76b6df62fbef6c23c6f7c98cc84e77c44a8d0640","196199b00cbc4e13bb0c251f62194665817dcac578ae1a0c056d886182701512","fe0834b727bb410bcc325d60f94165a799157b09c501ea8a98b11ec2d2198d01","0c693f185b67bf384fa1a628dd48c93ee8232ed7bf8a50165b9d172d11c039e2","a536750a649dae5df94963444a87b658c10e024ccd02507eeac1c768c884b572","dd9bfcbda27eefbcd050b8fde05951e7bf04dd3c32e2ae8365299b62739febf0","42dac05f5dc25149de4aef21eb5dfe0066525cde333b692398ba6710efa3e794","df20645d6722eae3f4f8b6ed9cb171ff73d0e5fea42926b17ab4297bdb5bd37b","3166a19e42b95948c585f037b6a9a2cf9eebb1ac1ca50ba5d951beabc1559e3a"};
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
                if(Hashes[i]!=null)using(var hash=SHA256.Create())Check(BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant()==Hashes[i],"embedded bytes match reviewed translation supplement: "+Codes[i]);
                stream.Position=0;using(var reader=new StreamReader(stream))
                {
                    var entries=new JavaScriptSerializer{MaxJsonLength=4*1024*1024}.Deserialize<Dictionary<string,string>>(reader.ReadToEnd());
                    Check(entries.Keys.OrderBy(key=>key,StringComparer.Ordinal).SequenceEqual(canonicalKeys),"raw locale has every canonical key without relying on fallback: "+Codes[i]);
                }
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
            checks=0;int failures=0;foreach(var test in new Action[]{EmbeddedPacksMatchReviewedSupplement,ChoicesAndSavedSelection,MissingKeysAndFormatsUseTheCanonicalFallback,TranslatedErrorsRetainTheirFailureMeaning,SavedSelectionAppliesInANewProcess})try{test();}catch(Exception error){failures++;Console.WriteLine("FAIL "+test.Method.Name+": "+error);}
            Console.WriteLine("LanguagePackTests: "+checks+" passed");return failures==0?0:1;
        }
    }
}
