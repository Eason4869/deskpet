using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Dafeiyu
{
    public sealed class OutfitClipDefinition
    {
        public string Path {get;private set;} public string ResourceName {get{return "Dafeiyu.Holidays."+Path.Replace('/','.');}}
        public int FrameCount {get;private set;} public int Columns {get;private set;} public double Fps {get;private set;}
        public double FPS {get{return Fps;}} public bool Loop {get;private set;}
        public int FrameWidth {get;private set;} public int FrameHeight {get;private set;}
        public double OffsetX {get;private set;} public double OffsetY {get;private set;}
        public string Action {get;private set;} public string Variant {get;private set;}
        internal OutfitClipDefinition(string path,int count,int columns,double fps,bool loop,int width,int height,double x,double y,string action,string variant)
        {Path=path;FrameCount=count;Columns=columns;Fps=fps;Loop=loop;FrameWidth=width;FrameHeight=height;OffsetX=x;OffsetY=y;Action=action;Variant=variant;}
    }
    public sealed class OutfitDefinition
    {
        public string CharacterId {get;private set;} public string OutfitId {get;private set;} public string HolidayId {get;private set;}
        public string Name {get;private set;} public string Version {get;private set;} public bool Ready {get;private set;} public bool GreetingsReady {get;private set;}
        public IList<OutfitClipDefinition> Clips {get;private set;} public OutfitClipDefinition Gaze {get;private set;} public OutfitClipDefinition EasterEgg {get;private set;}
        public string Preview {get;private set;} public string ActionName {get;private set;}
        internal OutfitDefinition(string character,string outfit,string holiday,string name,string version,bool ready,bool greetings,IEnumerable<OutfitClipDefinition> clips,OutfitClipDefinition gaze,OutfitClipDefinition egg,string preview,string actionName)
        {CharacterId=character;OutfitId=outfit;HolidayId=holiday;Name=name;Version=version;Ready=ready;GreetingsReady=greetings;Clips=new ReadOnlyCollection<OutfitClipDefinition>(clips.ToList());Gaze=gaze;EasterEgg=egg;Preview=preview;ActionName=actionName;}
        public OutfitClipDefinition FindClip(string action,string variant)
        {return Clips.FirstOrDefault(x=>x.Action==action && x.Variant==(String.IsNullOrEmpty(variant)?"default":variant));}
        internal void MarkUnavailable(){Ready=false;}
    }
    public sealed class HolidayCatalog
    {
        static readonly string[] Characters={"e-bao","dafeiyu","marfu","xiaobei","gugugaga"};
        static readonly string[] DefaultActions={"idle","running-right","running-left","waving","jumping","failed","waiting","running","review","lifted","landing","petting","poked","feeding","happy","sleeping","special","stumble","special2"};
        static readonly string[] AlternateActions={"lifted","landing","feeding","happy","sleeping","special","special2","stumble"};
        readonly Func<string,Stream> open;
        readonly Dictionary<string,string> validatedPngs=new Dictionary<string,string>(StringComparer.Ordinal);
        static readonly uint[] CrcTable=CreateCrcTable();
        public IList<OutfitDefinition> Outfits {get;private set;} public string Warning {get;private set;}
        // DataContractJsonSerializer populates these bounded input records.
#pragma warning disable 0649
        [DataContract] sealed class Index {[DataMember(Name="schema_version")]public int Version;[DataMember(Name="outfits")]public List<Entry> Outfits;}
        [DataContract] sealed class Entry
        {
            [DataMember(Name="character_id")]public string Character;[DataMember(Name="outfit_id")]public string Outfit;
            [DataMember(Name="holiday_id")]public string Holiday;[DataMember(Name="name")]public string Name;[DataMember(Name="version")]public string Version;
            [DataMember(Name="ready")]public bool Ready;[DataMember(Name="greetings_ready")]public bool Greetings;[DataMember(Name="definition")]public string Definition;
        }
        [DataContract] sealed class Definition
        {
            [DataMember(Name="schema_version")]public int Schema;[DataMember(Name="character_id")]public string Character;[DataMember(Name="outfit_id")]public string Outfit;
            [DataMember(Name="holiday_id")]public string Holiday;[DataMember(Name="name")]public string Name;[DataMember(Name="version")]public string Version;
            [DataMember(Name="clips")]public List<Clip> Clips;[DataMember(Name="gaze")]public Clip Gaze;[DataMember(Name="easter_egg")]public Clip Egg;
            [DataMember(Name="preview")]public string Preview;[DataMember(Name="action_name")]public string ActionName;
        }
        [DataContract] sealed class Clip
        {
            [DataMember(Name="action")]public string Action;[DataMember(Name="variant")]public string Variant;[DataMember(Name="path")]public string Path;
            [DataMember(Name="frame_count")]public int Count;[DataMember(Name="columns")]public int Columns;[DataMember(Name="fps")]public double Fps;[DataMember(Name="loop")]public bool Loop;
            [DataMember(Name="frame_width")]public int Width;[DataMember(Name="frame_height")]public int Height;[DataMember(Name="offset_x")]public double X;[DataMember(Name="offset_y")]public double Y;
        }
#pragma warning restore 0649
        public HolidayCatalog():this((string)null){}
        public HolidayCatalog(string rootDirectory)
        {
            if(rootDirectory==null)open=path=>typeof(HolidayCatalog).Assembly.GetManifestResourceStream("Dafeiyu.Holidays."+path.Replace('/','.'));
            else {string root=System.IO.Path.GetFullPath(rootDirectory).TrimEnd(System.IO.Path.DirectorySeparatorChar)+System.IO.Path.DirectorySeparatorChar;open=path=>{ValidatePath(path);string resolved=System.IO.Path.GetFullPath(System.IO.Path.Combine(root,path.Replace('/',System.IO.Path.DirectorySeparatorChar)));if(!resolved.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Asset escapes holiday root.");return File.OpenRead(resolved);};}
            var outfits=new List<OutfitDefinition>();
            try {
                var index=ReadJson<Index>("catalog.json");if(index==null || index.Version!=1 || index.Outfits==null || index.Outfits.Count>128)throw new InvalidDataException("Invalid holiday catalog.");
                var keys=new HashSet<string>(StringComparer.Ordinal);
                foreach(var entry in index.Outfits) {
                    if(entry==null || !Characters.Contains(entry.Character))throw new InvalidDataException("Untrusted holiday character.");
                    HolidayState.RequireId(entry.Outfit,false);HolidayState.RequireId(entry.Holiday,false);
                    if(String.IsNullOrWhiteSpace(entry.Name) || entry.Name.Length>80 || String.IsNullOrEmpty(entry.Version) || entry.Version.Length>64 || !keys.Add(entry.Character+":"+entry.Outfit))throw new InvalidDataException("Invalid or duplicate holiday outfit.");
                    OutfitDefinition outfit;
                    try{outfit=LoadDefinition(entry);}catch(Exception e){if(!Expected(e))throw;Warning=e.Message;outfit=new OutfitDefinition(entry.Character,entry.Outfit,entry.Holiday,entry.Name,entry.Version,false,entry.Greetings,new OutfitClipDefinition[0],null,null,null,null);}
                    outfits.Add(outfit);
                }
            }catch(Exception e){if(!Expected(e))throw;Warning=e.Message;outfits.Clear();}
            Outfits=new ReadOnlyCollection<OutfitDefinition>(outfits);
        }
        internal HolidayCatalog(IEnumerable<OutfitDefinition> outfits){Outfits=new ReadOnlyCollection<OutfitDefinition>(outfits.ToList());}
        static bool Expected(Exception e){return e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is SerializationException || e is ArgumentException || e is System.Xml.XmlException || e is OverflowException;}
        T ReadJson<T>(string path)
        {ValidatePath(path);using(var stream=open(path)){if(stream==null)throw new InvalidDataException("Missing holiday resource: "+path);using(var bounded=new MemoryStream()){CopyBounded(stream,bounded,1024*1024);bounded.Position=0;return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(bounded);}}}
        static void CopyBounded(Stream input,Stream output,int max)
        {byte[] bytes=new byte[8192];int n,total=0;while((n=input.Read(bytes,0,bytes.Length))>0){total=checked(total+n);if(total>max)throw new InvalidDataException("Holiday resource exceeds size limit.");output.Write(bytes,0,n);}}
        static void ValidatePath(string path)
        {if(String.IsNullOrEmpty(path) || path.Length>240 || path.StartsWith("/") || path.Contains("\\") || path.Contains(":") || path.Split('/').Any(x=>x.Length==0 || x=="." || x==".."))throw new InvalidDataException("Invalid holiday resource path.");}
        OutfitDefinition LoadDefinition(Entry entry)
        {
            var d=ReadJson<Definition>(entry.Definition);
            if(d==null || d.Schema!=1 || d.Character!=entry.Character || d.Outfit!=entry.Outfit || d.Holiday!=entry.Holiday || d.Name!=entry.Name || d.Version!=entry.Version || d.Clips==null || d.Clips.Count>64)throw new InvalidDataException("Outfit definition does not match catalog.");
            var clips=d.Clips.Select(x=>ConvertClip(x)).ToList();var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var clip in clips){if(!keys.Add(clip.Action+":"+clip.Variant))throw new InvalidDataException("Duplicate holiday clip.");bool allowed=clip.Variant=="default"?DefaultActions.Contains(clip.Action):clip.Variant=="alternate" && (AlternateActions.Contains(clip.Action) || clip.Action=="jumping" && (entry.Character=="e-bao" || entry.Character=="gugugaga"));if(clip.Columns!=8 || clip.FrameCount>8 || !allowed)throw new InvalidDataException("Invalid normal clip.");}
            foreach(string action in DefaultActions)if(!keys.Contains(action+":default"))throw new InvalidDataException("Incomplete default outfit: "+action);
            foreach(string action in AlternateActions)if(!keys.Contains(action+":alternate"))throw new InvalidDataException("Incomplete alternate outfit: "+action);
            if((entry.Character=="e-bao" || entry.Character=="gugugaga") && !keys.Contains("jumping:alternate"))throw new InvalidDataException("Missing alternate jumping.");
            var gaze=ConvertClip(d.Gaze);var egg=ConvertClip(d.Egg);
            if(gaze.FrameCount!=16 || gaze.Columns!=4 || egg.FrameCount!=8 || egg.Columns!=8 || egg.Loop || Math.Abs(egg.Fps-4)>0.0001)throw new InvalidDataException("Invalid gaze or Easter egg clip.");
            ValidatePng(d.Preview,480,520);
            if(String.IsNullOrWhiteSpace(d.ActionName) || d.ActionName.Length>80)throw new InvalidDataException("Missing holiday action name.");
            return new OutfitDefinition(entry.Character,entry.Outfit,entry.Holiday,entry.Name,entry.Version,entry.Ready,entry.Greetings,clips,gaze,egg,d.Preview,d.ActionName);
        }
        OutfitClipDefinition ConvertClip(Clip clip)
        {
            if(clip==null || clip.Count<1 || clip.Count>64 || clip.Columns<1 || clip.Columns>16 || clip.Width!=480 || clip.Height!=520 || Double.IsNaN(clip.Fps) || Double.IsInfinity(clip.Fps) || clip.Fps<=0 || clip.Fps>60 || Double.IsNaN(clip.X) || Double.IsNaN(clip.Y) || Math.Abs(clip.X)>480 || Math.Abs(clip.Y)>520)throw new InvalidDataException("Invalid holiday clip metadata.");
            ValidatePng(clip.Path,clip.Columns*clip.Width,((clip.Count+clip.Columns-1)/clip.Columns)*clip.Height);
            return new OutfitClipDefinition(clip.Path,clip.Count,clip.Columns,clip.Fps,clip.Loop,clip.Width,clip.Height,clip.X,clip.Y,clip.Action,String.IsNullOrEmpty(clip.Variant)?"default":clip.Variant);
        }
        void ValidatePng(string path,int width,int height)
        {
            ValidatePath(path);string geometry=width+":"+height,cached;if(validatedPngs.TryGetValue(path,out cached)){if(cached!=geometry)throw new InvalidDataException("Conflicting PNG geometry.");return;}byte[] b;
            using(var input=open(path)){if(input==null)throw new InvalidDataException("Missing holiday PNG: "+path);using(var output=new MemoryStream()){CopyBounded(input,output,32*1024*1024);b=output.ToArray();}}
            byte[] signature={137,80,78,71,13,10,26,10};if(b.Length<45 || !signature.SequenceEqual(b.Take(8)))throw new InvalidDataException("Invalid PNG signature.");
            int position=8;bool header=false,data=false,end=false;
            while(position<=b.Length-12) {
                uint length=U32(b,position);if(length>32*1024*1024 || length>b.Length-position-12)throw new InvalidDataException("Truncated PNG.");int n=(int)length;string type=Encoding.ASCII.GetString(b,position+4,4);
                uint crc=0xffffffff;for(int i=position+4;i<position+8+n;i++)crc=CrcTable[(crc^b[i])&255]^(crc>>8);crc^=0xffffffff;
                if(crc!=U32(b,position+8+n))throw new InvalidDataException("PNG checksum mismatch.");
                if(!header){if(type!="IHDR" || n!=13 || U32(b,position+8)!=width || U32(b,position+12)!=height || b[position+16]!=8 || !(new byte[]{0,2,3,4,6}).Contains(b[position+17]) || b[position+18]!=0 || b[position+19]!=0 || b[position+20]!=0)throw new InvalidDataException("PNG geometry/encoding mismatch.");header=true;}
                else if(type=="IHDR")throw new InvalidDataException("Duplicate PNG header.");
                if(type=="IDAT")data=true;
                position+=n+12;if(type=="IEND"){if(n!=0 || !data || position!=b.Length)throw new InvalidDataException("Invalid PNG ending.");end=true;break;}
            }
            if(!end)throw new InvalidDataException("PNG is incomplete.");
            validatedPngs.Add(path,geometry);
        }
        static uint[] CreateCrcTable(){var table=new uint[256];for(uint i=0;i<256;i++){uint crc=i;for(int j=0;j<8;j++)crc=(crc&1)!=0?(crc>>1)^0xedb88320:crc>>1;table[i]=crc;}return table;}
        static uint U32(byte[] b,int i){return ((uint)b[i]<<24)|((uint)b[i+1]<<16)|((uint)b[i+2]<<8)|b[i+3];}
        public OutfitDefinition Find(string characterId,string outfitId){return Outfits.FirstOrDefault(x=>x.CharacterId==characterId && x.OutfitId==outfitId);}
        internal bool ReportUnavailable(OutfitDefinition outfit)
        {
            // Reference identity also binds this catalog's immutable resource version.
            if(outfit==null || !Object.ReferenceEquals(Find(outfit.CharacterId,outfit.OutfitId),outfit) || !outfit.Ready)return false;
            outfit.MarkUnavailable();return true;
        }
        public OutfitDefinition Active(string characterId,IList<HolidayOccurrence> events)
        {foreach(var occurrence in events){var result=Outfits.FirstOrDefault(x=>x.CharacterId==characterId && x.HolidayId==occurrence.Id && x.Ready);if(result!=null)return result;}return null;}
    }
}
