using System;
using System.Collections.Generic;
using System.Reflection;

namespace Dafeiyu.Tests
{
    public static class SpeechLanguagePolicyTests
    {
        static int checks;

        static void Check(bool condition,string message)
        {
            if(!condition)throw new Exception(message);
            checks++;
        }

        static string Select(string uiLanguage,IEnumerable<string> availableCultures)
        {
            var type=typeof(TextResources).Assembly.GetType("Dafeiyu.SpeechLanguagePolicy");
            if(type==null)throw new Exception("System voice language selection is missing");
            var method=type.GetMethod("SelectCulture",BindingFlags.Public|BindingFlags.Static);
            if(method==null)throw new Exception("System voice culture selector is missing");
            return(string)method.Invoke(null,new object[]{uiLanguage,availableCultures});
        }

        public static int Main(){return RunAll();}

        public static int RunAll()
        {
            try
            {
                checks=0;
                // A hard-coded Chinese voice or a first-family-match selector breaks these fixtures.
                var cases=new[]{
                    new[]{"zh_CN","zh-CN","zh-SG"},
                    new[]{"zh_TW","zh-TW","zh-HK"},
                    new[]{"en_US","en-US","en-GB"},
                    new[]{"ja_JP","ja-JP","ja"},
                    new[]{"ko_KR","ko-KR","ko"},
                    new[]{"fr_FR","fr-FR","fr-CA"},
                    new[]{"de_DE","de-DE","de-AT"},
                    new[]{"hi_IN","hi-IN","hi"},
                    new[]{"id_ID","id-ID","id"},
                    new[]{"it_IT","it-IT","it-CH"},
                    new[]{"pt_BR","pt-BR","pt-PT"},
                    new[]{"es_419","es-419","es-MX"},
                    new[]{"es_ES","es-ES","es-AR"}
                };
                foreach(var item in cases)
                {
                    Check(Select(item[0],new[]{"zz-ZZ",item[2],item[1]})==item[1],
                        "Exact region must outrank an earlier family voice: "+item[0]);
                    Check(Select(item[0],new[]{"zz-ZZ",item[2]})==item[2],
                        "Use an available voice from the same language family: "+item[0]);
                }
                Check(Select("  eN_uS  ",new[]{"en-GB","EN-us"})=="EN-us",
                    "Normalize case and underscores without changing the returned installed culture");
                Check(Select("fr-FR",new[]{"FR_ca","fr-BE"})=="FR_ca",
                    "Keep the first family voice when no exact region exists");
                Check(Select("fr_FR",new[]{"zh-CN","en-US"})==null,
                    "No matching language must leave the synthesizer default unchanged");
                Check(Select("de_DE",new string[0])==null,"An empty installed list must retain the default");
                Check(Select("it_IT",null)==null,"A missing installed list must retain the default");
                Check(Select(null,new[]{"en-US"})==null,"A missing effective UI language must not pick a voice");
                Check(Select(" ",new[]{"en-US"})==null,"A blank effective UI language must not pick a voice");
                Check(Select("pt_BR",new[]{null,""," ","PT_br"})=="PT_br",
                    "Skip empty voice cultures and still find a normalized exact match");
                Console.WriteLine("SpeechLanguagePolicyTests: "+checks+" passed");
                return 0;
            }
            catch(Exception error)
            {
                Console.Error.WriteLine("FAIL SpeechLanguagePolicyTests: "+error);
                return 1;
            }
        }
    }
}
