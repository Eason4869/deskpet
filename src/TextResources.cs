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
    }

    public static class TextResources
    {
        const string ResourceName="Dafeiyu.Locales.zh_CN.json";
        static readonly Lazy<TextCatalog> catalog=new Lazy<TextCatalog>(Load,LazyThreadSafetyMode.ExecutionAndPublication);
        static TextCatalog Load()
        {
            using(var stream=typeof(TextResources).Assembly.GetManifestResourceStream(ResourceName))return TextCatalog.Load(stream);
        }
        public static string Get(string key){return catalog.Value.Get(key);}
        public static string Format(string key,params object[] args){return catalog.Value.Format(key,args);}
        public static string Format(IFormatProvider provider,string key,params object[] args){return catalog.Value.Format(provider,key,args);}
        // Error reporting must never recursively require a broken embedded catalog.
        public static string StartupMessage(string key)
        {
            try{return Get(key);}
            catch(InvalidDataException){return "界面文字资源不可用，请修复安装。";}
        }
    }
}
