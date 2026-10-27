using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Globalization;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    public sealed class UiLanguageChoice
    {
        public string Code {get;private set;}
        public string MenuName {get;private set;}
        internal UiLanguageChoice(string code,string menuName){Code=code;MenuName=menuName;}
        public string NativeName
        {
            get
            {
                switch(Code)
                {
                    case "system":return TextResources.Get("languages.system");
                    case "zh_TW":return TextResources.Get("languages.zhTW");
                    case "en_US":return TextResources.Get("languages.enUS");
                    case "ja_JP":return TextResources.Get("languages.jaJP");
                    case "ko_KR":return TextResources.Get("languages.koKR");
                    case "fr_FR":return TextResources.Get("languages.frFR");
                    case "de_DE":return TextResources.Get("languages.deDE");
                    case "hi_IN":return TextResources.Get("languages.hiIN");
                    case "id_ID":return TextResources.Get("languages.idID");
                    case "it_IT":return TextResources.Get("languages.itIT");
                    case "pt_BR":return TextResources.Get("languages.ptBR");
                    case "es_419":return TextResources.Get("languages.es419");
                    case "es_ES":return TextResources.Get("languages.esES");
                    default:return TextResources.Get("languages.zhCN");
                }
            }
        }
    }
    public static class UiLanguages
    {
        public const string Default="system";
        public const string FollowSystem="system";
        static readonly ReadOnlyCollection<UiLanguageChoice> choices=Array.AsReadOnly(new[]{new UiLanguageChoice("system","ProfileLanguageSystem"),new UiLanguageChoice("zh_CN","ProfileLanguageZhCn"),new UiLanguageChoice("zh_TW","ProfileLanguageZhTw"),new UiLanguageChoice("en_US","ProfileLanguageEnUs"),new UiLanguageChoice("ja_JP","ProfileLanguageJaJp"),new UiLanguageChoice("ko_KR","ProfileLanguageKoKr"),new UiLanguageChoice("fr_FR","ProfileLanguageFrFR"),new UiLanguageChoice("de_DE","ProfileLanguageDeDE"),new UiLanguageChoice("hi_IN","ProfileLanguageHiIN"),new UiLanguageChoice("id_ID","ProfileLanguageIdID"),new UiLanguageChoice("it_IT","ProfileLanguageItIT"),new UiLanguageChoice("pt_BR","ProfileLanguagePtBR"),new UiLanguageChoice("es_419","ProfileLanguageEs419"),new UiLanguageChoice("es_ES","ProfileLanguageEsES")});
        public static ReadOnlyCollection<UiLanguageChoice> Choices {get{return choices;}}
        public static string Normalize(string code)
        {
            string candidate=(code??"").Trim().Replace('-','_');
            foreach(var choice in choices)if(String.Equals(candidate,choice.Code,StringComparison.OrdinalIgnoreCase))return choice.Code;
            return Default;
        }
        public static string Resolve(string selected){return ResolveForCulture(selected,CultureInfo.CurrentUICulture.Name);}
        internal static string ResolveForCulture(string selected,string uiCulture)
        {
            string code=Normalize(selected);if(code!=FollowSystem)return code;
            string culture=(uiCulture??"").Replace('_','-').ToLowerInvariant(),language=culture.Split('-')[0],target;
            switch(language)
            {
                case "zh":target=culture.Contains("hant")||culture.Contains("-tw")||culture.Contains("-hk")||culture.Contains("-mo")||culture.Contains("-cht")?"zh_TW":"zh_CN";break;
                case "en":target="en_US";break;case "ja":target="ja_JP";break;case "ko":target="ko_KR";break;
                case "fr":target="fr_FR";break;case "de":target="de_DE";break;case "hi":target="hi_IN";break;
                case "id":case "in":target="id_ID";break;case "it":target="it_IT";break;case "pt":target="pt_BR";break;
                case "es":target=culture=="es"||culture.StartsWith("es-es",StringComparison.Ordinal)?"es_ES":"es_419";break;
                default:target="en_US";break;
            }
            foreach(var choice in choices)if(choice.Code==target)return target;
            return "en_US";
        }
        internal static bool IsNativeNameKey(string key)
        {return key=="languages.zhCN"||key=="languages.zhTW"||key=="languages.enUS"||key=="languages.jaJP"||key=="languages.koKR"||key=="languages.frFR"||key=="languages.deDE"||key=="languages.hiIN"||key=="languages.idID"||key=="languages.itIT"||key=="languages.ptBR"||key=="languages.es419"||key=="languages.esES";}
        // The standalone updater needs only this setting, not the app's secret store.
        internal static string ReadSavedLanguage(string path)
        {
            try
            {
                if(!File.Exists(path))return Default;
                if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)return Default;
                for(var parent=new FileInfo(path).Directory;parent!=null;parent=parent.Parent)if((parent.Attributes&FileAttributes.ReparsePoint)!=0)return Default;
                using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
                {
                    if(file.Length>4*1024*1024)return Default;
                    using(var reader=new StreamReader(file,new UTF8Encoding(false,true),true))
                    {var data=new JavaScriptSerializer{MaxJsonLength=4*1024*1024,RecursionLimit=32}.DeserializeObject(reader.ReadToEnd()) as Dictionary<string,object>;object code;return data!=null&&data.TryGetValue("UiLanguage",out code)&&code is string?Normalize((string)code):Default;}
                }
            }
            catch(Exception error)
            {if(!(error is IOException||error is UnauthorizedAccessException||error is ArgumentException||error is InvalidOperationException||error is System.Security.SecurityException))throw;return Default;}
        }
    }
}
