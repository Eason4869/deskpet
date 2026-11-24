using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Web.Script.Serialization;
namespace Dafeiyu
{
    public sealed class AgentRunRecord
    {
        public int Version=1;public string ExecutionId,SessionId,ProjectId,TaskId,CallId,ToolName,PayloadHash,State,Result;
        public Dictionary<string,string> BeforeHashes=new Dictionary<string,string>(),AfterHashes=new Dictionary<string,string>();
    }
    public sealed class AgentRunJournal
    {
        const int MaxRecordBytes = 1024 * 1024;
        readonly string directory;
        readonly Func<IDisposable> lease;
        readonly Func<string,bool,IDisposable> directoryLease;
        internal Action<AgentRunRecord> BeforeSave { get; set; }
        public bool ReconciliationRequired { get; private set; }

        public AgentRunJournal(string directory, Func<IDisposable> lease = null)
        {
            this.directory = Path.GetFullPath(directory);
            this.lease = lease;
        }

        internal AgentRunJournal(string directory, Func<string,bool,IDisposable> directoryLease)
            : this(directory)
        {
            this.directoryLease = directoryLease;
        }

        static string Hash(string value)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "");
        }

        string TaskPath(string taskId)
        {
            return Path.Combine(directory, "tasks", Hash(taskId ?? ""));
        }

        static bool IsPending(AgentRunRecord record)
        {
            return record.State == "prepared" || record.State == "started" || record.State == "needs_reconciliation";
        }

        static bool IsTerminal(AgentRunRecord record)
        {
            return record.State == "completed" || record.State == "failed" ||
                record.State == "cancelled" || record.State == "reconciled";
        }

        static bool IsHash(string value)
        {
            return value != null && value.Length == 64 && value.All(Uri.IsHexDigit);
        }

        static IOException Unreadable(Exception inner = null)
        {
            return new IOException(TextResources.Get("agentRunJournal.readCore.text.alternativeA"), inner);
        }

        static string IoPath(string path)
        {
            return Environment.OSVersion.Platform == PlatformID.Win32NT && path.Length >= 248
                ? Maintenance.CachePaths.NativePath(path) : path;
        }

        IDisposable HoldDirectory(string path, bool create)
        {
            AgentInstructions.RejectLinks(IoPath(path));
            if (directoryLease != null) return directoryLease(path, create);
            if (create) Directory.CreateDirectory(IoPath(path));
            AgentInstructions.RejectLinks(IoPath(path));
            return null;
        }

        static bool Exists(string path, bool directoryExpected = false)
        {
            path = IoPath(path);
            AgentInstructions.RejectLinks(path);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(path); }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
            if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                ((attributes & FileAttributes.Directory) != 0) != directoryExpected)
                throw Unreadable();
            return true;
        }

        static FileStream TryLock(string directory, string name)
        {
            string path = Path.Combine(directory, name);
            Exists(path);
            try { return new FileStream(IoPath(path), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException error)
            {
                int code = error.HResult & 0xffff;
                if (code == 32 || code == 33 || code == 11 || code == 35) return null;
                throw;
            }
        }

        static AgentRunRecord Read(string path, string taskId = null)
        {
            path = IoPath(path);
            AgentInstructions.RejectLinks(path);
            try
            {
                byte[] bytes;
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (file.Length > MaxRecordBytes)
                        throw new IOException(TextResources.Get("agentRunJournal.readCore.text"));
                    using (var buffer = new MemoryStream((int)file.Length))
                    {
                        file.CopyTo(buffer);
                        bytes = buffer.ToArray();
                    }
                }
                var record = new JavaScriptSerializer { MaxJsonLength = MaxRecordBytes }.Deserialize<AgentRunRecord>(
                    Encoding.UTF8.GetString(SecretProtection.Unprotect(bytes)));
                if (record == null || record.Version != 1 || String.IsNullOrEmpty(record.ExecutionId) ||
                    record.TaskId == null || record.SessionId == null || record.ProjectId == null ||
                    String.IsNullOrWhiteSpace(record.CallId) || record.ToolName == null ||
                    !IsHash(record.PayloadHash) || (!IsPending(record) && !IsTerminal(record)) ||
                    (taskId != null && record.TaskId != taskId))
                    throw Unreadable();
                return record;
            }
            catch (CryptographicException error) { throw Unreadable(error); }
            catch (ArgumentException error) { throw Unreadable(error); }
            catch (InvalidOperationException error) { throw Unreadable(error); }
        }

        void Save(string path, AgentRunRecord record, string previousState = null)
        {
            if (BeforeSave != null) BeforeSave(record);
            bool exists = Exists(path);
            if (previousState == null)
            {
                if (exists) throw Unreadable();
            }
            else
            {
                var previous = Read(path, record.TaskId);
                if (previous.ExecutionId != record.ExecutionId || previous.PayloadHash != record.PayloadHash ||
                    previous.State != previousState)
                    throw Unreadable();
            }

            byte[] bytes = SecretProtection.Protect(Encoding.UTF8.GetBytes(
                new JavaScriptSerializer { MaxJsonLength = MaxRecordBytes }.Serialize(record)));
            if (bytes.Length > MaxRecordBytes)
                throw new IOException(TextResources.Get("agentRunJournal.readCore.text"));
            string temporary = IoPath(Path.Combine(Path.GetDirectoryName(path), Guid.NewGuid().ToString("N") + ".tmp"));
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (exists) AtomicFile.Commit(temporary, IoPath(path));
                else File.Move(temporary, IoPath(path));
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        void Migrate()
        {
            var timeout = Stopwatch.StartNew();
            FileStream migrationLock;
            while ((migrationLock = TryLock(directory, "migration.lock")) == null)
            {
                if (timeout.ElapsedMilliseconds >= 30000) throw Unreadable();
                Thread.Sleep(25);
            }

            using (migrationLock)
            {
                // Validate every remaining flat record before moving any of this batch.
                // Each rename leaves either the original record or its partitioned copy.
                var moves = new List<KeyValuePair<string,string>>();
                var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string source in Directory.EnumerateFileSystemEntries(IoPath(directory), "*.run"))
                {
                    string name = RecordName(source);
                    var record = Read(source);
                    string task = TaskPath(record.TaskId);
                    string pending = Path.Combine(task, "pending", name);
                    string archived = Path.Combine(task, "archive", name);
                    string destination = IsPending(record) ? pending : archived;
                    if (!destinations.Add(Path.Combine(task, name)) || Exists(pending) || Exists(archived))
                        throw Unreadable();
                    moves.Add(new KeyValuePair<string,string>(source, destination));
                }

                foreach (var group in moves.GroupBy(move => Path.GetDirectoryName(move.Value), StringComparer.Ordinal))
                {
                    using (HoldDirectory(group.Key, true))
                    {
                        foreach (var move in group)
                        {
                            AgentInstructions.RejectLinks(IoPath(move.Key));
                            File.Move(IoPath(move.Key), IoPath(move.Value));
                        }
                    }
                }
            }
        }

        static string RecordName(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (!IsHash(name) || !String.Equals(Path.GetExtension(path), ".run", StringComparison.OrdinalIgnoreCase))
                throw Unreadable();
            return name.ToUpperInvariant() + ".run";
        }

        static List<KeyValuePair<string,AgentRunRecord>> ReadPending(string pending, string archive, string taskId)
        {
            var records = new List<KeyValuePair<string,AgentRunRecord>>();
            if (!Exists(pending, true)) return records;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in Directory.EnumerateFileSystemEntries(IoPath(pending), "*.run"))
            {
                string name = RecordName(path);
                if (!names.Add(name) || Exists(Path.Combine(archive, name))) throw Unreadable();
                records.Add(new KeyValuePair<string,AgentRunRecord>(path, Read(path, taskId)));
            }
            return records;
        }

        public bool HasPending(string taskId)
        {
            try { return ReconciliationRequired = FindPending(taskId ?? ""); }
            catch (IOException) { ReconciliationRequired = true; throw; }
            catch (UnauthorizedAccessException) { ReconciliationRequired = true; throw; }
        }

        bool FindPending(string taskId)
        {
            if (!Exists(directory, true)) return false;
            using (lease == null ? null : lease())
            using (HoldDirectory(directory, false))
            {
                Migrate();
                string task = TaskPath(taskId);
                if (!Exists(task, true)) return false;
                using (HoldDirectory(task, false))
                using (var taskLock = TryLock(task, "task.lock"))
                {
                    if (taskLock == null) return true;
                    string pending = Path.Combine(task, "pending");
                    if (!Exists(pending, true)) return false;
                    using (HoldDirectory(pending, false))
                        return ReadPending(pending, Path.Combine(task, "archive"), taskId)
                            .Any(entry => IsPending(entry.Value));
                }
            }
        }

        public async Task<string> ExecuteAsync(AgentToolInvocation invocation, Func<Action,Task<string>> execute,
            Func<Dictionary<string,string>> hashes = null)
        {
            try { return await ExecuteCoreAsync(invocation, execute, hashes).ConfigureAwait(false); }
            catch (IOException) { ReconciliationRequired = true; throw; }
            catch (UnauthorizedAccessException) { ReconciliationRequired = true; throw; }
        }

        async Task<string> ExecuteCoreAsync(AgentToolInvocation invocation, Func<Action,Task<string>> execute,
            Func<Dictionary<string,string>> hashes)
        {
            using (lease == null ? null : lease())
            using (HoldDirectory(directory, true))
            {
                Migrate();
                string task = TaskPath(invocation.TaskId);
                using (HoldDirectory(task, true))
                using (var taskLock = TryLock(task, "task.lock"))
                {
                    if (taskLock == null) return RequireReconciliation(invocation);
                    string name = Hash(invocation.Key) + ".run";
                    string pending = Path.Combine(task, "pending");
                    string archive = Path.Combine(task, "archive");
                    using (HoldDirectory(pending, true))
                    using (HoldDirectory(archive, true))
                    {
                        var records = ReadPending(pending, archive, invocation.TaskId);
                        string archived = Path.Combine(archive, name);
                        if (Exists(archived))
                        {
                            var previous = Read(archived, invocation.TaskId);
                            if (!IsTerminal(previous)) throw Unreadable();
                            return Replay(invocation, previous);
                        }

                        string path = Path.Combine(pending, name);
                        var current = records.FirstOrDefault(entry => RecordName(entry.Key) == name);
                        if (current.Value != null)
                        {
                            string result = Replay(invocation, current.Value);
                            if (IsTerminal(current.Value)) File.Move(IoPath(current.Key), IoPath(archived));
                            return result;
                        }

                        if (records.Any(entry => IsPending(entry.Value))) return RequireReconciliation(invocation);
                        // Recover an interrupted move after a terminal state was durably recorded.
                        foreach (var entry in records)
                            File.Move(IoPath(entry.Key), IoPath(Path.Combine(archive, RecordName(entry.Key))));

                        return await ExecuteNewAsync(invocation, execute, hashes, path, archived).ConfigureAwait(false);
                    }
                }
            }
        }

        string Replay(AgentToolInvocation invocation, AgentRunRecord old)
        {
            if (old.SessionId != invocation.SessionId || old.ProjectId != invocation.ProjectId ||
                old.CallId != invocation.CallId)
                throw Unreadable();
            bool job = AgentJobTools.IsJobTool(invocation.ToolName);
            if (old.PayloadHash != invocation.PayloadHash)
                throw new InvalidOperationException(job ? "job_idempotency_conflict" :
                    TextResources.Get("agentRunJournal.executeAsync.text"));
            if (old.ToolName != invocation.ToolName) throw Unreadable();
            if (!IsTerminal(old)) return RequireReconciliation(invocation);
            if (old.State == "reconciled")
                return job ? AgentJobTools.Error("execution_reconciliation_required") :
                    TextResources.Get("agentRunJournal.executeAsync.text.alternativeB");
            if (job) return old.State == "cancelled" ? AgentJobTools.Error("start_failed") : AgentJobTools.Replay(old.Result);
            return old.Result ?? TextResources.Get("agentRunJournal.executeAsync.text.alternativeA");
        }

        string RequireReconciliation(AgentToolInvocation invocation)
        {
            ReconciliationRequired = true;
            if (AgentJobTools.IsJobTool(invocation.ToolName)) return AgentJobTools.Error("execution_reconciliation_required");
            throw new ApiException(TextResources.Get("agentRunJournal.executeAsync.text.alternativeC"));
        }

        async Task<string> ExecuteNewAsync(AgentToolInvocation invocation, Func<Action,Task<string>> execute,
            Func<Dictionary<string,string>> hashes, string path, string archived)
        {
            var record = new AgentRunRecord
            {
                ExecutionId = Guid.NewGuid().ToString("N"), SessionId = invocation.SessionId, ProjectId = invocation.ProjectId,
                TaskId = invocation.TaskId, CallId = invocation.CallId, ToolName = invocation.ToolName,
                PayloadHash = invocation.PayloadHash, State = "prepared"
            };
            if (hashes != null) record.BeforeHashes = hashes();
            Save(path, record);
            string previousState = "prepared";
            try
            {
                string result = await execute(() =>
                {
                    record.State = "started";
                    Save(path, record, previousState);
                    previousState = "started";
                }).ConfigureAwait(false);

                if (hashes != null) record.AfterHashes = hashes();
                bool job = AgentJobTools.IsJobTool(invocation.ToolName);
                record.Result = result ?? "";
                if (!job && record.Result.Length > 24000)
                    record.Result = TextResources.Format("agentRunJournal.executeAsync.recordResultSubstring", record.Result.Substring(0, 24000));
                record.State = (job ? AgentJobTools.IsFailure(record.Result) : TextResources.IsToolError(record.Result))
                    ? "failed" : "completed";
                Archive(path, archived, record, previousState);
                return result;
            }
            catch (OperationCanceledException)
            {
                if (previousState == "prepared")
                {
                    record.State = "cancelled";
                    record.Result = TextResources.Get("agentRunJournal.executeAsync.recordResult");
                    try { Archive(path, archived, record, previousState); }
                    catch { ReconciliationRequired = true; throw; }
                }
                else ReconciliationRequired = true;
                throw;
            }
            catch
            {
                ReconciliationRequired = true;
                throw;
            }
        }

        void Archive(string path, string archived, AgentRunRecord record, string previousState)
        {
            if (Exists(archived)) throw Unreadable();
            Save(path, record, previousState);
            File.Move(IoPath(path), IoPath(archived));
        }

        public void Acknowledge(string taskId)
        {
            taskId = taskId ?? "";
            if (!Exists(directory, true)) { ReconciliationRequired = false; return; }
            using (lease == null ? null : lease())
            using (HoldDirectory(directory, false))
            {
                Migrate();
                string task = TaskPath(taskId);
                if (!Exists(task, true)) { ReconciliationRequired = false; return; }
                using (HoldDirectory(task, false))
                using (var taskLock = TryLock(task, "task.lock"))
                {
                    if (taskLock == null) throw Unreadable();
                    string pending = Path.Combine(task, "pending");
                    if (!Exists(pending, true)) { ReconciliationRequired = false; return; }
                    string archive = Path.Combine(task, "archive");
                    using (HoldDirectory(pending, false))
                    using (HoldDirectory(archive, true))
                    {
                        foreach (var entry in ReadPending(pending, archive, taskId))
                        {
                            var record = entry.Value;
                            string destination = Path.Combine(archive, RecordName(entry.Key));
                            if (IsPending(record))
                            {
                                string previousState = record.State;
                                record.State = "reconciled";
                                record.Result = TextResources.Get("agentRunJournal.acknowledge.recordResult");
                                Archive(entry.Key, destination, record, previousState);
                            }
                            else File.Move(IoPath(entry.Key), IoPath(destination));
                        }
                    }
                }
            }
            ReconciliationRequired = false;
        }
    }
    public sealed partial class AgentWorkspace
    {
        AgentRunJournal runJournal;
        public bool NeedsReconciliation {get{return runJournal!=null&&runJournal.ReconciliationRequired||jobsJournal!=null&&jobsJournal.ReconciliationRequired;}}
        Task<string> ExecuteRecordedAsync(AgentToolInvocation invocation,CancellationToken token)
        {
            bool effect=AgentToolCatalog.Definitions.Concat(dynamicTools).Any(t=>t.Name==invocation.ToolName&&!t.ReadOnly);
            if(runJournal==null||!effect)return ExecuteAuthorizedAsync(invocation,token,null);
            return Task.Run(()=>runJournal.ExecuteAsync(invocation,before=>ExecuteAuthorizedAsync(invocation,token,before),()=>InvocationHashes(invocation)),token);
        }
        Dictionary<string,string> InvocationHashes(AgentToolInvocation invocation)
        {
            var values=new Dictionary<string,string>();if(invocation.ToolName!="write_file"&&invocation.ToolName!="edit_file"&&invocation.ToolName!="restore_file")return values;object path;var args=invocation.ReadArguments();if(args.TryGetValue("path",out path)&&path is string){string full=Resolve((string)path);if(File.Exists(full))using(LockDirectories(Path.GetDirectoryName(full),false))using(var hash=SHA256.Create())values[full]=BitConverter.ToString(hash.ComputeHash(ReadBytes(full,CancellationToken.None,DocumentFormats.MaxDocumentBytes))).Replace("-","");else values[full]="missing";}return values;
        }
        internal static string RunJournalPath(string root){return Path.Combine(root,".deskpet-agent-kernel","runs");}
        internal static string JobRunJournalPath(string root){return Path.Combine(RunJournalPath(root),"background-jobs");}
        AgentRunJournal OpenRunJournal(string path)
        {
            return new AgentRunJournal(path,(directory,create)=>LockDirectories(directory,create));
        }
    }
}
