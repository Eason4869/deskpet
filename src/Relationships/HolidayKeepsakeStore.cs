using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Xml;
namespace Dafeiyu
{
    internal sealed class HolidayKeepsakeIncompatibleException:IOException
    {internal HolidayKeepsakeIncompatibleException(Exception inner):base("Holiday state is incompatible; original bytes preserved.",inner){}}
    internal sealed class HolidayKeepsakeStore
    {
        const int MaximumStateBytes=32*1024*1024;readonly string root;
        internal HolidayKeepsakeStore(string dataDirectory)
        {if(String.IsNullOrWhiteSpace(dataDirectory))throw new ArgumentException("Data directory required.");root=Path.Combine(Path.GetFullPath(dataDirectory),"commemorations");}
        string Safe(string relative){return CompanionPackStorage.AssetPath(root,relative);}
        internal IDisposable Acquire()
        {
            string path=Safe(".transaction.lock");Directory.CreateDirectory(root);var timer=Stopwatch.StartNew();
            while(true){path=Safe(".transaction.lock");try{return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
                catch(IOException){if(timer.ElapsedMilliseconds>=5000)throw;Thread.Sleep(20);}}
        }
        internal HolidayKeepsakeData Load()
        {
            string path=Safe("holiday-state.json");if(Directory.Exists(path))throw new IOException("State path is a directory.");
            if(!File.Exists(path)){RelationshipWriteRecovery.RequireNoCandidate(root,true);return new HolidayKeepsakeData();}
            try{
                using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete)){
                    if(input.Length==0||input.Length>MaximumStateBytes)throw new InvalidDataException("State size is invalid.");
                    RequireSingleObject(input);
                    using(var reader=JsonReaderWriterFactory.CreateJsonReader(input,XmlDictionaryReaderQuotas.Max)){
                        var data=(HolidayKeepsakeData)new DataContractJsonSerializer(typeof(HolidayKeepsakeData)).ReadObject(reader);
                        // Reader buffering can consume bytes beyond the first root.
                        // Require parser EOF, not merely the underlying stream end.
                        if(reader.MoveToContent()!=XmlNodeType.None||!reader.EOF)throw new InvalidDataException("Extra content after holiday state document.");
                        Validate(data);return data;
                    }
                }
            }catch(Exception error){if(error is SerializationException||error is InvalidDataException||error is ArgumentException||error is FormatException||error is System.Xml.XmlException||error is OverflowException)throw new HolidayKeepsakeIncompatibleException(error);throw;}
        }
        // XmlJsonReader itself can report EOF after the first object in "{}{}".
        // Check only the document envelope here; the JSON reader still validates
        // the complete grammar. Quotes/escapes prevent braces in names affecting
        // nesting. This scans actual characters, never a parser-buffer position.
        static void RequireSingleObject(Stream input)
        {
            bool started=false,complete=false,inString=false,escaped=false;int depth=0;
            using(var text=new StreamReader(input,new UTF8Encoding(false,true),true,4096,true)){
                var buffer=new char[4096];int count;
                while((count=text.Read(buffer,0,buffer.Length))>0)for(int i=0;i<count;i++){
                    char c=buffer[i];bool whitespace=c==' '||c=='\t'||c=='\r'||c=='\n';
                    if(complete){if(!whitespace)throw new InvalidDataException("Extra content after holiday state document.");continue;}
                    if(!started){if(whitespace)continue;if(c!='{')throw new InvalidDataException("Holiday state must be one JSON object.");started=true;}
                    if(inString){if(escaped)escaped=false;else if(c=='\\')escaped=true;else if(c=='"')inString=false;continue;}
                    if(c=='"')inString=true;
                    else if(c=='{'||c=='[')depth++;
                    else if(c=='}'||c==']'){depth--;if(depth==0)complete=true;}
                }
            }
            if(!complete||inString)throw new InvalidDataException("Incomplete holiday state document.");
            input.Position=0; // Rewind for grammar parsing, not an EOF assertion.
        }
        internal void Save(HolidayKeepsakeData data)
        {
            Validate(data);string temporary=".state-"+Guid.NewGuid().ToString("N")+".tmp";
            bool replacing=RelationshipWriteRecovery.AuthoritativeExists(Safe("holiday-state.json"));
            try{
                using(var output=new FileStream(Safe(temporary),FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                    new DataContractJsonSerializer(typeof(HolidayKeepsakeData)).WriteObject(output,data);
                    if(output.Length>MaximumStateBytes)throw new IOException("Holiday store full; no records discarded.");output.Flush(true);
                }
                string path=Safe("holiday-state.json"),temp=Safe(temporary);
                AtomicFile.Commit(temp,path);
            }finally{CleanOwnTemporary(temporary,replacing?"holiday-state.json":null);}
        }
        void CleanOwnTemporary(string relative,string authoritative=null)
        {try{if(authoritative!=null&&!File.Exists(Safe(authoritative)))return;string path=Safe(relative);if(File.Exists(path))File.Delete(path);}catch(InvalidDataException){}catch(IOException){}catch(UnauthorizedAccessException){}}
        internal void PublishScene(HolidayKeepsakeRecord record,byte[] bytes)
        {
            string relative="scenes/"+record.SceneSha256+".png",path=Safe(relative);
            if(File.Exists(path)){
                using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
                    if(input.Length!=bytes.Length)throw new InvalidDataException("Existing scene corrupt.");
                    int at=0,count;var buffer=new byte[16384];while((count=input.Read(buffer,0,buffer.Length))>0)for(int i=0;i<count;i++)if(at>=bytes.Length||buffer[i]!=bytes[at++])throw new InvalidDataException("Existing scene corrupt.");
                    if(at!=bytes.Length)throw new InvalidDataException("Existing scene truncated.");
                }return;
            }
            Directory.CreateDirectory(Safe("scenes"));string temporary="scenes/.scene-"+Guid.NewGuid().ToString("N")+".tmp";
            try{using(var output=new FileStream(Safe(temporary),FileMode.CreateNew,FileAccess.Write,FileShare.None)){output.Write(bytes,0,bytes.Length);output.Flush(true);}File.Move(Safe(temporary),Safe(relative));}
            finally{CleanOwnTemporary(temporary);}
        }
        internal byte[] ReadScene(HolidayKeepsakeRecord record,IHolidayKeepsakeSceneValidator validator)
        {using(var input=new FileStream(Safe("scenes/"+record.SceneSha256+".png"),FileMode.Open,FileAccess.Read,FileShare.Read))return HolidayKeepsakePng.Read(input,record.ByteLength,record.SceneSha256,record.PixelWidth,record.PixelHeight,validator);}
        internal static string Date(DateTime value){return value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);}
        static DateTime Parse(string value){return DateTime.ParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None);}
        internal static void Validate(HolidayKeepsakeData data)
        {
            if(data==null||data.Schema!=1||data.Records==null||data.DateHighWater==null)throw new InvalidDataException("Unknown or incomplete holiday state.");
            if(data.DateHighWater!="")Parse(data.DateHighWater);
            var years=new HashSet<string>(StringComparer.Ordinal);var instances=new HashSet<string>(StringComparer.Ordinal);
            foreach(var r in data.Records){
                if(r==null||!Key(r.CharacterId)||r.CompanionId!="official:"+r.CharacterId||!Key(r.HolidayId)||!Key(r.CalendarId)||!Key(r.OutfitId)||!Key(r.OutfitVersion)||!Key(r.SceneId)||!Key(r.SceneVersion)
                    ||r.CompanionName==null||r.CompanionName.Length>256||r.CompanionName.IndexOf('\0')>=0||r.SceneSha256==null||!Regex.IsMatch(r.SceneSha256,@"\A[0-9a-f]{64}\z")
                    ||!HolidayKeepsakePng.Geometry(r.PixelWidth,r.PixelHeight)||r.ByteLength<45||r.ByteLength>HolidayKeepsakePng.MaximumBytes)throw new InvalidDataException("Invalid holiday record facts.");
                var start=Parse(r.OccurrenceStartLocalDate);var end=Parse(r.OccurrenceEndExclusiveLocalDate);var earned=Parse(r.EarnedLocalDate);
                var time=new RelationshipTime(r.EarnedUtc,TimeSpan.FromMinutes(r.OffsetMinutes));
                if(start.Year!=r.OccurrenceYear||end<=start||(end-start).TotalDays>366||earned<start||earned>=end||time.LocalDate!=r.EarnedLocalDate||String.CompareOrdinal(data.DateHighWater,r.EarnedLocalDate)<0
                    ||r.InstanceKey!=r.CalendarId+":"+start.ToString("yyyyMMdd",CultureInfo.InvariantCulture)||!years.Add(r.CompanionId+"/"+r.HolidayId+"/"+r.OccurrenceYear)||!instances.Add(r.CompanionId+"/"+r.HolidayId+"/"+r.InstanceKey))throw new InvalidDataException("Invalid holiday occurrence or duplicate claim.");
            }
        }
        static bool Key(string value){return HolidayKeepsakeDefinition.ValidKey(value);}
    }
}
