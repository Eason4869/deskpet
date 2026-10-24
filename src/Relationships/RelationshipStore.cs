using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Threading;

namespace Dafeiyu
{
    public sealed class RelationshipStore
    {
        const int MaximumBytes = 32 * 1024 * 1024;
        readonly string root;
        public string FilePath { get { return CompanionPackStorage.AssetPath(root, "state.json"); } }
        public RelationshipStore(string dataDirectory)
        {
            if (String.IsNullOrWhiteSpace(dataDirectory)) throw new ArgumentException("A data directory is required.");
            root = CompanionPackStorage.AssetPath(Path.GetFullPath(dataDirectory), "relationships");
        }
        internal IDisposable Acquire()
        {
            string path = CompanionPackStorage.AssetPath(root, ".transaction.lock"); Directory.CreateDirectory(root);
            var timer = Stopwatch.StartNew();
            while (true) {
                try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { if (timer.ElapsedMilliseconds >= 5000) throw new IOException("Relationship store is busy."); Thread.Sleep(20); }
            }
        }
        internal RelationshipData Load()
        {
            string path = FilePath;
            if (!File.Exists(path)) return new RelationshipData();
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                if (input.Length == 0 || input.Length > MaximumBytes) throw new InvalidDataException("Relationship store size is invalid.");
                var result = (RelationshipData)new DataContractJsonSerializer(typeof(RelationshipData)).ReadObject(input);
                RelationshipRules.Validate(result); return result;
            }
        }
        internal void Save(RelationshipData data)
        {
            RelationshipRules.Validate(data);
            string path = FilePath, temporary = CompanionPackStorage.AssetPath(root, ".state-" + Guid.NewGuid().ToString("N") + ".tmp");
            try {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    new DataContractJsonSerializer(typeof(RelationshipData)).WriteObject(output, data);
                    if (output.Length > MaximumBytes) throw new InvalidDataException("Relationship store is full; receipts cannot be discarded.");
                    output.Flush(true);
                }
                // No recovery-to-defaults or backup fallback: corrupt/newer bytes remain authoritative and untouched.
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        internal static RelationshipData Copy(RelationshipData data)
        {
            using (var stream = new MemoryStream()) {
                var serializer = new DataContractJsonSerializer(typeof(RelationshipData)); serializer.WriteObject(stream, data); stream.Position = 0;
                return (RelationshipData)serializer.ReadObject(stream);
            }
        }
    }
}
