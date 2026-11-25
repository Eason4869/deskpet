using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
namespace Dafeiyu.Tests
{
    public static class GitHubToolsTests
    {
        static int checks;static void Check(bool ok,string why){if(!ok)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
        sealed class Handler:HttpMessageHandler
        {
            public readonly List<string> Requests=new List<string>();public string Body="";
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            {
                Requests.Add(request.Method+" "+request.RequestUri+" auth="+(request.Headers.Authorization==null?"none":request.Headers.Authorization.Scheme+" "+request.Headers.Authorization.Parameter));
                string path=request.RequestUri.AbsolutePath;
                if(request.Content!=null)Body=request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                string json=path.Contains("/contents/")?"{\"type\":\"file\",\"encoding\":\"base64\",\"content\":\"" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("hello github")) + "\"}":path.Contains("/issues")?"{\"number\":7,\"title\":\"bug\",\"body\":\"body\"}":"{\"ok\":true}";
                if(path=="/user" && request.Headers.UserAgent.Count>0)json="{\"login\":\"fixture-user\"}";
                if(path=="/user/repos")json="{\"full_name\":\"fixture-user/new-repo\",\"default_branch\":\"main\",\"private\":true,\"html_url\":\"https://example.test/untrusted\"}";
                if(path=="/repos/fixture-user/new-repo")json="{\"full_name\":\"fixture-user/new-repo\",\"default_branch\":\"main\"}";
                if(path=="/repos/fixture-user/new-repo/pulls")json="{\"number\":18}";
                if(path=="/repos/fixture/sample")json="{\"default_branch\":\"trunk\"}";
                if(path=="/repos/fixture/sample/pulls")json="{\"number\":17,\"html_url\":\"https://example.test/untrusted\"}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
            }
        }
        static ConnectionSettings Config(){return new ConnectionSettings{BaseUrl="https://api.github.com",ApiKey="secret",Protocol="github"};}
        sealed class FailureHandler:HttpMessageHandler
        {
            internal string Mode;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
            {
                if(Mode=="network")throw new HttpRequestException("PRIVATE transport details");
                if(Mode=="body")return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new BrokenStream())});
                if(Mode=="timeout")throw new OperationCanceledException();
                if(Mode=="cancel"){token.ThrowIfCancellationRequested();throw new OperationCanceledException(token);}
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden){Content=new StringContent("PRIVATE response body")});
            }
        }
        sealed class BrokenStream:System.IO.Stream
        {
            public override bool CanRead{get{return true;}}public override bool CanWrite{get{return false;}}public override bool CanSeek{get{return false;}}
            public override long Length{get{throw new NotSupportedException();}}public override long Position{get{throw new NotSupportedException();}set{throw new NotSupportedException();}}
            public override void Flush(){}public override void SetLength(long value){throw new NotSupportedException();}public override long Seek(long value,System.IO.SeekOrigin origin){throw new NotSupportedException();}
            public override void Write(byte[] buffer,int offset,int count){throw new NotSupportedException();}
            public override int Read(byte[] buffer,int offset,int count){throw new System.IO.IOException("PRIVATE body details");}
            public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken token){throw new System.IO.IOException("PRIVATE body details");}
            public override Task CopyToAsync(System.IO.Stream destination,int bufferSize,CancellationToken token){throw new System.IO.IOException("PRIVATE body details");}
        }
        public static int Main()
        {
            try
            {
                var handler=new Handler();var client=new GitHubClient(new Uri("https://api.github.com"),handler,"openai/codex","secret");
                var read=client.ExecuteAsync("github_read_file",new Dictionary<string,object>{{"path","README.md"}},CancellationToken.None).GetAwaiter().GetResult();
                Check(read.Contains("hello github") && handler.Requests[0].Contains("Bearer secret"),"GitHubReadFile reads repository content with authenticated API");
                var issue=client.ExecuteAsync("github_create_issue",new Dictionary<string,object>{{"title","fix"},{ "body","details"}},CancellationToken.None).GetAwaiter().GetResult();
                Check(issue.Contains("bug") && handler.Requests[1].StartsWith("POST"),"GitHubCreateIssue posts an authenticated issue");
                var web=client.ExecuteAsync("fetch_url",new Dictionary<string,object>{{"url","https://example.test/page"}},CancellationToken.None).GetAwaiter().GetResult();
                Check(web.Length>0 && handler.Requests.Count>=3,"FetchUrl returns bounded readable web text");
                GitHubAppApi();
                CreateRepositoryWithoutAnExistingBinding();
                AuthenticatedTransportChecksAccountBeforeWriting();
                AgentGitHubWorkflow();
                GitHubAppWorkspace();
                GitWorkspaceChangesRead();
                foreach(string tool in new[]{"fetch_url","github_read_issue"})foreach(string mode in new[]{"http","network","body","timeout"})
                using(var failing=new GitHubClient(new Uri("https://api.github.com"),new FailureHandler{Mode=mode},"openai/codex","secret"))
                {
                    string result=failing.ExecuteAsync(tool,new Dictionary<string,object>{{"url","https://example.test/page"},{"number",1}},CancellationToken.None).GetAwaiter().GetResult();
                    Check(result.StartsWith("错误：")&&!result.Contains("PRIVATE")&&!result.Contains("secret"),tool+" reports sanitized "+mode+" failure as a tool result");
                }
                using(var writing=new GitHubClient(new Uri("https://api.github.com"),new FailureHandler{Mode="network"},"openai/codex","secret"))
                {
                    bool uncertain=false;try{writing.ExecuteAsync("github_create_issue",new Dictionary<string,object>{{"title","issue"}},CancellationToken.None).GetAwaiter().GetResult();}catch(HttpRequestException){uncertain=true;}
                    Check(uncertain,"write transport failures still require reconciliation rather than inviting duplicate writes");
                }
                using(var stopping=new GitHubClient(new Uri("https://api.github.com"),new FailureHandler{Mode="cancel"},"openai/codex","secret"))
                using(var cancel=new CancellationTokenSource())
                {
                    cancel.Cancel();bool cancelled=false;try{stopping.ExecuteAsync("fetch_url",new Dictionary<string,object>{{"url","https://example.test/page"}},cancel.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
                    Check(cancelled,"explicit cancellation remains cancellation");
                }
                Console.WriteLine("GitHub tools tests: "+checks+" passed");return 0;
            }catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }

        static void GitHubAppApi()
        {
            var handler=new Handler();
            using(var client=GitHubClient.ForApp("fixture/sample","fixture-token",handler))
            {
                Check(client.GetAuthenticatedLoginAsync(CancellationToken.None).GetAwaiter().GetResult()=="fixture-user" &&
                    handler.Requests[0]=="GET https://api.github.com/user auth=Bearer fixture-token",
                    "GitHub app validates its account against the authenticated user endpoint with a User-Agent");
                Check(client.GetDefaultBranchAsync(CancellationToken.None).GetAwaiter().GetResult()=="trunk" &&
                    handler.Requests[1]=="GET https://api.github.com/repos/fixture/sample auth=Bearer fixture-token",
                    "GitHub app reads the linked repository default branch");
                var page=client.CreateAppPullRequestAsync("Fix tests","Details","feature/check","trunk",CancellationToken.None).GetAwaiter().GetResult();
                var payload=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string,string>>(handler.Body);
                Check(page.AbsoluteUri=="https://github.com/fixture/sample/pull/17" &&
                    handler.Requests[2]=="POST https://api.github.com/repos/fixture/sample/pulls auth=Bearer fixture-token" &&
                    payload["title"]=="Fix tests" && payload["body"]=="Details" && payload["head"]=="feature/check" && payload["base"]=="trunk",
                    "GitHub app sends the confirmed pull request and opens its canonical GitHub URL");
            }
        }

        static void CreateRepositoryWithoutAnExistingBinding()
        {
            var handler = new Handler();
            using (var client = GitHubClient.ForApp(null, "fixture-token", handler))
            {
                var arguments = new Dictionary<string, object> { { "name", "new-repo" } };
                string result = client.ExecuteAsync("github_create_repository", arguments, CancellationToken.None).GetAwaiter().GetResult();
                var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                var repository = serializer.Deserialize<Dictionary<string, string>>(result);
                var payload = serializer.Deserialize<Dictionary<string, object>>(handler.Body);
                Check(handler.Requests.SequenceEqual(new[] {
                    "GET https://api.github.com/user auth=Bearer fixture-token",
                    "POST https://api.github.com/user/repos auth=Bearer fixture-token"
                }) && (bool)payload["private"] && (string)payload["name"] == "new-repo",
                    "Repository creation requires the authenticated account and defaults to private without an existing repository binding");
                Check(repository["repository"] == "fixture-user/new-repo" &&
                    repository["url"] == "https://github.com/fixture-user/new-repo" && repository["default_branch"] == "main",
                    "Repository creation returns the canonical repository identity and ignores response URLs");
                arguments["name"] = "another-user/other-repo";
                bool rejected = false;
                try { client.ExecuteAsync("github_create_repository", arguments, CancellationToken.None).GetAwaiter().GetResult(); }
                catch (ArgumentException) { rejected = true; }
                Check(rejected && handler.Requests.Count == 2, "Repository creation rejects cross-account names before contacting GitHub");
            }
        }

        static void AuthenticatedTransportChecksAccountBeforeWriting()
        {
            var factory = typeof(GitHubClient).GetMethod("WithApiTransport",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Check(factory != null, "GitHub API accepts a delegated authentication transport");
            var requests = new List<string>();
            string activeLogin = "fixture-user";
            Func<HttpMethod, string, string, CancellationToken, Task<string>> transport = (method, path, json, token) => {
                requests.Add(method + " " + path);
                return Task.FromResult(path == "/user"
                    ? "{\"login\":\"" + activeLogin + "\"}"
                    : "{\"number\":17}");
            };
            using (var client = (GitHubClient)factory.Invoke(null, new object[] { "fixture/sample", "fixture-user", transport }))
            {
                var link = client.CreateAppPullRequestAsync("Title", "Body", "feature", "main", CancellationToken.None).GetAwaiter().GetResult();
                Check(link.AbsoluteUri == "https://github.com/fixture/sample/pull/17" &&
                    requests.SequenceEqual(new[] { "GET /user", "POST /repos/fixture/sample/pulls" }),
                    "A delegated write rechecks the selected account before contacting the mutation endpoint");
                activeLogin = "different-user";
                bool rejected = false;
                try { client.CreateAppPullRequestAsync("Title", "Body", "feature", "main", CancellationToken.None).GetAwaiter().GetResult(); }
                catch (ApiException) { rejected = true; }
                Check(rejected && requests.Count == 3 && requests[2] == "GET /user",
                    "A delegated GitHub write stops before POST when the active account changed");
                rejected = false;
                try { client.GetDefaultBranchAsync(CancellationToken.None).GetAwaiter().GetResult(); }
                catch (ApiException) { rejected = true; }
                Check(rejected && requests.Count == 4 && requests[3] == "GET /user",
                    "A switched CLI account cannot be used for repository reads either");
            }
        }

        sealed class GitHubApproval : IAgentApprovalProvider
        {
            internal bool Allow = true;
            internal readonly List<AgentApprovalRequest> Requests = new List<AgentApprovalRequest>();
            public Task<bool> ApproveAsync(AgentApprovalRequest request)
            {
                Requests.Add(request);
                return Task.FromResult(Allow);
            }
        }

        static void AgentGitHubWorkflow()
        {
            string root = Path.Combine(Path.GetTempPath(), "deskpet-agent-github-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                using (var workspace = new AgentWorkspace(root, true))
                {
                    var handler = new Handler();
                    var approval = new GitHubApproval();
                    workspace.ConfigureGitHubAutomation(new PetSettings());
                    workspace.ConfigureGitHub(GitHubClient.ForApp(null, "fixture-token", handler));
                    var profiles = new AgentProfileRegistry();
                    workspace.ConfigureKernel(profiles.PermissionRuntime("build"), null, approval, null, null);
                    workspace.ConfigureAgentProfile(profiles.Get("build"));
                    workspace.ConfigureDesktopSession();
                    Func<string, string, string> invoke = (tool, args) =>
                        workspace.ExecuteAsync(tool, args, CancellationToken.None).GetAwaiter().GetResult();
                    Check(workspace.AvailableToolNames.Contains("github_create_repository") &&
                        !workspace.AvailableToolNames.Contains("github_push"),
                        "The work agent can create a remote before binding a repository and cannot push an unbound project");
                    workspace.PlanMode = true;
                    Check(!workspace.AvailableToolNames.Contains("github_create_repository") &&
                        TextResources.IsToolError(invoke("github_create_repository", "{\"name\":\"new-repo\"}")) &&
                        handler.Requests.Count == 0, "Plan mode hides GitHub mutations and refuses direct requests");
                    workspace.PlanMode = false;
                    string created = invoke("github_create_repository", "{\"name\":\"new-repo\"}");
                    Check(created.Contains("fixture-user/new-repo"), "The agent creates a private remote through its authorized API connection");
                    Check(invoke("github_initialize", "{}").Contains("main"), "The agent initializes its own Git worktree");
                    Git(root, "config", "user.name", "GitHub Automation Fixture");
                    Git(root, "config", "user.email", "fixture@example.test");
                    Check(invoke("github_bind", "{\"repository\":\"fixture-user/new-repo\"}").Contains("fixture-user/new-repo"),
                        "The work agent checks repository identity before binding the approved origin");
                    File.WriteAllText(Path.Combine(root, "base.txt"), "base");
                    File.WriteAllText(Path.Combine(root, "keep.txt"), "keep staged");
                    Git(root, "add", "--", "keep.txt");
                    string commit = invoke("github_commit", "{\"paths\":[\"base.txt\"],\"message\":\"Initial step\"}");
                    Check(commit.Contains("commit") && Git(root, "ls-tree", "--name-only", "HEAD").Trim() == "base.txt" &&
                        Git(root, "diff", "--cached", "--name-only").Trim() == "keep.txt",
                        "The agent commits exactly the approved file and preserves unrelated staged content");
                    File.WriteAllText(Path.Combine(root, ".gitattributes"), "*.bin filter=lfs\n");
                    File.WriteAllText(Path.Combine(root, "blob.bin"), "filtered data");
                    string head = Git(root, "rev-parse", "HEAD");
                    Check(TextResources.IsToolError(invoke("github_commit", "{\"paths\":[\"blob.bin\"],\"message\":\"Filtered file\"}")) &&
                        Git(root, "rev-parse", "HEAD") == head,
                        "Selected-file commit detects Git filters instead of silently committing unfiltered content");
                    Check(invoke("github_branch", "{\"branch\":\"fixture-change\"}").Contains("fixture-change"),
                        "The agent can prepare a separate branch for a pull request");
                    approval.Allow = false;
                    Check(TextResources.IsToolError(invoke("github_push", "{}")) &&
                        approval.Requests.Last().Resource.Contains("fixture-user/new-repo"),
                        "Push requests specify the approved GitHub destination and cannot run after denial");
                    approval.Allow = true;
                    string pull = invoke("github_create_pull_request",
                        "{\"title\":\"Fixture change\",\"head\":\"fixture-change\",\"base\":\"main\"}");
                    Check(pull.Contains("\"number\":18") && handler.Requests.Last().Contains("POST https://api.github.com/repos/fixture-user/new-repo/pulls"),
                        "Creating a branch and opening a pull request use the same verified project repository");
                    Check(approval.Requests.All(request => request.Action == "github_write"),
                        "GitHub automation goes through the workspace write-approval boundary");
                    workspace.ConfigureAgentProfile(profiles.Get("explore"));
                    Check(!workspace.AvailableToolNames.Contains("github_initialize") &&
                        TextResources.IsToolError(invoke("github_login", "{\"open_browser\":false}")),
                        "Subagents cannot invoke the primary work agent's GitHub authorization or Git mutations");
                }
            }
            finally { DeleteGitFixture(root); }
        }

        static void GitHubAppWorkspace()
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "gh-" + Guid.NewGuid().ToString("N"));
            string workspace = Path.Combine(root, "workspace with spaces");
            Directory.CreateDirectory(workspace);
            try
            {
                const string repo = "fixture/sample";
                var cancel = CancellationToken.None;
                Git(workspace, "init", "-b", "feature/app");
                Git(workspace, "config", "user.name", "DeskPet fixture");
                Git(workspace, "config", "user.email", "deskpet@example.test");
                Git(workspace, "remote", "add", "origin", "https://github.com/fixture/sample.git");

                string staged = Path.Combine(workspace, "staged.txt");
                File.WriteAllText(staged, "staged bytes");
                Git(workspace, "add", "--", "staged.txt");
                File.WriteAllText(staged, "unstaged bytes");
                File.WriteAllText(Path.Combine(workspace, "untracked.txt"), "untracked bytes");

                string pushedRef = null, pushedBlob = null;
                var git = new GitHubAppGit((directory, args, isolated, token) => {
                    if (args[0] != "push") return GitHubAppGit.RunWindows(directory, args, isolated, token);
                    Check(isolated && directory != workspace && args[5] == "https://github.com/fixture/sample.git" &&
                        args.Take(5).SequenceEqual(new[] { "push", "--no-verify", "--no-follow-tags", "--no-signed", "--" }),
                        "Push uses the confirmed GitHub URL and only one branch refspec in an isolated Git repository");
                    pushedRef = args[6];
                    pushedBlob = Git(directory, "show", pushedRef.Split(':')[0] + ":staged.txt");
                    return new CommandResult { Output = "", ExitCode = 0 };
                });
                var snapshot = git.Inspect(workspace, repo, GitHubWorkspaceAction.Commit, cancel);
                Check(snapshot.StagedCount == 1 && snapshot.Revision == "", "An unborn branch captures its staged changes before confirmation");
                git.Commit(snapshot, "Fixture says \"hello\" & stays local", cancel);
                Check(Git(workspace, "show", "HEAD:staged.txt") == "staged bytes" &&
                    Git(workspace, "log", "-1", "--format=%s").Trim() == "Fixture says \"hello\" & stays local" &&
                    Git(workspace, "status", "--porcelain").Contains("?? untracked.txt") &&
                    Git(workspace, "status", "--porcelain").Contains(" M staged.txt"),
                    "Local commit uses the staged bytes and preserves other edits with a literally quoted message");

                Git(workspace, "add", "--", "staged.txt");
                snapshot = git.Inspect(workspace, repo, GitHubWorkspaceAction.Commit, cancel);
                File.WriteAllText(staged, "more staged bytes");
                Git(workspace, "add", "--", "staged.txt");
                bool changed = false;
                try { git.Commit(snapshot, "Must not commit", cancel); }
                catch (GitHubAppGitException error) { changed = error.Key == "workSummary.gitHub.changed"; }
                Check(changed && Git(workspace, "rev-parse", "HEAD").Trim() == snapshot.Revision,
                    "Changing the staged snapshot after confirmation rejects the local commit");

                var push = git.Inspect(workspace, repo, GitHubWorkspaceAction.Push, cancel);
                git.Push(push, cancel);
                Check(pushedRef == push.Revision + ":refs/heads/feature/app" && pushedBlob == "staged bytes",
                    "The push transports only the confirmed commit, with an unpushed staged edit left local");

                Git(workspace, "remote", "set-url", "--push", "origin", "https://github.com/other/sample.git");
                bool wrongRemote = false;
                try { git.Push(push, cancel); }
                catch (GitHubAppGitException error) { wrongRemote = error.Key == "workSummary.gitHub.incompatiblePush"; }
                Check(wrongRemote, "A changed push URL cannot redirect to another repository");
                Check(GitHubAppGit.ResolvePushTarget("git@github.com:fixture/sample.git", repo) == "ssh://git@github.com/fixture/sample.git" &&
                    GitHubAppGit.ResolvePushTarget("https://github.com@host.test/fixture/sample", repo) == null &&
                    GitHubAppGit.ResolvePushTarget("https://fixture-secret@github.com/fixture/sample", repo) == null &&
                    GitHubAppGit.ResolvePushTarget("https://github.com/fixture/sample\nhttps://github.com/fixture/sample", repo) == null,
                    "GitHub push accepts the linked SSH address and rejects a foreign host, embedded token and multiple destinations");

                var next = git.Inspect(workspace, repo, GitHubWorkspaceAction.Commit, cancel);
                git.Commit(next, "Follow-up commit", cancel);
                Check(Git(workspace, "show", "HEAD:staged.txt") == "more staged bytes" && Git(workspace, "rev-parse", "HEAD^").Trim() == next.Revision,
                    "A subsequent staged commit retains the confirmed parent revision");
            }
            finally
            {
                foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    var attributes = File.GetAttributes(file);
                    if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
                Directory.Delete(root, true);
            }
        }

        static void GitWorkspaceChangesRead()
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "git-changes-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var cancel = CancellationToken.None;
                Git(root, "init", "-b", "feature/workspace");
                Git(root, "config", "user.name", "DeskPet fixture");
                Git(root, "config", "user.email", "deskpet@example.test");
                File.WriteAllText(Path.Combine(root, "--file.txt"), "first\nsecond\n");
                Git(root, "add", "--", "--file.txt");
                File.AppendAllText(Path.Combine(root, "--file.txt"), "before first commit\n");
                var unborn = GitWorkspaceChanges.Read(root, false, cancel);
                Check(unborn.Files.Count == 1 && unborn.AddedLines == 3 && unborn.Branch == "feature/workspace",
                    "An unborn branch compares the tracked working copy to the empty tree");

                File.WriteAllText(Path.Combine(root, "old name.txt"), "rename content\n");
                File.WriteAllText(Path.Combine(root, "deleted.txt"), "remove me\n");
                File.WriteAllBytes(Path.Combine(root, "image.bin"), new byte[] { 0, 1, 2 });
                Git(root, "add", "--", ".");
                Git(root, "commit", "-qm", "baseline");
                Git(root, "remote", "add", "origin", "https://example.test/unreachable.git");
                Git(root, "update-ref", "refs/remotes/origin/main", "HEAD");
                Git(root, "branch", "--set-upstream-to=origin/main");
                File.AppendAllText(Path.Combine(root, "--file.txt"), "local commit\n");
                Git(root, "commit", "-qam", "local commit");

                File.AppendAllText(Path.Combine(root, "--file.txt"), "staged edit\n");
                Git(root, "add", "--", "--file.txt");
                File.AppendAllText(Path.Combine(root, "--file.txt"), "unstaged edit\n");
                Git(root, "mv", "old name.txt", "新 name.txt");
                File.Delete(Path.Combine(root, "deleted.txt"));
                File.WriteAllBytes(Path.Combine(root, "image.bin"), new byte[] { 0, 3, 4 });
                File.WriteAllText(Path.Combine(root, "draft.txt"), "untracked text\n");
                byte[] index = File.ReadAllBytes(Path.Combine(root, ".git", "index"));
                Git(root, "config", "diff.external", "false");
                Git(root, "config", "core.fsmonitor", "false");

                var snapshot = GitWorkspaceChanges.Read(root, false, cancel);
                var text = snapshot.Files.Single(file => file.Path == "--file.txt");
                var rename = snapshot.Files.Single(file => file.Status == GitWorkspaceChangeStatus.Renamed);
                Check(snapshot.Branch == "feature/workspace" && snapshot.Upstream == "origin/main" && snapshot.Files.Count == 4 &&
                    snapshot.AddedLines == 2 && snapshot.RemovedLines == 1 && snapshot.SkippedUntrackedCount == 1,
                    "Working-copy summary counts staged and unstaged lines once and lists untracked files separately");
                Check(rename.Path == "新 name.txt" && rename.PreviousPath == "old name.txt" &&
                    snapshot.Files.Single(file => file.Path == "deleted.txt").Status == GitWorkspaceChangeStatus.Deleted &&
                    snapshot.Files.Single(file => file.Path == "image.bin").IsBinary,
                    "Git changes preserve rename paths, deletions, and binary files");
                string diff = GitWorkspaceChanges.GetDiff(snapshot, text, cancel);
                Check(diff.Contains("+staged edit") && diff.Contains("+unstaged edit") && !diff.Contains("+local commit") && !diff.Contains("draft.txt"),
                    "Selecting a literal path returns its combined working-copy patch");
                Check(GitWorkspaceChanges.GetDiff(snapshot, rename, cancel).Contains("rename from old name.txt"),
                    "Selecting a rename shows its original and current paths");

                var upstream = GitWorkspaceChanges.Read(root, true, cancel);
                var upstreamText = upstream.Files.Single(file => file.Path == "--file.txt");
                string upstreamDiff = GitWorkspaceChanges.GetDiff(upstream, upstreamText, cancel);
                Check(upstream.UsesUpstream && upstream.TargetRevision == Git(root, "rev-parse", "HEAD").Trim() &&
                    upstream.Files.Count == 1 && upstream.AddedLines == 1 && upstream.RemovedLines == 0 && upstream.SkippedUntrackedCount == 0 &&
                    upstreamDiff.Contains("+local commit") && !upstreamDiff.Contains("staged edit"),
                    "Upstream comparison reads the two cached commits and excludes staged and unstaged edits");
                Check(index.SequenceEqual(File.ReadAllBytes(Path.Combine(root, ".git", "index"))) && !snapshot.IsTruncated,
                    "Reading Git changes and patches leaves the index intact");
            }
            finally { DeleteGitFixture(root); }
        }

        static void DeleteGitFixture(string root)
        {
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
            Directory.Delete(root, true);
        }

        static string Git(string directory, params string[] arguments)
        {
            var options = new[] { "-c", "core.longpaths=true" }.Concat(arguments);
            var info = new ProcessStartInfo("git.exe", String.Join(" ", options.Select(WindowsCommandRunner.Quote))) {
                WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var process = Process.Start(info))
            {
                string output = process.StandardOutput.ReadToEnd(), error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0) throw new Exception("Git fixture failed: " + error);
                return output;
            }
        }
    }
}
