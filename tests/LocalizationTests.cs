using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Globalization;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    public static class LocalizationTests
    {
        static int checks;
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
        static object Call(Type type,object instance,string method,Type[] signature,params object[] args)
        {try{return type.GetMethod(method,signature).Invoke(instance,args);}catch(TargetInvocationException e){throw e.InnerException;}}
        public static int Main()
        {
            checks=0;
            try
            {
                var assembly=typeof(PetSettings).Assembly;
                var catalog=assembly.GetType("Dafeiyu.TextCatalog");
                Check(catalog!=null,"shared TextCatalog must exist");
                object loaded;
                using(var stream=new MemoryStream(Encoding.UTF8.GetBytes("{\"plain\":\"中文\\n引号\\\"与 {普通}\\\\路径\",\"format\":\"数值 {0:N1} / {1} / {{保留}}\"}")))
                {loaded=Call(catalog,null,"Load",new[]{typeof(Stream)},stream);Check(stream.CanRead,"Load leaves caller stream open");}
                Func<string,string> get=key=>(string)Call(catalog,loaded,"Get",new[]{typeof(string)},key);
                Check(get("plain")=="中文\n引号\"与 {普通}\\路径","Get retains literal text and braces");
                Check(get("missing")=="[missing]" && get("PLAIN")=="[PLAIN]","missing and case-sensitive keys");
                var original=CultureInfo.CurrentCulture;
                foreach(string culture in new[]{"zh-CN","en-US","de-DE"})
                {
                    var provider=CultureInfo.GetCultureInfo(culture);
                    var actual=(string)Call(catalog,loaded,"Format",new[]{typeof(IFormatProvider),typeof(string),typeof(object[])},provider,"format",new object[]{12.5,null});
                    Check(actual==String.Format(provider,"数值 {0:N1} / {1} / {{保留}}",12.5,null),"explicit culture "+culture);
                    Check(Object.ReferenceEquals(original,CultureInfo.CurrentCulture),"no thread culture mutation");
                }
                bool failed=false;try{Call(catalog,loaded,"Format",new[]{typeof(string),typeof(object[])},"format",new object[0]);}catch(FormatException){failed=true;}
                Check(failed,"missing format arguments fail visibly");
                failed=false;try{Call(catalog,null,"Load",new[]{typeof(Stream)},new object[]{null});}catch(InvalidDataException){failed=true;}
                Check(failed,"missing embedded resource reports repair failure");
                foreach(string invalid in new[]{"[]","{broken","{\"x\":4}","{\"x\":null}","{\" \":\"value\"}"})
                {
                    failed=false;using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(invalid)))try{Call(catalog,null,"Load",new[]{typeof(Stream)},stream);}catch(InvalidDataException){failed=true;}
                    Check(failed,"reject invalid catalog "+invalid);
                }
                using(var resource=assembly.GetManifestResourceStream("Dafeiyu.Locales.zh_CN.json"))Check(resource!=null,"catalog embedded in defining assembly");
                var resources=assembly.GetType("Dafeiyu.TextResources");
                Check(resources!=null,"shared resource entry exists");
                var method=resources.GetMethod("Get",new[]{typeof(string)});
                Check((string)method.Invoke(null,new object[]{"common.cancel"})=="取消","embedded independent Chinese expectation");
                Check(resources.GetMethod("StartupMessage",new[]{typeof(string)})!=null,"startup error handler must survive unavailable resources");
                using(var fixture=typeof(LocalizationTests).Assembly.GetManifestResourceStream("Dafeiyu.Tests.LocalizationBaseline"))
                using(var reader=new StreamReader(fixture,Encoding.UTF8))
                {
                    var baseline=(Dictionary<string,object>)new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.DeserializeObject(reader.ReadToEnd());
                    foreach(var entry in (object[])baseline["resources"])
                    {
                        var row=(Dictionary<string,object>)entry;string key=(string)row["key"];
                        Check((string)method.Invoke(null,new object[]{key})==(string)row["expected"],"original text baseline "+key);
                    }
                }
                Parallel.For(0,10000,i=>{if((string)method.Invoke(null,new object[]{"common.cancel"})!="取消")throw new Exception("concurrent resource mutation");});
                Check(true,"10000 concurrent reads");
                Console.WriteLine("LocalizationTests: "+checks+" passed");return 0;
            }
            catch(Exception e){Console.Error.WriteLine("FAIL LocalizationTests: "+e);return 1;}
        }
    }
}
