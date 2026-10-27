using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu.ImChannels
{
    public sealed class WecomAdapter:ImAdapter
    {
        readonly object gate=new object();readonly Dictionary<string,TaskCompletionSource<bool>> receipts=new Dictionary<string,TaskCompletionSource<bool>>();
        public WecomAdapter(ImChannelProfile profile):base(profile){}
        public override async Task RunAsync(Func<ImMessage,bool> receive,Action<string> status,CancellationToken token)
        {
            Receive=receive;Status=status;await Connect("wss://openws.work.weixin.qq.com",token).ConfigureAwait(false);
            string request="aibot_subscribe_"+Guid.NewGuid().ToString("N");
            await Send(new{cmd="aibot_subscribe",headers=new{req_id=request},body=new{bot_id=Profile.AppId,secret=Profile.Secret}},token).ConfigureAwait(false);
            using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token)){deadline.CancelAfter(TimeSpan.FromSeconds(30));var auth=ParseBytes(await Read(deadline.Token).ConfigureAwait(false));if(ImProtocol.Str(ImProtocol.Get(auth,"headers"),"req_id")!=request||ImProtocol.Get(auth,"errcode")==null||ImProtocol.Num(auth,"errcode")!=0)throw new InvalidOperationException("im.authFailed");}
            Status("im.connected");
            try{await Pump(bytes=>{
                var frame=ParseBytes(bytes);string req=ImProtocol.Str(ImProtocol.Get(frame,"headers"),"req_id");
                if(IsConnectionReplaced(frame))throw new InvalidOperationException("im.connectionReplaced");
                TaskCompletionSource<bool> done;lock(gate)receipts.TryGetValue(req,out done);if(done!=null){if(ImProtocol.Get(frame,"errcode")!=null&&ImProtocol.Num(frame,"errcode")==0)done.TrySetResult(true);else done.TrySetException(new InvalidOperationException("im.platformFailed"));}
                else {var message=Parse(frame);if(message!=null)Receive(message);}return Task.FromResult(0);
            },()=>Send(new{cmd="ping",headers=new{req_id="ping_"+Guid.NewGuid().ToString("N")}},token),()=>30000,token).ConfigureAwait(false);}
            finally{lock(gate){foreach(var pair in receipts)pair.Value.TrySetCanceled();receipts.Clear();}}
        }
        public static bool IsConnectionReplaced(object frame){return ImProtocol.Str(frame,"cmd")=="aibot_event_callback"&&ImProtocol.Str(ImProtocol.Get(ImProtocol.Get(frame,"body"),"event"),"eventtype")=="disconnected_event";}
        public static ImMessage Parse(object frame)
        {
            if(ImProtocol.Str(frame,"cmd")!="aibot_msg_callback")return null;var body=ImProtocol.Get(frame,"body");string sender=ImProtocol.Str(ImProtocol.Get(body,"from"),"userid");bool group=ImProtocol.Str(body,"chattype")=="group";
            return new ImMessage{Id=ImProtocol.Str(body,"msgid"),Sender=sender,Chat=group?ImProtocol.Str(body,"chatid"):sender,IsGroup=group,Mentioned=group,Text=ImProtocol.Str(ImProtocol.Get(body,"text"),"content").Trim(),Unsupported=ImProtocol.Str(body,"msgtype")!="text",ReplyToken=ImProtocol.Str(ImProtocol.Get(frame,"headers"),"req_id"),Time=ImProtocol.Time(body,"create_time")};
        }
        public override async Task SendAsync(ImMessage message,string text,CancellationToken token)
        {
            var done=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);lock(gate)receipts.Add(message.ReplyToken,done);
            try{
                await Send(new{cmd="aibot_respond_msg",headers=new{req_id=message.ReplyToken},body=new{msgtype="stream",stream=new{id=Guid.NewGuid().ToString("N"),finish=true,content=Limited(text,4000)}}},token).ConfigureAwait(false);
                var winner=await Task.WhenAny(done.Task,Task.Delay(15000,token)).ConfigureAwait(false);token.ThrowIfCancellationRequested();if(winner!=done.Task)throw new InvalidOperationException("im.deliveryUncertain");await done.Task.ConfigureAwait(false);
            }finally{lock(gate)receipts.Remove(message.ReplyToken);}
        }
    }
}
