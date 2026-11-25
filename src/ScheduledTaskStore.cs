using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Dafeiyu
{
    [DataContract]
    internal sealed class ScheduledTaskRecord
    {
        [DataMember] internal ScheduledTaskDefinition Definition;
        [DataMember] internal DateTimeOffset StartAfterUtc;
        [DataMember] internal long LastCivilTicks;
        [DataMember] internal List<ScheduledTaskRun> Runs = new List<ScheduledTaskRun>();

        internal ScheduledTaskRecord Copy()
        {
            return new ScheduledTaskRecord
            {
                Definition = Definition.Copy(), StartAfterUtc = StartAfterUtc, LastCivilTicks = LastCivilTicks,
                Runs = Runs.Select(run => run.Copy()).ToList()
            };
        }
    }

    internal sealed class ScheduledTaskStore : IDisposable
    {
        const int MaximumBytes = 64 * 1024 * 1024;
        readonly string path;
        readonly FileStream lease;
        readonly DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(List<ScheduledTaskRecord>));

        internal ScheduledTaskStore(string root)
        {
            var directory = Path.Combine(root, "scheduled-tasks");
            CheckPath(directory);
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "tasks.dat");
            var lockPath = Path.Combine(directory, "writer.lock");
            CheckPath(directory);
            CheckPath(lockPath);
            lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }

        internal List<ScheduledTaskRecord> Load()
        {
            CheckPath(Path.GetDirectoryName(path)); CheckPath(path);
            if (!File.Exists(path)) return new List<ScheduledTaskRecord>();
            if (new FileInfo(path).Length > MaximumBytes) throw InvalidStorage();
            var plain = SecretProtection.Unprotect(File.ReadAllBytes(path));
            if (plain.Length == 0 || plain.Length > MaximumBytes) throw InvalidStorage();
            using (var stream = new MemoryStream(plain))
            {
                var records = (List<ScheduledTaskRecord>)serializer.ReadObject(stream);
                if (records == null || records.Any(r => r == null || r.Definition == null || !r.Definition.IsValid
                    || String.IsNullOrEmpty(r.Definition.Id) || r.Runs == null || r.LastCivilTicks < 0 || r.LastCivilTicks > DateTime.MaxValue.Ticks
                    || r.Runs.Any(run => run == null || run.TaskId != r.Definition.Id || String.IsNullOrEmpty(run.Id)
                        || !Enum.IsDefined(typeof(ScheduledRunState), run.State)))
                    || records.Select(r => r.Definition.Id).Distinct().Count() != records.Count) throw InvalidStorage();
                return records;
            }
        }

        internal void Save(List<ScheduledTaskRecord> records)
        {
            try { SaveCore(records); }
            catch (SerializationException error) { throw InvalidStorage(error); }
            catch (System.Security.Cryptography.CryptographicException error) { throw InvalidStorage(error); }
            catch (System.Xml.XmlException error) { throw InvalidStorage(error); }
        }

        void SaveCore(List<ScheduledTaskRecord> records)
        {
            CheckPath(Path.GetDirectoryName(path)); CheckPath(path);
            byte[] encrypted;
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, records);
                if (stream.Length > MaximumBytes) throw InvalidStorage();
                encrypted = SecretProtection.Protect(stream.ToArray());
            }
            if (encrypted.Length > MaximumBytes) throw InvalidStorage();
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(encrypted, 0, encrypted.Length);
                    stream.Flush(true);
                }
                AtomicFile.Commit(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary))
                    try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        static void CheckPath(string target)
        {
            if ((File.Exists(target) || Directory.Exists(target)) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                throw InvalidStorage();
        }

        static IOException InvalidStorage(Exception cause = null) { return new IOException(TextResources.Get("scheduledTasks.storageError"), cause); }

        public void Dispose() { lease.Dispose(); }
    }
}
