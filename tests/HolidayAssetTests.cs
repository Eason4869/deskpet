using System;
using System.IO;
using System.Linq;
using System.Text;
using System.IO.Compression;
using System.Collections.Generic;
namespace Dafeiyu.Tests
{
    public static class HolidayAssetTests
    {
        static int passed;
        static void Check(bool value,string name){if(!value)throw new Exception(name);passed++;}
        static uint Crc(byte[] bytes){uint crc=0xffffffff;foreach(byte b in bytes){crc^=b;for(int j=0;j<8;j++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;}return crc^0xffffffff;}
        static void Number(Stream s,uint n){s.WriteByte((byte)(n>>24));s.WriteByte((byte)(n>>16));s.WriteByte((byte)(n>>8));s.WriteByte((byte)n);}
        static void Chunk(Stream s,string kind,byte[] payload){Number(s,(uint)payload.Length);byte[] bytes=Encoding.ASCII.GetBytes(kind).Concat(payload).ToArray();s.Write(bytes,0,bytes.Length);Number(s,Crc(bytes));}
        static void Png(string path,int width,int height)
        {
            byte[] rows=new byte[(width*4+1)*height];uint a=1,b=0;foreach(byte value in rows){a=(a+value)%65521;b=(b+a)%65521;}
            byte[] payload;using(var compressed=new MemoryStream()){compressed.WriteByte(0x78);compressed.WriteByte(0x9c);using(var deflate=new DeflateStream(compressed,CompressionMode.Compress,true))deflate.Write(rows,0,rows.Length);Number(compressed,(b<<16)|a);payload=compressed.ToArray();}
            using(var s=File.Create(path)){byte[] signature={137,80,78,71,13,10,26,10};s.Write(signature,0,signature.Length);using(var header=new MemoryStream()){Number(header,(uint)width);Number(header,(uint)height);header.Write(new byte[]{8,6,0,0,0},0,5);Chunk(s,"IHDR",header.ToArray());}Chunk(s,"IDAT",payload);Chunk(s,"IEND",new byte[0]);}
        }
        static string Clip(string action,string variant,string path,int count,int columns,bool loop)
        {return "{\"action\":\""+action+"\",\"variant\":\""+variant+"\",\"path\":\""+path+"\",\"frame_count\":"+count+",\"columns\":"+columns+",\"fps\":4,\"loop\":"+(loop?"true":"false")+",\"frame_width\":480,\"frame_height\":520,\"offset_x\":0,\"offset_y\":0}";}
        static void Fixtures()
        {
            string root=Path.Combine(Path.GetTempPath(),"deskpet-holiday-assets-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try {
                Png(Path.Combine(root,"normal.png"),3840,520);Png(Path.Combine(root,"gaze.png"),1920,2080);Png(Path.Combine(root,"preview.png"),480,520);
                string metadata="\"character_id\":\"e-bao\",\"outfit_id\":\"halloween-2026\",\"holiday_id\":\"halloween\",\"name\":\"fixture\",\"version\":\"1\"";
                string index="{\"schema_version\":1,\"outfits\":[{"+metadata+",\"ready\":true,\"greetings_ready\":true,\"definition\":\"outfit.json\"}]}";
                var clips=new List<string>();foreach(string action in new[]{"idle","running-right","running-left","waving","jumping","failed","waiting","running","review","lifted","landing","petting","poked","feeding","happy","sleeping","special","stumble","special2"})clips.Add(Clip(action,"default","normal.png",8,8,true));
                foreach(string action in new[]{"lifted","landing","feeding","happy","sleeping","special","special2","stumble","jumping"})clips.Add(Clip(action,"alternate","normal.png",8,8,true));
                string definition="{\"schema_version\":1,"+metadata+",\"clips\":["+String.Join(",",clips)+"],\"gaze\":"+Clip("gaze","default","gaze.png",16,4,true)+",\"easter_egg\":"+Clip("egg","default","normal.png",8,8,false)+",\"preview\":\"preview.png\",\"action_name\":\"fixture action\"}";
                string indexPath=Path.Combine(root,"catalog.json"),definitionPath=Path.Combine(root,"outfit.json");File.WriteAllText(indexPath,index);File.WriteAllText(definitionPath,definition);
                var valid=new HolidayCatalog(root);Check(valid.Outfits.Count==1 && valid.Outfits[0].Ready,"complete source fixture Ready");Check(valid.Outfits[0].Clips.Count==28,"all exact action variants loaded");
                bool immutable=false;try{valid.Outfits.Clear();}catch(NotSupportedException){immutable=true;}Check(immutable,"catalog immutable");immutable=false;try{valid.Outfits[0].Clips.Clear();}catch(NotSupportedException){immutable=true;}Check(immutable,"clip list immutable");
                File.WriteAllText(definitionPath,definition.Replace("\"path\":\"normal.png\"","\"path\":\"../normal.png\""));Check(!new HolidayCatalog(root).Outfits[0].Ready,"resource path traversal rejected");
                File.WriteAllText(definitionPath,definition.Replace("\"action\":\"running-right\"","\"action\":\"idle\""));Check(!new HolidayCatalog(root).Outfits[0].Ready,"duplicate normal action rejected");
                File.WriteAllText(definitionPath,definition.Replace("\"clips\":[","\"clips\":["+Clip("undeclared","default","normal.png",8,8,true)+","));Check(!new HolidayCatalog(root).Outfits[0].Ready,"undeclared action rejected");
                File.WriteAllText(definitionPath,definition.Replace(clips[0]+",",""));Check(!new HolidayCatalog(root).Outfits[0].Ready,"missing required action rejected");
                File.WriteAllText(definitionPath,definition.Replace("\"character_id\":\"e-bao\"","\"character_id\":\"marfu\""));Check(!new HolidayCatalog(root).Outfits[0].Ready,"mismatched identity rejected");
                File.WriteAllText(definitionPath,definition);byte[] png=File.ReadAllBytes(Path.Combine(root,"normal.png"));png[40]^=1;File.WriteAllBytes(Path.Combine(root,"normal.png"),png);Check(!new HolidayCatalog(root).Outfits[0].Ready,"PNG checksum damage rejected");
                File.Delete(Path.Combine(root,"normal.png"));Check(new HolidayCatalog(root).Outfits.Count==1 && !new HolidayCatalog(root).Outfits[0].Ready,"missing resource keeps unavailable definition");
                File.WriteAllText(indexPath,index.Replace("\"character_id\":\"e-bao\"","\"character_id\":\"custom\""));Check(new HolidayCatalog(root).Outfits.Count==0,"custom character cannot register builtin outfit");
            }finally{string resolved=Path.GetFullPath(root),temporary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;if(resolved.StartsWith(temporary,StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("deskpet-holiday-assets-",StringComparison.Ordinal) && Directory.Exists(resolved))Directory.Delete(resolved,true);}
        }
        public static int Main()
        {
            try {
                passed=0;Fixtures();var catalog=new HolidayCatalog();
#if NET10_0_OR_GREATER
                Check(catalog.Outfits.Count==1&&catalog.Outfits[0].CharacterId=="e-bao"&&catalog.Outfits[0].Ready,"production Core embeds one complete Ebao outfit");
                string repository=Environment.CurrentDirectory;
                while(!Directory.Exists(Path.Combine(repository,"assets","holidays"))) {
                    repository=Path.GetDirectoryName(repository);if(String.IsNullOrEmpty(repository))throw new Exception("Run asset verification from the repository or a child directory.");
                }
                catalog=new HolidayCatalog(Path.Combine(repository,"assets","holidays"));
#endif
                if(catalog.Outfits.Count!=5 || catalog.Outfits.Any(x=>!x.Ready))throw new Exception("Five complete authored holiday outfits must be Ready.");
                foreach(var outfit in catalog.Outfits){if(outfit.EasterEgg.Loop || outfit.EasterEgg.FrameCount!=8 || outfit.Gaze.FrameCount!=16)throw new Exception("Invalid egg/gaze.");if(outfit.Clips.GroupBy(x=>x.Action+":"+x.Variant).Any(x=>x.Count()!=1))throw new Exception("duplicate clip");}
                var empty=new HolidayCatalog(Path.Combine(Path.GetTempPath(),"missing-holiday-catalog-"+Guid.NewGuid().ToString("N")));if(empty.Outfits.Any(x=>x.Ready))throw new Exception("Missing assets must never be ready.");
                Console.WriteLine("Holiday asset tests: "+passed+" fixture checks and five complete authored outfits passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL holiday assets: "+e);return 1;}
        }
    }
}
