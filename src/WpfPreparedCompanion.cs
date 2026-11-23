using System;
using System.Collections.Generic;
using System.Threading.Tasks;
namespace Dafeiyu
{
    internal static class WpfPreparedCompanion
    {
        static readonly CompanionDecodedCache<object> cache=new CompanionDecodedCache<object>(64L*1024*1024);
        internal static long RetainedPixelBytes {get{return cache.RetainedBytes;}}
        internal static long UnpinnedPixelBytes {get{return cache.UnpinnedBytes;}}
        internal static long PinnedPixelBytes {get{return cache.PinnedBytes;}}
        internal static int PinLeaseCount {get{return cache.PinLeaseCount;}}
        internal static int PinnedResourceCount {get{return cache.PinnedResourceCount;}}
        static readonly object gate=new object();
        static readonly HashSet<string> preparing=new HashSet<string>(StringComparer.Ordinal);
        static readonly HashSet<string> failed=new HashSet<string>(StringComparer.Ordinal);
        static string currentAtlas,currentIdle,currentFrame;
        internal static event Action<string> Prepared;
        internal static event Action<SkinDefinition> DefaultFailed;
        static void StartPreparation(string key,Action prepare,SkinDefinition source=null)
        {
            cache.Pin(key);var app=System.Windows.Application.Current;var dispatcher=app==null?null:app.Dispatcher;
            Task.Run(()=>{
                bool failedRead=false;
                try{prepare();}catch(Exception){failedRead=true;cache.Remove(key);lock(gate)failed.Add(key);}
                finally{
                    Action complete=()=>{try{if(failedRead&&source!=null&&DefaultFailed!=null)DefaultFailed(source);if(Prepared!=null)Prepared(key);}finally{cache.Unpin(key);lock(gate)preparing.Remove(key);}};
                    if(dispatcher!=null&&!dispatcher.HasShutdownStarted)dispatcher.BeginInvoke(complete);else complete();
                }
            });
        }

        internal static void PinFrame(MotionClip clip){var key=clip==null?null:cache.KeyFor(clip);if(key==currentFrame)return;if(key!=null)cache.Pin(key);if(currentFrame!=null)cache.Unpin(currentFrame);currentFrame=key;}
        internal static bool HasPending(string generation){lock(gate)return System.Linq.Enumerable.Any(preparing,key=>key.StartsWith(generation+"/",StringComparison.Ordinal)||key.StartsWith("holiday/"+generation+"/",StringComparison.Ordinal));}
        internal static void Retire(string generation){cache.RemovePrefix(generation+"/");cache.RemovePrefix("holiday/"+generation+"/");lock(gate)failed.RemoveWhere(key=>key.StartsWith(generation+"/",StringComparison.Ordinal)||key.StartsWith("holiday/"+generation+"/",StringComparison.Ordinal));}

        static string HolidayKey(OutfitClipDefinition clip){return "holiday/"+clip.SourceKey+"/"+clip.Path+"/wpf-pbgra32-mask8/"+clip.FrameCount+"/"+clip.Columns+"/"+clip.Fps.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"/"+clip.Loop+"/"+clip.FrameWidth+"/"+clip.FrameHeight;}
        internal static void RemoveHoliday(OutfitClipDefinition clip){cache.Remove(HolidayKey(clip));}
        internal static OutfitClipDefinition ReadyHoliday(OutfitClipDefinition source,OutfitClipDefinition idle)
        {
            if(Holiday(source)!=null)return source;
            string key=HolidayKey(source);
            lock(gate){
                if(failed.Contains(key))throw new System.IO.InvalidDataException("Companion costume read failed.");
                if(preparing.Add(key))StartPreparation(key,()=>new HolidayVisualAssets(null).Get(source));
            }
            if(Holiday(idle)!=null)return idle;
            string idleKey=HolidayKey(idle);lock(gate){if(failed.Contains(idleKey))throw new System.IO.InvalidDataException("Companion costume idle read failed.");if(preparing.Add(idleKey))StartPreparation(idleKey,()=>new HolidayVisualAssets(null).Get(idle));}
            return null;
        }
        internal static MotionClip Holiday(OutfitClipDefinition clip){return cache.Get(HolidayKey(clip)) as MotionClip;}
        internal static void Holiday(OutfitClipDefinition source,MotionClip clip){cache.Put(HolidayKey(source),clip,(long)clip.Count*clip.Width*clip.Height*5);}
        internal static string Key(SkinDefinition skin,string path){return (skin.Snapshot==null?skin.NativeSourceKey:skin.Snapshot.Generation)+"/"+path+"/wpf-pbgra32-mask8";}
        internal static SpriteAtlas Atlas(SkinDefinition skin){return cache.Get(Key(skin,"@gaze")) as SpriteAtlas;}
        internal static void Atlas(SkinDefinition skin,SpriteAtlas atlas){cache.Put(Key(skin,"@gaze"),atlas,atlas.DecodedBytes);}
        static string MotionKey(SkinDefinition skin,IllustratedAnimation spec){return Key(skin,spec.File)+"/"+spec.Columns+"/"+spec.Frames+"/"+spec.Fps.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"/"+spec.Loop;}
        internal static MotionClip Load(SkinDefinition skin,IllustratedAnimation spec)
        {
            string key=MotionKey(skin,spec);var clip=cache.Get(key) as MotionClip;if(clip!=null)return clip;
            var image=skin.ReadImage(spec.File,480*spec.Columns,520*((spec.Frames+spec.Columns-1)/spec.Columns));
            clip=new MotionClip(image,spec.Frames,spec.Frames/spec.Fps,spec.Loop,480,520,spec.Columns);
            cache.Put(key,clip,(long)clip.Count*clip.Width*clip.Height*5);return clip;
        }
        internal static bool HasIdle(SkinDefinition skin){return cache.Get(MotionKey(skin,skin.Manifest.Animations["idle"]))!=null;}
        internal static MotionClip Ready(SkinDefinition skin,IllustratedAnimation spec)
        {
            string key=MotionKey(skin,spec);var clip=cache.Get(key) as MotionClip;if(clip!=null)return clip;
            bool retry;lock(gate){retry=failed.Contains(key);if(!preparing.Contains(key)&&!retry){preparing.Add(key);StartPreparation(key,()=>Load(skin,spec),skin);}}
            if(retry&&DefaultFailed!=null)DefaultFailed(skin);
            return cache.Get(MotionKey(skin,skin.Manifest.Animations["idle"])) as MotionClip;
        }
        internal static IDisposable Hold(SkinDefinition skin)
        {
            string a=Key(skin,"@gaze"),b=skin.Illustrated?MotionKey(skin,skin.Manifest.Animations["idle"]):null;
            cache.Pin(a);if(b!=null)cache.Pin(b);return new HoldPins(a,b);
        }
        sealed class HoldPins:IDisposable{string a,b;internal HoldPins(string first,string second){a=first;b=second;}public void Dispose(){if(a!=null)cache.Unpin(a);if(b!=null)cache.Unpin(b);a=b=null;}}
        internal static void Deactivate(){PinFrame(null);if(currentAtlas!=null)cache.Unpin(currentAtlas);if(currentIdle!=null)cache.Unpin(currentIdle);currentAtlas=currentIdle=null;}
        internal static void Activate(SkinDefinition skin)
        {
            string atlas=skin.IsPlaceholder?null:Key(skin,"@gaze"),idle=skin.Illustrated?MotionKey(skin,skin.Manifest.Animations["idle"]):null;
            if(atlas!=null)cache.Pin(atlas);if(idle!=null)cache.Pin(idle);
            if(currentAtlas!=null)cache.Unpin(currentAtlas);if(currentIdle!=null)cache.Unpin(currentIdle);currentAtlas=atlas;currentIdle=idle;
        }
    }
}
