using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Dafeiyu.ImChannels;

namespace Dafeiyu.Tests
{
    public static class ImChannelTests
    {
        static int checks;
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
        static void Reject(Action action,string name){try{action();}catch{checks++;return;}throw new Exception(name);}
        public static int RunAll()
        {
            var profile=new ImChannelProfile{Kind="qq",Name="bot",AppId="app",Secret="secret",PetName="pet",Persona="private persona"};
            var msg=new ImMessage{Id="1",Sender="alice",Chat="room",IsGroup=true,Mentioned=true,Text="hello",Time=DateTimeOffset.UtcNow};
            Check(!profile.Allows(msg),"empty list must deny");
            profile.Users.Add("alice");Check(!profile.Allows(msg),"group also needs allowlist");
            profile.Chats.Add("room");Check(profile.Allows(msg),"both scopes permitted");
            msg.Mentioned=false;Check(!profile.Allows(msg),"group needs real mention");msg.Mentioned=true;
            var bob=msg.Copy();bob.Sender="bob";Check(msg.Scope(profile.Id)!=bob.Scope(profile.Id),"sender scope isolated");
            Check(msg.Scope("a")!=msg.Scope("b"),"channel scope isolated");
            var ambiguous=msg.Copy();ambiguous.Chat="room|alice";ambiguous.Sender="bob";Check(ambiguous.Scope("a")!=bob.Scope("a"),"scope separator collision");
            var copy=profile.Copy();copy.Users.Clear();Check(profile.Users.Count==1,"profile deep copy");
            string root=Path.Combine(Environment.CurrentDirectory,"work","deskpet-im-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try{
                var store=new ImChannelStore(root);store.Save(new[]{profile});
                string path=Path.Combine(root,"im-channels.json"),raw=File.ReadAllText(path);
                Check(!raw.Contains("private persona")&&!raw.Contains("secret")&&!raw.Contains("alice"),"no plaintext secrets or identities");
                var loaded=store.Load().Single();Check(loaded.Secret=="secret"&&loaded.Users.Single()=="alice","encrypted round trip");
                var large=Enumerable.Range(0,16).Select(n=>new ImChannelProfile{Name="big",PetName="pet",Persona=new string('界',16000),Users=Enumerable.Repeat(new string('界',256),128).ToList(),Chats=Enumerable.Repeat(new string('界',256),128).ToList()}).ToList();
                Reject(()=>store.Save(large),"oversized serialized config rejected before commit");Check(File.ReadAllText(path)==raw,"oversized save preserves original bytes");Check(store.Load().Single().Secret=="secret","oversized save leaves configuration readable");
                File.WriteAllText(path,"broken");Reject(()=>store.Load(),"corrupt config rejected");Reject(()=>store.Save(new[]{profile}),"corrupt config cannot be overwritten");Check(File.ReadAllText(path)=="broken","corrupt bytes preserved");
            }finally{Directory.Delete(root,true);}
            Check(ImProtocol.Endpoint("https://api.sgroup.qq.com/gateway","qq").Host=="api.sgroup.qq.com","QQ official endpoint");
            Reject(()=>ImProtocol.Endpoint("https://api.sgroup.qq.com.evil.test/","qq"),"host suffix boundary");
            Reject(()=>ImProtocol.Endpoint("https://user@api.sgroup.qq.com/","qq"),"no user info");
            Reject(()=>ImProtocol.Endpoint("http://api.sgroup.qq.com/","qq"),"TLS required");
            Reject(()=>ImProtocol.Endpoint("https://api.sgroup.qq.com:8443/","qq"),"standard port required");
            Reject(()=>ImProtocol.Endpoint("https://127.0.0.1/","weixin"),"dynamic local host rejected");
            var qq=QqAdapter.Parse(ImProtocol.Json("{\"t\":\"C2C_MESSAGE_CREATE\",\"d\":{\"id\":\"q1\",\"author\":{\"user_openid\":\"alice\"},\"content\":\"hi\"}}"));
            Check(qq.Sender=="alice"&&qq.Chat=="alice"&&!qq.IsGroup&&qq.Text=="hi","QQ private fixture");
            var group=QqAdapter.Parse(ImProtocol.Json("{\"t\":\"GROUP_AT_MESSAGE_CREATE\",\"d\":{\"id\":\"g1\",\"group_openid\":\"g\",\"author\":{\"member_openid\":\"alice\"},\"content\":\"<@!123> hi\"}}"));
            Check(group.IsGroup&&group.Mentioned&&group.Text=="hi","QQ group at fixture");
            Check(QqAdapter.Parse(ImProtocol.Json("{\"t\":\"GROUP_MESSAGE_CREATE\",\"d\":{}}"))==null,"unmentioned QQ group ignored");
            var ding=DingTalkAdapter.Parse(ImProtocol.Json("{\"msgId\":\"d1\",\"senderStaffId\":\"alice\",\"conversationId\":\"g\",\"conversationType\":\"2\",\"isInAtList\":true,\"msgtype\":\"text\",\"text\":{\"content\":\"hi\"},\"sessionWebhook\":\"https://oapi.dingtalk.com/robot/sendBySession?session=x\"}"));
            Check(ding.IsGroup&&ding.Mentioned&&ding.Text=="hi","DingTalk fixture");
            var wc=WecomAdapter.Parse(ImProtocol.Json("{\"cmd\":\"aibot_msg_callback\",\"headers\":{\"req_id\":\"r\"},\"body\":{\"msgid\":\"w1\",\"chattype\":\"single\",\"from\":{\"userid\":\"alice\"},\"msgtype\":\"text\",\"text\":{\"content\":\"hi\"}}}"));
            Check(wc.Sender=="alice"&&wc.ReplyToken=="r"&&wc.Text=="hi","WeCom fixture");
            Check(WecomAdapter.IsConnectionReplaced(ImProtocol.Json("{\"cmd\":\"aibot_event_callback\",\"body\":{\"event\":{\"eventtype\":\"disconnected_event\"}}}")),"WeCom replacement is a terminal event");
            var wx=WeixinAdapter.Parse(ImProtocol.Json("{\"message_id\":1,\"message_type\":1,\"from_user_id\":\"alice\",\"context_token\":\"ctx\",\"item_list\":[{\"type\":1,\"text_item\":{\"text\":\"hi\"}}]}"));
            Check(wx.Sender=="alice"&&wx.ReplyToken=="ctx"&&wx.Text=="hi","Weixin fixture");
            var frame=new FeishuFrame{Sequence=42,Log=9,Service=5,Method=1,Payload=Encoding.UTF8.GetBytes("hello")};frame.Headers["type"]="event";
            var decoded=FeishuFrame.Decode(frame.Encode());Check(decoded.Sequence==42&&decoded.Headers["type"]=="event"&&Encoding.UTF8.GetString(decoded.Payload)=="hello","protobuf frame roundtrip");
            Check(frame.Encode().SequenceEqual(new byte[]{8,42,16,9,24,5,32,1,42,13,10,4,116,121,112,101,18,5,101,118,101,110,116,66,5,104,101,108,108,111}),"official protobuf wire field numbers and lengths");
            var fragments=new FeishuFragments();var a=new FeishuFrame{Payload=Encoding.UTF8.GetBytes("first")};a.Headers["sum"]="2";a.Headers["seq"]="0";a.Headers["message_id"]="fragment";
            var b=new FeishuFrame{Payload=Encoding.UTF8.GetBytes("second")};b.Headers["sum"]="2";b.Headers["seq"]="1";b.Headers["message_id"]="fragment";
            Check(fragments.Add(b)==null&&Encoding.UTF8.GetString(fragments.Add(a))=="firstsecond","out-of-order platform fragments reassemble");
            Reject(()=>FeishuFrame.Decode(new byte[]{0x3a,0xff}),"truncated protobuf rejected");
            var fs=FeishuAdapter.Parse(ImProtocol.Json("{\"header\":{\"event_type\":\"im.message.receive_v1\"},\"event\":{\"sender\":{\"sender_id\":{\"open_id\":\"alice\"}},\"message\":{\"message_id\":\"f1\",\"chat_id\":\"room\",\"chat_type\":\"p2p\",\"message_type\":\"text\",\"content\":\"{\\\"text\\\":\\\"hi\\\"}\"}}}"));
            Check(fs.Sender=="alice"&&fs.Text=="hi"&&!fs.IsGroup,"Feishu fixture");
            Check(ImLoginQr.Png("https://example.com/login?test=1").Take(4).SequenceEqual(new byte[]{137,80,78,71}),"QR generated locally as PNG");
            ImChannelLifecycleTests.RunAll().GetAwaiter().GetResult();
            Console.WriteLine("ImChannelTests: "+checks+" passed");return 0;
        }
    }
}
