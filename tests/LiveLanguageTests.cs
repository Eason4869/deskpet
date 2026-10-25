using System;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class LiveLanguageTests
    {
        static int checks;
        sealed class DisplayOwner{public string Text{get;set;}}
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static WeakReference RetiredDisplay(){var owner=new DisplayOwner();LiveTextBindings.Bind(owner,"Text",()=>owner.Text??TextResources.Get("common.cancel"));return new WeakReference(owner);}
        static void Check(bool yes,string why){if(!yes)throw new Exception(why);checks++;}
        public static int Main()
        {
            try{
                TextResources.ConfigureStartupLanguage("zh_CN");int notices=0;
                EventHandler observe=delegate{notices++;Check(TextResources.CurrentLanguage==TextResources.CaptureLanguage().Code,"notification sees committed selection");};
                TextResources.LanguageChanged+=observe;
                foreach(string code in new[]{"en_US","ja_JP","zh_CN"}){var prepared=TextResources.PrepareLanguage(code);Check(TextResources.ApplyLanguage(prepared),"changes "+code);Check(TextResources.CurrentLanguage==code&&TextResources.Get("common.cancel")==prepared.Get("common.cancel"),"complete catalog "+code);}
                Check(notices==3&&!TextResources.ApplyLanguage(TextResources.PrepareLanguage("zh_CN")),"same effective locale does not notify");
                var message=TextResources.Message("common.cancel");var snapshot=TextResources.CaptureLanguage();TextResources.ApplyLanguage(TextResources.PrepareLanguage("en_US"));Check(message.Value=="Cancel","resource notice renders in current language");Check(snapshot.Code=="zh_CN"&&snapshot.Get("common.cancel")=="取消","old snapshot remains immutable");
                EventHandler broken=delegate{throw new InvalidOperationException("injected subscriber failure");};int later=0;EventHandler next=delegate{later++;};
                TextResources.LanguageChanged+=broken;TextResources.LanguageChanged+=next;
                TextResources.ApplyLanguage(TextResources.PrepareLanguage("ja_JP"));Check(later==1&&TextResources.CurrentLanguage=="ja_JP","one subscriber cannot block another or undo commit");
                TextResources.LanguageChanged-=broken;TextResources.LanguageChanged-=next;
                Parallel.For(0,1000,i=>{var selection=TextResources.CaptureLanguage();if(String.IsNullOrEmpty(selection.Get("common.cancel")))throw new Exception("incomplete selection");});Check(true,"concurrent complete reads");
                TextResources.LanguageChanged-=observe;TextResources.ApplyLanguage(TextResources.PrepareLanguage("zh_CN"));
                var display=new DisplayOwner();LiveTextBindings.Bind(display,"Text",()=>TextResources.Get("common.cancel"));display.Text="external draft";TextResources.ApplyLanguage(TextResources.PrepareLanguage("en_US"));Check(display.Text=="external draft","ordinary edits retain ownership of a changed display value");
                var retired=RetiredDisplay();for(int i=0;i<3&&retired.IsAlive;i++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}Check(!retired.IsAlive,"weak registrations collect an owner even when its render delegate captures it");TextResources.ApplyLanguage(TextResources.PrepareLanguage("zh_CN"));
                Console.WriteLine("LiveLanguageTests: "+checks+" passed");return 0;
            }catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
    }
}
