using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    public sealed class UsageStore
    {
        readonly object gate=new object();
        readonly string path;
        readonly Dictionary<string,UsageRecord> records=new Dictionary<string,UsageRecord>(StringComparer.Ordinal);
        readonly Dictionary<string,UsageRecord> pending=new Dictionary<string,UsageRecord>(StringComparer.Ordinal);
        string warning="";bool loadFailed;
        public event Action Changed;
        public string Warning {get{lock(gate)return warning;}}

        public UsageStore(string directory)
        {
            if(String.IsNullOrWhiteSpace(directory))throw new ArgumentException("A usage directory is required.","directory");
            path=Path.Combine(Path.GetFullPath(directory),"usage.jsonl");
            Load();
        }
        static bool StorageError(Exception error)
        {return error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException;}
        void Load()
        {
            var latest=new List<UsageRecord>(records.Values);bool damaged=false;
            try
            {
                if(File.Exists(path))foreach(string line in File.ReadLines(path,Encoding.UTF8))
                {
                    if(String.IsNullOrWhiteSpace(line))continue;
                    try
                    {
                        var value=Read(line);
                        if(value!=null && !String.IsNullOrWhiteSpace(value.RequestId))Merge(value);else damaged=true;
                    }
                    catch(Exception error)
                    {if(!(error is ArgumentException || error is InvalidOperationException || error is FormatException))throw;damaged=true;}
                }
                loadFailed=false;warning=damaged?"部分历史用量行已损坏，已保留原文件并跳过这些行。":"";
            }
            catch(Exception error)
            {
                if(!StorageError(error))throw;
                loadFailed=true;warning="历史用量暂时无法读取；当前请求仅暂存内存，请检查存储权限与空间。";
            }
            // Requests captured while the file was inaccessible remain the newest snapshots.
            foreach(var record in latest)Merge(record);
        }
        public IList<UsageRecord> ReadAll()
        {
            lock(gate)
            {
                var result=new List<UsageRecord>();
                foreach(var record in records.Values)result.Add(record.Copy());
                return result;
            }
        }
        public void AddOrUpdate(UsageRecord record)
        {
            if(record==null || String.IsNullOrWhiteSpace(record.RequestId))throw new ArgumentException("A request id is required.","record");
            lock(gate)
            {
                if(loadFailed)Load();
                var merged=Merge(record);pending[merged.RequestId]=merged.Copy();
                if(!loadFailed)try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using(var file=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read))
                    {
                        // Separate an incomplete row left by interruption before appending.
                        if(file.Length>0)
                        {
                            file.Seek(-1,SeekOrigin.End);
                            if(file.ReadByte()!=10){file.Seek(0,SeekOrigin.End);file.WriteByte(10);}
                        }
                        file.Seek(0,SeekOrigin.End);
                        foreach(var snapshot in pending.Values)
                        {byte[] bytes=Encoding.UTF8.GetBytes(Write(snapshot)+"\n");file.Write(bytes,0,bytes.Length);}
                        file.Flush(true);
                    }
                    pending.Clear();warning="";
                    if(new FileInfo(path).Length>Math.Max(4*1024*1024,records.Count*1024L))Compact();
                }
                catch(Exception error)
                {
                    if(!StorageError(error))throw;
                    warning="用量记录暂未完整写入磁盘，当前请求保留在内存；请检查存储权限与空间。";
                }
            }
            var changed=Changed;
            if(changed!=null)foreach(Action subscriber in changed.GetInvocationList())
                try{subscriber();}catch(Exception){/* A view cannot break model accounting. */}
        }
        void Compact()
        {
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                {
                    foreach(var snapshot in records.Values)
                    {byte[] bytes=Encoding.UTF8.GetBytes(Write(snapshot)+"\n");file.Write(bytes,0,bytes.Length);}
                    file.Flush(true);
                }
                File.Replace(temporary,path,null);
            }
            finally
            {try{if(File.Exists(temporary))File.Delete(temporary);}catch(Exception error){if(!StorageError(error))throw;}}
        }
        UsageRecord Merge(UsageRecord value)
        {
            UsageRecord old;var record=value.Copy();
            record.TimestampUtc=record.TimestampUtc==default(DateTime)?DateTime.UtcNow:
                record.TimestampUtc.Kind==DateTimeKind.Unspecified?DateTime.SpecifyKind(record.TimestampUtc,DateTimeKind.Utc):record.TimestampUtc.ToUniversalTime();
            if(records.TryGetValue(record.RequestId,out old))
            {
                record.TimestampUtc=old.TimestampUtc;
                record.Provider=record.Provider??old.Provider;record.Model=record.Model??old.Model;record.Feature=record.Feature??old.Feature;
                record.ConversationId=record.ConversationId??old.ConversationId;record.ProjectId=record.ProjectId??old.ProjectId;record.Status=record.Status??old.Status;
                // Unknown snapshots carry lifecycle metadata, not a competing measurement.
                // Keep the existing source until a snapshot actually supplies counters.
                if(!HasCounters(record) || (!old.IsEstimated && HasCounters(old) && record.IsEstimated))
                {
                    record.InputTokens=old.InputTokens;record.OutputTokens=old.OutputTokens;record.CachedInputTokens=old.CachedInputTokens;
                    record.ReasoningTokens=old.ReasoningTokens;record.TotalTokens=old.TotalTokens;record.IsEstimated=old.IsEstimated;
                }
                else if(old.IsEstimated==record.IsEstimated)
                {
                    record.InputTokens=UsageRecord.Max(old.InputTokens,record.InputTokens);record.OutputTokens=UsageRecord.Max(old.OutputTokens,record.OutputTokens);
                    record.CachedInputTokens=UsageRecord.Max(old.CachedInputTokens,record.CachedInputTokens);record.ReasoningTokens=UsageRecord.Max(old.ReasoningTokens,record.ReasoningTokens);
                    record.TotalTokens=UsageRecord.Max(old.TotalTokens,record.TotalTokens);
                }
            }
            record.TotalTokens=UsageRecord.Max(record.TotalTokens,UsageRecord.Sum(record.InputTokens,record.OutputTokens));
            records[record.RequestId]=record;return record;
        }
        static bool HasCounters(UsageRecord record)
        {return record.InputTokens.HasValue || record.OutputTokens.HasValue || record.CachedInputTokens.HasValue || record.ReasoningTokens.HasValue || record.TotalTokens.HasValue;}
        // Explicit ISO timestamps keep the on-disk contract identical in Framework and .NET.
        static string Write(UsageRecord r)
        {
            return new JavaScriptSerializer().Serialize(new Dictionary<string,object>{
                {"RequestId",r.RequestId},{"TimestampUtc",r.TimestampUtc.ToString("o",CultureInfo.InvariantCulture)},
                {"Provider",r.Provider},{"Model",r.Model},{"Feature",r.Feature},{"ConversationId",r.ConversationId},{"ProjectId",r.ProjectId},{"Status",r.Status},
                {"InputTokens",r.InputTokens},{"OutputTokens",r.OutputTokens},{"CachedInputTokens",r.CachedInputTokens},{"ReasoningTokens",r.ReasoningTokens},{"TotalTokens",r.TotalTokens},{"IsEstimated",r.IsEstimated}});
        }
        static UsageRecord Read(string line)
        {
            var data=new JavaScriptSerializer().DeserializeObject(line) as Dictionary<string,object>;
            if(data==null)return null;
            DateTime timestamp;
            if(!DateTime.TryParse(Text(data,"TimestampUtc"),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out timestamp))return null;
            object estimated;
            return new UsageRecord{RequestId=Text(data,"RequestId"),TimestampUtc=timestamp,Provider=Text(data,"Provider"),Model=Text(data,"Model"),Feature=Text(data,"Feature"),ConversationId=Text(data,"ConversationId"),ProjectId=Text(data,"ProjectId"),Status=Text(data,"Status"),
                InputTokens=Number(data,"InputTokens"),OutputTokens=Number(data,"OutputTokens"),CachedInputTokens=Number(data,"CachedInputTokens"),ReasoningTokens=Number(data,"ReasoningTokens"),TotalTokens=Number(data,"TotalTokens"),IsEstimated=data.TryGetValue("IsEstimated",out estimated) && Object.Equals(estimated,true)};
        }
        static string Text(Dictionary<string,object> data,string key){object raw;return data.TryGetValue(key,out raw)?raw as string:null;}
        static long? Number(Dictionary<string,object> data,string key)
        {
            object raw;if(!data.TryGetValue(key,out raw) || !(raw is int || raw is long))return null;
            long value=Convert.ToInt64(raw,CultureInfo.InvariantCulture);return value>=0?value:(long?)null;
        }
    }
}
