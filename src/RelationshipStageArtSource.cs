using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;
namespace Dafeiyu
{
    // Page owns this bounded cache. Renderers borrow frozen sources and never decode during paint.
    internal sealed class RelationshipStageArtSource:IDisposable
    {
        readonly Func<RelationshipStageArtAsset,Stream> open;
        readonly Dictionary<string,BitmapSource> cache=new Dictionary<string,BitmapSource>();
        string identity;bool disposed;
        internal RelationshipStageArtSource():this(OpenResource){}
        internal RelationshipStageArtSource(Func<RelationshipStageArtAsset,Stream> opener){open=opener;}
        internal static Stream OpenResource(RelationshipStageArtAsset asset){return typeof(RelationshipStageArtSource).Assembly.GetManifestResourceStream("Dafeiyu."+asset.RelativePath.Replace('/','.'));}
        internal static IRelationshipSceneProvider CreateProvider(){return new RelationshipSceneProvider(OpenResource);}
        internal int Count {get{return cache.Count;}}
        internal void SetIdentity(string companionId){if(identity==companionId)return;cache.Clear();identity=companionId;}
        internal BitmapSource Get(RelationshipCardData data){return Get(data.CompanionId,data.StageId);}
        internal BitmapSource Get(string companionId,string stageId)
        {
            if(disposed)return null;SetIdentity(companionId);RelationshipStageArtAsset asset;
            if(!RelationshipStageArt.TryGet(companionId,stageId,out asset))return null;
            BitmapSource value;if(cache.TryGetValue(asset.Version,out value))return value;
            value=null;
            try {
                byte[] bytes;using(var source=open(asset))bytes=RelationshipStageArt.ReadVerified(source,asset);
                using(var stream=new MemoryStream(bytes,false)){
                    var full=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
                    if(full.Frames.Count!=1||full.Frames[0].PixelWidth!=asset.PixelWidth||full.Frames[0].PixelHeight!=asset.PixelHeight)throw new InvalidDataException("Decoded scene geometry mismatch.");
                    // CopyPixels forces full compressed-pixel validation before accepting the scene.
                    var frame=full.Frames[0];int stride=(frame.PixelWidth*frame.Format.BitsPerPixel+7)/8;frame.CopyPixels(new byte[stride*frame.PixelHeight],stride,0);
                    stream.Position=0;var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.DecodePixelWidth=800;image.StreamSource=stream;image.EndInit();image.Freeze();value=image;
                }
            }catch(Exception error){if(!IsSourceFailure(error))throw;}
            cache.Add(asset.Version,value);return value;
        }
        static bool IsSourceFailure(Exception error){return error is InvalidDataException||error is IOException||error is ArgumentException||error is InvalidOperationException||error is NotSupportedException||error is System.Security.SecurityException||error is UnauthorizedAccessException||error is System.Runtime.InteropServices.COMException||error is FileFormatException;}
        public void Dispose(){if(disposed)return;disposed=true;cache.Clear();identity=null;}
    }
}
