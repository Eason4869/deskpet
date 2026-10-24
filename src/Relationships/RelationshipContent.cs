using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
namespace Dafeiyu
{
    [DataContract] public sealed class RelationshipStageContent
    {
        [DataMember(Name="welcome",EmitDefaultValue=false)] public Dictionary<string,string> Welcome {get;set;}
        [DataMember(Name="interaction",EmitDefaultValue=false)] public Dictionary<string,string> Interaction {get;set;}
        [DataMember(Name="action",EmitDefaultValue=false)] public string Action {get;set;}
    }
    [DataContract] public sealed class RelationshipPackContent
    {
        [DataMember(Name="fallback_locale",EmitDefaultValue=false)] public string FallbackLocale {get;set;}
        [DataMember(Name="stages")] public Dictionary<string,RelationshipStageContent> Stages {get;set;}
    }
    public static class RelationshipContent
    {
        // Read through the existing JSON convention, but reject coercion of
        // numbers/arrays into visible text and reject identity-bearing fields.
        public static RelationshipPackContent ParseJson(string json)
        {
            try{return ParseObject(new System.Web.Script.Serialization.JavaScriptSerializer{MaxJsonLength=65536}.DeserializeObject((json??"").TrimStart('\ufeff')));}
            catch(ArgumentException){throw Invalid();}catch(InvalidOperationException){throw Invalid();}
        }
        public static RelationshipPackContent ParseObject(object value)
        {
            var data=value as Dictionary<string,object>;if(data==null || data.Keys.Any(k=>k!="fallback_locale"&&k!="stages"))throw Invalid();
            object raw;string fallback=null;if(data.TryGetValue("fallback_locale",out raw)){fallback=raw as string;if(fallback==null)throw Invalid();}
            if(!data.TryGetValue("stages",out raw))throw Invalid();var stages=raw as Dictionary<string,object>;if(stages==null)throw Invalid();
            var pack=new RelationshipPackContent{FallbackLocale=fallback,Stages=new Dictionary<string,RelationshipStageContent>()};
            foreach(var pair in stages){var fields=pair.Value as Dictionary<string,object>;if(fields==null || fields.Keys.Any(k=>k!="welcome"&&k!="interaction"&&k!="action"))throw Invalid();var content=new RelationshipStageContent();if(fields.TryGetValue("action",out raw)){content.Action=raw as string;if(content.Action==null)throw Invalid();}if(fields.TryGetValue("welcome",out raw))content.Welcome=Texts(raw);if(fields.TryGetValue("interaction",out raw))content.Interaction=Texts(raw);pack.Stages.Add(pair.Key,content);}
            return pack;
        }
        static Dictionary<string,string> Texts(object raw)
        {
            var source=raw as Dictionary<string,object>;if(source==null)throw Invalid();var result=new Dictionary<string,string>();foreach(var pair in source){var value=pair.Value as string;if(value==null)throw Invalid();result.Add(pair.Key,value);}return result;
        }
        public static void Validate(RelationshipPackContent pack,IEnumerable<string> allowedActions)
        {
            if(pack==null)return;
            if(pack.Stages==null || pack.Stages.Count>6 || (!String.IsNullOrEmpty(pack.FallbackLocale) && !ValidLocale(pack.FallbackLocale)))throw Invalid();
            foreach(var pair in pack.Stages){if(!RelationshipRules.Stages.Contains(pair.Key) || pair.Value==null)throw Invalid();var item=pair.Value;if(!String.IsNullOrEmpty(item.Action) && (allowedActions==null || !allowedActions.Contains(item.Action)))throw Invalid();foreach(var texts in new[]{item.Welcome,item.Interaction}){if(texts==null)continue;if(texts.Count>13 || texts.Any(t=>!ValidLocale(t.Key) || String.IsNullOrWhiteSpace(t.Value) || t.Value.Length>280 || t.Value.Any(c=>Char.IsControl(c)&&c!='\n')))throw Invalid();}}
        }
        static bool ValidLocale(string locale){return new[]{"zh_CN","zh_TW","en_US","ja_JP","ko_KR","de_DE","fr_FR","es_ES","es_419","it_IT","pt_BR","hi_IN","id_ID"}.Contains(locale);}
        static InvalidDataException Invalid(){return new InvalidDataException(TextResources.Get("relationship.packInvalid"));}
        static string Stage(string stage){return RelationshipRules.Stages.Contains(stage)?stage:"first_meeting";}
        static string Line(string stage,RelationshipPackContent pack,string locale,bool welcome)
        {
            RelationshipStageContent content;string text;
            if(pack!=null && pack.Stages!=null && pack.Stages.TryGetValue(Stage(stage),out content) && content!=null){var lines=welcome?content.Welcome:content.Interaction;if(lines!=null && (lines.TryGetValue(locale??TextResources.CurrentLanguage,out text) || (!String.IsNullOrEmpty(pack.FallbackLocale)&&lines.TryGetValue(pack.FallbackLocale,out text))))return text;}
            return TextResources.Get("relationship."+(welcome?"welcome.":"interaction.")+Stage(stage));
        }
        public static string Welcome(string stage,RelationshipPackContent pack=null,string locale=null){return Line(stage,pack,locale,true);}
        public static string Interaction(string stage,RelationshipPackContent pack=null,string locale=null){return Line(stage,pack,locale,false);}
        public static string Action(string stage,RelationshipPackContent pack=null){RelationshipStageContent content;return pack!=null&&pack.Stages!=null&&pack.Stages.TryGetValue(Stage(stage),out content)&&content!=null?content.Action:null;}
        public static string StageName(string stage){return TextResources.Get("relationship.stage."+Stage(stage));}
        public static string StageContext(string stage){return TextResources.Format("relationship.aiContext",StageName(stage));}
    }
}
