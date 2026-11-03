using System;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace Dafeiyu.Tests
{
    public static class PersonaScreenAccessTests
    {
        static int checks;
        static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
        public static int Main()
        {
            string original=TextResources.CurrentLanguage;
            try{
                var legacy=new JavaScriptSerializer().Deserialize<Dictionary<string,Dictionary<string,string>>>(File.ReadAllText("tests/fixtures/persona-screen-access-legacy.json"));
                var english=TextResources.PrepareLanguage("en_US");var traditional=TextResources.PrepareLanguage("zh_TW");TextResources.ApplyLanguage(english);
                foreach(var locale in legacy){
                    var catalog=TextResources.CatalogFor(locale.Key);string access=catalog.Get("personaProfiles.screenAccess");
                    foreach(var entry in locale.Value){
                        string key="personaProfiles.defaultsUpdated."+entry.Key;string current=catalog.Get(key);
                        Check(current.Contains(access),locale.Key+" "+entry.Key+" explains authorized screen and file access");
                        Check(PersonaProfiles.LocalizeDefaultPrompt(entry.Value)==english.Get(key),locale.Key+" "+entry.Key+" old default upgrades to active language");
                        Check(PersonaProfiles.LocalizeDefaultPrompt(entry.Value,traditional)==traditional.Get(key),locale.Key+" "+entry.Key+" old default respects request language");
                        Check(PersonaProfiles.LocalizeDefaultPrompt(current,traditional)==traditional.Get(key),locale.Key+" "+entry.Key+" current default respects request language");
                        string custom=entry.Value+"\nUser-defined personality note";
                        Check(PersonaProfiles.LocalizeDefaultPrompt(custom)==custom,locale.Key+" "+entry.Key+" user edits are preserved");
                    }
                    string named=locale.Value["generic"].Replace("{pet_name}","星海 Ada");
                    Check(PersonaProfiles.LocalizeDefaultPrompt(named,traditional)==traditional.Get("personaProfiles.defaultsUpdated.generic").Replace("{pet_name}","星海 Ada"),locale.Key+" named legacy template preserves name");
                    var settings=new PetSettings{SkinId="e-bao",SystemPrompt=locale.Value["ebao"],PersonaProfileVersion=0};PersonaProfiles.Activate(settings,"e-bao",english.Get("personaProfiles.defaultsUpdated.ebao"),null,"e-bao");
                    Check(settings.SystemPrompt==english.Get("personaProfiles.defaultsUpdated.ebao") && !settings.PersonaOverrides.ContainsKey("e-bao"),locale.Key+" old default is not mistaken for user override");
                    settings.PersonaOverrides["e-bao"]=locale.Value["ebao"];PersonaProfiles.Activate(settings,"e-bao",english.Get("personaProfiles.defaultsUpdated.ebao"),null,"e-bao");
                    Check(settings.SystemPrompt==english.Get("personaProfiles.defaultsUpdated.ebao"),locale.Key+" stored old default override upgrades when activated");
                }
                Console.WriteLine("PersonaScreenAccessTests: "+checks+" passed");return 0;
            }catch(Exception error){Console.WriteLine("FAIL "+error);return 1;}
            finally{TextResources.ApplyLanguage(TextResources.PrepareLanguage(original));}
        }
    }
}
