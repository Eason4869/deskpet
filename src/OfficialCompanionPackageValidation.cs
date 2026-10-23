using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
namespace Dafeiyu
{
    internal static class OfficialCompanionPackageValidation
    {
        static readonly string[] Actions={"idle","running-right","running-left","waving","jumping","failed","waiting","running","review","lifted","landing","petting","poked","feeding","happy","sleeping","special"};
        static readonly string[] Extra={"stumble","special2"};
        static readonly string[] Alternate={"lifted","landing","feeding","happy","sleeping","special","special2","stumble"};
        static readonly uint[] Crc=CreateCrc();
        internal static void Extract(string zip,string stage,CancellationToken cancel)
        {
            OfficialCompanionStorage.SafeDirectory(stage);Directory.CreateDirectory(stage);
            using(var input=File.OpenRead(zip))using(var archive=new ZipArchive(input,ZipArchiveMode.Read)) {
                if(archive.Entries.Count>512)throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.extract.text"));
                var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
                // Validate the whole directory before creating any file. No path normalization.
                foreach(var entry in archive.Entries) {
                    cancel.ThrowIfCancellationRequested();string relative=entry.FullName;bool directory=relative.EndsWith("/",StringComparison.Ordinal);if(directory)relative=relative.Substring(0,relative.Length-1);
                    OfficialCompanionStorage.SafePath(stage,relative);
                    if(!names.Add(relative)||((entry.ExternalAttributes>>16)&0xf000)==0xa000||(entry.ExternalAttributes&0x400)!=0||relative.Equals("receipt.json",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.extract.text.alternativeA"));
                    if(directory){if(entry.Length!=0)throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.extract.text.alternativeB"));continue;}
                    long limit=FileLimit(relative);if(entry.Length<0||entry.Length>limit)throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.extract.text.alternativeC"));total=checked(total+entry.Length);if(total>OfficialCompanionRules.MaximumPackage)throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.extract.text.alternativeD"));
                }
                foreach(var entry in archive.Entries) {
                    cancel.ThrowIfCancellationRequested();string relative=entry.FullName;if(relative.EndsWith("/",StringComparison.Ordinal)){Directory.CreateDirectory(OfficialCompanionStorage.SafePath(stage,relative.Substring(0,relative.Length-1)));continue;}
                    string target=OfficialCompanionStorage.SafePath(stage,relative);Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using(var source=entry.Open())using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                        var buffer=new byte[65536];int n;long written=0;while((n=source.Read(buffer,0,buffer.Length))>0){cancel.ThrowIfCancellationRequested();written+=n;if(written>entry.Length||written>FileLimit(relative))throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.extract.text.alternativeE"));output.Write(buffer,0,n);}if(written!=entry.Length)throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.extract.text.alternativeF"));
                    }
                }
            }
        }
        internal static long FileLimit(string path)
        {
            string extension=Path.GetExtension(path);if(extension==".png")return 25L*1024*1024;if(extension==".json")return path=="pet.json"?65536:1024*1024;
            if(path=="README.txt"||path=="README.md"||path=="LICENSE.txt"||path=="LICENSE.md"||path=="LICENSE")return 65536;
            throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.fileLimit.text"));
        }
        internal static void Validate(string root,OfficialCompanionAsset asset,CancellationToken cancel)
        {
            var identity=OfficialJson.File(PathAt(root,"companions.json"),1024*1024);
            if(OfficialJson.Integer(identity,"schema_version")!=1||OfficialJson.Text(identity,"id",48)!=asset.Id||OfficialJson.Text(identity,"name",80)!=asset.Name||OfficialJson.Text(identity,"version",64)!=asset.Version||OfficialJson.Text(identity,"min_app_version",64)!=asset.MinimumAppVersion)throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.validate.text"));
            var pet=OfficialJson.File(PathAt(root,"pet.json"),65536);
            if(OfficialJson.Integer(pet,"version")!=2||OfficialJson.Text(pet,"id",48)!=asset.Id||OfficialJson.Text(pet,"name",80)!=asset.Name||OfficialJson.Text(pet,"pack_version",64)!=asset.Version||OfficialJson.Text(pet,"format",64)!="illustrated-atlases-v1"||OfficialJson.Integer(pet,"cell_width")!=480||OfficialJson.Integer(pet,"cell_height")!=520)throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.validate.text.alternativeA"));
            MultiText(pet,"system_prompt",8000);MultiText(pet,"description",1000);OfficialJson.Text(pet,"special_action",32);OfficialJson.Text(pet,"special_action2",32);
            var voices=OfficialJson.Object(OfficialJson.Required(pet,"voice_presets"));if(voices.ContainsKey("description"))MultiText(voices,"description",280);if(voices.ContainsKey("sample_text"))MultiText(voices,"sample_text",200);
            foreach(string key in new[]{"speech","live"}){var list=OfficialJson.Array(OfficialJson.Required(voices,key));if(list.Length<1||list.Length>16||list.Any(x=>!(x is string)||String.IsNullOrWhiteSpace((string)x)||((string)x).Length>64))throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.validate.text.alternativeB"));}
            var normal=OfficialJson.Object(OfficialJson.Required(pet,"animations"));var extra=OfficialJson.Object(OfficialJson.Required(pet,"extra_animations"));var alternate=OfficialJson.Object(OfficialJson.Required(pet,"alternate_animations"));
            RequireKeys(normal,Actions);RequireKeys(extra,Extra);RequireKeys(alternate,(asset.Id=="e-bao"||asset.Id=="gugugaga")?Alternate.Concat(new[]{"jumping"}).ToArray():Alternate);
            var pngs=new Dictionary<string,string>(StringComparer.Ordinal);int frames=0;
            foreach(var group in new[]{normal,extra,alternate})foreach(var item in group){cancel.ThrowIfCancellationRequested();var clip=OfficialJson.Object(item.Value);frames+=ValidateClip(root,item.Key,clip,pngs);if(frames>1024)throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.validate.text.alternativeC"));if(ReferenceEquals(group,alternate)){object firstRaw;normal.TryGetValue(item.Key,out firstRaw);if(firstRaw==null)extra.TryGetValue(item.Key,out firstRaw);var first=OfficialJson.Object(firstRaw);if(OfficialJson.Bool(first,"loop")!=OfficialJson.Bool(clip,"loop")||(item.Key=="jumping"&&Math.Abs(Duration(first)-Duration(clip))>1e-8))throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.validate.text.alternativeD"));}}
            if(!OfficialJson.Bool(OfficialJson.Object(normal["idle"]),"loop"))throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.validate.text.alternativeE"));
            var gaze=OfficialJson.Object(OfficialJson.Required(pet,"gaze"));if(OfficialJson.Integer(gaze,"columns")!=4||OfficialJson.Integer(gaze,"frames")!=16||OfficialJson.Text(gaze,"interpolation",20)!="none"||OfficialJson.Text(gaze,"order",40)!="clockwise-from-up")throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.validate.text.alternativeF"));
            Png(root,OfficialJson.Text(gaze,"file",220),1920,2080,pngs);Png(root,OfficialJson.Text(pet,"preview",220),480,520,pngs);
            string avatar=OfficialJson.Text(pet,"avatar",220);Png(root,avatar,0,0,pngs);
            ValidateHolidays(root,asset,cancel);
            // Every PNG, including unreferenced data, obeys the RGBA/CRC and size policy.
            foreach(string file in OfficialCompanionStorage.Inventory(root)){cancel.ThrowIfCancellationRequested();FileLimit(file);if(file.EndsWith(".png",StringComparison.Ordinal))Png(root,file,0,0,pngs);}
        }
        static string PathAt(string root,string relative){return OfficialCompanionStorage.SafePath(root,relative);}
        static void MultiText(Dictionary<string,object> value,string key,int max){object raw=OfficialJson.Required(value,key);string text=raw as string;if(String.IsNullOrWhiteSpace(text)||text.Length>max||text.Any(c=>Char.IsControl(c)&&c!='\r'&&c!='\n'&&c!='\t'))throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.multiText.text"));}
        static void RequireKeys(Dictionary<string,object> value,string[] names){if(value.Count!=names.Length||names.Any(x=>!value.ContainsKey(x)))throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.requireKeys.text"));}
        static double Hold(Dictionary<string,object> clip){return clip.ContainsKey("hold_last_seconds")?OfficialJson.Number(clip,"hold_last_seconds"):0;}
        static double Duration(Dictionary<string,object> clip){return OfficialJson.Integer(clip,"frames")/OfficialJson.Number(clip,"fps")+Hold(clip);}
        static int ValidateClip(string root,string action,Dictionary<string,object> clip,Dictionary<string,string> pngs)
        {
            long frames=OfficialJson.Integer(clip,"frames"),columns=OfficialJson.Integer(clip,"columns");double fps=OfficialJson.Number(clip,"fps"),hold=Hold(clip);bool loop=OfficialJson.Bool(clip,"loop");
            if(columns!=8||frames<1||frames>128||fps<1||fps>60||hold<0||hold>2||(hold!=0&&(action!="jumping"||loop)))throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.validateClip.text"));
            Png(root,OfficialJson.Text(clip,"file",220),3840,520*(((int)frames+7)/8),pngs);return (int)frames;
        }
        static void ValidateHolidays(string root,OfficialCompanionAsset asset,CancellationToken cancel)
        {
            string holidays=PathAt(root,"holidays");var catalog=new HolidayCatalog(holidays);
            if(!String.IsNullOrEmpty(catalog.Warning)||catalog.Outfits.Count!=asset.Outfits.Count||catalog.Outfits.Any(x=>x.CharacterId!=asset.Id||!x.Ready))throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.validateHolidays.text"));
            var pngs=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var expected in asset.Outfits){cancel.ThrowIfCancellationRequested();var outfit=catalog.Find(asset.Id,expected.OutfitId);if(outfit==null||outfit.Version!=expected.Version)throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.validateHolidays.text.alternativeA"));foreach(var clip in outfit.Clips.Concat(new[]{outfit.Gaze,outfit.EasterEgg}))Png(holidays,clip.Path,clip.Columns*clip.FrameWidth,((clip.FrameCount+clip.Columns-1)/clip.Columns)*clip.FrameHeight,pngs);Png(holidays,outfit.Preview,480,520,pngs);}
        }
        static void Png(string root,string relative,int width,int height,Dictionary<string,string> validated)
        {
            if(!relative.EndsWith(".png",StringComparison.Ordinal))throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.png.text"));string geometry=width+":"+height,cached;
            if(validated.TryGetValue(relative,out cached)){if(width!=0&&cached!=geometry)throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.png.text.alternativeA"));return;}
            string path=PathAt(root,relative);if(new FileInfo(path).Length>25L*1024*1024)throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.png.text.alternativeB"));byte[] data=File.ReadAllBytes(path);if(data.Length<45||!data.Take(8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}))throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.png.text.alternativeC"));
            bool header=false,idat=false,ended=false,idatClosed=false;int position=8;uint actualWidth=0,actualHeight=0;
            while(position<=data.Length-12){uint length=U32(data,position);if(length>25*1024*1024||length>data.Length-position-12)throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.png.text.alternativeD"));int size=(int)length;string type=Encoding.ASCII.GetString(data,position+4,4);if(!type.All(c=>(c>='A'&&c<='Z')||(c>='a'&&c<='z')))throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.png.text.alternativeE"));uint crc=0xffffffff;for(int i=position+4;i<position+8+size;i++)crc=Crc[(crc^data[i])&255]^(crc>>8);if((crc^0xffffffff)!=U32(data,position+8+size))throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.png.text.alternativeF"));
                if(!header){if(type!="IHDR"||size!=13)throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.png.text.alternativeG"));actualWidth=U32(data,position+8);actualHeight=U32(data,position+12);if(actualWidth<1||actualHeight<1||actualWidth>8192||actualHeight>8320||(long)actualWidth*actualHeight>32L*1024*1024||(width!=0&&(actualWidth!=width||actualHeight!=height))||data[position+16]!=8||data[position+17]!=6||data[position+18]!=0||data[position+19]!=0||data[position+20]!=0)throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.png.text.alternativeH"));header=true;}
                else if(type=="IHDR"||type=="PLTE"||(Char.IsUpper(type[0])&&type!="IDAT"&&type!="IEND"))throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.png.text.alternativeI"));
                if(type=="IDAT"){if(idatClosed)throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.png.text.alternativeJ"));idat=true;}else if(idat)idatClosed=true;
                position+=size+12;if(type=="IEND"){if(size!=0||!idat||position!=data.Length)throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.png.text.alternativeK"));ended=true;break;}
            }
            if(!ended)throw new InvalidDataException(TextResources.Get("officialCompanionPackageValidation.png.text.alternativeL"));validated.Add(relative,actualWidth+":"+actualHeight);
        }
        static uint U32(byte[] bytes,int at){return ((uint)bytes[at]<<24)|((uint)bytes[at+1]<<16)|((uint)bytes[at+2]<<8)|bytes[at+3];}
        static uint[] CreateCrc(){var result=new uint[256];for(uint i=0;i<256;i++){uint n=i;for(int j=0;j<8;j++)n=(n&1)!=0?(n>>1)^0xedb88320:n>>1;result[i]=n;}return result;}
    }
}
