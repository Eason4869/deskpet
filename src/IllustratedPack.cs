using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu
{
    [DataContract] public sealed class IllustratedAnimation
    {
        [DataMember(Name="file")] public string File {get;set;}
        [DataMember(Name="columns")] public int Columns {get;set;}
        [DataMember(Name="frames")] public int Frames {get;set;}
        [DataMember(Name="fps")] public double Fps {get;set;}
        [DataMember(Name="loop")] public bool Loop {get;set;}
        [DataMember(Name="hold_last_seconds",EmitDefaultValue=false)] public double HoldLastSeconds {get;set;}
        public double PoseDuration {get{return Frames/Fps;}}
        public double CompletionDuration {get{return PoseDuration+HoldLastSeconds;}}
    }
    [DataContract] public sealed class IllustratedGaze
    {
        [DataMember(Name="file")] public string File {get;set;}
        [DataMember(Name="columns")] public int Columns {get;set;}
        [DataMember(Name="frames")] public int Frames {get;set;}
        [DataMember(Name="interpolation")] public string Interpolation {get;set;}
        [DataMember(Name="order")] public string Order {get;set;}
    }
    public static class IllustratedPack
    {
        public static readonly string[] Actions={"idle","running-right","running-left","waving","jumping","failed","waiting","running","review","lifted","landing","petting","poked","feeding","happy","sleeping","special"};
        public static HashSet<string> Files(SkinManifest manifest)
        {
            var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase){manifest.Preview,manifest.Gaze.File};
            foreach(var set in new[]{manifest.Animations,manifest.ExtraAnimations,manifest.AlternateAnimations})if(set!=null)foreach(var clip in set.Values){if(clip==null)throw new InvalidOperationException(TextResources.Get("illustratedPack.files.text"));files.Add(clip.File);}
            if(!String.IsNullOrEmpty(manifest.Avatar))files.Add(manifest.Avatar);return files;
        }
        public static string AssetPath(string folder,string relative)
        {
            if(String.IsNullOrWhiteSpace(relative) || !relative.EndsWith(".png",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(TextResources.Get("illustratedPack.assetPath.text"));
            string root=Path.GetFullPath(folder);
            if(root.StartsWith(@"\\"))throw new InvalidOperationException(TextResources.Get("illustratedPack.assetPath.text.alternativeA"));
            try {return CompanionPackStorage.AssetPath(root,relative);}
            catch(InvalidDataException error){throw new InvalidOperationException(error.Message,error);}
        }
        public static IList<string> Validate(string folder,SkinManifest manifest,bool inspectFrames)
        {
            if(manifest.Format!="illustrated-atlases-v1" || manifest.CellWidth!=480 || manifest.CellHeight!=520)throw new InvalidOperationException(TextResources.Get("illustratedPack.validate.text"));
            if((manifest.SpecialAction??"").Length>32)throw new InvalidOperationException(TextResources.Get("illustratedPack.validate.text.alternativeA"));
            if(manifest.Animations==null || manifest.Animations.Count!=Actions.Length || Actions.Any(a=>!manifest.Animations.ContainsKey(a)))throw new InvalidOperationException(TextResources.Get("illustratedPack.validate.text.alternativeB"));
            var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long size=0;int frames=0;
            foreach(string action in Actions) {
                var clip=manifest.Animations[action];
                ValidateHold(action,clip);
                if(clip==null || clip.Columns!=8 || clip.Frames<1 || clip.Frames>128 || Double.IsNaN(clip.Fps) || Double.IsInfinity(clip.Fps) || clip.Fps<1 || clip.Fps>60)throw new InvalidOperationException(TextResources.Get("illustratedPack.validate.text.alternativeC"));
                frames+=clip.Frames;
                if(frames>1024)throw new InvalidOperationException(TextResources.Get("illustratedPack.validate.text.alternativeD"));
                string path=AssetPath(folder,clip.File);if(files.Add(clip.File))size+=new FileInfo(path).Length;
                if(size>256L*1024*1024)throw new InvalidOperationException(TextResources.Get("illustratedPack.validate.text.alternativeE"));
                ValidateImage(path,480*8,520*((clip.Frames+7)/8),inspectFrames,clip.Frames,8);
            }
            if(!manifest.Animations["idle"].Loop)throw new InvalidOperationException(TextResources.Get("illustratedPack.validate.text.alternativeF"));
            var gaze=manifest.Gaze;
            if(gaze==null || gaze.Columns!=4 || gaze.Frames!=16 || gaze.Interpolation!="none" || gaze.Order!="clockwise-from-up")throw new InvalidOperationException(TextResources.Get("illustratedPack.validate.text.alternativeG"));
            string gazePath=AssetPath(folder,gaze.File),preview=AssetPath(folder,manifest.Preview);
            if(files.Add(gaze.File))size+=new FileInfo(gazePath).Length;
            if(files.Add(manifest.Preview))size+=new FileInfo(preview).Length;
            if(size>256L*1024*1024)throw new InvalidOperationException(TextResources.Get("illustratedPack.validate.text.alternativeE"));
            ValidateImage(gazePath,1920,2080,inspectFrames,16,4);
            ValidateImage(preview,480,520,inspectFrames,1,1);
            if(!String.IsNullOrEmpty(manifest.PackageId) && !CompanionPackStorage.ValidId(manifest.PackageId))throw new InvalidOperationException(TextResources.Get("illustratedPack.validate.text.alternativeH"));
            if((manifest.PackVersion??"").Length>32 || (manifest.SpecialAction2??"").Length>32)throw new InvalidOperationException(TextResources.Get("illustratedPack.validate.text.alternativeI"));
            var voices=manifest.VoicePresets;
            if(voices!=null && ((voices.Description??"").Length>280 || (voices.SampleText??"").Length>200 ||
                new[]{voices.Speech,voices.Live}.Any(v=>v!=null && (v.Length>16 || v.Any(id=>String.IsNullOrWhiteSpace(id) || id.Length>64)))))throw new InvalidOperationException(TextResources.Get("illustratedPack.validate.text.alternativeJ"));
            foreach(var set in new[]{manifest.ExtraAnimations,manifest.AlternateAnimations})if(set!=null)foreach(var item in set) {
                bool extra=ReferenceEquals(set,manifest.ExtraAnimations);
                string[] allowed=extra?new[]{"stumble","special2"}:new[]{"lifted","landing","feeding","happy","sleeping","special","special2","stumble","jumping"};
                var clip=item.Value;
                ValidateHold(item.Key,clip);
                if(!allowed.Contains(item.Key) || clip==null || clip.Columns!=8 || clip.Frames<1 || clip.Frames>128 || Double.IsNaN(clip.Fps) || Double.IsInfinity(clip.Fps) || clip.Fps<1 || clip.Fps>60)throw new InvalidOperationException(TextResources.Get("illustratedPack.validate.text.alternativeK"));
                if(!extra) {
                    IllustratedAnimation first;
                    if(!manifest.Animations.TryGetValue(item.Key,out first) && (manifest.ExtraAnimations==null || !manifest.ExtraAnimations.TryGetValue(item.Key,out first)))throw new InvalidOperationException(TextResources.Get("illustratedPack.validate.text.alternativeL"));
                    if(clip.Loop!=first.Loop || item.Key=="jumping" && Math.Abs(clip.CompletionDuration-first.CompletionDuration)>1e-8)throw new InvalidOperationException(TextResources.Get("illustratedPack.validate.text.alternativeM"));
                }
                string asset=AssetPath(folder,clip.File);if(files.Add(clip.File))size+=new FileInfo(asset).Length;
                frames+=clip.Frames;if(size>256L*1024*1024 || frames>1024)throw new InvalidOperationException(TextResources.Get("illustratedPack.validate.text.alternativeN"));
                ValidateImage(asset,3840,520*((clip.Frames+7)/8),inspectFrames,clip.Frames,8);
            }
            if(!String.IsNullOrEmpty(manifest.Avatar)) {
                string avatar=AssetPath(folder,manifest.Avatar);if(files.Add(manifest.Avatar))size+=new FileInfo(avatar).Length;
                using(var input=File.OpenRead(avatar)) {
                    if(input.Length>25L*1024*1024 || size>256L*1024*1024)throw new InvalidOperationException(TextResources.Get("illustratedPack.validate.text.alternativeO"));
                    var header=new byte[33];if(input.Read(header,0,header.Length)!=header.Length)throw new InvalidOperationException(TextResources.Get("illustratedPack.validate.text.alternativeP"));
                    int width=ReadInt(header,16),height=ReadInt(header,20);
                    if(width!=height || width<64 || width>2048)throw new InvalidOperationException(TextResources.Get("illustratedPack.validate.text.alternativeQ"));
                    ReadImage(avatar,width,height);
                }
            }
            RelationshipContent.Validate(manifest.Relationship,manifest.Animations.Keys.Concat(manifest.ExtraAnimations==null?Enumerable.Empty<string>():manifest.ExtraAnimations.Keys));
            return files.ToList();
        }
        internal static void ValidateHold(string action,IllustratedAnimation clip)
        {
            if(clip==null)return;
            double hold=clip.HoldLastSeconds;
            if(Double.IsNaN(hold)||Double.IsInfinity(hold)||hold<0||hold>2||hold>0&&(action!="jumping"||clip.Loop))throw new InvalidOperationException(TextResources.Get("illustratedPack.validateHold.text"));
        }
        static void ValidateImage(string path,int width,int height,bool inspect,int frames,int columns)
        {
            var image=ReadImage(path,width,height);
            if(!inspect)return;
            // One cell buffer bounds validation memory; unused tail cells are ignored.
            var converted=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);
            var pixels=new byte[480*520*4];
            for(int i=0;i<frames;i++) {
                converted.CopyPixels(new Int32Rect(i%columns*480,i/columns*520,480,520),pixels,480*4,0);
                bool visible=false,transparent=false;
                for(int p=3;p<pixels.Length;p+=4){visible|=pixels[p]>16;transparent|=pixels[p]==0;}
                if(!visible || !transparent)throw new InvalidOperationException(TextResources.Get("illustratedPack.validateImage.text"));
            }
        }
        static void ValidateHeader(string path,int width,int height)
        {
            using(var stream=File.OpenRead(path)) {
                if(stream.Length<33 || stream.Length>25L*1024*1024)throw new InvalidOperationException(TextResources.Get("illustratedPack.validateHeader.text"));
                byte[] b=new byte[33];stream.Read(b,0,b.Length);
                byte[] signature={137,80,78,71,13,10,26,10};
                if(!b.Take(8).SequenceEqual(signature) || Encoding.ASCII.GetString(b,12,4)!="IHDR" || ReadInt(b,16)!=width || ReadInt(b,20)!=height)throw new InvalidOperationException(TextResources.Get("illustratedPack.validateHeader.text.alternativeA"));
            }
        }
        static int ReadInt(byte[] b,int i){return b[i]<<24|b[i+1]<<16|b[i+2]<<8|b[i+3];}
        public static BitmapSource ReadImage(string path,int width,int height)
        {
            ValidateHeader(path,width,height);
            try { using(var input=File.OpenRead(path)) {
                var image=BitmapDecoder.Create(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];
                if(image.PixelWidth!=width || image.PixelHeight!=height)throw new InvalidOperationException(TextResources.Get("illustratedPack.readImage.text"));
                image.Freeze();return image;
            } } catch(Exception e) {
                if(!(e is FileFormatException || e is NotSupportedException || e is ArgumentException))throw;
                throw new InvalidOperationException(TextResources.Get("illustratedPack.readImage.text.alternativeA"),e);
            }
        }
    }
}
