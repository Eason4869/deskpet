using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Web.Script.Serialization;

namespace Dafeiyu
{
    // Each frame is a separately illustrated pose. No blending or interpolation.
    // Two decoded clips are retained; v2 timing and source cells come from the manifest.
    public sealed class SmoothMotion
    {
        public const int Width=240,Height=260;
        internal static readonly double[] Durations={1.02,1.76,1.76,.64,.7,1.28,1.14,1.08,1.08,0,0,1,.64,1.6,.8,1.6,1.2,3.2,2};
        readonly string skin;
        readonly SkinDefinition definition;
        readonly Dictionary<int,MotionClip> clips=new Dictionary<int,MotionClip>();
        readonly LinkedList<int> recent=new LinkedList<int>();
        static Dictionary<string,int> counts;
        static readonly Dictionary<string,MotionRecord> specifications=new Dictionary<string,MotionRecord>();
        static Dictionary<string,int> Counts {
            get {
                if(counts!=null)return counts;
                var loaded=new Dictionary<string,int>();
                using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("Dafeiyu.MotionManifest")) {
                    if(input!=null)using(var reader=new StreamReader(input)) {
                        foreach(var record in new JavaScriptSerializer{MaxJsonLength=8000000}.Deserialize<MotionRecord[]>(reader.ReadToEnd())) {
                            loaded[record.pet+"-"+record.action]=record.frames;
                            specifications[record.pet+"-"+record.action]=record;
                            if(record.action==1 && !record.independent){loaded[record.pet+"-2"]=record.frames;specifications[record.pet+"-2"]=record;}
                        }
                    }
                }
                counts=loaded;return counts;
            }
        }
        public sealed class MotionRecord {public string pet{get;set;}public int action{get;set;}public int frames{get;set;}public double seconds{get;set;}public double fps{get;set;}public bool? loop{get;set;}public bool independent{get;set;}}
        public static bool TryTiming(string skinId,int action,out double duration,out bool loop)
        {
            duration=0;loop=false;int count;
            if(action<0 || action>=Durations.Length || !Counts.TryGetValue(skinId+"-"+action,out count))return false;
            MotionRecord record=specifications[skinId+"-"+action];
            duration=record.seconds>0?record.seconds:record.fps>0?record.frames/record.fps:Durations[action];
            loop=record.loop ?? (action!=4 && action!=12 && action!=13 && action!=14 && action!=15 && action!=16 && action!=18);
            return duration>0 && !Double.IsNaN(duration) && !Double.IsInfinity(duration);
        }
        public SmoothMotion(string skinId){skin=skinId;}
        public SmoothMotion(SkinDefinition value){skin=value.Id;definition=value;}
        public MotionClip Get(int action)
        {
            if(action<0 || action>=Durations.Length || Durations[action]==0)return null;
            int count=0;
            var quiet=action==0?AdditionalInteractions.Quiet(definition,"idle",true):null;
            bool imported=definition!=null && definition.Illustrated;
            if(quiet==null && !imported && !Counts.TryGetValue(skin+"-"+action,out count))return null;
            MotionClip clip;
            if(clips.TryGetValue(action,out clip)){if(recent.Last.Value!=action){var node=recent.Find(action);recent.Remove(node);recent.AddLast(node);}return clip;}
            if(quiet!=null)clip=AdditionalInteractions.LoadQuiet(quiet);
            else if(imported) {
                var spec=definition.Manifest.Animations[IllustratedPack.Actions[action<9?action:action-2]];
                string path=IllustratedPack.AssetPath(definition.Source,spec.File);
                var image=IllustratedPack.ReadImage(path,480*spec.Columns,520*((spec.Frames+spec.Columns-1)/spec.Columns));
                clip=new MotionClip(image,spec.Frames,spec.Frames/spec.Fps,spec.Loop,480,520,spec.Columns);
            } else using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("Dafeiyu.Motion."+skin+"-"+action)) {
                if(input==null)return null;
                double duration;bool loop;if(!TryTiming(skin,action,out duration,out loop))return null;
                clip=new MotionClip(input,count,duration,loop);
            }
            if(clips.Count==2){clips.Remove(recent.First.Value);recent.RemoveFirst();}
            clips[action]=clip;recent.AddLast(action);return clip;
        }
    }
    public sealed class MotionClip
    {
        readonly BitmapSource[] frames;
        readonly byte[][] masks;
        public int Count{get{return frames.Length;}}
        public double Duration{get;private set;}
        public bool Loop{get;private set;}
        public int Width{get;private set;}
        public int Height{get;private set;}
        internal MotionClip(Stream input,int count,double duration,bool loop)
            : this(BitmapDecoder.Create(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0],count,duration,loop,SmoothMotion.Width,SmoothMotion.Height,16) { }
        internal MotionClip(BitmapSource image,int count,double duration,bool loop,int w,int h,int columns)
        {
            Duration=duration;Loop=loop;Width=w;Height=h;frames=new BitmapSource[count];masks=new byte[count][];
            if(image.PixelWidth!=w*columns || image.PixelHeight!=h*((count+columns-1)/columns))throw new InvalidDataException("Invalid motion atlas dimensions.");
            for(int i=0;i<count;i++) {
                var crop=new CroppedBitmap(image,new Int32Rect(i%columns*w,i/columns*h,w,h));
                var converted=new FormatConvertedBitmap(crop,PixelFormats.Pbgra32,null,0);
                byte[] pixels=new byte[w*h*4];converted.CopyPixels(pixels,w*4,0);
                frames[i]=BitmapSource.Create(w,h,96,96,PixelFormats.Pbgra32,null,pixels,w*4);frames[i].Freeze();
                var mask=new byte[w*h];for(int p=0;p<mask.Length;p++)mask[p]=pixels[p*4+3];masks[i]=mask;
            }
        }
        public int Sample(double seconds)
        {
            if(Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds<=0)return 0;
            double phase=Loop?(seconds%Duration)/Duration:Math.Min(1,seconds/Duration);
            return Math.Min(Count-1,(int)Math.Floor(phase*Count+1e-8));
        }
        public BitmapSource Frame(int sample){return frames[sample];}
        public byte AlphaAt(int sample,int x,int y){return x<0 || y<0 || x>=Width || y>=Height?(byte)0:masks[sample][y*Width+x];}
    }
}
