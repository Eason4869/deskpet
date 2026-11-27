using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace Dafeiyu
{
    internal sealed class GitHubAgentLocal
    {
        const int MaximumPaths = 32;
        const string FilterPattern = @"^filter\..*\.(clean|process|smudge)$";
        static readonly string[] GitOptions = {
            "--literal-pathspecs", "--no-replace-objects", "--no-optional-locks",
            "-c", "core.splitIndex=false", "-c", "status.submoduleSummary=false", "-c", "diff.ignoreSubmodules=all"
        };
        static readonly Regex ObjectId = new Regex(@"\A(?:[0-9a-f]{40}|[0-9a-f]{64})\z", RegexOptions.CultureInvariant);
        static readonly Regex WindowsDevice = new Regex(@"\A(?:CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|(?:COM|LPT)[1-9¹²³])(?:\.|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly HashSet<string> ProtectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".git", ".deskpet-agent-kernel", ".deskpet-agent-backups", ".ssh", ".gnupg", ".aws", ".azure", ".kube", ".docker",
            ".netrc", "_netrc", ".npmrc", ".pypirc", ".git-credentials", ".gitconfig", ".htpasswd",
            "credentials", "secrets", "id_rsa", "id_dsa", "id_ecdsa", "id_ed25519", "settings.json"
        };
        static readonly HashSet<string> SecretExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".key", ".pem", ".pfx", ".p12", ".jks", ".keystore", ".ppk"
        };

        readonly string workspace;
        readonly Action beforeEffect;

        internal GitHubAgentLocal(string workspace, Action beforeEffect = null)
        {
            this.workspace = AgentSandboxDirectories.Validate(workspace).TrimEnd('\\', '/');
            this.beforeEffect = beforeEffect ?? delegate { };
        }

        internal string Initialize(string branch, CancellationToken token)
        {
            ValidateBranch(branch, token);
            if (HasGitMarker(workspace))
            {
                RequireRepository(token);
                string current = ReadBranch(token);
                if (current != branch) throw Problem("changed");
                return current;
            }

            for (string ancestor = Path.GetDirectoryName(workspace); ancestor != null; ancestor = Path.GetDirectoryName(ancestor))
            {
                token.ThrowIfCancellationRequested();
                if (HasGitMarker(ancestor)) throw Problem("invalidWorktree");
            }
            if (Run(token, "rev-parse", "--git-dir").ExitCode == 0) throw Problem("invalidWorktree");
            beforeEffect();
            Read(token, "init", "--quiet", "--template=", "--initial-branch=" + branch);
            RequireRepository(token);
            return ReadBranch(token);
        }

        internal string BindOrigin(string repository, CancellationToken token)
        {
            string normalized = WorkSummaryRepository.Normalize(repository);
            if (normalized.Length == 0 || !String.Equals(repository, normalized, StringComparison.OrdinalIgnoreCase))
                throw Problem("invalidInput");
            RequireRepository(token);
            string current = ReadOrigin(token);
            if (current.Length != 0)
            {
                if (!Same(current, normalized)) throw Problem("incompatiblePush");
                return current;
            }

            beforeEffect();
            Read(token, "remote", "add", "origin", "https://github.com/" + normalized + ".git");
            current = ReadOrigin(token);
            if (!Same(current, normalized)) throw Problem("incompatiblePush");
            return current;
        }

        internal string CreateBranch(string branch, CancellationToken token)
        {
            RequireRepository(token);
            ValidateBranch(branch, token);
            string current = ReadBranch(token);
            if (current == branch) return current;
            RequireNoInProgressOperation(token);
            RequireSafeFilters(token);
            beforeEffect();
            Read(token, "switch", "--quiet", "--no-recurse-submodules", "--create", branch);
            return ReadBranch(token);
        }

        internal string CommitSelected(string[] paths, string message, CancellationToken token)
        {
            if (paths == null || paths.Length == 0 || paths.Length > MaximumPaths ||
                String.IsNullOrWhiteSpace(message) || message.Length > 200 || message.Any(Char.IsControl))
                throw Problem("invalidInput");

            RequireRepository(token);
            string branch = ReadBranch(token);
            RequireNoInProgressOperation(token);
            RequireSafeFilters(token);

            var selected = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in paths)
            {
                token.ThrowIfCancellationRequested();
                bool exists;
                string file = ValidateFilePath(path, out exists);
                if (selected.ContainsKey(file)) throw Problem("invalidInput");
                selected.Add(file, exists);
            }
            string[] files = selected.Keys.ToArray();
            RejectFileFilters(files, token);
            var identity = CommitIdentity(token);
            string revision = ReadRevision(token);
            var index = ReadTrackedPaths(selected, token, new[] { "ls-files", "--stage", "-z", "--" }.Concat(files).ToArray());
            var previous = revision.Length == 0 ? new HashSet<string>(StringComparer.Ordinal) :
                ReadTrackedPaths(selected, token, new[] { "ls-tree", "--full-tree", "-r", "-z", revision, "--" }.Concat(files).ToArray());
            if (selected.Any(file => !file.Value && !index.Contains(file.Key) && !previous.Contains(file.Key)))
                throw Problem("invalidInput");

            string[] untracked = files.Where(file => selected[file] && !index.Contains(file)).ToArray();
            if (ReadBranch(token) != branch || ReadRevision(token) != revision) throw Problem("changed");
            foreach (string file in files)
            {
                bool exists;
                ValidateFilePath(file, out exists);
                if (exists != selected[file]) throw Problem("changed");
            }
            beforeEffect();
            if (untracked.Length != 0)
            {
                var add = new[] { "add", "--intent-to-add", "--" }.Concat(untracked).ToArray();
                if (Run(token, add).ExitCode != 0) throw Problem("selectedCommitFailedAfterPreparation");
            }

            // Git's partial commit locks the real index and preserves staged entries for other paths.
            var commit = new[] { "commit", "--quiet", "--only", "--no-verify", "--no-gpg-sign", "-m", message, "--" };
            if (Run(token, identity.Concat(commit).Concat(files).ToArray()).ExitCode != 0)
                throw Problem(untracked.Length == 0 ? "operationFailed" : "selectedCommitFailedAfterPreparation");
            string result = ReadRevision(token);
            if (result.Length == 0 || result == revision) throw Problem("operationFailed");
            return result;
        }

        internal string CurrentRepository(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!HasGitMarker(workspace)) return "";
            RequireRepository(token);
            return ReadOrigin(token);
        }

        string ValidateFilePath(string path, out bool exists)
        {
            exists = false;
            if (String.IsNullOrWhiteSpace(path) || path.Length > 240 || Path.IsPathRooted(path) ||
                path.Any(character => Char.IsControl(character) || ":*?\"<>|".IndexOf(character) >= 0))
                throw Problem("invalidInput");

            string[] parts = path.Split('\\', '/');
            string current = workspace;
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0 || part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal) ||
                    part.IndexOf('~') >= 0 || WindowsDevice.IsMatch(part) || IsProtectedName(part))
                    throw Problem("invalidInput");

                current = Path.Combine(current, part);
                var attributes = Attributes(current);
                if (!attributes.HasValue) continue;
                bool directory = (attributes.Value & FileAttributes.Directory) != 0;
                if ((attributes.Value & FileAttributes.ReparsePoint) != 0 || directory == (i == parts.Length - 1))
                    throw Problem("invalidInput");
                if (!directory)
                {
                    AgentSandboxDirectories.ValidateFile(current);
                    exists = true;
                }
            }
            return String.Join("/", parts);
        }

        static bool IsProtectedName(string name)
        {
            if (ProtectedNames.Contains(name) || SecretExtensions.Contains(Path.GetExtension(name))) return true;
            return new[] { ".env", "credentials", "secret", "secrets", "password", "passwords", "token", "tokens", "auth" }
                .Any(prefix => Same(name, prefix) || name.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase));
        }

        HashSet<string> ReadTrackedPaths(Dictionary<string, bool> selected, CancellationToken token, string[] arguments)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (string entry in Read(token, arguments).Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int tab = entry.IndexOf('\t');
                if (tab < 7 || entry.StartsWith("160000 ", StringComparison.Ordinal) || entry.StartsWith("120000 ", StringComparison.Ordinal))
                    throw Problem("invalidInput");
                string path = entry.Substring(tab + 1);
                if (!selected.Keys.Contains(path, StringComparer.Ordinal)) throw Problem("invalidInput");
                result.Add(path);
            }
            return result;
        }

        void RequireRepository(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!HasGitMarker(workspace) || !Same(AgentGitTools.FindRoot(workspace, workspace, true, token), workspace))
                throw Problem("invalidWorktree");
            if (!Same(Read(token, "rev-parse", "--show-toplevel").TrimEnd('\r', '\n').Replace('/', '\\'), workspace))
                throw Problem("invalidWorktree");
        }

        void RequireSafeFilters(CancellationToken token)
        {
            var filters = Run(token, "config", "--includes", "--name-only", "--get-regexp", FilterPattern);
            if (filters.ExitCode != 1 || !String.IsNullOrEmpty(filters.Output)) throw Problem("operationFailed");
        }

        void RejectFileFilters(string[] files, CancellationToken token)
        {
            var attributes = Read(token, new[] { "check-attr", "-z", "filter", "--" }.Concat(files).ToArray()).Split('\0');
            if (attributes.Length != files.Length * 3 + 1) throw Problem("operationFailed");
            for (int i = 2; i < attributes.Length - 1; i += 3)
                if (attributes[i] != "unspecified" && attributes[i] != "unset") throw Problem("operationFailed");
        }

        string[] CommitIdentity(CancellationToken token)
        {
            var options = new List<string>();
            foreach (string key in new[] { "user.name", "user.email" })
            {
                var value = GitHubAppGit.RunWindows(workspace, new[] { "config", "--get", key }, false, token);
                if (value.TimedOut || value.Cancelled) throw new OperationCanceledException();
                if (value.ExitCode == 1 && String.IsNullOrEmpty(value.Output)) continue;
                string setting = value.Output.TrimEnd('\r', '\n');
                if (value.ExitCode != 0 || value.Truncated || setting.Length == 0 || setting.Length > 320 || setting.Any(Char.IsControl))
                    throw Problem("operationFailed");
                options.Add("-c");
                options.Add(key + "=" + setting);
            }
            return options.ToArray();
        }

        void RequireNoInProgressOperation(CancellationToken token)
        {
            string[] markers = { "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "rebase-merge", "rebase-apply", "sequencer" };
            var command = new[] { "rev-parse", "--path-format=absolute" }.Concat(markers.SelectMany(marker => new[] { "--git-path", marker }));
            string[] paths = Read(token, command.ToArray()).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (paths.Length != markers.Length || paths.Any(path => !Path.IsPathRooted(path) || path.Any(Char.IsControl) || Attributes(path).HasValue))
                throw Problem("operationFailed");
        }

        string ReadBranch(CancellationToken token)
        {
            const string prefix = "refs/heads/";
            string reference = Read(token, "symbolic-ref", "--quiet", "HEAD").TrimEnd('\r', '\n');
            if (!reference.StartsWith(prefix, StringComparison.Ordinal)) throw Problem("invalidWorktree");
            string branch = reference.Substring(prefix.Length);
            ValidateBranch(branch, token);
            return branch;
        }

        void ValidateBranch(string branch, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(branch) || branch.Length > 240 || branch.StartsWith("-", StringComparison.Ordinal) ||
                branch.Any(Char.IsControl) || Run(token, "check-ref-format", "refs/heads/" + branch).ExitCode != 0)
                throw Problem("invalidInput");
        }

        string ReadRevision(CancellationToken token)
        {
            var result = Run(token, "rev-parse", "--verify", "--quiet", "HEAD");
            string revision = result.Output.TrimEnd('\r', '\n');
            if (result.ExitCode == 1 && revision.Length == 0) return "";
            if (result.ExitCode != 0 || !ObjectId.IsMatch(revision)) throw Problem("invalidWorktree");
            return revision;
        }

        string ReadOrigin(CancellationToken token)
        {
            string[] remotes = Read(token, "remote").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (!remotes.Contains("origin", StringComparer.Ordinal)) return "";

            string raw = Read(token, "config", "--includes", "--null", "--get-all", "remote.origin.url");
            if (!raw.EndsWith("\0", StringComparison.Ordinal) || raw.Count(character => character == '\0') != 1)
                throw Problem("incompatiblePush");
            string address = raw.Substring(0, raw.Length - 1);
            string repository = new WorkSummaryRepository(address, "").Repository;
            if (repository.Length == 0 || GitHubAppGit.ResolvePushTarget(address, repository) == null)
                throw Problem("incompatiblePush");

            string fetch = Read(token, "remote", "get-url", "--all", "origin").TrimEnd('\r', '\n');
            string push = Read(token, "remote", "get-url", "--push", "--all", "origin").TrimEnd('\r', '\n');
            if (GitHubAppGit.ResolvePushTarget(fetch, repository) == null || GitHubAppGit.ResolvePushTarget(push, repository) == null)
                throw Problem("incompatiblePush");
            return repository;
        }

        static bool HasGitMarker(string root) { return Attributes(Path.Combine(root, ".git")).HasValue; }

        static FileAttributes? Attributes(string path)
        {
            try { return File.GetAttributes(path); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }

        string Read(CancellationToken token, params string[] arguments)
        {
            var result = Run(token, arguments);
            if (result.ExitCode != 0) throw Problem("operationFailed");
            return result.Output;
        }

        CommandResult Run(CancellationToken token, params string[] arguments)
        {
            token.ThrowIfCancellationRequested();
            var result = GitHubAppGit.RunProjectGit(workspace, GitOptions.Concat(arguments).ToArray(), token);
            token.ThrowIfCancellationRequested();
            if (result.TimedOut || result.Cancelled) throw new OperationCanceledException();
            if (result.Truncated) throw Problem("operationFailed");
            return result;
        }

        static bool Same(string first, string second) { return String.Equals(first, second, StringComparison.OrdinalIgnoreCase); }
        static GitHubAppGitException Problem(string key) { return new GitHubAppGitException("workSummary.gitHub." + key); }
    }
}
