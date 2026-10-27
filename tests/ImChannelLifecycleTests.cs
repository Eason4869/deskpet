using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Dafeiyu.ImChannels;

namespace Dafeiyu.Tests
{
    public static class ImChannelLifecycleTests
    {
        sealed class Api:IConversationApi,IAgentConversationApi,IReasoningConversationApi
        {
            internal IList<ChatMessage> Last;internal string Output="hello from model";
            public Task<string> ChatAsync(ConnectionSettings settings,IList<ChatMessage> messages,CancellationToken cancel){Last=messages.Select(x=>x.Copy()).ToList();return Task.FromResult(Output);}
            public Task<string> TranscribeAsync(ConnectionSettings settings,byte[] wav,CancellationToken cancel){throw new NotSupportedException();}
            public Task<byte[]> SpeechAsync(ConnectionSettings settings,string voice,string text,CancellationToken cancel){throw new NotSupportedException();}
            public Task<string> ChatWithReasoningAsync(ConnectionSettings settings,IList<ChatMessage> messages,Action<string> delta,Action<string> reasoning,bool stream,CancellationToken cancel){reasoning("private scratchpad");return ChatAsync(settings,messages,cancel);}
            public Task<string> ChatAgentAsync(ConnectionSettings settings,IList<ChatMessage> messages,AgentWorkspace workspace,Action<string> delta,Action<string> reasoning,Action<AgentToolEvent> tool,bool stream,CancellationToken cancel){throw new Exception("Remote tools must never run");}
            public void Dispose(){}
        }
        sealed class Adapter:IImAdapter
        {
            internal Func<ImMessage,bool> Receive;internal int Sent;internal readonly TaskCompletionSource<bool> Ready=new TaskCompletionSource<bool>();
            public async Task RunAsync(Func<ImMessage,bool> receive,Action<string> status,CancellationToken token){Receive=receive;status("im.connected");Ready.TrySetResult(true);await Task.Delay(-1,token);}
            public Task SendAsync(ImMessage message,string text,CancellationToken token){token.ThrowIfCancellationRequested();Interlocked.Increment(ref Sent);return Task.FromResult(0);}
            public void Dispose(){}
        }
        sealed class Replaced:IImAdapter
        {
            public Task RunAsync(Func<ImMessage,bool> receive,Action<string> status,CancellationToken token){throw new InvalidOperationException("im.connectionReplaced");}
            public Task SendAsync(ImMessage message,string text,CancellationToken token){throw new Exception("replaced adapter cannot send");}
            public void Dispose(){}
        }
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);}
        public static async Task RunAll()
        {
            var api=new Api();var api2=new Api();var cfg=new PetSettings{AgentEnabled=true,SaveChatHistory=true,SystemPrompt="desktop private prompt"};cfg.Text.BaseUrl="https://example.com/v1";cfg.Text.Model="fixture";cfg.Text.ApiKey="fixture";
            var bound=new ImChannelProfile{Name="bound",PetName="bot",PetId="pet",Persona="bound prompt"};
            using(var conversation=new ImConversation(new Dafeiyu.Maintenance.DataMaintenanceGate(),api))using(var separate=new ImConversation(new Dafeiyu.Maintenance.DataMaintenanceGate(),api2)){
                Check(await conversation.ReplyAsync(bound,cfg,new ImMessage{Text="first"},CancellationToken.None)=="hello from model","existing session replies");
                Check(api.Last.Any(x=>x.Role=="system"&&x.Content.Contains("bound prompt"))&&!api.Last.Any(x=>x.Content.Contains("desktop private prompt")),"bound persona without desktop context");
                await conversation.ReplyAsync(bound,cfg,new ImMessage{Text="second"},CancellationToken.None);Check(api.Last.Any(x=>x.Content=="first"),"same scope keeps context");Check(api.Last.All(x=>String.IsNullOrEmpty(x.Reasoning)),"IM retention strips reasoning");
                await separate.ReplyAsync(bound,cfg,new ImMessage{Text="other user"},CancellationToken.None);Check(!api2.Last.Any(x=>x.Content=="first"),"separate sender cannot read context");
                await conversation.ReplyAsync(bound,cfg,new ImMessage{Text="/new"},CancellationToken.None);await conversation.ReplyAsync(bound,cfg,new ImMessage{Text="third"},CancellationToken.None);Check(!api.Last.Any(x=>x.Content=="first"),"new clears only current context");
                api.Output=new string('x',70000);await conversation.ReplyAsync(bound,cfg,new ImMessage{Text="large"},CancellationToken.None);Check(conversation.RetainedCharacters<=64000,"single oversized turn cannot exceed IM context limit");
            }
            string root=Path.Combine(Environment.CurrentDirectory,"work","im-life-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try{
                var adapter=new Adapter();var started=new TaskCompletionSource<bool>();var cancelled=new TaskCompletionSource<bool>();int calls=0;
                using(var manager=new ImChannelManager(root,()=>new PetSettings(),new Dafeiyu.Maintenance.DataMaintenanceGate(),p=>adapter,async (p,m,c)=>{Interlocked.Increment(ref calls);started.TrySetResult(true);try{await Task.Delay(-1,c);}catch(OperationCanceledException){cancelled.TrySetResult(true);throw;}return "never";})){
                    var profile=new ImChannelProfile{Kind="qq",Name="test",PetName="pet",AppId="app",Secret="secret"};profile.Users.Add("alice");
                    await manager.SaveAsync(profile);await manager.StartAsync(profile.Id);await adapter.Ready.Task;
                    var duplicateBot=profile.Copy();duplicateBot.Id=Guid.NewGuid().ToString("N");await manager.SaveAsync(duplicateBot);bool refused=false;try{await manager.StartAsync(duplicateBot.Id);}catch(InvalidOperationException){refused=true;}Check(refused,"same bot cannot be connected by two profiles");await manager.DeleteAsync(duplicateBot.Id);
                    Check(!adapter.Receive(new ImMessage{Id="unknown",Sender="bob",Chat="bob",Text="hi"}),"unknown sender must not enter queue");
                    var message=new ImMessage{Id="one",Sender="alice",Chat="alice",Text="hello"};Check(adapter.Receive(message),"valid admitted");await started.Task;
                    Check(!adapter.Receive(message),"duplicate refused");
                    Check(!adapter.Receive(new ImMessage{Id="stale",Sender="alice",Chat="alice",Text="old",Time=DateTimeOffset.UtcNow.AddMinutes(-6)}),"stale refused");
                    for(int n=0;n<32;n++)Check(adapter.Receive(new ImMessage{Id="queued-"+n,Sender="alice",Chat="alice",Text="pending"}),"bounded queue accepts within capacity");
                    Check(!adapter.Receive(new ImMessage{Id="overflow",Sender="alice",Chat="alice",Text="overflow"}),"queue rejects excess without spawning work");
                    Check(adapter.Receive(new ImMessage{Id="stop",Sender="alice",Chat="alice",Text="/stop"}),"stop admitted out of band");
                    Check(await Task.WhenAny(cancelled.Task,Task.Delay(2000))==cancelled.Task,"stop cancels active generation promptly");
                    profile.Users.Clear();await manager.SaveAsync(profile);Check(adapter.Sent==0,"revoked generation never sends");Check(calls==1,"only authorized input calls model");
                    await manager.DeleteAsync(profile.Id);Check(manager.Profiles.Count==0,"delete removes profile");
                    using(var duplicate=new ImChannelManager(root,()=>new PetSettings(),new Dafeiyu.Maintenance.DataMaintenanceGate()))Check(duplicate.Warning.Length>0,"second host cannot own channels");
                }
                int attempts=0;
                using(var terminal=new ImChannelManager(Path.Combine(root,"terminal"),()=>new PetSettings(),new Dafeiyu.Maintenance.DataMaintenanceGate(),p=>{Interlocked.Increment(ref attempts);return new Replaced();})){
                    var profile=new ImChannelProfile{Kind="wecom",Name="terminal",PetName="pet",AppId="bot",Secret="secret"};await terminal.SaveAsync(profile);await terminal.StartAsync(profile.Id);
                    var deadline=DateTime.UtcNow.AddSeconds(3);while(terminal.State(profile.Id)!="im.connectionReplaced"&&DateTime.UtcNow<deadline)await Task.Delay(10);
                    Check(terminal.State(profile.Id)=="im.connectionReplaced","server replacement is reported");await Task.Delay(1200);Check(attempts==1,"server replacement does not reconnect and steal connection");
                }
                Console.WriteLine("ImChannelLifecycleTests: admission, dedup, expiry, stop, revoke, delete passed");
            }finally{Directory.Delete(root,true);}
        }
    }
}
