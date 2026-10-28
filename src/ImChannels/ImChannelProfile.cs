using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;

namespace Dafeiyu.ImChannels
{
    [DataContract] public sealed class ImChannelProfile
    {
        public static readonly string[] Kinds={"qq","weixin","feishu","dingtalk","wecom"};
        [DataMember] public string Id=Guid.NewGuid().ToString("N");
        [DataMember] public string Kind="qq",Name="",AppId="",Secret="",PetId="",PetName="",Persona="",ApiBase="";
        [DataMember] public bool Enabled;
        [DataMember] public List<string> Users=new List<string>(),Chats=new List<string>();
        public ImChannelProfile Copy(){return new ImChannelProfile{Id=Id,Kind=Kind,Name=Name,AppId=AppId,Secret=Secret,PetId=PetId,PetName=PetName,Persona=Persona,ApiBase=ApiBase,Enabled=Enabled,Users=new List<string>(Users??new List<string>()),Chats=new List<string>(Chats??new List<string>())};}
        public void Validate()
        {
            Guid id;if(!Guid.TryParseExact(Id,"N",out id)||!Kinds.Contains(Kind)||String.IsNullOrWhiteSpace(Name)||Name.Length>80||String.IsNullOrWhiteSpace(PetName)||PetName.Length>160||Persona==null||Persona.Length>16000||AppId==null||AppId.Length>512||Secret==null||Secret.Length>8192)throw new InvalidOperationException("im.invalidConfig");
            foreach(var list in new[]{Users,Chats})if(list==null||list.Count>128||list.Any(x=>String.IsNullOrWhiteSpace(x)||x.Length>256||x.Any(Char.IsControl)))throw new InvalidOperationException("im.invalidConfig");
            if(Kind=="weixin"&&!String.IsNullOrEmpty(ApiBase))ImProtocol.Endpoint(ApiBase,"weixin");
        }
        public bool Allows(ImMessage message){return message!=null&&!String.IsNullOrEmpty(message.Sender)&&Users!=null&&(Users.Count==0||Users.Contains(message.Sender))&&(!message.IsGroup||(message.Mentioned&&Chats!=null&&(Chats.Count==0||Chats.Contains(message.Chat))));}
        public static string PlatformSite(string kind){switch(kind){case "qq":return "https://q.qq.com/";case "weixin":return "https://weixin.qq.com/";case "feishu":return "https://open.feishu.cn/app/";case "dingtalk":return "https://open-dev.dingtalk.com/";case "wecom":return "https://work.weixin.qq.com/";default:throw new InvalidOperationException("im.invalidConfig");}}
        public override string ToString(){return Name+" ("+ImText.Get("im.platform."+Kind)+")";}
    }
    public sealed class ImMessage
    {
        public string Id="",Sender="",Chat="",Text="",ReplyToken="";
        public bool IsGroup,Mentioned,Unsupported;
        public DateTimeOffset Time=DateTimeOffset.UtcNow;
        public DateTimeOffset ReplyExpiry=DateTimeOffset.MaxValue;
        public ImMessage Copy(){return (ImMessage)MemberwiseClone();}
        public string Scope(string channel)
        {
            string[] parts={channel,IsGroup?"group":"dm",Chat,Sender};
            string joined=String.Concat(parts.Select(x=>(x??"").Length+":"+(x??"")));
            using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(joined))).Replace("-","");
        }
    }
}
