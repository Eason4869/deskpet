using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class AboutWindow : Window
{
    internal sealed record MacAsset(string Name, Uri DownloadUrl, bool Preview);
    private static readonly Regex PackageName = new(@"^DeskPet-macOS-(?<version>[0-9]+\.[0-9]+\.[0-9]+)-(?<cpu>arm64|x86_64)(?<preview>-preview)?\.(?<format>dmg|zip)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static string? MacArchitecture(string architecture) => architecture.ToLowerInvariant() switch { "x64" or "x86_64" => "x86_64", "arm64" => "arm64", _ => null };
    internal static bool MatchesArchitecture(string name, string architecture)
    {
        var match = PackageName.Match(name);
        return match.Success && MacArchitecture(architecture) is { } cpu && string.Equals(match.Groups["cpu"].Value, cpu, StringComparison.OrdinalIgnoreCase);
    }
    internal static MacAsset? SelectMacAsset(JsonElement release, string repository, string architecture)
    {
        var cpu = MacArchitecture(architecture);
        var repo = repository.Split('/');
        if (cpu == null || repo.Length != 2 || repo.Any(string.IsNullOrWhiteSpace) || release.ValueKind != JsonValueKind.Object || !release.TryGetProperty("tag_name", out var tagValue) || tagValue.ValueKind != JsonValueKind.String || !release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;
        var tag = tagValue.GetString()!;
        if (!Regex.IsMatch(tag, @"^[vV]?[0-9]+\.[0-9]+\.[0-9]+$", RegexOptions.CultureInvariant) || !Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        MacAsset? selected = null; var bestScore = -1;
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object || !asset.TryGetProperty("name", out var nameValue) || nameValue.ValueKind != JsonValueKind.String || !asset.TryGetProperty("browser_download_url", out var urlValue) || urlValue.ValueKind != JsonValueKind.String || !asset.TryGetProperty("size", out var sizeValue) || sizeValue.ValueKind != JsonValueKind.Number || !sizeValue.TryGetInt64(out var size) || size <= 0 || size > 1024L * 1024 * 1024) continue;
            var name = nameValue.GetString()!;
            var match = PackageName.Match(name);
            if (!match.Success || !string.Equals(match.Groups["cpu"].Value, cpu, StringComparison.OrdinalIgnoreCase) || match.Groups["version"].Value != version.ToString(3)) continue;
            if (!Uri.TryCreate(urlValue.GetString(), UriKind.Absolute, out var url) || url.Scheme != "https" || !string.Equals(url.Host, "github.com", StringComparison.OrdinalIgnoreCase) || !url.IsDefaultPort || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0) continue;
            var segments = Uri.UnescapeDataString(url.AbsolutePath).Split('/');
            if (segments.Length != 7 || !string.Equals(segments[1], repo[0], StringComparison.OrdinalIgnoreCase) || !string.Equals(segments[2], repo[1], StringComparison.OrdinalIgnoreCase) || segments[3] != "releases" || segments[4] != "download" || segments[5] != tag || segments[6] != name) continue;
            var preview = match.Groups["preview"].Success;
            var score = (match.Groups["format"].Value.Equals("dmg", StringComparison.OrdinalIgnoreCase) ? 100 : 0) + (preview ? 0 : 10);
            if (score <= bestScore) continue;
            bestScore = score; selected = new MacAsset(name, url, preview);
        }
        return selected;
    }
    private readonly DesktopController owner;
    private readonly TextBlock notice = Ui.Text("");
    private readonly TextBox repository;
    private readonly CancellationTokenSource cancel = new();
    private Uri? releaseDownload;
    private readonly Button download;
    public AboutWindow(DesktopController controller)
    {
        owner = controller; Title = TextResources.Get("aboutWindow.aboutWindow.title"); Width = 570; Height = 490;
        var version = typeof(App).Assembly.GetName().Version;
        repository = Ui.Input(owner.Settings.RepositoryUrl);
        download = Ui.AsyncButton(TextResources.Get("aboutWindow.aboutWindow.asyncButton"), async () => { if (releaseDownload != null) await Launcher.LaunchUriAsync(releaseDownload); }, owner.Error); download.IsEnabled = false;
        repository.TextChanged += (_, _) => { releaseDownload = null; download.IsEnabled = false; };
        Content = new Border { Padding = new Thickness(24), Child = Ui.Column(Ui.Text(TextResources.Format("aboutWindow.aboutWindow.template",ReleaseInfo.DisplayVersion), 24), Ui.Text(TextResources.Get("aboutWindow.aboutWindow.text")), Ui.Field(TextResources.Get("aboutWindow.aboutWindow.field"), repository), Ui.AsyncButton(TextResources.Get("aboutWindow.aboutWindow.asyncButton.alternativeA"), CheckAsync, owner.Error), download, notice, Ui.Text(TextResources.Get("aboutWindow.aboutWindow.text.alternativeA")), Ui.Button(TextResources.Get("common.close"), Close)) };
        Closed += (_, _) => { cancel.Cancel(); cancel.Dispose(); };
    }
    private async Task CheckAsync()
    {
        var token = cancel.Token;
        var requestedRepository = repository.Text?.Trim() ?? "";
        var repo = GitHubUpdates.Repository(repository.Text ?? "");
        owner.Settings.RepositoryUrl = repository.Text!.Trim(); owner.Save();
        notice.Text = TextResources.Get("aboutWindow.checkAsync.notice"); releaseDownload = null; download.IsEnabled = false;
        using var client = owner.Settings.UpdateUseProxy ? ProxyConfiguration.CreateHttpClient(owner.Settings.UpdateProxyAddress, true) : new HttpClient(new HttpClientHandler { UseProxy = false });
        client.Timeout = TimeSpan.FromSeconds(30); client.DefaultRequestHeaders.UserAgent.ParseAdd("DeskPet/" + typeof(App).Assembly.GetName().Version?.ToString(3));
        using var response = await client.GetAsync("https://api.github.com/repos/" + repo + "/releases/latest", token);
        if (!response.IsSuccessStatusCode) throw new ApiException(TextResources.Format("aboutWindow.checkAsync.template",(int)response.StatusCode));
        var data = await response.Content.ReadAsByteArrayAsync(token);
        if (data.Length > 1024 * 1024) throw new ApiException(TextResources.Get("aboutWindow.checkAsync.text"));
        using var json = JsonDocument.Parse(data); var root = json.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        token.ThrowIfCancellationRequested();
        if (repository.Text?.Trim() != requestedRepository) { notice.Text = TextResources.Get("aboutWindow.checkAsync.notice.alternativeA"); return; }
        var next = Version.TryParse(tag.TrimStart('v', 'V'), out var v) ? v : null;
        var current = typeof(App).Assembly.GetName().Version!;
        var newer = next != null && new Version(next.Major, next.Minor, Math.Max(0, next.Build)) > new Version(current.Major, current.Minor, current.Build);
        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();
        var selected = SelectMacAsset(root, repo, arch);
        releaseDownload = selected?.DownloadUrl; download.IsEnabled = selected != null;
        notice.Text = (newer ? TextResources.Get("aboutWindow.checkAsync.notice.alternativeB") : TextResources.Get("aboutWindow.checkAsync.notice.alternativeC")) + tag + "。\n" + (selected != null ? TextResources.Get("aboutWindow.checkAsync.notice.alternativeD") + selected.Name + (selected.Preview ? TextResources.Get("aboutWindow.checkAsync.notice.alternativeE") : "") : TextResources.Get("aboutWindow.checkAsync.notice.alternativeF"));
    }
}
