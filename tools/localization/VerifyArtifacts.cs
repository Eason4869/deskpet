using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using System.Threading;

internal static class VerifyArtifacts
{
    // Runs under Framework for WPF/helper, and .NET 10 for the actual published Core.
    // Accepts the canonical SHA256 or an explicit locale/hash manifest; reads no external locale at runtime.
    static int Main(string[] args)
    {
        try
        {
            if(args.Length==2&&args[0]=="--onboarding-titles")
            {
                var source=Assembly.LoadFrom(Path.GetFullPath(args[1]));
                var lessons=source.GetType("Dafeiyu.OnboardingLessons",true).GetProperty("All",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
                var titles=new List<string>();
                foreach(object lesson in (Array)lessons.GetValue(null,null))titles.Add((string)lesson.GetType().GetField("Title").GetValue(lesson));
                Console.Write(Convert.ToBase64String(Encoding.UTF8.GetBytes(String.Join("\n",titles.ToArray()))));
                return 0;
            }
            if(args.Length==3&&args[0]=="--language-lookup")
            {
                if(!Array.Exists(Codes,code=>code==args[1]))throw new ArgumentException("Unknown locale probe.");
                Probe(args[2],args[1],args[1]);return 0;
            }
            if(args.Length==4&&args[0]=="--system-language-lookup")
            {
                if(!Array.Exists(Codes,code=>code==args[2]))throw new ArgumentException("Unknown expected system locale.");
                Thread.CurrentThread.CurrentUICulture=CultureInfo.GetCultureInfo(args[1]);
                Probe(args[3],"system",args[2]);return 0;
            }
            if(args.Length<2)throw new ArgumentException("Expected catalog SHA256 and assembly paths.");
            var expected=ParseCatalogs(args[0]);
            foreach(string path in args.Length==2?new[]{args[1]}:Slice(args))
            {
                var assembly=Assembly.LoadFrom(Path.GetFullPath(path));
                foreach(var locale in expected)using(var stream=assembly.GetManifestResourceStream("Dafeiyu.Locales."+locale.Key+".json"))
                {
                    if(stream==null)throw new InvalidDataException("Missing embedded locale "+locale.Key+": "+path);
                    using(var sha=SHA256.Create())
                        if(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant()!=locale.Value)throw new InvalidDataException("Embedded locale bytes differ "+locale.Key+": "+path);
                }
                var type=assembly.GetType("Dafeiyu.TextResources",true);
                var get=type.GetMethod("Get",new[]{typeof(string)});
                if((string)get.Invoke(null,new object[]{"common.cancel"})!="取消")throw new InvalidDataException("Standalone resource lookup failed: "+path);
                Console.WriteLine("PASS "+expected.Count+" embedded locale hashes and standalone lookup: "+Path.GetFileName(path));
            }
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    static void Probe(string path,string selected,string expected)
    {
        var source=Assembly.LoadFrom(Path.GetFullPath(path));var texts=source.GetType("Dafeiyu.TextResources",true);
        texts.GetMethod("ConfigureStartupLanguage",new[]{typeof(string)}).Invoke(null,new object[]{selected});
        if((string)texts.GetProperty("CurrentLanguage").GetValue(null,null)!=expected)throw new InvalidDataException("Locale selection did not apply.");
        string label=(string)texts.GetMethod("Get",new[]{typeof(string)}).Invoke(null,new object[]{"common.cancel"});
        Console.Write(Convert.ToBase64String(Encoding.UTF8.GetBytes(label)));
    }
    static readonly string[] Codes={"zh_CN","zh_TW","en_US","ja_JP","ko_KR","fr_FR","de_DE","hi_IN","id_ID","it_IT","pt_BR","es_419","es_ES"};
    static Dictionary<string,string> ParseCatalogs(string value)
    {
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        if(value.IndexOf('=')<0){result.Add("zh_CN",value);return result;}
        foreach(string pair in value.Split(';'))
        {
            var parts=pair.Split('=');if(parts.Length!=2||!Array.Exists(Codes,code=>code==parts[0])||parts[1].Length!=64)throw new ArgumentException("Invalid locale hash manifest.");
            foreach(char digit in parts[1])if(!Uri.IsHexDigit(digit))throw new ArgumentException("Invalid locale SHA256.");result.Add(parts[0],parts[1].ToLowerInvariant());
        }
        if(result.Count!=Codes.Length)throw new ArgumentException("All thirteen locale hashes are required.");return result;
    }
    static string[] Slice(string[] args){var paths=new string[args.Length-1];Array.Copy(args,1,paths,0,paths.Length);return paths;}
}
