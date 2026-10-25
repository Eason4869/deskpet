using System;
using System.Threading;
namespace Dafeiyu
{
    // Only request policy uses this flow-local snapshot. UI resource reads stay live.
    public static class LanguageRequestScope
    {
        static readonly AsyncLocal<LanguageSelection> current=new AsyncLocal<LanguageSelection>();
        public static LanguageSelection Capture(){return current.Value??TextResources.CaptureLanguage();}
        public static IDisposable Enter(LanguageSelection language)
        {
            if(language==null)throw new ArgumentNullException("language");
            var previous=current.Value;current.Value=language;return new Lease(previous);
        }
        sealed class Lease:IDisposable
        {
            readonly LanguageSelection previous;bool disposed;
            internal Lease(LanguageSelection previous){this.previous=previous;}
            public void Dispose(){if(disposed)return;disposed=true;current.Value=previous;}
        }
    }
}
