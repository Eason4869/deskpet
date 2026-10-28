using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Dafeiyu.ImChannels
{
    public static class ImProtocol
    {
        public const int MaxBytes=1048576;
        public static Dictionary<string,object> Json(string json){return new JavaScriptSerializer{MaxJsonLength=MaxBytes,RecursionLimit=32}.Deserialize<Dictionary<string,object>>(json);}
        public static string Encode(object value){return new JavaScriptSerializer{MaxJsonLength=MaxBytes,RecursionLimit=32}.Serialize(value);}
        public static object Get(object source,string key){var map=source as IDictionary<string,object>;object value;return map!=null&&map.TryGetValue(key,out value)?value:null;}
        public static string Str(object source,string key){return Convert.ToString(Get(source,key),CultureInfo.InvariantCulture)??"";}
        public static long Num(object source,string key,long fallback=0){long value;return Int64.TryParse(Str(source,key),out value)?value:fallback;}
        public static bool Bool(object source,string key){return Get(source,key) is bool&&(bool)Get(source,key);}
        public static object[] Array(object source,string key){var items=Get(source,key) as IEnumerable;return items==null||items is string?new object[0]:items.Cast<object>().ToArray();}
        public static DateTimeOffset Time(object data,string key){var raw=Str(data,key);DateTimeOffset value;long number;if(Int64.TryParse(raw,out number)){try{return DateTimeOffset.FromUnixTimeMilliseconds(number<100000000000?number*1000:number);}catch(ArgumentOutOfRangeException){return DateTimeOffset.MinValue;}}return DateTimeOffset.TryParse(raw,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out value)?value:DateTimeOffset.UtcNow;}
        public static Uri Endpoint(string address,string kind)
        {
            Uri uri;if(!Uri.TryCreate(address,UriKind.Absolute,out uri)||(uri.Scheme!="https"&&uri.Scheme!="wss")||uri.Port!=443||uri.UserInfo.Length>0||uri.Fragment.Length>0)throw new InvalidOperationException("im.endpointRejected");
            string[] domains=kind=="qq"?new[]{"sgroup.qq.com","bots.qq.com"}:kind=="weixin"?new[]{"ilinkai.weixin.qq.com"}:kind=="feishu"?new[]{"feishu.cn","larksuite.com"}:kind=="dingtalk"?new[]{"dingtalk.com"}:kind=="wecom"?new[]{"work.weixin.qq.com"}:new string[0];
            if(!domains.Any(host=>uri.DnsSafeHost.Equals(host,StringComparison.OrdinalIgnoreCase)||uri.DnsSafeHost.EndsWith("."+host,StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("im.endpointRejected");return uri;
        }
        public static string CleanMention(string text){return System.Text.RegularExpressions.Regex.Replace(text??"",@"<@!?\w+>|@_user_\d+","").Trim();}
        public static IEnumerable<string> Chunks(string text,int maxBytes=3000)
        {
            var chunk=new StringBuilder();int size=0;
            foreach(var rune in Elements(text??"")){int count=Encoding.UTF8.GetByteCount(rune);if(size+count>maxBytes&&chunk.Length>0){yield return chunk.ToString();chunk.Clear();size=0;}chunk.Append(rune);size+=count;}
            if(chunk.Length>0)yield return chunk.ToString();
        }
        static IEnumerable<string> Elements(string text){var enumerator=StringInfo.GetTextElementEnumerator(text);while(enumerator.MoveNext())yield return (string)enumerator.Current;}
    }
}
