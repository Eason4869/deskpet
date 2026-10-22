using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Dafeiyu
{
    public static class HolidayGreetingPhrases
    {
        static readonly IDictionary<string,IList<string>> halloween=Load();
        static readonly IList<string> empty=new ReadOnlyCollection<string>(new string[0]);
        [DataContract] sealed class Library {
            [DataMember(Name="schema_version")]public int Version;
            [DataMember(Name="holiday_id")]public string Holiday;
            [DataMember(Name="characters")]public List<Character> Characters;
        }
        [DataContract] sealed class Character {
            [DataMember(Name="character_id")]public string Id;
            [DataMember(Name="phrases")]public List<string> Phrases;
        }
        static IDictionary<string,IList<string>> Load()
        {
            var result=new Dictionary<string,IList<string>>(StringComparer.Ordinal);
            try {
                using(var input=typeof(HolidayGreetingPhrases).Assembly.GetManifestResourceStream("Dafeiyu.Holidays.halloween.greetings.json")) {
                    if(input==null)return result;
                    var library=(Library)new DataContractJsonSerializer(typeof(Library)).ReadObject(input);
                    if(library==null || library.Version!=1 || library.Holiday!="halloween" || library.Characters==null || library.Characters.Count!=5)return result;
                    foreach(var character in library.Characters) {
                        if(character==null || !new[]{"e-bao","dafeiyu","marfu","xiaobei","gugugaga"}.Contains(character.Id) || result.ContainsKey(character.Id) || character.Phrases==null || character.Phrases.Count!=6 || character.Phrases.Distinct().Count()!=6 || character.Phrases.Any(x=>String.IsNullOrWhiteSpace(x) || x.Length>80 || x.Any(Char.IsControl)))return new Dictionary<string,IList<string>>();
                        result.Add(character.Id,new ReadOnlyCollection<string>(character.Phrases));
                    }
                }
            }catch(Exception error){if(!(error is IOException || error is SerializationException || error is System.Xml.XmlException || error is ArgumentException))throw;result.Clear();}
            return result;
        }
        public static IList<string> ForCharacter(string characterId,string holidayId,string stage)
        {IList<string> phrases;return holidayId=="halloween" && stage=="day" && characterId!=null && halloween.TryGetValue(characterId,out phrases)?phrases:empty;}
        public static string Choose(HolidayGreetingContext context,int index,IList<string> recent)
        {
            if(context==null || !context.UseHolidayPool)return null;
            var phrases=ForCharacter(context.CharacterId,context.HolidayId,context.Stage);if(phrases.Count==0)return null;
            int first=(int)((uint)index%(uint)phrases.Count);
            for(int offset=0;offset<phrases.Count;offset++) {
                string line=phrases[(first+offset)%phrases.Count];
                if(recent==null || !recent.Any(x=>GreetingService.Key(x)==GreetingService.Key(line)))return line;
            }
            return phrases[first];
        }
        public static string ContextText(HolidayGreetingContext context,DateTime localTime)
        {
            if(context==null || context.Date!=localTime.Date)return "";
            string name=context.HolidayId=="halloween"?"万圣节":context.HolidayId;
            return "\n节日上下文（本地日期）："+localTime.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture)+"；活动："+name+"；阶段："+context.Stage+"；实际服饰："+(String.IsNullOrEmpty(context.OutfitName)?"原装":context.OutfitName)+"；收藏："+(context.IsOwned?"已拥有":"尚未拥有")+"。这是当前真实日期的背景，不要求每句都说节日快乐，不承诺奖励、不声称服饰已经解锁。";
        }
    }
}
