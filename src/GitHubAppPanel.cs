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
        readonly PasswordBox token = new PasswordBox { MinHeight = 38, Padding = new Thickness(10, 7, 10, 7),
            Background = ThemeManager.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), MaxLength = 512 };
        readonly TextBlock connection = Ui.Text("", 12, Ui.Muted), status = Ui.Text("", 12, Ui.Muted);
        readonly Button connect, disconnect, saveRepository;
        CancellationTokenSource pending;
        string login;
        bool disposed;

        internal GitHubAppPanel(PetWindow pet)
        {
            this.pet = pet;
            Name = "GitHubAppPanel";
            LocalizedUi.Automation(this, () => TextResources.Get("gitHubApp.connection"));
            var body = new StackPanel();
            body.Children.Add(AppCardHeader.GitHub());
            connection.Name = "GitHubAppConnectionStatus";
            connection.Margin = new Thickness(0, 5, 0, 10);
            body.Children.Add(connection);

            var details = new StackPanel { Name = "GitHubAppSettings", Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
            Func<string> manageLabel = () => details.Visibility == Visibility.Visible ? TextResources.Get("gitHubApp.hideSettings") : TextResources.Get("gitHubApp.manage");
            var manage = LocalizedUi.Button(manageLabel);
            manage.Name = "ManageGitHubApp";
            manage.Click += delegate {
                details.Visibility = details.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                LocalizedUi.Set(manage, "Content", manageLabel);
                LocalizedUi.Automation(manage, manageLabel);
            };
            disconnect = LocalizedUi.Button(() => TextResources.Get("gitHubApp.disconnect"));
            disconnect.Name = "DisconnectGitHubApp";
            disconnect.Click += async delegate { await DisconnectAsync(); };
            var actions = new WrapPanel();
            actions.Children.Add(manage);
            actions.Children.Add(disconnect);
            body.Children.Add(actions);

            AddHint(details, () => TextResources.Get("gitHubApp.hint"), 0);
            token.Name = "GitHubAppToken";
            AddField(details, () => TextResources.Get("gitHubApp.token"), token);
            AddHint(details, () => TextResources.Get("gitHubApp.tokenHint"), 8);
            var createToken = LocalizedUi.Button(() => TextResources.Get("gitHubApp.tokenPage"));
            createToken.Name = "OpenGitHubTokenSettings";
            createToken.Click += delegate { MarkdownLinkMenu.OpenExternally(new Uri("https://github.com/settings/personal-access-tokens/new")); };
            connect = LocalizedUi.Button(() => TextResources.Get("gitHubApp.connect"), true);
            connect.Name = "ConnectGitHubApp";
            connect.Click += async delegate { await ConnectAsync(); };
            var accountActions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            accountActions.Children.Add(connect);
            accountActions.Children.Add(createToken);
            details.Children.Add(accountActions);

            repository.Name = "GitHubAppDefaultRepository";
            repository.MaxLength = 300;
            repository.Text = pet.Settings.GitHubRepository ?? "";
            AddField(details, () => TextResources.Get("gitHubApp.repository"), repository);
            saveRepository = LocalizedUi.Button(() => TextResources.Get("gitHubApp.saveRepository"));
            saveRepository.HorizontalAlignment = HorizontalAlignment.Left;
            saveRepository.Margin = new Thickness(0, 10, 0, 0);
            saveRepository.Name = "SaveGitHubAppRepository";
            saveRepository.Click += async delegate { await SaveRepositoryAsync(); };
            details.Children.Add(saveRepository);
            body.Children.Add(details);

            status.Name = "GitHubAppStatus";
            status.Visibility = Visibility.Collapsed;
            status.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(status);
            Content = new Border { Child = body, BorderBrush = Ui.Line, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 10) };
            token.PasswordChanged += delegate { Refresh(); };
            Refresh();
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

        void Refresh()
        {
            bool connected = GitHubAppConnection.ReadToken(pet.Settings).Length > 0;
            LocalizedUi.Set(connection, "Text", () => !connected ? TextResources.Get("gitHubApp.disconnected") :
                String.IsNullOrEmpty(login) ? TextResources.Get("gitHubApp.connected") : TextResources.Format("gitHubApp.connectedAs", login));
            disconnect.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            using (var value = token.SecurePassword) connect.IsEnabled = value.Length > 0;
        }

        async Task ConnectAsync()
        {
            string value = token.Password.Trim();
            if (!Regex.IsMatch(value, @"\Agithub_pat_[A-Za-z0-9_]{20,500}\z"))
            {
                ShowStatus(() => TextResources.Get("gitHubApp.invalidToken"));
                return;
            }
            string confirmedLogin = null;
            await RunAsync("connectGitHub", async cancel => {
                using (var client = GitHubClient.ForApp(null, value))
                    confirmedLogin = await client.GetAuthenticatedLoginAsync(cancel);
                cancel.ThrowIfCancellationRequested();
                var connectionSettings = new ConnectionSettings { BaseUrl = GitHubClient.PublicApi, Protocol = "github", ApiKey = value };
                if (!await pet.PersistSettingsAsync(settings => settings.GitHub = connectionSettings.Copy())) return false;
                login = confirmedLogin;
                token.Clear();
                return true;
            });
        }

        async Task DisconnectAsync()
        {
            await RunAsync("disconnectGitHub", async cancel => {
                if (!await pet.PersistSettingsAsync(settings => settings.GitHub = new ConnectionSettings { BaseUrl = GitHubClient.PublicApi, Protocol = "github" })) return false;
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

        async Task RunAsync(string action, Func<CancellationToken, Task<bool>> work)
        {
            if (pending != null || disposed) return;
            using (var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            {
                pending = cancel;
                status.Visibility = Visibility.Collapsed;
                try
                {
                    bool saved = await pet.RunSettingsEditAsync(this, () => work(cancel.Token), action);
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
            disposed = true;
            if (pending != null) pending.Cancel();
            token.Clear();
        }
    }

    internal static class GitHubAppConnection
    {
        internal static string ReadToken(PetSettings settings)
        {
            var github = settings.GitHub;
            if (github == null || !String.Equals((github.BaseUrl ?? "").TrimEnd('/'), GitHubClient.PublicApi.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) return "";
            return github.ApiKey;
        }

        internal static bool IsOperationError(Exception error)
        {
            return error is ApiException || error is HttpRequestException || error is IOException || error is UnauthorizedAccessException ||
                error is ArgumentException || error is InvalidOperationException || error is FormatException || error is Win32Exception ||
                error is CryptographicException || error is NotSupportedException || error is System.Security.SecurityException;
        }
    }
}
