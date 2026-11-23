using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using Dafeiyu.Maintenance;

namespace Dafeiyu
{
    [DataContract]
    internal sealed class AttachmentReference
    {
        internal AttachmentReference(string id, string name, long size, string hash, string ownerId)
        {
            Id = id;
            Name = name;
            Size = size;
            Hash = hash;
            OwnerId = ownerId;
        }

        [DataMember] internal string Id { get; private set; }
        [DataMember] internal string Name { get; private set; }
        [DataMember] internal long Size { get; private set; }
        [DataMember] internal string Hash { get; private set; }
        [DataMember(EmitDefaultValue=false)] internal string OwnerId { get; private set; }

        internal bool IsValid
        {
            get { return AttachmentFileStore.ValidId(Id) && Name != null && Name.Length > 0 &&
                Name.Length <= 255 && Size > 0 && Size <= AttachmentFileStore.AbsoluteFileLimit &&
                Hash != null && Regex.IsMatch(Hash, @"\A[a-f0-9]{64}\z") &&
                (OwnerId == null || AttachmentFileStore.ValidOwner(OwnerId)); }
        }
    }

    internal sealed class AttachmentFileLease : IDisposable
    {
        Action released;
        internal AttachmentFileLease(string path, FileStream stream,Action released)
        {
            Path = path;
            Stream = stream;
            this.released=released;
        }

        internal string Path { get; private set; }
        internal FileStream Stream { get; private set; }
        public void Dispose()
        {
            var finish=Interlocked.Exchange(ref released,null);
            if(finish==null)return;
            try{Stream.Dispose();}finally{finish();}
        }
    }

    internal sealed class AttachmentFileStore
    {
        internal const string FolderName = "file-attachments";
        internal const long AbsoluteFileLimit = 512L * 1024 * 1024;
        const long ProfileLimit = 2L * 1024 * 1024 * 1024;

        static readonly object Gate = new object();
        static readonly HashSet<string> Temporary = new HashSet<string>();
        static readonly HashSet<string> DeferredDeletes = new HashSet<string>();
        static readonly Dictionary<string,int> Readers = new Dictionary<string,int>(
            Environment.OSVersion.Platform==PlatformID.Win32NT?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
        static readonly Regex IdPattern = new Regex(@"\A[pt]-[a-f0-9]{32}\.[a-z0-9]{1,12}(?:\.[a-z0-9]{1,12})?\z");
        static readonly Regex SuffixPattern = new Regex(@"\A\.[a-z0-9]{1,12}(?:\.[a-z0-9]{1,12})?\z");
        readonly string root;
        readonly string directory;

        static AttachmentFileStore()
        {
            AppDomain.CurrentDomain.ProcessExit += delegate
            {
                lock (Gate) foreach (string path in Temporary) DeleteIfPresent(path);
            };
        }

        internal AttachmentFileStore(string dataRoot)
        {
            root = Path.GetFullPath(dataRoot);
            directory = Path.Combine(root, FolderName);
        }

        internal static bool ValidId(string id)
        {
            return !String.IsNullOrEmpty(id) && id.Length <= 60 && IdPattern.IsMatch(id);
        }

        internal static bool ValidOwner(string id)
        {
            Guid value;
            return id != null && Guid.TryParseExact(id, "N", out value) && value.ToString("N") == id;
        }

        string DirectoryFor(string ownerId)
        {
            if (ownerId == null) return directory;
            if (!ValidOwner(ownerId)) throw new ArgumentException("Invalid attachment owner.");
            return Path.Combine(root, "conversations", ownerId, FolderName);
        }

        internal AttachmentReference Import(string source, string suffix, long maximumBytes, bool persistent,
            CancellationToken token, Action<string, Stream> validate = null, string ownerId = null)
        {
            token.ThrowIfCancellationRequested();
            if (String.IsNullOrEmpty(suffix) || !SuffixPattern.IsMatch(suffix)) throw new ArgumentException("Invalid attachment file suffix.");
            if (maximumBytes <= 0 || maximumBytes > AbsoluteFileLimit) throw new ArgumentOutOfRangeException("maximumBytes");
            ownerId = ValidOwner(ownerId) ? ownerId : null;
            string location = DirectoryFor(ownerId);
            string id = (persistent ? "p-" : "t-") + Guid.NewGuid().ToString("N") + suffix;
            string target = Path.Combine(location, id);
            string temporary = target + ".part";
            string name = Path.GetFileName(source);
            if (String.IsNullOrEmpty(name) || name.Length > 255) throw new ArgumentException("Invalid attachment file name.");

            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan))
            {
                long length = input.Length;
                if (length <= 0 || length > maximumBytes) throw new IOException(TextResources.Get("chatAttachment.read.text"));
                DateTime originalWrite = File.GetLastWriteTimeUtc(source);
                if (validate != null) { validate(source, input); input.Position = 0; }
                lock (Gate)
                {
                    token.ThrowIfCancellationRequested();
                    CachePaths.Check(location);
                    Directory.CreateDirectory(location);
                    using (CacheDirectoryPins.OpenDirectory(location))
                    {
                        EnsureCapacity(length, token);
                        try
                        {
                            string hash;
                            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            using (var sha = SHA256.Create())
                            {
                                byte[] buffer = new byte[65536];
                                long copied = 0;
                                int read;
                                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    token.ThrowIfCancellationRequested();
                                    copied += read;
                                    if (copied > length) throw new AttachmentFileProblem(TextResources.Get("fileCompatibility.changed"));
                                    sha.TransformBlock(buffer, 0, read, buffer, 0);
                                    output.Write(buffer, 0, read);
                                }
                                if (copied != length || originalWrite != File.GetLastWriteTimeUtc(source)) throw new AttachmentFileProblem(TextResources.Get("fileCompatibility.changed"));
                                if (validate != null) validate(source, input);
                                sha.TransformFinalBlock(new byte[0], 0, 0);
                                hash = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                                output.Flush(true);
                            }
                            token.ThrowIfCancellationRequested();
                            File.Move(temporary, target);
                            if (!persistent) Temporary.Add(target);
                            return new AttachmentReference(id, name, length, hash, ownerId);
                        }
                        finally { DeleteIfPresent(temporary); }
                    }
                }
            }
        }

        void EnsureCapacity(long incoming, CancellationToken token)
        {
            var locations = new List<string> { directory };
            string conversations = Path.Combine(root, "conversations");
            CachePaths.Check(conversations);
            if (Directory.Exists(conversations))
                foreach (string child in Directory.EnumerateDirectories(conversations))
                {
                    token.ThrowIfCancellationRequested();
                    string owner = Path.GetFileName(child);
                    if (ValidOwner(owner)) locations.Add(DirectoryFor(owner));
                }
            long used = 0;
            foreach (string location in locations)
            {
                CachePaths.Check(location);
                if (!Directory.Exists(location)) continue;
                foreach (var existing in new DirectoryInfo(location).EnumerateFiles())
                {
                    token.ThrowIfCancellationRequested();
                    if (!ValidId(existing.Name)) continue;
                    CachePaths.Check(existing.FullName);
                    used += existing.Length;
                    if (used > ProfileLimit - incoming) throw new AttachmentFileProblem(TextResources.Get("fileCompatibility.quota"));
                }
            }
        }

        internal bool Exists(string id)
        {
            if (!ValidId(id)) return false;
            string path = Path.Combine(directory, id);
            CachePaths.Check(path);
            return File.Exists(path);
        }

        internal AttachmentFileLease Open(string id)
        {
            if (!ValidId(id)) throw new ArgumentException("Invalid attachment reference.");
            return OpenPath(Path.Combine(directory, id));
        }

        internal AttachmentFileLease Open(AttachmentReference reference, CancellationToken token = default(CancellationToken))
        {
            if (reference == null || !reference.IsValid) throw new ArgumentException("Invalid attachment reference.");
            string path = Path.Combine(DirectoryFor(reference.OwnerId), reference.Id);
            CachePaths.Check(path);
            if (!File.Exists(path))
            {
                path = Path.Combine(root,"preserved-attachments",reference.Hash+FileContentService.Extension(reference.Id));
                if(!File.Exists(path))throw new FileNotFoundException(TextResources.Get("fileCompatibility.missing"));
            }
            var lease = OpenPath(path);
            try
            {
                if(lease.Stream.Length==reference.Size && HashStream(lease.Stream,token)==reference.Hash)
                {
                    lease.Stream.Position=0;
                    return lease;
                }
                throw new AttachmentFileProblem(TextResources.Get("fileCompatibility.changed"));
            }
            catch { lease.Dispose(); throw; }
        }

        static string HashStream(Stream stream,CancellationToken token)
        {
            byte[] block=new byte[65536];
            using(var sha=SHA256.Create())
            {
                int count;
                while((count=stream.Read(block,0,block.Length))!=0)
                {
                    token.ThrowIfCancellationRequested();
                    sha.TransformBlock(block,0,count,block,0);
                }
                sha.TransformFinalBlock(new byte[0],0,0);
                return BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();
            }
        }

        internal void Track(AttachmentReference reference,bool temporary)
        {
            if(reference==null||!reference.IsValid)return;
            string path=Path.Combine(DirectoryFor(reference.OwnerId),reference.Id);
            lock(Gate)
            {
                if(temporary)Temporary.Add(path);
                else Temporary.Remove(path);
            }
        }

        internal void DiscardDraft(AttachmentReference reference)
        {
            if(reference==null||!reference.IsValid)return;
            string path=Path.Combine(DirectoryFor(reference.OwnerId),reference.Id);
            lock(Gate)if(Temporary.Contains(path))Discard(reference);
        }

        static AttachmentFileLease OpenPath(string path)
        {
            lock(Gate)
            {
                CachePaths.Check(path);
                var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
                int count;
                Readers.TryGetValue(path,out count);Readers[path]=count+1;
                return new AttachmentFileLease(path,stream,()=>
                {
                    lock(Gate)
                    {
                        int remaining=Readers[path]-1;
                        if(remaining>0){Readers[path]=remaining;return;}
                        Readers.Remove(path);
                        if(DeferredDeletes.Remove(path))DeleteIfPresent(path);
                    }
                });
            }
        }

        internal void Discard(AttachmentReference reference)
        {
            if (reference == null || !reference.IsValid) return;
            lock (Gate)
            {
                string path = Path.Combine(DirectoryFor(reference.OwnerId), reference.Id);
                CachePaths.Check(path);
                if(Readers.ContainsKey(path))DeferredDeletes.Add(path);
                else DeleteIfPresent(path);
                Temporary.Remove(path);
            }
        }

        static void DeleteIfPresent(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal sealed class AttachmentFileProblem : IOException
    {
        internal AttachmentFileProblem(string message):base(message) { }
    }
}
