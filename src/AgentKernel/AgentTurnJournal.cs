using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml;
using Dafeiyu.Maintenance;
using Microsoft.Win32.SafeHandles;

namespace Dafeiyu
{
    [DataContract]
    internal sealed class AgentTurnEvent
    {
        [DataMember(IsRequired = true)] public int Version { get; internal set; }
        [DataMember(IsRequired = true)] public long Sequence { get; internal set; }
        [DataMember(IsRequired = true)] internal long CreatedUtcTicks { get; set; }
        [DataMember(IsRequired = true)] public string Kind { get; set; }
        [DataMember(IsRequired = true)] public string TurnId { get; set; }
        [DataMember(EmitDefaultValue = false)] public string TaskId { get; set; }
        [DataMember(EmitDefaultValue = false)] public string StepId { get; set; }
        [DataMember(EmitDefaultValue = false)] public string CallId { get; set; }
        [DataMember(EmitDefaultValue = false)] public string Outcome { get; set; }
        [DataMember(EmitDefaultValue = false)] public string Text { get; set; }
        [DataMember(EmitDefaultValue = false)] public AgentToolEvent Tool { get; set; }
        [DataMember(EmitDefaultValue = false)] public long Revision { get; set; }

        public DateTime CreatedUtc { get { return new DateTime(CreatedUtcTicks, DateTimeKind.Utc); } }

        internal AgentTurnEvent WithPosition(long sequence)
        {
            var record = (AgentTurnEvent)MemberwiseClone();
            record.Version = 1;
            record.Sequence = sequence;
            record.CreatedUtcTicks = DateTime.UtcNow.Ticks;
            record.Tool = Tool == null ? null : Tool.Copy();
            return record;
        }
    }

    internal sealed class AgentTurnJournal
    {
        internal const string FileName = "turn.events";
        const string ActiveLockName = "turn.active.lock";
        const string WriterLockName = "turn.write.lock";
        const string Envelope = "DPTE1:";
        const int MaxFrameBytes = 4 * 1024 * 1024;
        static readonly bool Windows = Environment.OSVersion.Platform == PlatformID.Win32NT;
        static readonly AgentTurnEvent[] Empty = new AgentTurnEvent[0];

        readonly string directory;
        readonly string path;
        readonly object gate = new object();
        TurnLease liveTurn;
        FileStream liveWriter;
        Position livePosition;

        struct Position
        {
            internal long Sequence;
            internal long End;
        }

        internal AgentTurnJournal(string directory)
        {
            this.directory = Path.GetFullPath(directory);
            path = IoPath(Path.Combine(this.directory, FileName));
        }

        internal IDisposable AcquireTurn()
        {
            lock (gate)
            {
                if (liveTurn != null) throw Busy();
                var pins = PinDirectory(true);
                try
                {
                    var handle = TryLock(ActiveLockName);
                    if (handle == null) throw Busy();
                    liveTurn = new TurnLease(this, handle, pins);
                    return liveTurn;
                }
                catch
                {
                    pins.Dispose();
                    throw;
                }
            }
        }

        internal AgentTurnEvent Append(string kind, string turnId, string taskId = null, string stepId = null,
            string callId = null, string outcome = null, string text = null, AgentToolEvent tool = null)
        {
            return AppendBatch(new[]
            {
                new AgentTurnEvent
                {
                    Kind = kind, TurnId = turnId, TaskId = taskId, StepId = stepId,
                    CallId = callId, Outcome = outcome, Text = text, Tool = tool
                }
            })[0];
        }

        internal IReadOnlyList<AgentTurnEvent> AppendBatch(IEnumerable<AgentTurnEvent> events)
        {
            if (events == null) throw new ArgumentNullException("events");
            var batch = events.ToArray();
            if (batch.Length == 0) return Empty;
            if (batch.Any(record => record == null || !HasIdentity(record) || record.Revision < 0))
                throw new ArgumentException("Invalid agent turn event.", "events");

            lock (gate)
            {
                if (liveTurn != null)
                {
                    using (WaitWriter())
                    {
                        if (Windows) return AppendLive(batch);
                        return AppendUncached(batch);
                    }
                }

                // An unleased append must queue behind other unleased writers. The
                // active-turn check never waits while the writer lock is held.
                using (PinDirectory(true))
                using (WaitWriter())
                using (var inactive = TryLock(ActiveLockName))
                {
                    if (inactive == null) throw Busy();
                    return AppendUncached(batch);
                }
            }
        }

        IReadOnlyList<AgentTurnEvent> AppendUncached(AgentTurnEvent[] batch)
        {
            using (var stream = OpenFile(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
            {
                var position = Scan(stream, null);
                return AppendTo(stream, batch, ref position);
            }
        }

        IReadOnlyList<AgentTurnEvent> AppendLive(AgentTurnEvent[] batch)
        {
            // The Windows handle excludes other writers and deletion for the entire lease.
            // Its verified offset can be reused for subsequent batches without decrypting the prefix.
            try
            {
                if (liveWriter == null)
                {
                    liveWriter = OpenFile(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                    livePosition = Scan(liveWriter, null);
                }
                return AppendTo(liveWriter, batch, ref livePosition);
            }
            catch
            {
                CloseLiveWriter();
                throw;
            }
        }

        internal IReadOnlyList<AgentTurnEvent> Read()
        {
            using (var pins = PinDirectory(false))
            {
                if (pins == null) return Empty;
                using (WaitWriter())
                using (var stream = OpenRead())
                {
                    if (stream == null) return Empty;
                    var records = new List<AgentTurnEvent>();
                    Scan(stream, records);
                    return records.AsReadOnly();
                }
            }
        }

        internal IReadOnlyList<AgentTurnEvent> RecoverInterrupted()
        {
            using (var pins = PinDirectory(false))
            {
                if (pins == null) return Empty;
                using (var inactive = TryLock(ActiveLockName))
                {
                    if (inactive == null) return Empty;
                    using (WaitWriter())
                    {
                        if (!Exists(path, false)) return Empty;
                        using (var stream = OpenFile(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                        {
                            var records = new List<AgentTurnEvent>();
                            var position = Scan(stream, records);
                            var interrupted = Interrupted(records);
                            return AppendTo(stream, interrupted, ref position);
                        }
                    }
                }
            }
        }

        internal void Clear()
        {
            if (!TryClear(records => true)) throw Busy();
        }

        internal bool TryClear(Func<IReadOnlyList<AgentTurnEvent>, bool> shouldClear)
        {
            if (shouldClear == null) throw new ArgumentNullException("shouldClear");
            return TryRemove(shouldClear);
        }

        internal void Discard()
        {
            if (!TryRemove(null)) throw Busy();
        }

        bool TryRemove(Func<IReadOnlyList<AgentTurnEvent>, bool> shouldClear)
        {
            using (var pins = PinDirectory(false))
            {
                if (pins == null) return true;
                using (var inactive = TryLock(ActiveLockName))
                {
                    if (inactive == null) return false;
                    using (var writer = TryLock(WriterLockName))
                    {
                        if (writer == null) return false;
                        if (!Exists(path, false)) return true;
                        return Remove(shouldClear);
                    }
                }
            }
        }

        bool Remove(Func<IReadOnlyList<AgentTurnEvent>, bool> shouldClear)
        {
            if (Windows || shouldClear != null)
            {
                using (var stream = OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.None, Windows))
                {
                    if (shouldClear != null)
                    {
                        var records = new List<AgentTurnEvent>();
                        Scan(stream, records);
                        if (!shouldClear(records.AsReadOnly())) return false;
                    }

                    // Delete the exact verified Windows handle before a different
                    // file can take its place at this path.
                    if (Windows)
                    {
                        const int fileDispositionInfo = 4;
                        byte delete = 1;
                        if (!SetFileInformationByHandle(stream.SafeFileHandle, fileDispositionInfo, ref delete, 1))
                            throw WindowsIoError();
                        return true;
                    }
                }
            }
            File.Delete(path);
            return true;
        }

        static AgentTurnEvent[] Interrupted(List<AgentTurnEvent> records)
        {
            var turns = new Dictionary<string, AgentTurnEvent>(StringComparer.Ordinal);
            var steps = new Dictionary<Tuple<string, string>, AgentTurnEvent>();
            var tools = new Dictionary<Tuple<string, string, string>, AgentTurnEvent>();

            foreach (var record in records)
            {
                switch (record.Kind)
                {
                    case "turn.started":
                        turns[record.TurnId] = record;
                        break;
                    case "turn.finished":
                        turns.Remove(record.TurnId);
                        break;
                    case "step.started":
                        steps[Tuple.Create(record.TurnId, record.StepId)] = record;
                        break;
                    case "step.finished":
                        steps.Remove(Tuple.Create(record.TurnId, record.StepId));
                        break;
                    case "tool.started":
                        tools[Tuple.Create(record.TurnId, record.StepId, record.CallId)] = record;
                        break;
                    case "tool.finished":
                        tools.Remove(Tuple.Create(record.TurnId, record.StepId, record.CallId));
                        break;
                }
            }

            var interrupted = new List<AgentTurnEvent>();
            foreach (var started in tools.Values.OrderBy(record => record.Sequence))
                interrupted.Add(Finish(started, "tool.finished"));
            foreach (var started in steps.Values.OrderBy(record => record.Sequence))
                interrupted.Add(Finish(started, "step.finished"));
            foreach (var started in turns.Values.OrderBy(record => record.Sequence))
                interrupted.Add(Finish(started, "turn.finished"));
            return interrupted.ToArray();
        }

        static AgentTurnEvent Finish(AgentTurnEvent started, string kind)
        {
            return new AgentTurnEvent
            {
                Kind = kind, TurnId = started.TurnId, TaskId = started.TaskId, StepId = started.StepId,
                CallId = started.CallId, Outcome = "interrupted"
            };
        }

        static IReadOnlyList<AgentTurnEvent> AppendTo(FileStream stream, AgentTurnEvent[] batch, ref Position position)
        {
            if (batch.Length == 0) return Empty;
            var records = new AgentTurnEvent[batch.Length];
            var frames = new byte[batch.Length][];
            var serializer = Serializer();
            for (int index = 0; index < batch.Length; index++)
            {
                records[index] = batch[index].WithPosition(checked(position.Sequence + index + 1));
                frames[index] = Encode(serializer, records[index]);
            }

            if (stream.Length != position.End) stream.SetLength(position.End);
            stream.Position = position.End;
            foreach (var frame in frames) stream.Write(frame, 0, frame.Length);
            stream.Flush(true);
            position = new Position { Sequence = records[records.Length - 1].Sequence, End = stream.Position };
            return Array.AsReadOnly(records);
        }

        static Position Scan(FileStream stream, List<AgentTurnEvent> records)
        {
            var position = new Position();
            var chunk = new byte[8192];
            long offset = 0;
            var serializer = Serializer();
            using (var line = new MemoryStream())
            {
                int count;
                while ((count = stream.Read(chunk, 0, chunk.Length)) != 0)
                {
                    int start = 0;
                    while (start < count)
                    {
                        int newline = Array.IndexOf(chunk, (byte)'\n', start, count - start);
                        int end = newline < 0 ? count : newline;
                        if (line.Length + end - start >= MaxFrameBytes) throw Unreadable();
                        line.Write(chunk, start, end - start);
                        if (newline < 0) break;

                        var record = Decode(serializer, line.GetBuffer(), (int)line.Length, position.Sequence + 1);
                        if (records != null) records.Add(record);
                        position = new Position { Sequence = record.Sequence, End = offset + end + 1 };
                        line.SetLength(0);
                        start = end + 1;
                    }
                    offset += count;
                }
            }
            // Only the final frame may be left incomplete by a crash before its newline.
            return position;
        }

        static DataContractJsonSerializer Serializer()
        {
            return new DataContractJsonSerializer(typeof(AgentTurnEvent));
        }

        static byte[] Encode(DataContractJsonSerializer serializer, AgentTurnEvent record)
        {
            using (var buffer = new MemoryStream())
            {
                byte[] plain = null;
                try
                {
                    serializer.WriteObject(buffer, record);
                    if (buffer.Length >= MaxFrameBytes) throw new IOException("agent_turn_event_too_large");
                    plain = buffer.ToArray();
                    byte[] encrypted = SecretProtection.Protect(plain);
                    string frame = Envelope + Convert.ToBase64String(encrypted) + "\n";
                    if (frame.Length > MaxFrameBytes) throw new IOException("agent_turn_event_too_large");
                    return Encoding.ASCII.GetBytes(frame);
                }
                finally
                {
                    Array.Clear(buffer.GetBuffer(), 0, (int)buffer.Length);
                    if (plain != null) Array.Clear(plain, 0, plain.Length);
                }
            }
        }

        static AgentTurnEvent Decode(DataContractJsonSerializer serializer, byte[] bytes, int size, long sequence)
        {
            byte[] plain = null;
            try
            {
                string frame = Encoding.ASCII.GetString(bytes, 0, size);
                if (!frame.StartsWith(Envelope, StringComparison.Ordinal)) throw Unreadable();
                string payload = frame.Substring(Envelope.Length);
                byte[] encrypted = Convert.FromBase64String(payload);
                if (Convert.ToBase64String(encrypted) != payload) throw Unreadable();
                plain = SecretProtection.Unprotect(encrypted);
                using (var body = new MemoryStream(plain, false))
                {
                    var record = serializer.ReadObject(body) as AgentTurnEvent;
                    if (record == null || record.Version != 1 || record.Sequence != sequence || !HasIdentity(record) ||
                        record.CreatedUtcTicks <= 0 || record.CreatedUtcTicks > DateTime.MaxValue.Ticks || record.Revision < 0)
                        throw Unreadable();
                    return record;
                }
            }
            catch (CryptographicException error) { throw Unreadable(error); }
            catch (SerializationException error) { throw Unreadable(error); }
            catch (XmlException error) { throw Unreadable(error); }
            catch (FormatException error) { throw Unreadable(error); }
            catch (ArgumentException error) { throw Unreadable(error); }
            catch (InvalidOperationException error) { throw Unreadable(error); }
            finally
            {
                if (plain != null) Array.Clear(plain, 0, plain.Length);
            }
        }

        static bool HasIdentity(AgentTurnEvent record)
        {
            return !String.IsNullOrWhiteSpace(record.Kind) && !String.IsNullOrWhiteSpace(record.TurnId);
        }

        CacheDirectoryPins PinDirectory(bool create)
        {
            string root = IoPath(directory);
            CachePaths.Check(root);
            if (create) Directory.CreateDirectory(root);
            else if (!Exists(root, true)) return null;
            return CacheDirectoryPins.OpenDirectory(root);
        }

        static string IoPath(string path)
        {
            return Windows && path.Length >= 248 ? CachePaths.NativePath(path) : path;
        }

        static bool Exists(string path, bool directoryExpected)
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(path); }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                ((attributes & FileAttributes.Directory) != 0) != directoryExpected)
                throw Unreadable();
            return true;
        }

        FileStream OpenRead()
        {
            return Exists(path, false) ? OpenFile(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite) : null;
        }

        FileStream TryLock(string name)
        {
            try
            {
                return OpenFile(IoPath(Path.Combine(directory, name)), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException error)
            {
                int code = error.HResult & 0xffff;
                if (code == 32 || code == 33 || (!Windows && (code == 11 || code == 35))) return null;
                throw;
            }
        }

        FileStream WaitWriter()
        {
            var timer = Stopwatch.StartNew();
            do
            {
                var writer = TryLock(WriterLockName);
                if (writer != null) return writer;
                Thread.Sleep(10);
            } while (timer.Elapsed < TimeSpan.FromSeconds(10));
            throw Busy();
        }

        static FileStream OpenFile(string path, FileMode mode, FileAccess access, FileShare share, bool delete = false)
        {
            if (!Windows)
            {
                Exists(path, false);
                var stream = new FileStream(path, mode, access, share);
                try { Exists(path, false); return stream; }
                catch { stream.Dispose(); throw; }
            }

            const uint openReparsePoint = 0x00200000;
            const uint genericRead = 0x80000000;
            const uint genericWrite = 0x40000000;
            const uint deleteAccess = 0x00010000;
            uint rights = genericRead;
            if (access == FileAccess.ReadWrite) rights |= genericWrite;
            if (delete) rights |= deleteAccess;
            var handle = CreateFile(path, rights, (uint)share, IntPtr.Zero, (uint)mode, openReparsePoint, IntPtr.Zero);
            try
            {
                if (handle.IsInvalid) throw WindowsIoError();
                FileInformation information;
                if (!GetFileInformationByHandle(handle, out information) ||
                    (information.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 ||
                    information.Links != 1)
                    throw Unreadable();
                return new FileStream(handle, access);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        static IOException WindowsIoError()
        {
            return new IOException("agent_turn_storage_unavailable", unchecked((int)0x80070000) | Marshal.GetLastWin32Error());
        }

        static IOException Busy()
        {
            return new IOException("agent_turn_journal_busy");
        }

        static IOException Unreadable(Exception error = null)
        {
            return new IOException("agent_turn_journal_unreadable", error);
        }

        void CloseLiveWriter()
        {
            var writer = liveWriter;
            liveWriter = null;
            livePosition = new Position();
            if (writer != null) writer.Dispose();
        }

        sealed class TurnLease : IDisposable
        {
            readonly AgentTurnJournal journal;
            readonly FileStream handle;
            readonly CacheDirectoryPins pins;

            internal TurnLease(AgentTurnJournal journal, FileStream handle, CacheDirectoryPins pins)
            {
                this.journal = journal;
                this.handle = handle;
                this.pins = pins;
            }

            public void Dispose()
            {
                lock (journal.gate)
                {
                    if (journal.liveTurn != this) return;
                    try { journal.CloseLiveWriter(); }
                    finally
                    {
                        journal.liveTurn = null;
                        try { handle.Dispose(); }
                        finally { pins.Dispose(); }
                    }
                }
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct FileInformation
        {
            internal FileAttributes Attributes;
            internal System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
            internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security,
            uint mode, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool SetFileInformationByHandle(SafeFileHandle file, int information, ref byte disposition, uint size);
    }
}
