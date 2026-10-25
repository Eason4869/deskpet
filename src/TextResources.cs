using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Dafeiyu
{
    // Read-only after construction. No current-directory or external language-pack fallback.
    public sealed class TextCatalog
    {
        readonly Dictionary<string,string> values;
        readonly HashSet<string> reported=new HashSet<string>(StringComparer.Ordinal);
        TextCatalog(Dictionary<string,string> values){this.values=values;}
        public static TextCatalog Load(Stream stream)
        {
            if(stream==null)throw new InvalidDataException("界面文字资源不可用，请修复安装。");
            try
            {
                string json;
                using(var reader=new StreamReader(stream,new UTF8Encoding(false,true),true,4096,true))json=reader.ReadToEnd();
                var source=new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.DeserializeObject(json) as Dictionary<string,object>;
                if(source==null)throw new InvalidDataException("Expected a string catalog object.");
                var values=new Dictionary<string,string>(StringComparer.Ordinal);
                foreach(var item in source)
                {
                    if(String.IsNullOrWhiteSpace(item.Key)||!(item.Value is string))throw new InvalidDataException("Invalid catalog entry: "+item.Key);
                    values.Add(item.Key,(string)item.Value);
                }
                return new TextCatalog(values);
            }
            catch(Exception error)
            {
                if(!(error is ArgumentException||error is InvalidOperationException||error is IOException||error is DecoderFallbackException))throw;
                throw new InvalidDataException("界面文字资源不可用，请修复安装。",error);
            }
        }
        public string Get(string key)
        {
            if(key==null)throw new ArgumentNullException("key");
            string value;if(values.TryGetValue(key,out value))return value;
            lock(reported)if(reported.Add(key))foreach(TraceListener listener in Trace.Listeners)listener.WriteLine("Missing text resource: "+key);
            return "["+key+"]";
        }
        public string Format(string key,params object[] args){return Format(CultureInfo.CurrentCulture,key,args);}
        public string Format(IFormatProvider provider,string key,params object[] args){return String.Format(provider,Get(key),args);}
        internal TextCatalog WithFallback(TextCatalog fallback)
        {
            if(fallback==null)throw new ArgumentNullException("fallback");
            var merged=new Dictionary<string,string>(fallback.values,StringComparer.Ordinal);
            foreach(var item in values)if(!UiLanguages.IsNativeNameKey(item.Key))merged[item.Key]=item.Value;
            return new TextCatalog(merged);
        }
    }

    public static class TextResources
    {
        const string ResourceName="Dafeiyu.Locales.zh_CN.json";
        static readonly Lazy<TextCatalog> catalog=new Lazy<TextCatalog>(Load,LazyThreadSafetyMode.ExecutionAndPublication);
        static readonly Dictionary<string,Lazy<TextCatalog>> catalogs=CreateCatalogs();
        static readonly object languageGate=new object();
        static volatile LanguageSelection selection;
        public static event EventHandler LanguageChanged;
        static LanguageSelection Selection {get{return selection??PrepareLanguage("zh_CN");}}
        static readonly Lazy<string[]> errorPrefixes=new Lazy<string[]>(LoadErrorPrefixes,LazyThreadSafetyMode.ExecutionAndPublication);
        static TextCatalog Load()
        {
            using(var stream=typeof(TextResources).Assembly.GetManifestResourceStream(ResourceName))return TextCatalog.Load(stream);
        }
        static Dictionary<string,Lazy<TextCatalog>> CreateCatalogs()
        {
            var result=new Dictionary<string,Lazy<TextCatalog>>(StringComparer.Ordinal);
            foreach(var choice in UiLanguages.Choices)
            {
                string code=choice.Code;
                if(code==UiLanguages.FollowSystem)continue;
                result.Add(code,code=="zh_CN"?catalog:new Lazy<TextCatalog>(()=>{
                    using(var stream=typeof(TextResources).Assembly.GetManifestResourceStream("Dafeiyu.Locales."+code+".json"))return TextCatalog.Load(stream).WithFallback(catalog.Value);
                },LazyThreadSafetyMode.ExecutionAndPublication));
            }
            return result;
        }
        internal static TextCatalog CatalogFor(string code){return catalogs[UiLanguages.Resolve(code)].Value;}
        public static string CurrentLanguage {get{return Selection.Code;}}
        public static LanguageSelection CaptureLanguage(){return Selection;}
        public static LanguageSelection PrepareLanguage(string code)
        {string resolved=UiLanguages.Resolve(code);return new LanguageSelection(resolved,catalogs[resolved].Value);}
        public static bool ApplyLanguage(LanguageSelection prepared)
        {
            if(prepared==null)throw new ArgumentNullException("prepared");
            lock(languageGate){if(Selection.Code==prepared.Code){selection=prepared;return false;}selection=prepared;}
            var listeners=LanguageChanged;
            if(listeners!=null)foreach(EventHandler listener in listeners.GetInvocationList())
                try{listener(null,EventArgs.Empty);}catch(Exception error){foreach(TraceListener diagnostic in Trace.Listeners)try{diagnostic.WriteLine("Language refresh failed: "+error);}catch(Exception){/* A diagnostic sink cannot undo a committed preference. */}}
            return true;
        }
        public static void ConfigureStartupLanguage(string code)
        {
            lock(languageGate)selection=PrepareLanguage(code);
        }
        static string[] LoadErrorPrefixes()
        {
            var prefixes=new HashSet<string>(StringComparer.Ordinal);
            foreach(var choice in UiLanguages.Choices)
            {
                if(choice.Code==UiLanguages.FollowSystem)continue;
                string prefix=CatalogFor(choice.Code).Format(CultureInfo.InvariantCulture,"agentWorkspace.executeAsync.taskFromResult","").Trim();
                if(prefix.Length<2)throw new InvalidDataException("Invalid embedded error prefix.");
                prefixes.Add(prefix);prefixes.Add(prefix.Replace('\uff1a',':'));prefixes.Add(prefix.Replace(':','\uff1a'));
            }
            var result=new string[prefixes.Count];prefixes.CopyTo(result);return result;
        }
        public static bool IsToolError(string result)
        {if(result==null)return false;foreach(string prefix in errorPrefixes.Value)if(result.StartsWith(prefix,StringComparison.Ordinal))return true;return false;}
        public static string Get(string key){return Selection.Get(key);}
        // Explicit catalog reads retain one requested locale without changing startup selection.
        internal static string Get(string key,TextCatalog source){if(source==null)throw new ArgumentNullException("source");return source.Get(key);}
        internal static string FormatForCatalog(TextCatalog source,string key,params object[] args){if(source==null)throw new ArgumentNullException("source");return source.Format(key,args);}
        public static string Format(string key,params object[] args){return Selection.Format(key,args);}
        public static string Format(IFormatProvider provider,string key,params object[] args){return String.Format(provider,Selection.Get(key),args);}
        // Error reporting must never recursively require a broken embedded catalog.
        public static string StartupMessage(string key)
        {
            try{return Get(key);}
            catch(InvalidDataException){return "界面文字资源不可用，请修复安装。";}
        }
    }
}
