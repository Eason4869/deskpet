using System;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Dafeiyu
{
    internal sealed class GitHubAppPanel : UserControl, IDisposable
    {
        readonly PetWindow pet;
        readonly TextBox repository = Ui.Field();
        readonly TextBox pushProxyAddress = Ui.Field();
        readonly PasswordBox token = new PasswordBox { MinHeight = 38, Padding = new Thickness(10, 7, 10, 7),
            Background = ThemeManager.FieldSurface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), MaxLength = 512 };
        readonly TextBlock connection = Ui.Text("", 12, Ui.Muted), dialogConnection = Ui.Text("", 12, Ui.Muted);
        readonly TextBlock status = Ui.Text("", 12, Ui.Muted);
        readonly StackPanel details = new StackPanel { Name = "GitHubAppSettings" };
        readonly Button manage, connect, disconnect;
        readonly CheckBox usePushProxy;
        AppManagementWindow management;
        CancellationTokenSource pending;
        string login;
        bool disposed;

        internal GitHubAppPanel(PetWindow pet)
        {
            this.pet = pet;
            Name = "GitHubAppPanel";
            LocalizedUi.Automation(this, () => TextResources.Get("gitHubApp.connection"));
            connection.Name = "GitHubAppConnectionStatus";
            Content = AppManagementWindow.CreateCard("GitHub", AppCardHeader.GitHub(), connection, out manage);
            manage.Click += delegate { OpenManagement(); };

            var connectionSection = AppManagementWindow.AddSection(details, () => TextResources.Get("apps.management.connection"));
            dialogConnection.Name = "GitHubAppManagementStatus";
            connectionSection.Children.Add(dialogConnection);
            var web = AppManagementWindow.WebLink(() => TextResources.Get("gitHubApp.management.login"),
                "OpenGitHubWeb", "https://github.com/login");
            disconnect = LocalizedUi.Button(() => TextResources.Get("gitHubApp.disconnect"));
            disconnect.Name = "DisconnectGitHubApp";
            disconnect.Click += async delegate { await DisconnectAsync(); };
            AppManagementWindow.AddActions(connectionSection, web, disconnect);

            var auth = AppManagementWindow.AddSection(details, () => TextResources.Get("apps.management.authorization"));
            AddHint(auth, () => TextResources.Get("gitHubApp.management.cliHint"), 0);
            var connectCli = LocalizedUi.Button(() => TextResources.Get("gitHubApp.cliConnect"), true);
            connectCli.Name = "ConnectGitHubCli";
            connectCli.Click += async delegate { await ConnectCliAsync(); };
            var loginCli = LocalizedUi.Button(() => TextResources.Get("gitHubApp.cliLogin"));
            loginCli.Name = "AuthorizeGitHubCli";
            loginCli.Click += delegate { OpenCliLogin(); };
            AppManagementWindow.AddActions(auth, connectCli, loginCli);
            auth.Children.Add(new Border { Height = 1, Background = ThemeManager.CardBorder, Margin = new Thickness(0, 7, 0, 0) });
            token.Name = "GitHubAppToken";
            AddField(auth, () => TextResources.Get("gitHubApp.token"), token);
            AddHint(auth, () => TextResources.Get("gitHubApp.hint"), 5);
            AddHint(auth, () => TextResources.Get("gitHubApp.tokenHint"), 4);
            var createToken = AppManagementWindow.WebLink(() => TextResources.Get("gitHubApp.tokenPage"),
                "OpenGitHubTokenSettings", "https://github.com/settings/personal-access-tokens/new");
            connect = LocalizedUi.Button(() => TextResources.Get("gitHubApp.connect"));
            connect.Name = "ConnectGitHubApp";
            connect.Click += async delegate { await ConnectAsync(); };
            AppManagementWindow.AddActions(auth, connect, createToken);

            var repositorySection = AppManagementWindow.AddSection(details, () => TextResources.Get("gitHubApp.repository"));
            repository.Name = "GitHubAppDefaultRepository";
            repository.MaxLength = 300;
            repository.Text = pet.Settings.GitHubRepository ?? "";
            LocalizedUi.Automation(repository, () => TextResources.Get("gitHubApp.repository"));
            repositorySection.Children.Add(repository);
            var saveRepository = LocalizedUi.Button(() => TextResources.Get("gitHubApp.saveRepository"));
            saveRepository.Name = "SaveGitHubAppRepository";
            saveRepository.Click += async delegate { await SaveRepositoryAsync(); };
            AppManagementWindow.AddActions(repositorySection, saveRepository);

            var pushNetwork = AppManagementWindow.AddSection(details, () => TextResources.Get("gitHubApp.management.pushNetwork"));
            pushProxyAddress.Name = "GitHubPushProxyAddress";
            pushProxyAddress.MaxLength = 300;
            usePushProxy = SettingsSurface.Toggle(() => TextResources.Get("gitHubApp.management.pushProxy"),
                pet.Settings.GitHub != null && pet.Settings.GitHub.UseProxy, enabled => pushProxyAddress.IsEnabled = enabled);
            usePushProxy.Name = "UseGitHubPushProxy";
            pushNetwork.Children.Add(usePushProxy);
            AddField(pushNetwork, () => TextResources.Get("gitHubApp.management.pushProxyAddress"), pushProxyAddress);
            var saveNetwork = LocalizedUi.Button(() => TextResources.Get("common.save"));
            saveNetwork.Name = "SaveGitHubPushNetwork";
            saveNetwork.Click += async delegate { await SavePushNetworkAsync(); };
            AppManagementWindow.AddActions(pushNetwork, saveNetwork);

            status.Name = "GitHubAppStatus";
            status.Visibility = Visibility.Collapsed;
            token.PasswordChanged += delegate { Refresh(); };
            Refresh();
        }

        void OpenManagement()
        {
            if (disposed || management != null) return;
            Refresh();
            var current = pet.Settings.GitHub;
            usePushProxy.IsChecked = current != null && current.UseProxy;
            pushProxyAddress.IsEnabled = usePushProxy.IsChecked == true;
            pushProxyAddress.Text = ProxyConfiguration.DisplayAddress(current == null ? null : current.ProxyAddress);
            var header = AppCardHeader.GitHub(() => TextResources.Get("gitHubApp.management.description"));
            management = new AppManagementWindow(Window.GetWindow(this), "GitHub", header, details, status, 640);
            try { management.ShowDialog(); }
            finally
            {
                management = null;
                if (pending != null) pending.Cancel();
                token.Clear();
                if (!disposed && IsVisible) manage.Focus();
            }
        }

        static void AddHint(Panel panel, Func<string> label, int top)
        {
            var hint = LocalizedUi.Text(label, 12, Ui.Muted);
            hint.Margin = new Thickness(0, top, 0, 0);
            panel.Children.Add(hint);
        }

        static void AddField(Panel panel, Func<string> label, FrameworkElement field)
        {
            var caption = LocalizedUi.Text(label, 12, Ui.PageInk);
            caption.Margin = new Thickness(0, panel.Children.Count == 0 ? 0 : 14, 0, 7);
            panel.Children.Add(caption);
            LocalizedUi.Automation(field, label);
            panel.Children.Add(field);
        }

        internal void Refresh()
        {
            if (disposed) return;
            bool connected = GitHubAppConnection.IsConnected(pet.Settings);
            string account = GitHubClient.UsesCli(pet.Settings) ? pet.Settings.GitHubCliAccount : login;
            Func<string> label = () => !connected ? TextResources.Get("gitHubApp.disconnected") :
                String.IsNullOrEmpty(account) ? TextResources.Get("gitHubApp.connected") : TextResources.Format("gitHubApp.connectedAs", account);
            LocalizedUi.Set(connection, "Text", label);
            LocalizedUi.Set(dialogConnection, "Text", label);
            disconnect.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            using (var value = token.SecurePassword) connect.IsEnabled = value.Length > 0 ||
                (GitHubClient.UsesCli(pet.Settings) && GitHubAppConnection.ReadToken(pet.Settings).Length > 0);
        }

        async Task ConnectAsync()
        {
            string value = token.Password.Trim();
            if (value.Length == 0 && GitHubClient.UsesCli(pet.Settings)) value = GitHubAppConnection.ReadToken(pet.Settings);
            if (!Regex.IsMatch(value, @"\Agithub_pat_[A-Za-z0-9_]{20,500}\z"))
            {
                ShowStatus(() => TextResources.Get("gitHubApp.invalidToken"));
                return;
            }
            string confirmedLogin = null;
            await RunAsync("connectGitHub", async cancel => {
                using (var client = GitHubClient.ForApp(null, value, proxyAddress: GitHubClient.ConfiguredProxyAddress(pet.Settings.GitHub)))
                    confirmedLogin = await client.GetAuthenticatedLoginAsync(cancel);
                cancel.ThrowIfCancellationRequested();
                if (!await pet.PersistSettingsAsync(settings => {
                    var next = PublicConnection(settings.GitHub, "github");
                    next.ApiKey = value;
                    settings.GitHub = next;
                    settings.GitHubCliAccount = null;
                })) return false;
                login = confirmedLogin;
                token.Clear();
                return true;
            });
        }

        async Task ConnectCliAsync()
        {
            var cli = GitHubCliTransport.Find();
            if (cli == null) { ShowStatus(() => TextResources.Get("gitHubApp.cliUnavailable")); return; }
            await RunAsync("connectGitHubCli", async cancel => {
                string account;
                using (var client = GitHubClient.ForCli(null, "", cli, GitHubClient.ConfiguredProxyAddress(pet.Settings.GitHub)))
                    account = await client.GetAuthenticatedLoginAsync(cancel);
                cancel.ThrowIfCancellationRequested();
                return await pet.PersistSettingsAsync(settings => {
                    var previous = settings.GitHub;
                    var connectionSettings = PublicConnection(previous, GitHubClient.CliProtocol);
                    if (previous != null && GitHubClient.IsPublicEndpoint(previous.BaseUrl))
                        connectionSettings.EncryptedKey = previous.EncryptedKey;
                    settings.GitHub = connectionSettings;
                    settings.GitHubCliAccount = account;
                });
            });
        }

        void OpenCliLogin()
        {
            if (pending != null || disposed) return;
            var cli = GitHubCliTransport.Find();
            if (cli == null) { ShowStatus(() => TextResources.Get("gitHubApp.cliUnavailable")); return; }
            try
            {
                cli.OpenLogin(GitHubClient.ConfiguredProxyAddress(pet.Settings.GitHub));
                ShowStatus(() => TextResources.Get("gitHubApp.cliLoginPending"));
            }
            catch (Exception error)
            {
                if (!GitHubAppConnection.IsOperationError(error)) throw;
                ShowStatus(() => TextResources.Get("gitHubApp.failed"));
            }
        }

        async Task DisconnectAsync()
        {
            await RunAsync("disconnectGitHub", async cancel => {
                if (!await pet.PersistSettingsAsync(settings => {
                    settings.GitHub = PublicConnection(settings.GitHub, "github");
                    settings.GitHubCliAccount = null;
                })) return false;
                login = null;
                token.Clear();
                return true;
            });
        }

        async Task SaveRepositoryAsync()
        {
            string normalized = WorkSummaryRepository.Normalize(repository.Text);
            if (normalized.Length == 0 && !String.IsNullOrWhiteSpace(repository.Text))
            {
                ShowStatus(() => TextResources.Get("gitHubUpdates.repository.text"));
                return;
            }
            await RunAsync("saveGitHubRepository", async cancel => {
                if (!await pet.PersistSettingsAsync(settings => settings.GitHubRepository = normalized)) return false;
                repository.Text = normalized;
                return true;
            });
        }

        static ConnectionSettings PublicConnection(ConnectionSettings previous, string protocol)
        {
            return new ConnectionSettings
            {
                BaseUrl = GitHubClient.PublicApi, Protocol = protocol,
                UseProxy = previous != null && previous.UseProxy,
                ProxyAddress = ProxyConfiguration.Normalize(previous == null ? null : previous.ProxyAddress)
            };
        }

        async Task SavePushNetworkAsync()
        {
            bool enabled = usePushProxy.IsChecked == true;
            string address = null;
            if (enabled)
            {
                try { address = ProxyConfiguration.Endpoint(pushProxyAddress.Text).AbsoluteUri; }
                catch (InvalidOperationException error) { ShowStatus(() => error.Message); return; }
            }
            await RunAsync("saveGitHubPushNetwork", async cancel => {
                if (!await pet.PersistSettingsAsync(settings => {
                    if (settings.GitHub == null) settings.GitHub = PublicConnection(null, "github");
                    settings.GitHub.UseProxy = enabled;
                    if (address != null) settings.GitHub.ProxyAddress = address;
                })) return false;
                if (address != null) pushProxyAddress.Text = address;
                return true;
            });
        }

        async Task RunAsync(string action, Func<CancellationToken, Task<bool>> work)
        {
            if (pending != null || disposed) return;
            using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            {
                pending = cancel;
                status.Visibility = Visibility.Collapsed;
                try
                {
                    bool saved = await pet.RunSettingsEditAsync(details, () => work(cancel.Token), action);
                    if (!disposed) ShowStatus(() => saved ? TextResources.Get("gitHubApp.saved") : TextResources.Get("gitHubApp.saveFailed"));
                }
                catch (OperationCanceledException) { if (!disposed) ShowStatus(() => TextResources.Get("gitHubApp.timeout")); }
                catch (Exception error)
                {
                    if (!GitHubAppConnection.IsOperationError(error)) throw;
                    if (!disposed) ShowStatus(() => TextResources.Get("gitHubApp.failed"));
                }
                finally { pending = null; if (!disposed) Refresh(); }
            }
        }

        void ShowStatus(Func<string> label)
        {
            LocalizedUi.Set(status, "Text", label);
            status.Visibility = Visibility.Visible;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (pending != null) pending.Cancel();
            if (management != null) management.Close();
            else token.Clear();
        }
    }

    internal static class GitHubAppConnection
    {
        internal static string ReadToken(PetSettings settings)
        {
            var github = settings.GitHub;
            if (github == null || !GitHubClient.IsPublicEndpoint(github.BaseUrl)) return "";
            return github.ApiKey;
        }

        internal static bool IsConnected(PetSettings settings)
        {
            return GitHubClient.UsesCli(settings) ? GitHubCliTransport.Find() != null : ReadToken(settings).Length != 0;
        }

        internal static bool SameConnection(PetSettings previous, PetSettings current)
        {
            if (previous.GitHub == null || current.GitHub == null) return false;
            return previous.GitHub.Protocol == current.GitHub.Protocol && previous.GitHub.BaseUrl == current.GitHub.BaseUrl &&
                previous.GitHub.EncryptedKey == current.GitHub.EncryptedKey && previous.GitHubCliAccount == current.GitHubCliAccount;
        }

        internal static bool IsOperationError(Exception error)
        {
            return error is ApiException || error is HttpRequestException || error is IOException || error is UnauthorizedAccessException ||
                error is ArgumentException || error is InvalidOperationException || error is FormatException || error is Win32Exception ||
                error is CryptographicException || error is NotSupportedException || error is System.Security.SecurityException;
        }
    }
}
