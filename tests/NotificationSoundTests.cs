using System;
using System.IO;
using System.Linq;
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
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        static void Until(Func<bool> ready,string name){Check(SpinWait.SpinUntil(ready,4000),name);}
        static object Player(PetSettings settings,Func<ILiveAudioOutput> output,Func<bool> busy,Func<DateTime> clock)
        {return new NotificationSoundPlayer(()=>settings,output,busy,clock);}
        static object Player(PetSettings settings,Func<ILiveAudioOutput> output,Func<DateTime> clock)
        {return new NotificationSoundPlayer(()=>settings,output,clock:clock);}
        static bool Play(object player,string cue,string id){return ((NotificationSoundPlayer)player).TryPlay((NotificationCue)Enum.Parse(typeof(NotificationCue),cue),id);}
        static void Stop(object player){((NotificationSoundPlayer)player).Stop();}
        static void Dispose(object player){((IDisposable)player).Dispose();}
#if !NET8_0_OR_GREATER
        [STAThread] public static int Main(string[] args)
        {
            if(args.Length==2&&args[0]=="--theme-wav"){WriteThemeWavs(args[1]);return 0;}
            return Run();
        }
#endif
        public static int Run()
        {
            try
            {
                var themeEnabled=typeof(PetSettings).GetProperty("ThemeTransitionSoundEnabled");
                var themeVolume=typeof(PetSettings).GetProperty("ThemeTransitionSoundVolume");
                Check(themeEnabled!=null&&themeVolume!=null,"theme sounds have independent preferences");
                var enabled=typeof(PetSettings).GetProperty("NotificationSoundsEnabled");
                if(enabled==null)throw new Exception("Missing notification sound preference");
                Check(enabled.PropertyType==typeof(bool),"notification sound preference exists");
                Check(true.Equals(enabled.GetValue(new PetSettings(),null)),"notification sounds default on");
                using(var stream=new MemoryStream(Encoding.UTF8.GetBytes("{}")))
                    Check(true.Equals(enabled.GetValue(new DataContractJsonSerializer(typeof(PetSettings)).ReadObject(stream),null)),"old settings default notification sounds on");
                var settings=new PetSettings();enabled.SetValue(settings,false,null);
                Check(false.Equals(enabled.GetValue(settings.Copy(),null)),"copy preserves disabled sounds");
                string root=Path.Combine("work","notification-sounds-"+Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                try
                {
                    var store=new SettingsStore(Path.Combine(root,"settings.json"));store.Save(settings);
                    Check(false.Equals(enabled.GetValue(store.Load(),null)),"disabled sounds persist");
                    var copy=typeof(SettingsStore).GetMethod("CopyPreferences",BindingFlags.Static|BindingFlags.NonPublic);
                    if(copy==null)throw new Exception("Missing preference copy contract");
                    copy.Invoke(null,new object[]{new PetSettings(),settings});
                    Check(true.Equals(enabled.GetValue(settings,null)),"preference reset restores sounds default");
                }
                finally{Directory.Delete(root,true);}
                Check(typeof(PetSettings).Assembly.GetType("Dafeiyu.NotificationSoundPlayer")!=null&&typeof(PetSettings).Assembly.GetType("Dafeiyu.NotificationCue")!=null,"shared notification sound service exists");
                ThemePreferences();ThemePlayback();ThemeMotionProfiles();ThemeInterruption();DelayedDevice();DeferredTail();
                Resources();Policy(enabled);Lifecycle(enabled);QuietLifecycle();SerializedRestart();
                Console.WriteLine("Notification sound tests: "+checks+" passed");return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL notification sound: "+e);return 1;}
        }
        static void ThemePreferences()
        {
            var settings=new PetSettings();
            Check(!settings.ThemeTransitionSoundEnabled&&settings.ThemeTransitionSoundVolume==30&&settings.ThemeTransitionEffect==ThemeTransitionMotion.RandomSelection,
                "new settings default to Random and theme sounds off at 30 percent");
            using(var stream=new MemoryStream(Encoding.UTF8.GetBytes("{}")))
            {
                var previous=new DataContractJsonSerializer(typeof(PetSettings)).ReadObject(stream) as PetSettings;
                Check(previous!=null&&!previous.ThemeTransitionSoundEnabled&&previous.ThemeTransitionSoundVolume==30&&previous.ThemeTransitionEffect==ThemeTransitionMotion.RandomSelection,
                    "older settings without a selection default to Random and opt-in audio");
            }
            settings.ThemeTransitionSoundEnabled=true;
            settings.ThemeTransitionSoundVolume=0;
            settings.ThemeTransitionEffect="ripple";
            var copy=settings.Copy();
            Check(copy.ThemeTransitionSoundEnabled&&copy.ThemeTransitionSoundVolume==0,"theme volume zero survives a preference copy");
            string root=Path.Combine("work","theme-sounds-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var store=new SettingsStore(Path.Combine(root,"settings.json"));
                store.Save(settings);
                var saved=store.Load();
                Check(saved.ThemeTransitionSoundEnabled&&saved.ThemeTransitionSoundVolume==0&&saved.ThemeTransitionEffect=="ripple",
                    "theme motion, sound and silent volume survive reload");
                var copyPreferences=typeof(SettingsStore).GetMethod("CopyPreferences",BindingFlags.Static|BindingFlags.NonPublic);
                if(copyPreferences==null)throw new Exception("Missing preference copy contract");
                copyPreferences.Invoke(null,new object[]{new PetSettings(),settings});
                Check(!settings.ThemeTransitionSoundEnabled&&settings.ThemeTransitionSoundVolume==30&&settings.ThemeTransitionEffect==ThemeTransitionMotion.RandomSelection,
                    "reset restores the theme motion and sound defaults");
            }
            finally{Directory.Delete(root,true);}
        }

        static void ThemePlayback()
        {
            var settings=new PetSettings();
            var output=new FakeOutput();
            int creates=0;
            var duration=LiquidThemeShape.DefaultDuration;
            using(var player=new NotificationSoundPlayer(()=>settings,()=>{Interlocked.Increment(ref creates);return output;}))
            {
                Check(player.TryPlayTheme(true,duration,false)==null&&creates==0,"default theme preferences leave the device unopened");
                settings.ThemeTransitionSoundEnabled=true;
                var sound=player.TryPlayTheme(true,duration,false);
                if(sound==null)throw new Exception("Theme transition was not admitted");
                Check(player.TryPlayTheme(false,duration,false)==null,"one theme transition owns the shared output");
                Until(()=>output.Plays==1,"drop plays at the start of the reveal");
                sound.Advance(LiquidThemeShape.ImpactProgress-.01);
                Check(output.Plays==1,"splash waits for the actual impact frame");
                sound.Advance(LiquidThemeShape.ImpactProgress);
                Until(()=>output.Plays==2,"impact frame triggers the splash");
                sound.Advance(LiquidThemeShape.ImpactProgress);
                sound.Advance(LiquidThemeShape.FlowProgress);
                Until(()=>output.Plays==3,"rising phase emits continuous flow once");
                Check(output.Rate==48000&&output.Bytes>48000&&output.Volume==30,"flow playback uses the remaining reveal duration and theme volume");
                settings.ThemeTransitionSoundVolume=47;
                player.Refresh();
                Until(()=>output.Volume==47&&output.Plays==3,"volume changes apply without replaying a cue");
                sound.Complete();
                sound.Complete();
                Until(()=>output.Plays==4&&output.Interrupts>0,"normal completion stops the flow and plays one finish");
                Until(()=>output.Disposals==1,"the finish tail releases the shared device");
            }
        }

        static void ThemeMotionProfiles()
        {
            var profiles = new System.Collections.Generic.List<byte[]>();
            foreach (string motion in ThemeTransitionMotion.AnimatedEffects)
            {
                double onset, travel, end;
                ThemeTransitionMotion.SoundStages(motion, out onset, out travel, out end);
                Check(0 < onset && onset < travel && travel < end && end <= 1, motion + " has ordered visual sound stages");
                var duration = TimeSpan.FromMilliseconds(800);
                var light = LiquidThemeAudio.Render(LiquidThemeCue.Flow, false, duration, motion);
                var dark = LiquidThemeAudio.Render(LiquidThemeCue.Flow, true, duration, motion);
                Check(light.Length == 76800 && light.Any(sample => sample != 0) && !light.SequenceEqual(dark),
                    motion + " generates bounded, audible direction-specific PCM");
                Check(profiles.All(other => !light.SequenceEqual(other)), motion + " has its own travel sound");
                profiles.Add(light);
                // The recorded zipper retains more tooth transients than the tonal presets.
                double highFrequencyLimit = motion == ThemeTransitionMotion.Zipper ? .125 : .018;
                foreach (LiquidThemeCue cue in Enum.GetValues(typeof(LiquidThemeCue)))
                {
                    light = LiquidThemeAudio.Render(cue, false, duration, motion);
                    dark = LiquidThemeAudio.Render(cue, true, duration, motion);
                    double lightSlope = PcmSlopeEnergy(light), darkSlope = PcmSlopeEnergy(dark);
                    Check(lightSlope < highFrequencyLimit && darkSlope < lightSlope,
                        motion + " " + cue + " stays mellow and shifts lower for the dark direction");
                }
                if (motion == ThemeTransitionMotion.Flow) continue;

                var settings = new PetSettings { ThemeTransitionSoundEnabled = true };
                var output = new FakeOutput();
                using (var player = new NotificationSoundPlayer(() => settings, () => output))
                {
                    var sound = player.TryPlayTheme(false, TimeSpan.FromSeconds(4), false, motion);
                    if (sound == null) throw new Exception(motion + " transition sound was not admitted");
                    Until(() => output.Plays == 1, motion + " plays one opening cue");
                    sound.Advance(onset);
                    Until(() => output.Plays == 2, motion + " starts its contact cue at its own visual phase");
                    sound.Advance(travel);
                    Until(() => output.Plays == 3, motion + " keeps its movement sound in sync");
                    sound.Cancel();
                    sound.Complete();
                    Until(() => output.Disposals == 1 && output.Plays == 3, motion + " cancellation suppresses its finish cue");
                }
            }
        }

        static double PcmSlopeEnergy(byte[] pcm)
        {
            double energy = 0, slope = 0;
            int previous = 0;
            for (int i = 0; i < pcm.Length; i += 2)
            {
                int sample = (short)(pcm[i] | pcm[i + 1] << 8);
                double delta = sample - previous;
                energy += (double)sample * sample;
                slope += delta * delta;
                previous = sample;
            }
            return slope / energy;
        }

        static void ThemeInterruption()
        {
            var settings=new PetSettings{ThemeTransitionSoundEnabled=true};
            var duration=LiquidThemeShape.DefaultDuration;
            var output=new FakeOutput();
            bool busy=false;
            using(var player=new NotificationSoundPlayer(()=>settings,()=>output,()=>busy))
            {
                settings.QuietMode=true;
                Check(player.TryPlayTheme(true,duration,false)==null,"quiet mode suppresses enabled theme feedback");
                settings.QuietMode=false;
                settings.NotificationSoundsEnabled=false;
                Check(player.TryPlayTheme(true,duration,false)==null,"master sound mute suppresses theme feedback");
                settings.NotificationSoundsEnabled=true;
                busy=true;
                Check(player.TryPlayTheme(true,duration,false)==null,"speech and live audio have priority");
                busy=false;
                var sound=player.TryPlayTheme(false,duration,false);
                Until(()=>output.Plays==1,"theme cue starts when idle");
                sound.Advance(LiquidThemeShape.FlowProgress);
                Until(()=>output.Plays==2,"a skipped impact frame avoids stale splash and starts the current flow");
                settings.ThemeTransitionSoundVolume=0;
                player.Refresh();
                Until(()=>output.Interrupts>0&&output.Disposals==1,"zero volume immediately interrupts and releases the flow");
                sound.Complete();
                Check(output.Plays==2,"cancelled flow has no finish");
            }
            output=new FakeOutput();
            settings.ThemeTransitionSoundVolume=30;
            using(var player=new NotificationSoundPlayer(()=>settings,()=>output))
            {
                var sound=player.TryPlayTheme(false,duration,true);
                sound.Advance(LiquidThemeShape.FlowProgress);
                sound.Complete();
                Until(()=>output.Disposals==1,"reduced motion plays a short cue and releases it");
                Check(output.Plays==1&&output.Bytes<48000,"reduced motion omits the streaming and finish cues");
            }
            output=new FakeOutput{ThrowOnEnqueue=true};
            using(var player=new NotificationSoundPlayer(()=>settings,()=>output))
            {
                Check(player.TryPlayTheme(false,duration,false)!=null,"device errors are handled asynchronously");
                Until(()=>output.Disposals==1,"a playback error releases the output");
            }
        }

        static void DelayedDevice()
        {
            var settings=new PetSettings{ThemeTransitionSoundEnabled=true};
            var output=new DelayedOutput();
            using(var player=new NotificationSoundPlayer(()=>settings,()=>output))
            {
                var sound=player.TryPlayTheme(false,LiquidThemeShape.DefaultDuration,false);
                if(sound==null)throw new Exception("Theme transition was not admitted");
                try
                {
                    Check(output.Entered.Wait(4000),"device opens lazily on the first cue");
                    sound.Advance(LiquidThemeShape.FlowProgress);
                    output.Ready.Set();
                    Until(()=>output.Plays==1,"only the current phase is submitted after a slow device opens");
                    Check(output.Bytes>48000,"the expired drop never enters the output queue");
                    sound.Cancel();
                    Until(()=>output.Disposals==1,"cancelling delayed playback still releases its device");
                }
                finally{output.Ready.Set();}
            }
        }

        sealed class DelayedOutput : IGuardedLiveAudioOutput
        {
            internal readonly ManualResetEventSlim Entered=new ManualResetEventSlim();
            internal readonly ManualResetEventSlim Ready=new ManualResetEventSlim();
            internal int Plays,Bytes,Disposals;
            internal volatile bool Pending;
            public bool HasPendingAudio {get{return Pending;}}
            public void Enqueue(byte[] bytes,int rate){throw new InvalidOperationException("Expected guarded submission");}
            public bool TryEnqueue(byte[] bytes,int rate,Func<bool> permit)
            {
                Entered.Set();
                Ready.Wait();
                if(!permit())return false;
                Bytes=bytes.Length;
                Interlocked.Increment(ref Plays);
                return true;
            }
            public void Interrupt(){ }
            public void SetVolume(int _){ }
            public void Dispose(){Interlocked.Increment(ref Disposals);}
        }

        static void DeferredTail()
        {
            var settings=new PetSettings{ThemeTransitionSoundEnabled=true};
            var output=new DelayedOutput{Pending=true};
            output.Ready.Set();
            using(var player=new NotificationSoundPlayer(()=>settings,()=>output))
            {
                Check(player.TryPlayTheme(false,LiquidThemeShape.DefaultDuration,true)!=null,"short cue is accepted on an idle native stream");
                Until(()=>output.Plays==1,"the short cue is queued");
                Check(!SpinWait.SpinUntil(()=>output.Disposals!=0,240),"native playback acknowledgement controls the tail instead of PCM duration");
                output.Pending=false;
                Until(()=>output.Disposals==1,"the acknowledged native tail is released");
            }
        }

        static void WriteThemeWavs(string output)
        {
            Directory.CreateDirectory(output);
            foreach (string motion in ThemeTransitionMotion.AnimatedEffects)
            foreach (bool dark in new[] { true, false })
            {
                int rate=LiquidThemeAudio.SampleRate;
                var duration=ThemeTransitionMotion.Duration(motion, LiquidThemeShape.DefaultDuration);
                double onset, travel, end;
                ThemeTransitionMotion.SoundStages(motion, out onset, out travel, out end);
                int frames=(int)(rate*duration.TotalSeconds);
                var outro=LiquidThemeAudio.Render(LiquidThemeCue.Finish,dark,TimeSpan.Zero,motion);
                var samples=new short[frames+outro.Length/2];
                Mix(samples,0,LiquidThemeAudio.Render(LiquidThemeCue.Drop,dark,TimeSpan.Zero,motion),(int)(frames*onset));
                Mix(samples,(int)(frames*onset),LiquidThemeAudio.Render(LiquidThemeCue.Splash,dark,TimeSpan.Zero,motion),(int)(frames*(travel-onset)));
                Mix(samples,(int)(frames*travel),LiquidThemeAudio.Render(LiquidThemeCue.Flow,dark,
                    TimeSpan.FromSeconds(duration.TotalSeconds*(end-travel)),motion));
                Mix(samples,frames,outro);
                string file=Path.Combine(output,"DeskPet-"+motion+"-"+(dark?"Light-to-Dark":"Dark-to-Light")+".wav");
                using(var writer=new BinaryWriter(File.Create(file)))
                {
                    writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+samples.Length*2);
                    writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);
                    writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);
                    writer.Write((short)2);writer.Write((short)16);
                    writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(samples.Length*2);
                    foreach(short sample in samples)writer.Write((short)(sample*30/100));
                }
            }
        }

        static void Mix(short[] target,int offset,byte[] pcm,int limit=int.MaxValue)
        {
            int frames=Math.Min(Math.Min(pcm.Length/2,limit),target.Length-offset);
            int fade=frames<pcm.Length/2?Math.Min(frames,LiquidThemeAudio.SampleRate/100):0;
            for(int i=0;i<frames;i++)
            {
                var sample=(short)(pcm[i*2]|pcm[i*2+1]<<8);
                if(i>=frames-fade)sample=(short)(sample*(frames-i-1)/fade);
                target[offset+i]+=sample;
            }
        }

        static void Resources()
        {
            foreach(string file in new[]{"task-complete","review-needed","permission-needed","attention-failed"})
            using(var stream=typeof(PetSettings).Assembly.GetManifestResourceStream("Dafeiyu.Sounds."+file+".wav"))
            {
                Check(stream!=null,"embedded approved cue "+file);
                if(stream==null)throw new Exception("Missing approved cue "+file);
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
                Check(!((NotificationSoundPlayer)player).TryPlay(NotificationCue.Review,null)&&!Play(player,"Review",""),"empty event identity suppressed");
            }
            finally{Dispose(player);}
        }
        static void Lifecycle(PropertyInfo enabled)
        {
            var settings=new PetSettings();DateTime now=new DateTime(2026,11,5);var entered=new ManualResetEventSlim();var release=new ManualResetEventSlim();var output=new FakeOutput();
            int creates=0;var player=Player(settings,()=>{Interlocked.Increment(ref creates);entered.Set();release.Wait();return output;},()=>now);
            Check(Play(player,"Review","waiting"),"pending cue admitted");Until(()=>entered.IsSet,"device factory runs on background worker");
            var admission=Task.Run(()=>Play(player,"Permission","while-factory-blocked"));Check(admission.Wait(500)&&!admission.Result,"new event admission does not wait for device creation");
            var stop=Task.Run(()=>{Stop(player);Dispose(player);});Check(stop.Wait(500),"stop and dispose do not wait for device creation");
            release.Set();Until(()=>output.Disposals==1,"device created during shutdown is disposed");
            Check(output.Plays==0&&!Play(player,"Complete","closed")&&creates==1,"shutdown prevents pending and future playback");
            output=new FakeOutput();player=Player(settings,()=>output,()=>now);
            try
            {
                Check(Play(player,"Complete","interrupt"),"playback admitted before explicit mute");Until(()=>output.Plays==1,"playback begins before mute");
                enabled.SetValue(settings,false,null);Stop(player);Until(()=>output.Disposals==1,"mute stops already playing cue");
            }
            finally{Dispose(player);}
            enabled.SetValue(settings,true,null);int calls=0;
            player=Player(settings,()=>{Interlocked.Increment(ref calls);throw new InvalidOperationException("device unavailable");},()=>now);
            try{Check(Play(player,"Failure","unavailable"),"device failure stays asynchronous");Until(()=>calls==1,"unavailable factory attempted once");now=now.AddSeconds(4);int retry=0;Until(()=>Play(player,"Review","retry-"+Interlocked.Increment(ref retry)),"unavailable audio safely clears pending playback");}
            finally{Dispose(player);}
            output=new FakeOutput{ThrowOnEnqueue=true};player=Player(settings,()=>output,()=>now);
            try{Check(Play(player,"Complete","device-error"),"enqueue failure stays asynchronous");Until(()=>output.Disposals==1,"failed output is released");}
            finally{Dispose(player);}
        }
        static void QuietLifecycle()
        {
            var settings=new PetSettings();DateTime now=new DateTime(2026,11,5);var entered=new ManualResetEventSlim();var release=new ManualResetEventSlim();var output=new FakeOutput();int creates=0;
            var player=Player(settings,()=>{Interlocked.Increment(ref creates);entered.Set();release.Wait();return output;},()=>now);
            try
            {
                Check(Play(player,"Complete","pending-before-quiet"),"pending sound accepted before quiet");Until(()=>entered.IsSet,"pending factory entered before quiet");
                settings.QuietMode=true;Stop(player);now=now.AddSeconds(4);Check(!Play(player,"Review","during-quiet"),"quiet suppresses new pending events");
                settings.QuietMode=false;release.Set();Until(()=>output.Disposals==1,"quiet cancels pending output even when quiet ends before factory returns");
                Check(output.Plays==0&&creates==1,"pending cue does not play after quiet is disabled");
                Check(!Play(player,"Complete","pending-before-quiet")&&!Play(player,"Review","during-quiet"),"quiet exit never replays old or suppressed events");
            }
            finally{release.Set();Dispose(player);}
            output=new FakeOutput();player=Player(settings,()=>output,()=>now);
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
            var player=Player(settings,()=>{if(Interlocked.Increment(ref creates)==1){firstEntered.Set();releaseFirst.Wait();return first;}secondEntered.Set();return second;},()=>now);
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
