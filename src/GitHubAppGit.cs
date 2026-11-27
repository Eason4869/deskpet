using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Dafeiyu
{
    internal enum GitHubWorkspaceAction { Commit, Push, PullRequest }

    internal sealed class GitHubPushAuthentication
    {
        internal string Login, CliExecutable, Token;

        internal void Configure(List<string> options, Dictionary<string, string> environment)
        {
            if (!String.IsNullOrEmpty(CliExecutable))
            {
                string command = "'" + CliExecutable.Replace('\\', '/').Replace("'", "'\\''") + "' auth git-credential";
                options.AddRange(new[] { "-c", "credential.https://github.com.username=" + Login,
                    "-c", "credential.https://github.com.helper=!" + command });
            }
            else if (!String.IsNullOrEmpty(Token))
            {
                const string key = "DESKPET_GITHUB_GIT_AUTHORIZATION";
                options.Add("--config-env=http.https://github.com/.extraHeader=" + key);
                environment[key] = "Authorization: Basic " + Convert.ToBase64String(new UTF8Encoding(false).GetBytes(Login + ":" + Token));
            }
        }
    }

    internal sealed class GitHubAppGit
    {
        internal sealed class Snapshot
        {
            internal string Directory, Repository, Branch, Revision, Staged, Remote;
            internal int StagedCount { get { return Staged == null ? 0 : Staged.Count(c => c == '\0') / 2; } }
        }

        internal delegate CommandResult RunCommand(string directory, string[] arguments, bool isolated, CancellationToken token);
        readonly RunCommand run;
        readonly bool useConnectedAccount;

        internal GitHubAppGit(RunCommand run = null, GitHubPushAuthentication authentication = null, string proxyAddress = null)
        {
            useConnectedAccount = authentication != null &&
                (!String.IsNullOrEmpty(authentication.CliExecutable) || !String.IsNullOrEmpty(authentication.Token));
            Uri proxy = String.IsNullOrEmpty(proxyAddress) ? null : ProxyConfiguration.Endpoint(proxyAddress);
            this.run = run ?? ((directory, arguments, isolated, token) => RunWindows(directory, arguments, isolated, token, authentication, proxy));
        }

        internal Snapshot Inspect(string directory, string repository, GitHubWorkspaceAction action, CancellationToken token)
        {
            string root = Path.GetFullPath(directory);
            var info = WorkSummaryRepository.Read(root, token);
            if (info == null || info.Branch.Length == 0 || !Same(info.Repository, repository) ||
                !Same(AgentGitTools.FindRoot(root, root, true, token), root))
                throw Problem("invalidWorktree");

            string branchRef = "refs/heads/" + info.Branch;
            if (Read(root, token, "symbolic-ref", "--quiet", "HEAD").Trim() != branchRef ||
                Execute(root, false, token, "check-ref-format", branchRef).ExitCode != 0)
                throw Problem("invalidWorktree");

            var revision = Execute(root, false, token, "rev-parse", "--verify", "--quiet", "HEAD");
            string hash = revision.Output.Trim();
            if (revision.ExitCode != 0 && (action != GitHubWorkspaceAction.Commit || revision.ExitCode != 1 || hash.Length != 0))
                throw Problem("invalidWorktree");
            if (revision.ExitCode == 0 && !Regex.IsMatch(hash, @"\A(?:[0-9a-f]{40}|[0-9a-f]{64})\z"))
                throw Problem("invalidWorktree");

            var result = new Snapshot { Directory = root, Repository = info.Repository, Branch = info.Branch, Revision = hash };
            if (action == GitHubWorkspaceAction.Commit)
            {
                RejectCommitOperations(root, token);
                result.Staged = Read(root, token, "diff", "--cached", "--raw", "--abbrev=64", "--no-renames", "--no-ext-diff", "--no-textconv",
                    "--ignore-submodules=none", "-z", "--");
                if (result.Staged.Length == 0) throw Problem("noStaged");
            }
            if (action == GitHubWorkspaceAction.Push)
            {
                var origin = Execute(root, false, token, "remote", "get-url", "--push", "--all", "origin");
                result.Remote = origin.ExitCode == 0 ? ResolvePushTarget(origin.Output.Trim(), result.Repository) : null;
                if (result.Remote == null) throw Problem("incompatiblePush");
            }
            return result;
        }

        internal void Verify(Snapshot confirmed, GitHubWorkspaceAction action, CancellationToken token)
        {
            var current = Inspect(confirmed.Directory, confirmed.Repository, action, token);
            if (confirmed.Branch != current.Branch || confirmed.Revision != current.Revision ||
                confirmed.Staged != current.Staged || !Same(confirmed.Remote, current.Remote))
                throw Problem("changed");
        }

        internal void Commit(Snapshot confirmed, string message, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(message) || message.Length > 200 || message.Any(Char.IsControl)) throw Problem("invalidInput");
            Verify(confirmed, GitHubWorkspaceAction.Commit, token);
            string directory = confirmed.Directory;
            string tree = ReadHash(directory, token, "write-tree");
            var arguments = new List<string> { "commit-tree", tree, "--no-gpg-sign", "-m", message };
            if (confirmed.Revision.Length > 0) arguments.AddRange(new[] { "-p", confirmed.Revision });
            string revision = ReadHash(directory, token, arguments.ToArray());
            string changes = Read(directory, token, "diff-tree", "--root", "--no-commit-id", "-r", "--raw", "--abbrev=64",
                "--no-renames", "--no-ext-diff", "--no-textconv", "--ignore-submodules=none", "-z", revision, "--");
            if (changes != confirmed.Staged) throw Problem("changed");
            Verify(confirmed, GitHubWorkspaceAction.Commit, token);
            // Compare-and-swap the checked branch, including the first commit on an unborn branch.
            string expected = confirmed.Revision.Length == 0 ? new string('0', revision.Length) : confirmed.Revision;
            var result = Execute(directory, false, token, "update-ref", "--create-reflog", "-m", "commit: " + message,
                "refs/heads/" + confirmed.Branch, revision, expected);
            if (result.ExitCode != 0) throw Problem("operationFailed");
        }

        internal void Push(Snapshot confirmed, CancellationToken token, Action beforePush = null)
        {
            Verify(confirmed, GitHubWorkspaceAction.Push, token);
            string source = confirmed.Directory;
            string objects = GitPath(source, "objects", token);
            string shallow = GitPath(source, "shallow", token);
            if (!Directory.Exists(objects)) throw Problem("invalidWorktree");

            // Push from an isolated bare repository, keeping the original worktree's
            // URL rewrites, proxies, hooks, helpers and push options out of the network operation.
            string scratch = Path.Combine(Path.GetTempPath(), "dpgh-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            try
            {
                string objectFormat = confirmed.Revision.Length == 64 ? "sha256" : "sha1";
                var initialized = Execute(scratch, true, token, "init", "--bare", "--template=", "--object-format=" + objectFormat);
                if (initialized.ExitCode != 0) throw Problem("pushFailed");
                var alternate = Path.Combine(scratch, "objects", "info", "alternates");
                File.WriteAllText(alternate, objects.Replace('\\', '/') + "\n", new UTF8Encoding(false));
                if (File.Exists(shallow)) File.Copy(shallow, Path.Combine(scratch, "shallow"));
                string remote = useConnectedAccount ? "https://github.com/" + confirmed.Repository + ".git" : confirmed.Remote;
                if (beforePush != null) beforePush();
                var result = Execute(scratch, true, token, "push", "--no-verify", "--no-follow-tags", "--no-signed", "--",
                    remote, confirmed.Revision + ":refs/heads/" + confirmed.Branch);
                if (result.ExitCode != 0) throw Problem("pushFailed");
            }
            finally
            {
                try { Directory.Delete(scratch, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        internal static string ResolvePushTarget(string address, string repository)
        {
            if (String.IsNullOrWhiteSpace(address) || address.Any(Char.IsControl)) return null;
            const string scpPrefix = "git@github.com:";
            if (address.StartsWith(scpPrefix, StringComparison.OrdinalIgnoreCase))
                address = "ssh://git@github.com/" + address.Substring(scpPrefix.Length);
            Uri uri;
            if (!Uri.TryCreate(address, UriKind.Absolute, out uri) || uri.Host != "github.com" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                return null;
            bool https = uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0;
            bool ssh = uri.Scheme == "ssh" && (uri.IsDefaultPort || uri.Port == 22) && uri.UserInfo == "git";
            if ((!https && !ssh) || !Same(WorkSummaryRepository.Normalize("https://github.com" + uri.AbsolutePath), repository)) return null;
            return (https ? "https://github.com/" : "ssh://git@github.com/") + repository + ".git";
        }

        void RejectCommitOperations(string root, CancellationToken token)
        {
            foreach (string path in new[] { "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "rebase-merge", "rebase-apply", "sequencer" })
            {
                string state = GitPath(root, path, token);
                if (File.Exists(state) || Directory.Exists(state)) throw Problem("operationFailed");
            }
        }

        string ReadHash(string directory, CancellationToken token, params string[] arguments)
        {
            string hash = Read(directory, token, arguments).Trim();
            if (!Regex.IsMatch(hash, @"\A(?:[0-9a-f]{40}|[0-9a-f]{64})\z")) throw Problem("operationFailed");
            return hash;
        }

        string GitPath(string directory, string relative, CancellationToken token)
        {
            string path = Read(directory, token, "rev-parse", "--path-format=absolute", "--git-path", relative).TrimEnd('\r', '\n');
            if (path.Any(Char.IsControl) || !Path.IsPathRooted(path)) throw Problem("invalidWorktree");
            return path;
        }

        string Read(string directory, CancellationToken token, params string[] arguments)
        {
            var result = Execute(directory, false, token, arguments);
            if (result.ExitCode != 0) throw Problem("operationFailed");
            return result.Output;
        }

        CommandResult Execute(string directory, bool isolated, CancellationToken token, params string[] arguments)
        {
            token.ThrowIfCancellationRequested();
            var result = run(directory, arguments, isolated, token);
            token.ThrowIfCancellationRequested();
            if (result.TimedOut) throw new OperationCanceledException();
            if (result.Truncated) throw Problem("operationFailed");
            return result;
        }

        internal static CommandResult RunWindows(string directory, string[] arguments, bool isolated, CancellationToken token)
        {
            return RunWindows(directory, arguments, isolated, token, null);
        }

        internal static CommandResult RunProjectGit(string directory, string[] arguments, CancellationToken token)
        {
            return RunWindows(directory, arguments, false, token, null, null, true);
        }

        static CommandResult RunWindows(string directory, string[] arguments, bool isolated, CancellationToken token,
            GitHubPushAuthentication authentication, Uri proxy = null, bool projectConfigOnly = false)
        {
            var options = new List<string> { "--no-pager", "-c", "core.hooksPath=NUL", "-c", "core.fsmonitor=false", "-c", "core.untrackedCache=false", "-c", "core.longpaths=true",
                "-c", "color.ui=false", "-c", "maintenance.auto=false", "-c", "gc.auto=0", "-c", "submodule.recurse=false", "-c", "push.recurseSubmodules=no",
                "-c", "protocol.allow=never", "-c", "credential.helper=", "-c", "commit.gpgSign=false" };
            var environment = new Dictionary<string, string> { { "GIT_TERMINAL_PROMPT", "0" }, { "GIT_NO_LAZY_FETCH", "1" } };
            if (projectConfigOnly)
            {
                environment["GIT_CONFIG_NOSYSTEM"] = "1";
                environment["GIT_CONFIG_GLOBAL"] = "NUL";
                environment["GIT_ATTR_NOSYSTEM"] = "1";
                options.AddRange(new[] { "-c", "core.attributesFile=NUL" });
            }
            if (isolated)
            {
                options.AddRange(new[] { "-c", "protocol.https.allow=always", "-c", "protocol.ssh.allow=always", "-c", "http.followRedirects=false",
                    "-c", "credential.interactive=false" });
                bool httpsPush = arguments.Length >= 6 && arguments[0] == "push" &&
                    arguments[5].StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase);
                if (httpsPush && proxy != null) options.AddRange(new[] { "-c", "http.https://github.com/.proxy=" + proxy.AbsoluteUri });
                if (httpsPush && authentication != null) authentication.Configure(options, environment);
                else options.AddRange(new[] { "-c", "credential.helper=manager" });
                environment["GIT_CONFIG_NOSYSTEM"] = "1";
                environment["GIT_CONFIG_GLOBAL"] = "NUL";
                environment["GCM_INTERACTIVE"] = "Never";
                environment["GIT_SSH_COMMAND"] = "ssh -oBatchMode=yes -oStrictHostKeyChecking=yes -oHostname=github.com -oPort=22 -oProxyCommand=none -oProxyJump=none -oPermitLocalCommand=no";
            }
            else options.AddRange(new[] { "-c", "core.bare=false", "-c", "core.worktree=" + directory });
            options.AddRange(arguments);
            string binary = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Select(part => part.Trim().Trim('"')).Where(Path.IsPathRooted)
                .Select(part => Path.Combine(part, "git.exe")).FirstOrDefault(File.Exists);
            if (binary == null) throw Problem("operationFailed");
            var command = new AgentCommand { Id = Guid.NewGuid().ToString("N"), Executable = Path.GetFullPath(binary), Arguments = options.ToArray(), Workspace = directory };
            return WindowsCommandRunner.Run(command, isolated ? 90 : 15, token, true, null, environment, false);
        }

        static bool Same(string first, string second) { return String.Equals(first, second, StringComparison.OrdinalIgnoreCase); }
        static GitHubAppGitException Problem(string key) { return new GitHubAppGitException("workSummary.gitHub." + key); }
    }

    internal sealed class GitHubAppGitException : Exception
    {
        internal GitHubAppGitException(string key) { Key = key; }
        internal string Key { get; private set; }

        internal string LocalizedMessage()
        {
            switch (Key)
            {
                case "workSummary.gitHub.invalidWorktree": return TextResources.Get("workSummary.gitHub.invalidWorktree");
                case "workSummary.gitHub.changed": return TextResources.Get("workSummary.gitHub.changed");
                case "workSummary.gitHub.noStaged": return TextResources.Get("workSummary.gitHub.noStaged");
                case "workSummary.gitHub.invalidInput": return TextResources.Get("workSummary.gitHub.invalidInput");
                case "workSummary.gitHub.incompatiblePush": return TextResources.Get("workSummary.gitHub.incompatiblePush");
                case "workSummary.gitHub.pushFailed": return TextResources.Get("workSummary.gitHub.pushFailed");
                case "workSummary.gitHub.selectedCommitFailedAfterPreparation":
                    return TextResources.Get("workSummary.gitHub.selectedCommitFailedAfterPreparation");
                default: return TextResources.Get("workSummary.gitHub.operationFailed");
            }
        }
    }
}
