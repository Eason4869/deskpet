using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace Dafeiyu
{
    internal enum GitWorkspaceChangeStatus { Added, Modified, Deleted, Renamed, Copied, TypeChanged, Unmerged, Unknown }

    internal sealed class GitWorkspaceChangeEntry
    {
        internal GitWorkspaceChangeEntry(string path, string previousPath, GitWorkspaceChangeStatus status)
        {
            Path = path;
            PreviousPath = previousPath;
            Status = status;
        }

        internal string Path { get; private set; }
        internal string PreviousPath { get; private set; }
        internal GitWorkspaceChangeStatus Status { get; private set; }
        internal long AddedLines { get; set; }
        internal long RemovedLines { get; set; }
        internal bool IsBinary { get; set; }
    }

    internal sealed class GitWorkspaceChangeSnapshot
    {
        internal GitWorkspaceChangeSnapshot(string root, string branch, string upstream, string revision, string targetRevision,
            List<GitWorkspaceChangeEntry> files, int untracked, bool truncated)
        {
            Root = root;
            RepositoryName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            Branch = branch;
            Upstream = upstream;
            Revision = revision;
            TargetRevision = targetRevision;
            Files = files.AsReadOnly();
            AddedLines = files.Sum(file => file.AddedLines);
            RemovedLines = files.Sum(file => file.RemovedLines);
            SkippedUntrackedCount = untracked;
            IsTruncated = truncated;
        }

        internal string Root { get; private set; }
        internal string Revision { get; private set; }
        internal string TargetRevision { get; private set; }
        internal string RepositoryName { get; private set; }
        internal string Branch { get; private set; }
        internal string Upstream { get; private set; }
        internal bool UsesUpstream { get { return TargetRevision.Length > 0; } }
        internal IReadOnlyList<GitWorkspaceChangeEntry> Files { get; private set; }
        internal long AddedLines { get; private set; }
        internal long RemovedLines { get; private set; }
        internal int SkippedUntrackedCount { get; private set; }
        internal bool IsTruncated { get; private set; }
    }

    internal static class GitWorkspaceChanges
    {
        const int CommandTimeoutSeconds = 8;
        const int MaximumPatchCharacters = 80000;
        const string HeadPrefix = "refs/heads/", RemotePrefix = "refs/remotes/";
        const string EmptyTreeSha1 = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";
        const string EmptyTreeSha256 = "6ef19b41225c5369f1c104d45d8d85efa9b057b53b14b4b9b939dd74decc5321";
        static readonly string[] DiffOptions = { "diff", "--no-color", "--no-ext-diff", "--no-textconv", "--find-renames", "-l200",
            "--ignore-submodules=dirty", "--submodule=short", "--no-indent-heuristic", "--ita-visible-in-index" };
        static readonly SemaphoreSlim Processes = new SemaphoreSlim(3);

        internal static GitWorkspaceChangeSnapshot Read(string root, bool compareUpstream, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            if (String.IsNullOrWhiteSpace(root)) return null;
            root = Path.GetFullPath(root);
            if (!File.Exists(Path.Combine(root, ".git")) && !Directory.Exists(Path.Combine(root, ".git"))) return null;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                var token = timeout.Token;
                Processes.Wait(token);
                try { return ReadCore(root, compareUpstream, token); }
                finally { Processes.Release(); }
            }
        }

        static GitWorkspaceChangeSnapshot ReadCore(string root, bool compareUpstream, CancellationToken token)
        {
            root = AgentGitTools.FindRoot(root, root, true, token);
            var common = ReadOnlyArguments(root);
            AgentGitTools.ValidateWorktreeFilters(common, root, CommandTimeoutSeconds, token);

            string branchRef = Run(root, common, token, false, "symbolic-ref", "--quiet", "HEAD").Output.Trim();
            string branch = branchRef.StartsWith(HeadPrefix, StringComparison.Ordinal) ? branchRef.Substring(HeadPrefix.Length) : "HEAD";
            string head = ResolveRevision(root, common, "HEAD", token);
            string upstream = "", upstreamRevision = "";
            if (head.Length > 0 && branchRef.StartsWith(HeadPrefix, StringComparison.Ordinal))
            {
                string upstreamRef = Run(root, common, token, true, "for-each-ref", "--count=1", "--format=%(upstream)", "--", branchRef).Output.Trim();
                if (upstreamRef.StartsWith(RemotePrefix, StringComparison.Ordinal) || upstreamRef.StartsWith(HeadPrefix, StringComparison.Ordinal))
                {
                    upstreamRevision = ResolveRevision(root, common, upstreamRef, token);
                    if (upstreamRevision.Length > 0)
                        upstream = upstreamRef.Substring(upstreamRef.StartsWith(RemotePrefix, StringComparison.Ordinal) ? RemotePrefix.Length : HeadPrefix.Length);
                }
            }
            bool useUpstream = compareUpstream && upstreamRevision.Length > 0;
            string revision = useUpstream ? upstreamRevision : head;
            string targetRevision = useUpstream ? head : "";
            if (revision.Length == 0)
            {
                string format = Run(root, common, token, true, "rev-parse", "--show-object-format").Output.Trim();
                if (format != "sha1" && format != "sha256") throw ReadFailed();
                revision = format == "sha256" ? EmptyTreeSha256 : EmptyTreeSha1;
            }

            var tracked = Run(root, common, token, true, DiffOptions.Concat(new[] { "--raw", "--numstat", "-z" })
                .Concat(ComparisonArguments(revision, targetRevision)).ToArray());
            var untracked = useUpstream ? null : Run(root, common, token, true, "ls-files", "--others", "--exclude-standard", "-z", "--");
            var files = tracked.Truncated ? new List<GitWorkspaceChangeEntry>() : ParseChanges(tracked.Output);
            return new GitWorkspaceChangeSnapshot(root, branch, upstream, revision, targetRevision, files,
                untracked == null ? 0 : untracked.Output.Count(c => c == '\0'), tracked.Truncated || (untracked != null && untracked.Truncated));
        }

        internal static string GetDiff(GitWorkspaceChangeSnapshot snapshot, GitWorkspaceChangeEntry entry, CancellationToken cancel)
        {
            if (snapshot == null || entry == null || !snapshot.Files.Contains(entry)) throw new ArgumentException();
            cancel.ThrowIfCancellationRequested();
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                var token = timeout.Token;
                Processes.Wait(token);
                try
                {
                    string root = AgentGitTools.FindRoot(snapshot.Root, snapshot.Root, true, token);
                    var common = ReadOnlyArguments(root);
                    AgentGitTools.ValidateWorktreeFilters(common, root, CommandTimeoutSeconds, token);
                    var paths = entry.PreviousPath.Length == 0 ? new[] { entry.Path } : new[] { entry.PreviousPath, entry.Path };
                    var result = Run(root, common, token, true,
                        DiffOptions.Concat(new[] { "--patch", "--unified=3", "--src-prefix=a/", "--dst-prefix=b/" })
                        .Concat(ComparisonArguments(snapshot.Revision, snapshot.TargetRevision)).Concat(paths).ToArray());
                    if (!result.Truncated && result.Output.Length <= MaximumPatchCharacters) return result.Output;
                    string patch = result.Output.Substring(0, Math.Min(result.Output.Length, MaximumPatchCharacters));
                    return patch + "\n" + TextResources.Get("agentGitTools.run.text");
                }
                finally { Processes.Release(); }
            }
        }

        static string[] ComparisonArguments(string revision, string target)
        {
            return target.Length == 0 ? new[] { revision, "--" } : new[] { revision, target, "--" };
        }

        static string[] ReadOnlyArguments(string root)
        {
            return AgentGitTools.ReadOnlyArguments(root).Concat(new[] { "-c", "core.bare=false", "-c", "core.longpaths=true", "-c", "core.safecrlf=false" }).ToArray();
        }

        static string ResolveRevision(string root, string[] common, string reference, CancellationToken token)
        {
            var result = Run(root, common, token, false, "rev-parse", "--verify", "--quiet", reference + "^{commit}");
            if (result.ExitCode != 0) return "";
            string revision = result.Output.Trim();
            if (result.Truncated || !Regex.IsMatch(revision, @"\A(?:[0-9a-f]{40}|[0-9a-f]{64})\z")) throw ReadFailed();
            return revision;
        }

        static CommandResult Run(string root, string[] common, CancellationToken token, bool requireSuccess, params string[] command)
        {
            var result = AgentGitTools.RunCommand(common.Concat(command).ToArray(), root, CommandTimeoutSeconds, token, stdoutOnly: true);
            if (requireSuccess && result.ExitCode != 0) throw ReadFailed();
            return result;
        }

        static List<GitWorkspaceChangeEntry> ParseChanges(string output)
        {
            var files = new List<GitWorkspaceChangeEntry>();
            var byPath = new Dictionary<string, GitWorkspaceChangeEntry>(StringComparer.Ordinal);
            string[] parts = output.Split('\0');
            int position = 0;
            int end = parts.Length - 1;
            if (parts[end].Length != 0) throw ReadFailed();
            while (position < end && parts[position].StartsWith(":", StringComparison.Ordinal))
            {
                string raw = parts[position++];
                string status = raw.Substring(raw.LastIndexOf(' ') + 1);
                if (status.Length == 0) throw ReadFailed();
                string path = ReadPath(parts, end, ref position);
                string previous = "";
                if (status[0] == 'R' || status[0] == 'C')
                {
                    previous = path;
                    path = ReadPath(parts, end, ref position);
                }
                if (byPath.ContainsKey(path)) continue;
                var entry = new GitWorkspaceChangeEntry(path, previous, ParseStatus(status[0]));
                byPath.Add(path, entry);
                files.Add(entry);
            }
            while (position < end)
            {
                string[] stats = parts[position++].Split(new[] { '\t' }, 3);
                if (stats.Length != 3) throw ReadFailed();
                string path = stats[2];
                if (path.Length == 0)
                {
                    ReadPath(parts, end, ref position);
                    path = ReadPath(parts, end, ref position);
                }
                GitWorkspaceChangeEntry entry;
                if (!byPath.TryGetValue(path, out entry)) throw ReadFailed();
                if (stats[0] == "-" && stats[1] == "-") entry.IsBinary = true;
                else
                {
                    entry.AddedLines = ParseLineCount(stats[0]);
                    entry.RemovedLines = ParseLineCount(stats[1]);
                }
            }
            return files;
        }

        static string ReadPath(string[] parts, int end, ref int position)
        {
            if (position >= end || parts[position].Length == 0) throw ReadFailed();
            return parts[position++];
        }

        static long ParseLineCount(string value)
        {
            long count;
            if (!Int64.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out count)) throw ReadFailed();
            return count;
        }

        static GitWorkspaceChangeStatus ParseStatus(char status)
        {
            switch (status)
            {
                case 'A': return GitWorkspaceChangeStatus.Added;
                case 'M': return GitWorkspaceChangeStatus.Modified;
                case 'D': return GitWorkspaceChangeStatus.Deleted;
                case 'R': return GitWorkspaceChangeStatus.Renamed;
                case 'C': return GitWorkspaceChangeStatus.Copied;
                case 'T': return GitWorkspaceChangeStatus.TypeChanged;
                case 'U': return GitWorkspaceChangeStatus.Unmerged;
                default: return GitWorkspaceChangeStatus.Unknown;
            }
        }

        static IOException ReadFailed() { return new IOException(); }
    }
}
