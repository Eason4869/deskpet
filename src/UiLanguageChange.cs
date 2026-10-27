using System;
namespace Dafeiyu
{
    public static class UiLanguageChange
    {
        [ThreadStatic] static bool applying;
        public static bool Apply(SettingsStore store,PetSettings liveSettings,string code)
        {return Commit(store,liveSettings,code,false);}
        public static bool ApplyAndContinueGuide(SettingsStore store,PetSettings liveSettings,string code)
        {return Commit(store,liveSettings,code,true);}
        static bool Commit(SettingsStore store,PetSettings settings,string code,bool guide)
        {
            if(store==null)throw new ArgumentNullException("store");
            if(settings==null)throw new ArgumentNullException("settings");
            if(applying)throw new InvalidOperationException("A language change is already being committed.");
            applying=true;
            try{
                var prepared=TextResources.PrepareLanguage(code);
                store.SaveLanguageSelection(settings,code,guide);
                return TextResources.ApplyLanguage(prepared);
            }finally{applying=false;}
        }
    }
}
