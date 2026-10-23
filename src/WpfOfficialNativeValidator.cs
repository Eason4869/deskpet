using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Dafeiyu
{
    // Validation retains no decoder/catalog/openers from a movable staging root.
    internal sealed class WpfOfficialNativeValidator
    {
        readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);
        readonly HashSet<string> validated=new HashSet<string>(StringComparer.Ordinal);
        internal async Task ValidateAsync(OfficialCompanionInstallation installation,CancellationToken cancel)
        {
            await gate.WaitAsync(cancel).ConfigureAwait(false);
            try {
                string key=installation.Asset.Id+":"+installation.Asset.Sha256;
                if(validated.Contains(key))return;
                await Task.Run(()=>{
                    ValidateDefault(installation.RootDirectory,cancel);
                    var catalog=new HolidayCatalog(installation.HolidayRootDirectory);
                    if(!String.IsNullOrEmpty(catalog.Warning)||catalog.Outfits.Count!=installation.Asset.Outfits.Count)throw Invalid();
                    foreach(var expected in installation.Asset.Outfits) {
                        var outfit=catalog.Find(installation.Asset.Id,expected.OutfitId);
                        if(outfit==null||!outfit.Ready||outfit.Version!=expected.Version)throw Invalid();
                        foreach(var clip in outfit.Clips.Concat(new[]{outfit.Gaze,outfit.EasterEgg}).GroupBy(c=>c.Path,StringComparer.Ordinal).Select(g=>g.OrderByDescending(c=>c.FrameCount).First())) {
                            cancel.ThrowIfCancellationRequested();
                            using(var stream=clip.OpenResource())ValidateRaster(stream,clip.FrameWidth*clip.Columns,clip.FrameHeight*((clip.FrameCount+clip.Columns-1)/clip.Columns),clip.FrameCount,clip.Columns,true,cancel);
                        }
                        using(var stream=outfit.OpenPreview())ValidateRaster(stream,480,520,1,1,true,cancel);
                    }
                    cancel.ThrowIfCancellationRequested();
                },cancel).ConfigureAwait(false);
                if(validated.Count>=32)validated.Clear();validated.Add(key);
            }finally{gate.Release();}
        }
        internal static void ValidateDefault(string root,CancellationToken cancel)
        {
            SkinManifest manifest;
            using(var stream=File.OpenRead(CompanionPackStorage.AssetPath(root,"pet.json")))manifest=SkinCatalog.ReadManifest(stream);
            IllustratedPack.Validate(root,manifest,false);
            foreach(var clip in new[]{manifest.Animations,manifest.ExtraAnimations,manifest.AlternateAnimations}.Where(s=>s!=null).SelectMany(s=>s.Values).GroupBy(c=>c.File,StringComparer.OrdinalIgnoreCase).Select(g=>g.OrderByDescending(c=>c.Frames).First())) {
                cancel.ThrowIfCancellationRequested();
                ValidatePng(IllustratedPack.AssetPath(root,clip.File),480*clip.Columns,520*((clip.Frames+clip.Columns-1)/clip.Columns),clip.Frames,clip.Columns,true,cancel);
            }
            ValidatePng(IllustratedPack.AssetPath(root,manifest.Gaze.File),1920,2080,16,4,true,cancel);
            ValidatePng(IllustratedPack.AssetPath(root,manifest.Preview),480,520,1,1,true,cancel);
            if(!String.IsNullOrEmpty(manifest.Avatar)) {
                string path=IllustratedPack.AssetPath(root,manifest.Avatar);
                using(var input=File.OpenRead(path)) {
                    var decoder=BitmapDecoder.Create(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
                    var frame=decoder.Frames[0];ValidatePixels(frame,frame.PixelWidth,frame.PixelHeight,1,1,false,cancel);
                }
            }
        }
        internal static void ValidatePng(string path,int width,int height,int frames,int columns,bool inspect,CancellationToken cancel)
        {using(var stream=File.OpenRead(path))ValidateRaster(stream,width,height,frames,columns,inspect,cancel);}
        static void ValidateRaster(Stream stream,int width,int height,int frames,int columns,bool inspect,CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();if(stream==null)throw Invalid();
            try {
                var decoded=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
                if(decoded.Frames.Count!=1)throw Invalid();
                ValidatePixels(decoded.Frames[0],width,height,frames,columns,inspect,cancel);
            }catch(Exception error){if(error is OperationCanceledException)throw;if(error is InvalidDataException)throw;if(error is FileFormatException||error is NotSupportedException||error is ArgumentException||error is InvalidOperationException)throw Invalid();throw;}
        }
        static void ValidatePixels(BitmapSource image,int width,int height,int frames,int columns,bool inspect,CancellationToken cancel)
        {
            if(width<=0||height<=0||width>3840||height>8320||image.PixelWidth!=width||image.PixelHeight!=height)throw Invalid();
            var converted=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);
            // Read every raster row, including atlas tail cells, with bounded memory.
            var row=new byte[checked(width*4)];
            for(int y=0;y<height;y++){cancel.ThrowIfCancellationRequested();converted.CopyPixels(new Int32Rect(0,y,width,1),row,width*4,0);}
            if(!inspect)return;
            int cellWidth=width/columns,cellHeight=height/((frames+columns-1)/columns);
            var pixels=new byte[checked(cellWidth*cellHeight*4)];
            for(int i=0;i<frames;i++) {
                cancel.ThrowIfCancellationRequested();
                converted.CopyPixels(new Int32Rect(i%columns*cellWidth,i/columns*cellHeight,cellWidth,cellHeight),pixels,cellWidth*4,0);
                bool visible=false,transparent=false;
                for(int p=3;p<pixels.Length;p+=4){visible|=pixels[p]>16;transparent|=pixels[p]==0;}
                if(!visible||!transparent)throw Invalid();
            }
        }
        static InvalidDataException Invalid(){return new InvalidDataException(TextResources.Get("wpfOfficialNativeValidator.invalid.text"));}
    }
}
