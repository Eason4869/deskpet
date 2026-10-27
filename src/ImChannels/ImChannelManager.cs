using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dafeiyu.Maintenance;

namespace Dafeiyu.ImChannels
{
    public sealed class ImChannelManager:IDisposable
    {
        readonly object gate=new object();readonly SemaphoreSlim edits=new SemaphoreSlim(1,1);
        readonly ImChannelStore store;readonly Func<PetSettings> settings;readonly DataMaintenanceGate maintenance;
        readonly Func<ImChannelProfile,IImAdapter> factory;readonly Func<ImChannelProfile,ImMessage,CancellationToken,Task<string>> responder;
        readonly Dictionary<string,Runtime> running=new Dictionary<string,Runtime>();readonly Dictionary<string,string> statuses=new Dictionary<string,string>(),senders=new Dictionary<string,string>();
        IList<ImChannelProfile> profiles=new List<ImChannelProfile>();readonly IDisposable registration;FileStream ownership;bool disposed;public string Warning {get;private set;}
        public event Action Changed;
        public ImChannelManager(string root,Func<PetSettings> settings,DataMaintenanceGate maintenance,Func<ImChannelProfile,IImAdapter> factory=null,Func<ImChannelProfile,ImMessage,CancellationToken,Task<string>> responder=null)
        {
            this.settings=settings;this.maintenance=maintenance;this.factory=factory??CreateAdapter;this.responder=responder;store=new ImChannelStore(root);Warning="";
            try{Directory.CreateDirectory(root);ownership=new FileStream(Path.Combine(root,"im-channels.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);profiles=store.Load();}catch{Warning="im.storageFailed";}
            registration=maintenance.Register(StopAllAsync,()=>Busy,this);
        }
        static IImAdapter CreateAdapter(ImChannelProfile p){switch(p.Kind){case "qq":return new QqAdapter(p);case "weixin":return new WeixinAdapter(p);case "feishu":return new FeishuAdapter(p);case "dingtalk":return new DingTalkAdapter(p);case "wecom":return new WecomAdapter(p);default:throw new InvalidOperationException("im.invalidConfig");}}
        public IList<ImChannelProfile> Profiles {get{lock(gate)return profiles.Select(x=>x.Copy()).ToList();}}
        public bool Busy {get{lock(gate)return running.Values.Any(x=>x.Active);}}
        public string State(string id){lock(gate){string state;return statuses.TryGetValue(id,out state)?state:"im.paused";}}
        public string LastSender(string id){lock(gate){string sender;return senders.TryGetValue(id,out sender)?sender:"";}}
        void Notify(){var handlers=Changed;if(handlers!=null)foreach(Action handler in handlers.GetInvocationList())try{handler();}catch{}}
        void Require(){if(disposed)throw new ObjectDisposedException("ImChannelManager");if(Warning.Length>0||ownership==null)throw new InvalidOperationException("im.storageFailed");}
        void SetState(Runtime runtime,string state){lock(gate){Runtime current;if(runtime.Stop.IsCancellationRequested||!running.TryGetValue(runtime.Profile.Id,out current)||current!=runtime)return;statuses[runtime.Profile.Id]=state;}Notify();}
        void CheckBotOwner(ImChannelProfile profile){lock(gate)if(running.Values.Any(x=>!x.Stop.IsCancellationRequested&&x.Profile.Id!=profile.Id&&x.Profile.Kind==profile.Kind&&((profile.AppId.Length>0&&x.Profile.AppId==profile.AppId)||(profile.Secret.Length>0&&x.Profile.Secret==profile.Secret))))throw new InvalidOperationException("im.connectionReplaced");}
        public async Task SaveAsync(ImChannelProfile profile)
        {
            var snapshot=settings().Copy();var copy=profile.Copy();copy.Validate();using(maintenance.EnterActivity()){
                await edits.WaitAsync().ConfigureAwait(false);try{Require();if(copy.Enabled)CheckBotOwner(copy);await StopRuntime(copy.Id).ConfigureAwait(false);
                    IList<ImChannelProfile> next;lock(gate){next=profiles.Where(x=>x.Id!=copy.Id).Select(x=>x.Copy()).ToList();next.Add(copy);}store.Save(next);lock(gate)profiles=next;
                    if(copy.Enabled)StartRuntime(copy,snapshot);
                }finally{edits.Release();}Notify();
            }
        }
        public async Task StartAsync(string id)
        {
            var snapshot=settings().Copy();using(maintenance.EnterActivity()){
                await edits.WaitAsync().ConfigureAwait(false);try{Require();var copy=Profiles.Single(x=>x.Id==id);if(copy.Kind!="weixin"&&(String.IsNullOrWhiteSpace(copy.AppId)||String.IsNullOrWhiteSpace(copy.Secret))||copy.Kind=="weixin"&&String.IsNullOrEmpty(copy.Secret))throw new InvalidOperationException("im.authFailed");
                    CheckBotOwner(copy);await StopRuntime(id).ConfigureAwait(false);copy.Enabled=true;var next=Profiles.ToList();next[next.FindIndex(x=>x.Id==id)]=copy;store.Save(next);lock(gate)profiles=next;StartRuntime(copy,snapshot);
                }finally{edits.Release();}
            }
        }
        public async Task PauseAsync(string id)
        {
            using(maintenance.EnterActivity()){await edits.WaitAsync().ConfigureAwait(false);try{Require();await StopRuntime(id).ConfigureAwait(false);var next=Profiles.ToList();var item=next.FirstOrDefault(x=>x.Id==id);if(item!=null)item.Enabled=false;store.Save(next);lock(gate)profiles=next;}finally{edits.Release();}Notify();}
        }
        public async Task DeleteAsync(string id)
        {
            using(maintenance.EnterActivity()){await edits.WaitAsync().ConfigureAwait(false);try{Require();await StopRuntime(id).ConfigureAwait(false);var next=Profiles.Where(x=>x.Id!=id).ToList();store.Save(next);lock(gate){profiles=next;statuses.Remove(id);senders.Remove(id);}}finally{edits.Release();}Notify();}
        }
        void StartRuntime(ImChannelProfile profile,PetSettings snapshot)
        {
            var runtime=new Runtime{Profile=profile.Copy(),Settings=snapshot};lock(gate)running.Add(profile.Id,runtime);
            runtime.Worker=Task.Run(()=>Work(runtime));runtime.Network=Task.Run(()=>Connect(runtime));SetState(runtime,"im.connecting");
        }
        public async Task RestoreAsync(){if(Warning.Length>0)return;foreach(var profile in Profiles.Where(x=>x.Enabled))try{await StartAsync(profile.Id).ConfigureAwait(false);}catch{lock(gate)statuses[profile.Id]="im.failed";}Notify();}
        async Task Connect(Runtime runtime)
        {
            int attempt=0;var token=runtime.Stop.Token;
            while(!token.IsCancellationRequested){IImAdapter adapter=null;
                try{adapter=factory(runtime.Profile);lock(gate)runtime.Adapter=adapter;
                    await adapter.RunAsync(message=>Admit(runtime,adapter,message),state=>{if(state=="im.connected")attempt=0;SetState(runtime,state);},token).ConfigureAwait(false);
                    if(!token.IsCancellationRequested)throw new InvalidOperationException("im.disconnected");
                }catch(OperationCanceledException){if(token.IsCancellationRequested)break;SetState(runtime,"im.reconnecting");}
                catch(Exception error){if(token.IsCancellationRequested)break;if(error is InvalidOperationException&&(error.Message=="im.authFailed"||error.Message=="im.connectionReplaced")){SetState(runtime,error.Message);lock(gate){runtime.Queue.Clear();runtime.Stop.Cancel();}break;}SetState(runtime,"im.reconnecting");}
                finally{lock(gate){if(runtime.Adapter==adapter)runtime.Adapter=null;}if(adapter!=null)adapter.Dispose();}
                try{await Task.Delay(TimeSpan.FromSeconds(Math.Min(60,Math.Pow(2,Math.Min(6,attempt++)))),token).ConfigureAwait(false);}catch(OperationCanceledException){break;}
            }
        }
        bool Admit(Runtime runtime,IImAdapter adapter,ImMessage source)
        {
            if(source==null)return false;var message=source.Copy();string scope=message.Scope(runtime.Profile.Id);bool notify=false;
            lock(gate){if(disposed||runtime.Stop.IsCancellationRequested||runtime.Adapter!=adapter||maintenance.IsMaintenanceActive)return false;
                if(message.Sender.Length>256||message.Chat.Length>256||message.Sender.Any(Char.IsControl)||message.Chat.Any(Char.IsControl))return false;
                senders[runtime.Profile.Id]=message.Sender+(message.IsGroup?" / "+message.Chat:"");notify=true;
            }
            if(notify)Notify();
            lock(gate){
                if(runtime.Stop.IsCancellationRequested||!runtime.Profile.Allows(message)||message.Id.Length==0||message.Id.Length>512||message.Chat.Length==0||message.Text.Length>8000||(!message.Unsupported&&String.IsNullOrWhiteSpace(message.Text))||DateTimeOffset.UtcNow-message.Time>TimeSpan.FromMinutes(5)||message.Time-DateTimeOffset.UtcNow>TimeSpan.FromMinutes(1))return false;
                foreach(string key in runtime.Seen.Where(x=>DateTimeOffset.UtcNow-x.Value>TimeSpan.FromMinutes(10)).Select(x=>x.Key).ToArray())runtime.Seen.Remove(key);
                string identity=scope+":"+message.Id;if(runtime.Seen.ContainsKey(identity))return false;
                if(runtime.Seen.Count>=4096)return false;
                if(message.Text=="/stop"){
                    if(runtime.Scope==scope&&runtime.ActiveCancel!=null){try{runtime.ActiveCancel.Cancel();}catch(ObjectDisposedException){}}
                    var kept=runtime.Queue.Where(x=>x.Scope!=scope).ToArray();runtime.Queue.Clear();foreach(var item in kept)runtime.Queue.Enqueue(item);
                    runtime.Seen[identity]=DateTimeOffset.UtcNow;return true;
                }
                if(runtime.Queue.Count>=32)return false;
                runtime.Seen[identity]=DateTimeOffset.UtcNow;runtime.Queue.Enqueue(new Pending{Message=message,Scope=scope,Adapter=adapter});runtime.Signal.Release();return true;
            }
        }
        async Task Work(Runtime runtime)
        {
            var token=runtime.Stop.Token;
            try{while(true){await runtime.Signal.WaitAsync(token).ConfigureAwait(false);Pending pending;
                    lock(gate){if(runtime.Queue.Count==0)continue;pending=runtime.Queue.Dequeue();runtime.Scope=pending.Scope;runtime.Active=true;runtime.ActiveCancel=CancellationTokenSource.CreateLinkedTokenSource(token);}
                    try{
                        using(var deadline=runtime.ActiveCancel){deadline.CancelAfter(TimeSpan.FromMinutes(5));var cancel=deadline.Token;
                            if(DateTimeOffset.UtcNow-pending.Message.Time>TimeSpan.FromMinutes(5))continue;
                            IDisposable activity;if(!maintenance.TryEnterActivity(out activity))continue;
                            using(activity){string answer;
                                if(pending.Message.Unsupported)answer=TextResources.Get("im.textOnly");
                                else if(pending.Message.Text=="/help")answer=TextResources.Get("im.help");
                                else if(responder!=null)answer=await responder(runtime.Profile,pending.Message,cancel).ConfigureAwait(false);
                                else {ImConversation conversation;if(!runtime.Conversations.TryGetValue(pending.Scope,out conversation)){if(runtime.Conversations.Count>=64){answer=TextResources.Get("im.sessionLimit");goto Send;}conversation=new ImConversation(maintenance);runtime.Conversations.Add(pending.Scope,conversation);}answer=await conversation.ReplyAsync(runtime.Profile,runtime.Settings,pending.Message,cancel).ConfigureAwait(false);}
                                Send:cancel.ThrowIfCancellationRequested();lock(gate){if(runtime.Adapter!=pending.Adapter||runtime.Stop.IsCancellationRequested)continue;}
                                await pending.Adapter.SendAsync(pending.Message,answer,cancel).ConfigureAwait(false);
                            }
                        }
                    }catch(OperationCanceledException){}catch(ApiException){SetState(runtime,"im.modelFailed");}catch{SetState(runtime,"im.deliveryFailed");}
                    finally{lock(gate){runtime.ActiveCancel=null;runtime.Active=false;runtime.Scope="";}}
                }}catch(OperationCanceledException){}
        }
        async Task StopRuntime(string id)
        {
            Runtime runtime;lock(gate){if(!running.TryGetValue(id,out runtime)){statuses[id]="im.paused";return;}runtime.Stop.Cancel();runtime.Queue.Clear();}
            await Task.WhenAll(runtime.Network,runtime.Worker).ConfigureAwait(false);
            foreach(var conversation in runtime.Conversations.Values)conversation.Dispose();runtime.Signal.Dispose();runtime.Stop.Dispose();lock(gate){running.Remove(id);statuses[id]="im.paused";}
        }
        public async Task StopAllAsync(){await edits.WaitAsync().ConfigureAwait(false);try{string[] ids;lock(gate)ids=running.Keys.ToArray();foreach(var id in ids)await StopRuntime(id).ConfigureAwait(false);}finally{edits.Release();}Notify();}
        public void Dispose(){if(disposed)return;disposed=true;StopAllAsync().GetAwaiter().GetResult();registration.Dispose();if(ownership!=null)ownership.Dispose();}
        sealed class Pending{internal ImMessage Message;internal string Scope;internal IImAdapter Adapter;}
        sealed class Runtime
        {
            internal ImChannelProfile Profile;internal PetSettings Settings;internal readonly CancellationTokenSource Stop=new CancellationTokenSource();internal CancellationTokenSource ActiveCancel;
            internal Task Network,Worker;internal IImAdapter Adapter;internal bool Active;internal string Scope="";
            internal readonly Queue<Pending> Queue=new Queue<Pending>();internal readonly SemaphoreSlim Signal=new SemaphoreSlim(0);
            internal readonly Dictionary<string,DateTimeOffset> Seen=new Dictionary<string,DateTimeOffset>();internal readonly Dictionary<string,ImConversation> Conversations=new Dictionary<string,ImConversation>();
        }
    }
}
