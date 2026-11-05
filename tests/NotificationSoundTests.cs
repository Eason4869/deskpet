using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu.Tests
{
    public static class NotificationSoundTests
    {
        static int checks;
        static Type playerType,cueType;
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        static void Until(Func<bool> ready,string name){Check(SpinWait.SpinUntil(ready,4000),name);}
        static object Player(PetSettings settings,Func<ILiveAudioOutput> output,Func<bool> busy,Func<DateTime> clock)
        {return Activator.CreateInstance(playerType,new object[]{new Func<PetSettings>(()=>settings),output,busy,clock});}
        static bool Play(object player,string cue,string id){return (bool)playerType.GetMethod("TryPlay").Invoke(player,new[]{Enum.Parse(cueType,cue),(object)id});}
        static void Stop(object player){playerType.GetMethod("Stop").Invoke(player,null);}
        static void Dispose(object player){((IDisposable)player).Dispose();}
        [STAThread] public static int Main()
        {
            try
            {
                var enabled=typeof(PetSettings).GetProperty("NotificationSoundsEnabled");
                Check(enabled!=null,"notification sound preference exists");
                Check((bool)enabled.GetValue(new PetSettings(),null),"notification sounds default on");
                using(var stream=new MemoryStream(Encoding.UTF8.GetBytes("{}")))
                    Check((bool)enabled.GetValue(new DataContractJsonSerializer(typeof(PetSettings)).ReadObject(stream),null),"old settings default notification sounds on");
                var settings=new PetSettings();enabled.SetValue(settings,false,null);
                Check(!(bool)enabled.GetValue(settings.Copy(),null),"copy preserves disabled sounds");
                string root=Path.Combine("work","notification-sounds-"+Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                try
                {
                    var store=new SettingsStore(Path.Combine(root,"settings.json"));store.Save(settings);
                    Check(!(bool)enabled.GetValue(store.Load(),null),"disabled sounds persist");
                    typeof(SettingsStore).GetMethod("CopyPreferences",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{new PetSettings(),settings});
                    Check((bool)enabled.GetValue(settings,null),"preference reset restores sounds default");
                }
                finally{Directory.Delete(root,true);}
                playerType=typeof(PetSettings).Assembly.GetType("Dafeiyu.NotificationSoundPlayer");cueType=typeof(PetSettings).Assembly.GetType("Dafeiyu.NotificationCue");
                Check(playerType!=null&&cueType!=null,"shared notification sound service exists");
                Resources();Policy(enabled);Lifecycle(enabled);QuietLifecycle();SerializedRestart();
                Console.WriteLine("Notification sound tests: "+checks+" passed");return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL notification sound: "+e);return 1;}
        }
        static void Resources()
        {
            foreach(string file in new[]{"task-complete","review-needed","permission-needed","attention-failed"})
            using(var stream=typeof(PetSettings).Assembly.GetManifestResourceStream("Dafeiyu.Sounds."+file+".wav"))
            {
                Check(stream!=null,"embedded approved cue "+file);
                using(var reader=new BinaryReader(stream))
                {
                    Check(new string(reader.ReadChars(4))=="RIFF","WAV container "+file);reader.ReadInt32();
                    Check(new string(reader.ReadChars(4))=="WAVE","WAV type "+file);
                    Check(new string(reader.ReadChars(4))=="fmt "&&reader.ReadInt32()==16,"PCM format chunk "+file);
                    Check(reader.ReadInt16()==1&&reader.ReadInt16()==1&&reader.ReadInt32()==48000,"mono PCM 48000 "+file);
                    Check(reader.ReadInt32()==96000&&reader.ReadInt16()==2&&reader.ReadInt16()==16,"16 bit audio "+file);
                    Check(new string(reader.ReadChars(4))=="data","PCM data chunk "+file);int length=reader.ReadInt32();
                    Check(length>24000&&length<96000&&length%2==0,"short bounded duration "+file);
                    int peak=0;long energy=0;for(int i=0;i<length/2;i++){int sample=reader.ReadInt16();peak=Math.Max(peak,Math.Abs(sample));energy+=(long)sample*sample;}
                    Check(peak>100&&peak<6554&&energy>0,"gentle unclipped audible amplitude "+file);
                }
            }
        }
        static void Policy(PropertyInfo enabled)
        {
            var settings=new PetSettings{Volume=37};var outputs=new FakeOutput[3];DateTime now=new DateTime(2026,11,5);bool busy=false;int creates=0;
            var player=Player(settings,()=>{int index=Interlocked.Increment(ref creates)-1;return outputs[index]=new FakeOutput();},()=>busy,()=>now);
            try
            {
                Check(creates==0,"audio output created lazily");
                Check(Play(player,"Complete","done"),"completion accepted");Until(()=>outputs[0]!=null&&outputs[0].Plays==1,"completion PCM delivered off caller");var output=outputs[0];
                Check(output.Rate==48000&&output.Bytes>24000&&output.Volume==37,"shared output receives supported PCM and current volume");
                now=now.AddSeconds(4);Check(!Play(player,"Review","overlap"),"active cue never overlaps another cue");
                Until(()=>output.Disposals==1,"short playback finishes and releases device");
                Check(!Play(player,"Complete","done"),"same event is deduplicated");
                Check(Play(player,"Permission","permission"),"permission accepted after cooldown");Until(()=>outputs[1]!=null&&outputs[1].Plays==1,"permission PCM delivered");output=outputs[1];
                Until(()=>output.Disposals==1,"permission finishes and releases device");
                Check(!Play(player,"Review","cooldown"),"rapid separate events respect cooldown");now=now.AddSeconds(4);
                enabled.SetValue(settings,false,null);Check(!Play(player,"Review","disabled"),"disabled preference suppresses cue");enabled.SetValue(settings,true,null);
                settings.QuietMode=true;Check(!Play(player,"Permission","quiet"),"quiet mode suppresses cue");settings.QuietMode=false;
                settings.Volume=0;Check(!Play(player,"Complete","muted"),"zero volume suppresses cue");settings.Volume=37;
                busy=true;Check(!Play(player,"Complete","busy"),"speech or live audio suppresses cue");busy=false;
                Check(!Play(player,"Review","disabled"),"suppressed event cannot reappear later");
                enabled.SetValue(settings,false,null);bool allSuppressed=true;for(int i=0;i<130;i++)allSuppressed&=!Play(player,"Review","history-"+i);Check(allSuppressed,"disabled event history stays silent");enabled.SetValue(settings,true,null);
                Check(Play(player,"Failure","done"),"bounded dedup evicts old events and failure cue is supported");Until(()=>outputs[2]!=null&&outputs[2].Plays==1,"failure PCM delivered");output=outputs[2];
                Stop(player);Until(()=>output.Disposals==1&&output.Interrupts>0,"stop interrupts and disposes output");
                Check(!Play(player,"Review",null)&&!Play(player,"Review",""),"empty event identity suppressed");
            }
            finally{Dispose(player);}
        }
        static void Lifecycle(PropertyInfo enabled)
        {
            var settings=new PetSettings();DateTime now=new DateTime(2026,11,5);var entered=new ManualResetEventSlim();var release=new ManualResetEventSlim();var output=new FakeOutput();
            int creates=0;var player=Player(settings,()=>{Interlocked.Increment(ref creates);entered.Set();release.Wait();return output;},null,()=>now);
            Check(Play(player,"Review","waiting"),"pending cue admitted");Until(()=>entered.IsSet,"device factory runs on background worker");
            var admission=Task.Run(()=>Play(player,"Permission","while-factory-blocked"));Check(admission.Wait(500)&&!admission.Result,"new event admission does not wait for device creation");
            var stop=Task.Run(()=>{Stop(player);Dispose(player);});Check(stop.Wait(500),"stop and dispose do not wait for device creation");
            release.Set();Until(()=>output.Disposals==1,"device created during shutdown is disposed");
            Check(output.Plays==0&&!Play(player,"Complete","closed")&&creates==1,"shutdown prevents pending and future playback");
            output=new FakeOutput();player=Player(settings,()=>output,null,()=>now);
            try
            {
                Check(Play(player,"Complete","interrupt"),"playback admitted before explicit mute");Until(()=>output.Plays==1,"playback begins before mute");
                enabled.SetValue(settings,false,null);Stop(player);Until(()=>output.Disposals==1,"mute stops already playing cue");
            }
            finally{Dispose(player);}
            enabled.SetValue(settings,true,null);int calls=0;
            player=Player(settings,()=>{Interlocked.Increment(ref calls);throw new InvalidOperationException("device unavailable");},null,()=>now);
            try{Check(Play(player,"Failure","unavailable"),"device failure stays asynchronous");Until(()=>calls==1,"unavailable factory attempted once");now=now.AddSeconds(4);int retry=0;Until(()=>Play(player,"Review","retry-"+Interlocked.Increment(ref retry)),"unavailable audio safely clears pending playback");}
            finally{Dispose(player);}
            output=new FakeOutput{ThrowOnEnqueue=true};player=Player(settings,()=>output,null,()=>now);
            try{Check(Play(player,"Complete","device-error"),"enqueue failure stays asynchronous");Until(()=>output.Disposals==1,"failed output is released");}
            finally{Dispose(player);}
        }
        static void QuietLifecycle()
        {
            var settings=new PetSettings();DateTime now=new DateTime(2026,11,5);var entered=new ManualResetEventSlim();var release=new ManualResetEventSlim();var output=new FakeOutput();int creates=0;
            var player=Player(settings,()=>{Interlocked.Increment(ref creates);entered.Set();release.Wait();return output;},null,()=>now);
            try
            {
                Check(Play(player,"Complete","pending-before-quiet"),"pending sound accepted before quiet");Until(()=>entered.IsSet,"pending factory entered before quiet");
                settings.QuietMode=true;Stop(player);now=now.AddSeconds(4);Check(!Play(player,"Review","during-quiet"),"quiet suppresses new pending events");
                settings.QuietMode=false;release.Set();Until(()=>output.Disposals==1,"quiet cancels pending output even when quiet ends before factory returns");
                Check(output.Plays==0&&creates==1,"pending cue does not play after quiet is disabled");
                Check(!Play(player,"Complete","pending-before-quiet")&&!Play(player,"Review","during-quiet"),"quiet exit never replays old or suppressed events");
            }
            finally{release.Set();Dispose(player);}
            output=new FakeOutput();player=Player(settings,()=>output,null,()=>now);
            try
            {
                Check(Play(player,"Permission","playing-before-quiet"),"cue accepted before quiet begins");Until(()=>output.Plays==1,"cue starts before quiet begins");
                settings.QuietMode=true;Stop(player);Until(()=>output.Disposals==1&&output.Interrupts==1,"quiet interrupts and releases playing output");
                settings.QuietMode=false;now=now.AddSeconds(4);Check(!Play(player,"Permission","playing-before-quiet"),"leaving quiet never replays interrupted cue");
            }
            finally{Dispose(player);}
        }
        static void SerializedRestart()
        {
            var settings=new PetSettings();DateTime now=new DateTime(2026,11,5);var firstEntered=new ManualResetEventSlim();var releaseFirst=new ManualResetEventSlim();var secondEntered=new ManualResetEventSlim();
            var first=new FakeOutput();var second=new FakeOutput();int creates=0;
            var player=Player(settings,()=>{if(Interlocked.Increment(ref creates)==1){firstEntered.Set();releaseFirst.Wait();return first;}secondEntered.Set();return second;},null,()=>now);
            try
            {
                Check(Play(player,"Review","old-pending"),"first pending request admitted");Until(()=>firstEntered.IsSet,"first factory held");Stop(player);now=now.AddSeconds(4);
                Check(Play(player,"Complete","new-request"),"new request admitted after explicit stop and cooldown");
                Check(!secondEntered.Wait(100),"replacement waits until old output is released");
                releaseFirst.Set();Until(()=>first.Disposals==1&&second.Plays==1,"replacement starts after cancelled output is released");
                Check(first.Plays==0,"cancelled factory never enqueues late PCM");Dispose(player);Until(()=>second.Disposals==1,"replacement device is disposed on shutdown");
            }
            finally{releaseFirst.Set();Dispose(player);}
        }
        sealed class FakeOutput:ILiveAudioOutput
        {
            internal int Plays,Disposals,Interrupts,Rate,Volume,Bytes;internal bool ThrowOnEnqueue;
            public void SetVolume(int value){Volume=value;}
            public void Enqueue(byte[] bytes,int rate){if(ThrowOnEnqueue)throw new InvalidOperationException("fixture audio failure");Rate=rate;Bytes=bytes.Length;Interlocked.Increment(ref Plays);}
            public void Interrupt(){Interlocked.Increment(ref Interrupts);}
            public void Dispose(){Interlocked.Increment(ref Disposals);}
        }
    }
}
