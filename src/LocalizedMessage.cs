using System;
namespace Dafeiyu
{
    // Keep a key and frozen arguments, not an already-translated display string.
    public sealed class LocalizedMessage
    {
        readonly LocalizedMessage prefix;
        readonly object suffix;
        readonly string key;
        readonly object[] arguments;
        internal LocalizedMessage(string key,object[] arguments)
        {this.key=key;this.arguments=arguments==null?new object[0]:(object[])arguments.Clone();}
        LocalizedMessage(LocalizedMessage prefix,object suffix){this.prefix=prefix;this.suffix=suffix;}
        public static LocalizedMessage Concat(LocalizedMessage prefix,object suffix){return new LocalizedMessage(prefix,suffix);}
        public string Value {get{if(prefix!=null)return prefix.Value+(suffix is LocalizedMessage?((LocalizedMessage)suffix).Value:Convert.ToString(suffix));return arguments.Length==0?TextResources.CaptureLanguage().Get(key):TextResources.CaptureLanguage().Format(key,arguments);}}
    }
}
