using System;
namespace Dafeiyu
{
    // An immutable catalog handle, also retained by an in-flight request.
    public sealed class LanguageSelection
    {
        readonly TextCatalog catalog;
        public string Code {get;private set;}
        internal LanguageSelection(string code,TextCatalog catalog){Code=code;this.catalog=catalog;}
        public string Get(string key){return catalog.Get(key);}
        public string Format(string key,params object[] args){return catalog.Format(key,args);}
    }
}
