using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Dafeiyu;
namespace DeskPet.Desktop;
// Page owns decoded sources; they never enter SpriteAssets' evictable image cache.
internal sealed class RelationshipStageArtSource:IDisposable
{
    readonly Func<RelationshipStageArtAsset,Stream> open;
    readonly Dictionary<string,Bitmap?> cache=new(StringComparer.Ordinal);
    string? identity;bool disposed;
    internal RelationshipStageArtSource():this(OpenResource){}
    internal RelationshipStageArtSource(Func<RelationshipStageArtAsset,Stream> opener){open=opener;}
    internal static Stream OpenResource(RelationshipStageArtAsset asset)=>AssetLoader.Open(new Uri("avares://DeskPet/Assets/"+asset.RelativePath));
    internal static IRelationshipSceneProvider CreateProvider()=>new RelationshipSceneProvider(OpenResource);
    internal int Count=>cache.Count;
    internal void SetIdentity(string companionId){if(identity==companionId)return;Clear();identity=companionId;}
    void Clear(){foreach(var image in cache.Values)image?.Dispose();cache.Clear();}
    internal Bitmap? Get(RelationshipCardData data)=>Get(data.CompanionId,data.StageId);
    internal Bitmap? Get(string companionId,string stageId)
    {
        if(disposed)return null;SetIdentity(companionId);
        if(!RelationshipStageArt.TryGet(companionId,stageId,out var asset))return null;
        if(cache.TryGetValue(asset.Version,out var found))return found;
        Bitmap? image=null;
        try {
            byte[] bytes;using(var source=open(asset))bytes=RelationshipStageArt.ReadVerified(source,asset);
            using var stream=new MemoryStream(bytes,false);
            using(var full=new Bitmap(stream))if(full.PixelSize.Width!=asset.PixelWidth||full.PixelSize.Height!=asset.PixelHeight)throw new InvalidDataException("Decoded scene geometry mismatch.");
            stream.Position=0;image=Bitmap.DecodeToWidth(stream,800);
        }catch(Exception error)when(error is InvalidDataException or IOException or ArgumentException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException or System.Security.SecurityException){image?.Dispose();image=null;}
        cache.Add(asset.Version,image);return image;
    }
    public void Dispose(){if(disposed)return;disposed=true;Clear();identity=null;}
}
