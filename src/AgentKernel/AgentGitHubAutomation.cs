using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Dafeiyu
{
    public sealed partial class AgentWorkspace
    {
        bool githubAutomation, githubLoginStarted, githubHumanApproval;

        internal void ConfigureGitHubAutomation(PetSettings settings)
        {
            githubAutomation = WindowsCommandRunner.IsWindowsHost;
            ConfigureGitHub(GitHubClient.ForConnection(settings.GitHubRepository, settings));
        }

        static bool IsGitHubAutomationTool(string name)
        {
            switch (name)
            {
                case "github_login": case "github_create_repository": case "github_initialize":
                case "github_bind": case "github_branch": case "github_commit": case "github_push":
                    return true;
                default: return false;
            }
        }

        bool GitHubAutomationAvailable(string name)
        {
            if (!githubAutomation || !githubHumanApproval || readOnly ||
                (kernelProfile != null && kernelProfile.Mode != "primary")) return false;
            switch (name)
            {
                case "github_create_repository": case "github_bind":
                    return github != null && github.CanCreateRepository;
                case "github_push": return githubConnected;
                default: return true;
            }
        }

        string GitHubAutomationResource(string name, Dictionary<string, object> args)
        {
            if (name == "github_bind") return "github.com/" + WorkSummaryRepository.Normalize(GetString(args, "repository", "")) + " :: " + Root;
            if (name == "github_create_repository")
                return "github.com :: " + GetString(args, "name", "") + " (" + GetString(args, "visibility", "private") + ")";
            if (name == "github_push") return "github.com/" + (github == null ? "" : github.Repository) + " :: " + Root;
            if (name == "github_login") return "github.com :: " + Root;
            return Root;
        }

        async Task<string> ExecuteGitHubAutomationAsync(string name, Dictionary<string, object> args,
            string authorizedResource, CancellationToken token, Action beforeEffect)
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!GitHubAutomationAvailable(name) || GitHubAutomationResource(name, args) != authorizedResource)
                    return GitHubToolError(TextResources.Get("workSummary.gitHub.changed"));

                var json = new JavaScriptSerializer();
                Action begin = beforeEffect ?? delegate { };
                var local = new GitHubAgentLocal(Root, beforeEffect);
                switch (name)
                {
                    case "github_login":
                        object openValue;
                        bool openBrowser = !args.TryGetValue("open_browser", out openValue) || !(openValue is bool) || (bool)openValue;
                        return await ConnectGitHubForAgentAsync(local, openBrowser, token, begin).ConfigureAwait(false);
                    case "github_create_repository":
                        return await github.CreateRepositoryAsync(args, token, beforeEffect).ConfigureAwait(false);
                    case "github_initialize":
                        return json.Serialize(new { branch = local.Initialize(GetString(args, "branch", "main"), token) });
                    case "github_bind":
                        string repository = WorkSummaryRepository.Normalize(GetString(args, "repository", ""));
                        if (repository.Length == 0) return GitHubToolError(TextResources.Get("gitHubUpdates.repository.text"));
                        if (githubConnected && !String.Equals(github.Repository, repository, StringComparison.OrdinalIgnoreCase))
                            return GitHubToolError(TextResources.Get("workSummary.gitHub.changed"));
                        await github.ConfirmRepositoryAsync(repository, token).ConfigureAwait(false);
                        github.UseRepository(local.BindOrigin(repository, token));
                        githubConnected = true;
                        return json.Serialize(new { repository = github.Repository });
                    case "github_branch":
                        return json.Serialize(new { branch = local.CreateBranch(GetString(args, "branch", ""), token) });
                    case "github_commit":
                        object value;
                        var paths = args.TryGetValue("paths", out value) && !(value is string) ? value as IEnumerable : null;
                        if (paths == null) return GitHubToolError(TextResources.Get("workSummary.gitHub.invalidInput"));
                        var selected = paths.Cast<object>().Take(33).Select(path => path as string).ToArray();
                        if (selected.Length > 32) return GitHubToolError(TextResources.Get("workSummary.gitHub.invalidInput"));
                        return json.Serialize(new { commit = local.CommitSelected(selected, GetString(args, "message", ""), token) });
                    case "github_push":
                        string current = local.CurrentRepository(token);
                        if (!String.Equals(current, github.Repository, StringComparison.OrdinalIgnoreCase))
                            return GitHubToolError(TextResources.Get("workSummary.gitHub.incompatiblePush"));
                        var reader = new GitHubAppGit();
                        var snapshot = reader.Inspect(Root, current, GitHubWorkspaceAction.Push, token);
                        var authentication = await github.GetPushAuthenticationAsync(token).ConfigureAwait(false);
                        new GitHubAppGit(authentication: authentication).Push(snapshot, token, beforeEffect);
                        return json.Serialize(new { repository = current, branch = snapshot.Branch, commit = snapshot.Revision });
                    default:
                        return GitHubToolError(TextResources.Get("gitHubClient.executeCoreAsync.text"));
                }
            }
            catch (GitHubAppGitException error)
            {
                if (error.Key == "workSummary.gitHub.pushFailed") throw new ApiException(error.LocalizedMessage());
                return GitHubToolError(error.LocalizedMessage());
            }
            catch (ArgumentException) { return GitHubToolError(TextResources.Get("workSummary.gitHub.invalidInput")); }
            catch (UnauthorizedAccessException) { return GitHubToolError(TextResources.Get("workSummary.gitHub.invalidWorktree")); }
            finally { gate.Release(); }
        }

        async Task<string> ConnectGitHubForAgentAsync(GitHubAgentLocal local, bool openBrowser, CancellationToken token, Action begin)
        {
            string repository = github == null ? "" : github.Repository;
            string origin = local.CurrentRepository(token);
            if (repository.Length == 0) repository = origin;
            if (github != null && github.CanCreateRepository)
            {
                try
                {
                    string login = await github.GetAuthenticatedLoginAsync(token).ConfigureAwait(false);
                    github.UseRepository(repository);
                    githubConnected = repository.Length != 0;
                    return new JavaScriptSerializer().Serialize(new { status = "connected", login, repository });
                }
                catch (ApiException) { }
            }

            var cli = GitHubCliTransport.Find();
            if (cli == null) return GitHubToolError(TextResources.Get("gitHubApp.cliUnavailable"));
            string account;
            using (var candidate = GitHubClient.ForCli(repository, "", cli))
            {
                try { account = await candidate.GetAuthenticatedLoginAsync(token).ConfigureAwait(false); }
                catch (ApiException)
                {
                    if (openBrowser && !githubLoginStarted)
                    {
                        begin();
                        cli.OpenLogin();
                        githubLoginStarted = true;
                    }
                    return new JavaScriptSerializer().Serialize(new {
                        status = "authorization_required", message = TextResources.Get("gitHubApp.cliLoginPending")
                    });
                }
            }
            ConfigureGitHub(GitHubClient.ForCli(repository, account, cli));
            githubLoginStarted = false;
            return new JavaScriptSerializer().Serialize(new { status = "connected", login = account, repository });
        }

        static string GitHubToolError(string message)
        {
            return TextResources.Get("webSearch.errorPrefix") + message;
        }
    }
}
