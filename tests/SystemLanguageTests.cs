using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
namespace Dafeiyu.Tests
{
    public static class SystemLanguageTests
    {
        static int checks;
        static void Check(bool yes,string why){if(!yes)throw new Exception(why);checks++;}
        static string Resolve(string selected,string culture)
        {
            var method=typeof(UiLanguages).GetMethod("ResolveForCulture",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
            if(method==null)throw new Exception("System UI language resolution is missing");
            return(string)method.Invoke(null,new object[]{selected,culture});
        }
        public static int Main(string[] args){return args.Length==4&&args[0]=="--system-language-fixture"?RunFixture(args[1],args[2],args[3]):args.Length==0?RunAll():2;}
        public static int RunAll()
        {
            try
            {
                checks=0;Check(new PetSettings().UiLanguage=="system","new settings follow the system UI language by default");
                foreach(var pair in new[]{new[]{"zh-CN","zh_CN"},new[]{"zh-SG","zh_CN"},new[]{"zh-Hant","zh_TW"},new[]{"zh-TW","zh_TW"},new[]{"zh-HK","zh_TW"},new[]{"zh-MO","zh_TW"},new[]{"en-GB","en_US"},new[]{"en-AU","en_US"},new[]{"ja-JP","ja_JP"},new[]{"ko-KR","ko_KR"},new[]{"zz-ZZ","en_US"},new[]{"","en_US"}})
                    Check(Resolve("system",pair[0])==pair[1],"system language family/region resolves: "+pair[0]);
                foreach(string culture in new[]{"fr-FR","de-DE","hi-IN","id-ID","it-IT","pt-PT","pt-BR","es-ES","es-MX","es-AR"})
                {
                    string target=culture.StartsWith("pt-")?"pt_BR":culture=="es-ES"?"es_ES":culture.StartsWith("es-")?"es_419":culture.Replace('-','_');
                    bool available=UiLanguages.Choices.Any(choice=>choice.Code==target);
                    Check(Resolve("system",culture)==(available?target:"en_US"),"system region selects an available translation or English: "+culture);
                }
                Check(Resolve("en_US","zh-TW")=="en_US"&&Resolve("zh_CN","en-GB")=="zh_CN","manual choices override the system UI language");
                Check(UiLanguages.Choices.First().Code=="system","system-following choice is first");
                foreach(var item in new[]{new[]{"system","zh-TW","zh_TW"},new[]{"system","en-GB","en_US"},new[]{"system","ja-JP","ja_JP"},new[]{"en_US","zh-CN","en_US"}})
                {
                    string assembly=typeof(SystemLanguageTests).Assembly.Location;var start=new ProcessStartInfo{UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                    if(assembly.EndsWith(".dll",StringComparison.OrdinalIgnoreCase)){using(var current=Process.GetCurrentProcess())start.FileName=current.MainModule.FileName;start.Arguments="\""+assembly+"\" ";}else start.FileName=assembly;
                    start.Arguments+="--system-language-fixture "+String.Join(" ",item);
                    using(var process=Process.Start(start))try{if(!process.WaitForExit(10000))throw new Exception("System-language startup fixture timed out");string output=process.StandardOutput.ReadToEnd(),error=process.StandardError.ReadToEnd();Check(process.ExitCode==0,"independent startup follows OS or manual preference: "+String.Join(" ",item)+" "+output+error);}finally{if(!process.HasExited){process.Kill();process.WaitForExit(5000);}}
                }
                Console.WriteLine("SystemLanguageTests: "+checks+" passed");return 0;
            }
            catch(Exception error){Console.Error.WriteLine("FAIL SystemLanguageTests: "+error);return 1;}
        }
        public static int RunFixture(string selected,string culture,string expected)
        {
            try
            {
                Thread.CurrentThread.CurrentUICulture=CultureInfo.GetCultureInfo(culture);
                string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"system-language-"+Guid.NewGuid().ToString("N")+".json");
                try
                {
                    File.WriteAllText(path,"{}");var store=new SettingsStore(path);var settings=store.Load();if(settings.UiLanguage!="system")return 1;
                    settings.UiLanguage=selected;store.Save(settings);settings=store.Load();TextResources.ConfigureStartupLanguage(settings.UiLanguage);
                    if(TextResources.CurrentLanguage!=expected||settings.UiLanguage!=selected)return 1;
                    var catalog=(TextCatalog)typeof(TextResources).GetMethod("CatalogFor",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{expected});
                    if(TextResources.Get("common.cancel")!=catalog.Get("common.cancel")||!ReplyLanguagePolicy.Apply("custom persona").Contains(catalog.Get("languages.replyInstruction")))return 1;
                    return 0;
                }
                finally{if(File.Exists(path))File.Delete(path);}
            }
            catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
    }
}
