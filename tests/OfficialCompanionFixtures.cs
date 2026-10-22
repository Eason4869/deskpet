using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
namespace Dafeiyu.Tests
{
    internal sealed class OfficialCompanionFixture : HttpMessageHandler
    {
        internal static readonly string[] Ids={"e-bao","dafeiyu","marfu","xiaobei","gugugaga"};
        internal const string Version="0.10.3-dev.1",Tag="companions-v0.10.3-dev.1";
        internal readonly Dictionary<string,byte[]> Files=new Dictionary<string,byte[]>();
        internal readonly List<string> Requests=new List<string>();
        // Leave room for immutable version names and unique .part names on Framework hosts.
        // The dedicated long-path case below controls its own boundary explicitly.
        internal readonly string Root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"oc",Guid.NewGuid().ToString("N"));
        internal bool Offline,CorruptMirror,WrongSize,WrongDigest,ForeignUrl,IndexIncomplete,AllIncompatible;
        internal Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> Intercept;
        internal int Packages,Metadata;
        internal OfficialCompanionFixture(){Directory.CreateDirectory(Root);foreach(string id in Ids)Files[PackageName(id)]=Package(id,null);}
        internal static string PackageName(string id){return "DeskPet-Companion-"+id+"-"+Version+".zip";}
        internal static string Hash(byte[] bytes){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        internal static Dictionary<string,object> Obj(params object[] fields){var d=new Dictionary<string,object>();for(int i=0;i<fields.Length;i+=2)d.Add((string)fields[i],fields[i+1]);return d;}
        internal static byte[] Json(object value){return Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(value));}
        internal byte[] Index()
        {
            return Json(Obj("schema_version",1,"companions",Ids.Take(IndexIncomplete?4:5).Select(id=>Obj("id",id,"name",id,"version",Version,"min_app_version",AllIncompatible?"99.0.0":Version,"asset_name",PackageName(id),"size",WrongSize?Files[PackageName(id)].Length+1:Files[PackageName(id)].Length,"sha256",WrongDigest?new string('0',64):Hash(Files[PackageName(id)]),"outfits",new[]{Obj("outfit_id","halloween-2026","version","1.0.0")})).ToArray()));
        }
        internal Dictionary<string,object> Release(string tag,byte[] index)
        {
            var assets=Files.Select(p=>Obj("name",p.Key,"size",p.Value.Length,"digest","sha256:"+Hash(p.Value),"browser_download_url",(ForeignUrl?"https://evil.example/": "https://github.com/Eason4869/deskpet/releases/download/"+tag+"/")+p.Key)).ToList();
            string indexName="DeskPet-Companions-"+tag.Replace("companions-v","").TrimStart('v')+".json";
            assets.Add(Obj("name",indexName,"size",index.Length,"digest","sha256:"+Hash(index),"browser_download_url","https://github.com/Eason4869/deskpet/releases/download/"+tag+"/"+indexName));
            return Obj("tag_name",tag,"html_url","https://github.com/Eason4869/deskpet/releases/tag/"+tag,"draft",false,"prerelease",tag.Contains("-dev"),"assets",assets.ToArray());
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            lock(Requests)Requests.Add(request.RequestUri.AbsoluteUri);
            if(Intercept!=null){var intercepted=await Intercept(request,token).ConfigureAwait(false);if(intercepted!=null)return intercepted;}
            token.ThrowIfCancellationRequested();if(Offline)return Response(new byte[0],HttpStatusCode.ServiceUnavailable);
            if(request.RequestUri.Host=="api.github.com"){Interlocked.Increment(ref Metadata);return Response(Json(new[]{Release(Tag,Index())}));}
            string name=Uri.UnescapeDataString(request.RequestUri.Segments.Last());
            if(name.EndsWith(".json"))return Response(Index());
            Interlocked.Increment(ref Packages);byte[] bytes;if(!Files.TryGetValue(name,out bytes))return Response(new byte[0],HttpStatusCode.NotFound);
            return Response(CorruptMirror&&request.RequestUri.Host=="mirror.example"?new byte[]{1,2,3}:bytes);
        }
        internal static HttpResponseMessage Response(byte[] bytes,HttpStatusCode status=HttpStatusCode.OK){return new HttpResponseMessage(status){Content=new ByteArrayContent(bytes)};}
        internal GitHubUpdates Transport(){return new GitHubUpdates(new Uri("https://api.github.com/"),this);}
        internal Task<HttpResponseMessage> Dispatch(HttpRequestMessage request,CancellationToken cancel){return SendAsync(request,cancel);}
        internal static byte[] WithSymlink(byte[] bytes){using(var output=new MemoryStream()){output.Write(bytes,0,bytes.Length);output.Position=0;using(var zip=new ZipArchive(output,ZipArchiveMode.Update,true)){var entry=zip.CreateEntry("link.png");entry.ExternalAttributes=unchecked((int)0xa1ff0000);using(var writer=new StreamWriter(entry.Open()))writer.Write("preview.png");}return output.ToArray();}}
        protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing&&Directory.Exists(Root))Directory.Delete(Root,true);}
        internal static byte[] Package(string id,Action<Dictionary<string,byte[]>> mutate)
        {
            var normal=new Dictionary<string,object>();foreach(string action in new[]{"idle","running-right","running-left","waving","jumping","failed","waiting","running","review","lifted","landing","petting","poked","feeding","happy","sleeping","special"})normal[action]=Clip(action=="jumping"?5:1,action=="idle",action=="jumping"?1/.13:8,action=="jumping"?(id=="e-bao"?.35:id=="dafeiyu"||id=="marfu"?.05:0):0);
            var alternate=new Dictionary<string,object>();foreach(string action in new[]{"lifted","landing","feeding","happy","sleeping","special","special2","stumble"})alternate[action]=Clip(1,false,8,0);
            if(id=="e-bao"||id=="gugugaga")alternate["jumping"]=Clip(id=="e-bao"?8:5,false,id=="e-bao"?8:1/.13,0);
            var manifest=Obj("version",2,"id",id,"name",id,"pack_version",Version,"format","illustrated-atlases-v1","cell_width",480,"cell_height",520,"preview","preview.png","avatar","preview.png","system_prompt","You are a companion.","description","An official companion.","special_action","Action","special_action2","Action two","voice_presets",Obj("description","Voice","sample_text","hello","speech",new[]{"voice"},"live",new[]{"voice"}),"animations",normal,"extra_animations",Obj("stumble",Clip(1,false,8,0),"special2",Clip(1,false,8,0)),"alternate_animations",alternate,"gaze",Obj("file","gaze.png","columns",4,"frames",16,"interpolation","none","order","clockwise-from-up"));
            var holidayClips=new List<object>();foreach(string action in normal.Keys.Concat(new[]{"stumble","special2"})){holidayClips.Add(HolidayClip(action,"default",1,false));}
            foreach(string action in alternate.Keys)holidayClips.Add(HolidayClip(action,"alternate",1,false));
            var files=new Dictionary<string,byte[]>{
                {"pet.json",Json(manifest)},{"companions.json",Json(Obj("schema_version",1,"id",id,"name",id,"version",Version,"min_app_version",Version))},
                {"animations/clip.png",Png(3840,520,6)},{"gaze.png",Png(1920,2080,6)},{"preview.png",Png(480,520,6)},
                {"holidays/catalog.json",Json(Obj("schema_version",1,"outfits",new[]{Obj("character_id",id,"outfit_id","halloween-2026","holiday_id","halloween","name","Halloween","version","1.0.0","ready",true,"greetings_ready",true,"definition","halloween/"+id+"/outfit.json")}))},
                {"holidays/halloween/"+id+"/outfit.json",Json(Obj("schema_version",1,"character_id",id,"outfit_id","halloween-2026","holiday_id","halloween","name","Halloween","version","1.0.0","clips",holidayClips.ToArray(),"gaze",Obj("path","gaze.png","frame_count",16,"columns",4,"fps",8,"loop",true,"frame_width",480,"frame_height",520,"offset_x",0,"offset_y",0),"easter_egg",HolidayClip("egg","default",8,false),"preview","preview.png","action_name","Egg"))},
                {"holidays/clip.png",Png(3840,520,6)},{"holidays/gaze.png",Png(1920,2080,6)},{"holidays/preview.png",Png(480,520,6)}
            };
            if(mutate!=null)mutate(files);
            using(var output=new MemoryStream()){using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true)){foreach(var p in files)using(var stream=zip.CreateEntry(p.Key).Open())stream.Write(p.Value,0,p.Value.Length);}return output.ToArray();}
        }
        static object Clip(int frames,bool loop,double fps,double hold){return Obj("file","animations/clip.png","columns",8,"frames",frames,"fps",fps,"loop",loop,"hold_last_seconds",hold);}
        static object HolidayClip(string action,string variant,int frames,bool loop){return Obj("action",action,"variant",variant,"path","clip.png","frame_count",frames,"columns",8,"fps",action=="egg"?4:8,"loop",loop,"frame_width",480,"frame_height",520,"offset_x",0,"offset_y",0);}
        static readonly Dictionary<string,byte[]> PngCache=new Dictionary<string,byte[]>();
        internal static byte[] Png(int width,int height,byte color)
        {lock(PngCache){string key=width+":"+height+":"+color;byte[] data;if(!PngCache.TryGetValue(key,out data)){data=MakePng(width,height,color);PngCache.Add(key,data);}return (byte[])data.Clone();}}
        static byte[] MakePng(int width,int height,byte color)
        {
            using(var output=new MemoryStream()){
                output.Write(new byte[]{137,80,78,71,13,10,26,10},0,8);
                var header=new byte[13];Put(header,0,(uint)width);Put(header,4,(uint)height);header[8]=8;header[9]=color;Chunk(output,"IHDR",header);
                byte[] raw=new byte[(width*4+1)*height];byte[] compressed;
                using(var body=new MemoryStream()){body.WriteByte(0x78);body.WriteByte(0x9c);using(var deflate=new DeflateStream(body,CompressionLevel.Optimal,true))deflate.Write(raw,0,raw.Length);uint a=1,b=0;foreach(byte x in raw){a=(a+x)%65521;b=(b+a)%65521;}byte[] adler=new byte[4];Put(adler,0,b<<16|a);body.Write(adler,0,4);compressed=body.ToArray();}
                Chunk(output,"IDAT",compressed);Chunk(output,"IEND",new byte[0]);return output.ToArray();
            }
        }
        static void Put(byte[] b,int p,uint n){b[p]=(byte)(n>>24);b[p+1]=(byte)(n>>16);b[p+2]=(byte)(n>>8);b[p+3]=(byte)n;}
        static void Chunk(Stream stream,string type,byte[] data){byte[] label=Encoding.ASCII.GetBytes(type),length=new byte[4];Put(length,0,(uint)data.Length);stream.Write(length,0,4);stream.Write(label,0,4);stream.Write(data,0,data.Length);uint crc=0xffffffff;foreach(byte x in label.Concat(data)){crc^=x;for(int i=0;i<8;i++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}Put(length,0,crc^0xffffffff);stream.Write(length,0,4);}
    }
    internal sealed class OfficialDelegateHandler:HttpMessageHandler
    {
        readonly Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action;internal OfficialDelegateHandler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action){this.action=action;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel){return action(request,cancel);}
    }
}
