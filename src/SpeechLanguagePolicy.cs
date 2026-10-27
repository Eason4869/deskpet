using System;
using System.Collections.Generic;

namespace Dafeiyu
{
    public static class SpeechLanguagePolicy
    {
        // Return an installed culture unchanged so callers can select its voice.
        // A null result deliberately leaves the synthesizer's default in place.
        public static string SelectCulture(string uiLanguage,IEnumerable<string> availableCultures)
        {
            string requested=Normalize(uiLanguage);
            if(requested.Length==0||availableCultures==null)return null;
            string language=requested.Split('-')[0],familyMatch=null;
            foreach(string culture in availableCultures)
            {
                string candidate=Normalize(culture);
                if(candidate.Length==0)continue;
                if(String.Equals(requested,candidate,StringComparison.OrdinalIgnoreCase))return culture;
                if(familyMatch==null&&String.Equals(language,candidate.Split('-')[0],StringComparison.OrdinalIgnoreCase))
                    familyMatch=culture;
            }
            return familyMatch;
        }

        private static string Normalize(string culture)
        {
            return(culture??"").Trim().Replace('_','-');
        }
    }
}
