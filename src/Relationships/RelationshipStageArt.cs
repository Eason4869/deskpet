using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
namespace Dafeiyu
{
    // Only the controlled catalog can create descriptors; arbitrary names/paths never enter selection.
    public sealed class RelationshipStageArtAsset
    {
        public string CompanionId {get;private set;}
        public string CharacterId {get;private set;}
        public string StageId {get;private set;}
        public string RelativePath {get;private set;}
        public string SceneId {get;private set;}
        public string SceneVersion {get;private set;}
        public string Version {get{return SceneVersion;}}
        public string HolidayId {get;private set;}
        public string OutfitId {get;private set;}
        public string OutfitVersion {get;private set;}
        public int PixelWidth {get;private set;}
        public int PixelHeight {get;private set;}
        public int ByteLength {get;private set;}
        public string Sha256 {get;private set;}
        // Reviewed future holiday manifests use the same immutable contract. This
        // remains internal: no external arbitrary-file descriptor/import API.
        internal RelationshipStageArtAsset(string companion,string holiday,string outfit,string outfitVersion,
            string scene,string sceneVersion,string path,int width,int height,int bytes,string hash,string stage)
        {
            if(String.IsNullOrEmpty(companion)||!companion.StartsWith("official:",StringComparison.Ordinal)
                ||!HolidayKeepsakeDefinition.ValidKey(holiday)||!HolidayKeepsakeDefinition.ValidKey(outfit)
                ||!HolidayKeepsakeDefinition.ValidKey(outfitVersion)||!HolidayKeepsakeDefinition.ValidKey(scene)
                ||!HolidayKeepsakeDefinition.ValidKey(sceneVersion))throw new ArgumentException("Invalid reviewed scene metadata.");
            CompanionId=companion;CharacterId=companion.Substring(9);StageId=stage;RelativePath=path;
            HolidayId=holiday;OutfitId=outfit;OutfitVersion=outfitVersion;SceneId=scene;SceneVersion=sceneVersion;
            PixelWidth=width;PixelHeight=height;ByteLength=bytes;Sha256=hash;
        }
        internal RelationshipStageArtAsset(string companion,string stage,string path,int width,int height,int bytes,string hash)
        {
            CompanionId=companion;CharacterId=companion.Substring(9);StageId=stage;RelativePath=path;
            PixelWidth=width;PixelHeight=height;ByteLength=bytes;Sha256=hash;
            SceneId=stage=="halloween"?"holiday:halloween:"+CharacterId:"stage:"+CharacterId+":"+stage;
            SceneVersion="generated-1-"+hash.Substring(0,16);
            HolidayId=stage=="halloween"?"halloween":null;OutfitId=HolidayId==null?null:"halloween-2026";OutfitVersion=HolidayId==null?null:"1.0.0";
        }
    }
    public static class RelationshipStageArt
    {
        public const string Version="relationship-scenes-1";
        public const int MaximumBytes=12*1024*1024;
        static readonly ReadOnlyCollection<RelationshipStageArtAsset> all=Array.AsReadOnly(new[]{
            new RelationshipStageArtAsset("official:dafeiyu","first_meeting","relationships/dafeiyu/first_meeting.png",1619,971,2342411,"c694c4243075c7a58c5a2348eac32110361f5c3cb0cce985c1bdd9e7b5ac6759"),
            new RelationshipStageArtAsset("official:dafeiyu","familiar","relationships/dafeiyu/familiar.png",1619,971,2445771,"5d5953cfc06d6f634cb2fc5b748f3059b6f170481aac7e76fd655aec8dfda385"),
            new RelationshipStageArtAsset("official:dafeiyu","close","relationships/dafeiyu/close.png",1619,971,2405927,"39428808f58e38a1faa4ad501fdb0522f0b1d094d07e0e897c6ada6f7af00c72"),
            new RelationshipStageArtAsset("official:dafeiyu","trusted","relationships/dafeiyu/trusted.png",1619,971,2298808,"738164e660c38c6f53cefddc4c6654e80df49208ff39910e7e87781f5a05d202"),
            new RelationshipStageArtAsset("official:dafeiyu","heartfelt","relationships/dafeiyu/heartfelt.png",1619,971,2392161,"c89bd12610f2581efedad352338dbe1eab77d30cbb5e6e47e842da11033263d8"),
            new RelationshipStageArtAsset("official:dafeiyu","best_friend","relationships/dafeiyu/best_friend.png",1619,971,2596199,"5b2bf58b96857be819d5e952e4efde131aaa8cdb30098cc11ce954bf75f6741d"),
            new RelationshipStageArtAsset("official:marfu","first_meeting","relationships/marfu/first_meeting.png",1619,972,2504229,"d1495a5878768cc1a2db3e5d62cd2081abf34f3c1c5cae8ed0830dc30c2d3ad9"),
            new RelationshipStageArtAsset("official:marfu","familiar","relationships/marfu/familiar.png",1619,971,2505700,"64c1d1af95e835026cc29f730a66229e56838857b5df84d48b1df6154840019a"),
            new RelationshipStageArtAsset("official:marfu","close","relationships/marfu/close.png",1619,971,2409538,"6bb4a8aa9243c3352b2156781a5269a656e184c97bf989d09203616e13575b1f"),
            new RelationshipStageArtAsset("official:marfu","trusted","relationships/marfu/trusted.png",1619,971,2439980,"1aae25d2cdd451f643f6603bf1d431469432c36ccff6089b25b5b15c584d8fbb"),
            new RelationshipStageArtAsset("official:marfu","heartfelt","relationships/marfu/heartfelt.png",1619,972,2574268,"75097bba41d2520e7d0dd378fd1512c1d70464a404f634810e3f322c425fd4ff"),
            new RelationshipStageArtAsset("official:marfu","best_friend","relationships/marfu/best_friend.png",1619,971,2612787,"8b59ce77c03ef9c26d34c6e868d1f3294d3284daf37978a60d8ccd987ef5da7a"),
            new RelationshipStageArtAsset("official:xiaobei","first_meeting","relationships/xiaobei/first_meeting.png",1619,972,2294119,"4275a93b445d06bb09a9d3bfb81ffb4298723326bd80a42054bc7983df181dbd"),
            new RelationshipStageArtAsset("official:xiaobei","familiar","relationships/xiaobei/familiar.png",1619,971,2218579,"71f633544f1dffede9715769f2fe4bf93a440f92cf00b20839ca4c6abcce77fe"),
            new RelationshipStageArtAsset("official:xiaobei","close","relationships/xiaobei/close.png",1619,971,2265056,"fc73e511c2dc0264a6b68a1926a293bbae94c68a75fbb7acc853bf6b6f6f3ccb"),
            new RelationshipStageArtAsset("official:xiaobei","trusted","relationships/xiaobei/trusted.png",1619,971,2259985,"e1ab49c2ce25dd038d76ace3c1676fb9bc3fc408d9905d9b8d4a5be2b0a1e6e0"),
            new RelationshipStageArtAsset("official:xiaobei","heartfelt","relationships/xiaobei/heartfelt.png",1619,971,2302117,"1a7b6b8a16fb978c23d25c8adf9f84273f85c85573df6e0f48720d32853bd9bf"),
            new RelationshipStageArtAsset("official:xiaobei","best_friend","relationships/xiaobei/best_friend.png",1619,971,2492807,"9695026ac2e166cae3131f1a7818129255da5b182be601efaa5e3e8ae5ef9725"),
            new RelationshipStageArtAsset("official:gugugaga","first_meeting","relationships/gugugaga/first_meeting.png",1619,971,2256517,"e0910454d72b8f0f3a009a0e1e6c35ada23dd8cb8380100484747aad3f37bfe5"),
            new RelationshipStageArtAsset("official:gugugaga","familiar","relationships/gugugaga/familiar.png",1619,971,2286984,"8b354c97b6298098f1c3608af6256185b75d3cc720f9fe8b0ba5791b57b8aed6"),
            new RelationshipStageArtAsset("official:gugugaga","close","relationships/gugugaga/close.png",1619,971,2279316,"77c2e973cde481ac537e02b900d959f2e53fb8aac4c32a35f614aa8ab93f456c"),
            new RelationshipStageArtAsset("official:gugugaga","trusted","relationships/gugugaga/trusted.png",1619,971,2375695,"c00ac75a1c55f111d048762ab7472f982df4962a07436c8ce40c459b3bb8f1a2"),
            new RelationshipStageArtAsset("official:gugugaga","heartfelt","relationships/gugugaga/heartfelt.png",1619,971,2383801,"b489cda8a2b9c867cc1655987768b472a9ecac9bd4bcb2bb20a64cd42d1c378a"),
            new RelationshipStageArtAsset("official:gugugaga","best_friend","relationships/gugugaga/best_friend.png",1619,971,2615526,"03a9527ef3b526c9b958d1545d05ae2088329c2f4bd8d5a5b7494a4e1c218427"),
            new RelationshipStageArtAsset("official:e-bao","halloween","relationships/holidays/halloween/e-bao.png",1619,971,2530942,"6fc76d72723f6d917eabe8adbc90a759109314cf102314ae21f89aec199b357d"),
            new RelationshipStageArtAsset("official:dafeiyu","halloween","relationships/holidays/halloween/dafeiyu.png",1619,971,2596945,"46e00738f2aa6f6931fe51b6cedce49b34c1bd2dcbcd9b279bab0c6af942886b"),
            new RelationshipStageArtAsset("official:marfu","halloween","relationships/holidays/halloween/marfu.png",1619,971,2617771,"486ac3319c4b859ee8ad7bbe941caba533070ab9dfd23bf8025eabebbb75389a"),
            new RelationshipStageArtAsset("official:xiaobei","halloween","relationships/holidays/halloween/xiaobei.png",1619,972,2632053,"330ec4023f1e3d49f839997dd8378effec0e65cbb58859df723706be6236e552"),
            new RelationshipStageArtAsset("official:gugugaga","halloween","relationships/holidays/halloween/gugugaga.png",1619,971,2597343,"b4288cbb8ab1ed2a2e84f8fb9b70aa1dcf6ec90f095bbd35c907f2dc47d622eb"),
            new RelationshipStageArtAsset("official:e-bao","first_meeting","relationships/e-bao/first_meeting.png",1619,971,2164733,"e2a9f8aeb8a6dd9912302036031b7c8c528939859d6bbcc6a295917a4eab958c"),
            new RelationshipStageArtAsset("official:e-bao","familiar","relationships/e-bao/familiar.png",1619,971,2199927,"af1617d695f6717d1ea25e4d65dfbb38d27485d52845ca83f4a3a13c6335a536"),
            new RelationshipStageArtAsset("official:e-bao","close","relationships/e-bao/close.png",1619,971,2144102,"627966c3fb35d22ce96ee67620b80b98ce516e1253fc98d69c75fcb778b280ce"),
            new RelationshipStageArtAsset("official:e-bao","trusted","relationships/e-bao/trusted.png",1619,971,2083166,"99df95ad412041d97351015d4d62c2d52deb5e1550bf0d0285191d2fad6f5f26"),
            new RelationshipStageArtAsset("official:e-bao","heartfelt","relationships/e-bao/heartfelt.png",1619,971,2397412,"3f96a61de1138de3bfd75e9927eeee49addf85e64af2010bacc407986859f0b0"),
            new RelationshipStageArtAsset("official:e-bao","best_friend","relationships/e-bao/best_friend.png",1619,971,2491441,"576c2b0eb334d1891536148019bf0b6e3cb6aec96e059bfba12ec50d1be029f2"),
        });
        public static IList<RelationshipStageArtAsset> All {get{return all;}}
        public static bool TryGet(string companionId,string stageId,out RelationshipStageArtAsset asset)
        {
            asset=null;if(stageId=="halloween")return false;
            foreach(var item in all)if(item.CompanionId==companionId&&item.StageId==stageId){asset=item;return true;}return false;
        }
        internal static RelationshipStageArtAsset Holiday(string companionId,string verifiedCharacterId,string holidayId,string outfitId,string outfitVersion)
        {
            if(companionId!="official:"+verifiedCharacterId)return null;
            // Both reviewed costume revisions share the original scene. Bind the
            // actual selected revision without changing old receipts or art inventory.
            if(holidayId!="halloween" || outfitId!="halloween-2026" || outfitVersion!="1.0.0"&&outfitVersion!="1.0.3")return null;
            foreach(var item in all)if(item.CompanionId==companionId&&item.HolidayId==holidayId&&item.OutfitId==outfitId&&item.HolidayId!=null){
                if(item.OutfitVersion==outfitVersion)return item;
                return new RelationshipStageArtAsset(item.CompanionId,item.HolidayId,item.OutfitId,outfitVersion,item.SceneId,item.SceneVersion,item.RelativePath,item.PixelWidth,item.PixelHeight,item.ByteLength,item.Sha256,item.StageId);
            }return null;
        }
        // Bounded stream copy before native decoding. A trusted digest plus native full decode
        // checks the immutable selected PNG without trusting stream Length or seekability.
        public static byte[] ReadVerified(Stream stream,RelationshipStageArtAsset asset)
        {
            if(stream==null||asset==null)throw new InvalidDataException("Missing scene.");
            using(var output=new MemoryStream()){
                var buffer=new byte[16384];int n;
                while((n=stream.Read(buffer,0,buffer.Length))>0){if(output.Length+n>MaximumBytes)throw new InvalidDataException("Scene exceeds bound.");output.Write(buffer,0,n);}
                var bytes=output.ToArray();
                if(bytes.Length!=asset.ByteLength||bytes.Length<33)throw new InvalidDataException("Incomplete scene.");
                byte[] signature={137,80,78,71,13,10,26,10};for(int i=0;i<8;i++)if(bytes[i]!=signature[i])throw new InvalidDataException("Not PNG.");
                if(bytes[8]!=0||bytes[9]!=0||bytes[10]!=0||bytes[11]!=13||bytes[12]!=73||bytes[13]!=72||bytes[14]!=68||bytes[15]!=82)throw new InvalidDataException("Invalid IHDR.");
                int width=Big(bytes,16),height=Big(bytes,20);
                if(width!=asset.PixelWidth||height!=asset.PixelHeight||width<800||width>3200||height<=0||height>2000||(long)width*height>6400000||Math.Abs(width/(height*(5.0/3))-1)>.005)throw new InvalidDataException("Scene geometry mismatch.");
                using(var sha=SHA256.Create())if(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant()!=asset.Sha256)throw new InvalidDataException("Scene digest mismatch.");
                return bytes;
            }
        }
        static int Big(byte[] b,int at){return (b[at]<<24)|(b[at+1]<<16)|(b[at+2]<<8)|b[at+3];}
    }
    public interface IRelationshipSceneProvider
    {
        RelationshipSceneCapture Capture(string companionId,string verifiedCharacterId,string holidayId,string outfitId,string outfitVersion);
    }
    // Pure immutable source capture; no bitmap, mutable page reference or I/O at capture time.
    // The consumer owns each returned stream, validates and freezes original bytes on its worker.
    public sealed class RelationshipSceneCapture
    {
        readonly Func<Stream> opener;
        public RelationshipStageArtAsset Descriptor {get;private set;}
        public string SourceGeneration {get;private set;}
        internal RelationshipSceneCapture(RelationshipStageArtAsset descriptor,string generation,Func<Stream> open){Descriptor=descriptor;SourceGeneration=generation;opener=open;}
        public Stream OpenRead(){return opener();}
    }
    public sealed class RelationshipSceneProvider:IRelationshipSceneProvider
    {
        readonly Func<RelationshipStageArtAsset,Stream> opener;
        readonly string generation=Guid.NewGuid().ToString("N");
        public RelationshipSceneProvider(Func<RelationshipStageArtAsset,Stream> open){if(open==null)throw new ArgumentNullException("open");opener=open;}
        public RelationshipSceneCapture Capture(string companionId,string verifiedCharacterId,string holidayId,string outfitId,string outfitVersion)
        {
            var asset=RelationshipStageArt.Holiday(companionId,verifiedCharacterId,holidayId,outfitId,outfitVersion);if(asset==null)return null;
            var bound=opener;return new RelationshipSceneCapture(asset,generation,delegate{return bound(asset);});
        }
    }
}
