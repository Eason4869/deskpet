using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Dafeiyu.Maintenance;

namespace Dafeiyu
{
    public sealed class GmailAppStatus
    {
        public bool IsConfigured { get; internal set; }
        public bool IsConnected { get; internal set; }
        public bool HasLocalCredentials { get; internal set; }
        public string EmailAddress { get; internal set; }
        public string ErrorMessage { get; internal set; }
        internal long ConnectionVersion { get; set; }
    }

    public sealed class GmailAppDisconnectResult
    {
        public bool WasConnected { get; internal set; }
        public bool Revoked { get; internal set; }
    }

    public sealed class GmailAppException : Exception
    {
        internal GmailAppException(string message) : base(message) { }
    }

    public sealed class GmailAppService : IDisposable
    {
        const string TokenEndpoint = "https://oauth2.googleapis.com/token";
        const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";
        const string GmailEndpoint = "https://gmail.googleapis.com/gmail/v1/users/me/";
        public const int MaxPageSize = 25;
        static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
        static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(60);
        static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(3);
        readonly string manifest;
        readonly GmailAppTokenStore store;
        readonly HttpClient http;
        readonly Action<Uri> openBrowser;
        readonly DataMaintenanceGate maintenance;
        readonly object lifecycle = new object();
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly SemaphoreSlim operations = new SemaphoreSlim(1, 1);
        long connectionVersion = 1;
        bool disposed;

        internal event Action ConnectionChanged;

        public GmailAppService(string dataDirectory, DataMaintenanceGate maintenance = null)
            : this(dataDirectory, null, new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false },
                GmailAppOAuth.OpenBrowser, maintenance) { }

        internal GmailAppService(string dataDirectory, string manifest, HttpMessageHandler handler, Action<Uri> openBrowser, DataMaintenanceGate maintenance = null)
        {
            this.manifest = manifest;
            this.openBrowser = openBrowser;
            this.maintenance = maintenance;
            store = new GmailAppTokenStore(dataDirectory);
            http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = GmailAppJson.MaximumResponseBytes };
        }

        public GmailAppStatus GetStatus()
        {
            var status = new GmailAppStatus();
            GmailAppOAuthClient client = null;
            try { client = RequireClient(); }
            catch (GmailAppException error) { status.ErrorMessage = error.Message; }
            status.IsConfigured = client != null;
            try
            {
                using (EnterActivity())
                lock (lifecycle)
                {
                    status.HasLocalCredentials = store.Exists;
                    var credentials = store.Read();
                    if (credentials == null || client == null) return status;
                    status.HasLocalCredentials = true;
                    if (credentials.ClientId != client.Id) status.ErrorMessage = TextResources.Get("gmailApp.clientChanged");
                    else MarkConnected(status, credentials);
                }
            }
            catch (GmailAppException error) { if (status.ErrorMessage == null) status.ErrorMessage = error.Message; }
            return status;
        }

        public Task<GmailAppStatus> ConnectAsync(CancellationToken cancellationToken)
        {
            return RunAsync(SignInTimeout, cancellationToken, ConnectCoreAsync);
        }

        async Task<GmailAppStatus> ConnectCoreAsync(CancellationToken cancel)
        {
            var client = RequireClient();
            GmailAppCredentials previous;
            try { previous = store.Read(); }
            catch (GmailAppException) { previous = null; }
            var status = new GmailAppStatus { IsConfigured = true };
            if (previous != null && previous.ClientId == client.Id) return MarkConnected(status, previous);

            var authorization = await GmailAppOAuth.AuthorizeAsync(client, openBrowser, cancel).ConfigureAwait(false);
            var data = await RequestAsync(TokenEndpoint, null, new[]
            {
                GmailAppOAuth.Pair("grant_type", "authorization_code"), GmailAppOAuth.Pair("code", authorization.Code),
                GmailAppOAuth.Pair("code_verifier", authorization.Verifier), GmailAppOAuth.Pair("redirect_uri", authorization.RedirectUri),
                GmailAppOAuth.Pair("client_id", client.Id), GmailAppOAuth.Pair("client_secret", client.Secret)
            }, cancel).ConfigureAwait(false);
            var credentials = ReadTokens(data, client.Id, null);
            ExceptionDispatchInfo failure;
            try
            {
                var profile = await RequestAsync(GmailEndpoint + "profile?fields=emailAddress", credentials.AccessToken, null, cancel).ConfigureAwait(false);
                credentials.EmailAddress = GmailAppJson.String(profile, "emailAddress");
                if (!GmailAppJson.IsEmail(credentials.EmailAddress)) throw InvalidResponse();
                cancel.ThrowIfCancellationRequested();
                ChangeConnection(credentials);
                return MarkConnected(status, credentials);
            }
            catch (Exception error)
            {
                if (!(error is GmailAppException || error is GmailHttpException || error is OperationCanceledException)) throw;
                failure = ExceptionDispatchInfo.Capture(error);
            }
            using (var rollback = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                await TryRevokeAsync(credentials.RefreshToken, rollback.Token).ConfigureAwait(false);
            failure.Throw();
            return null;
        }

        public Task<GmailAppDisconnectResult> DisconnectAsync(CancellationToken cancellationToken)
        {
            return RunAsync(RequestTimeout, cancellationToken, async cancel =>
            {
                var result = new GmailAppDisconnectResult { WasConnected = store.Exists };
                GmailAppCredentials credentials;
                try { credentials = store.Read(); }
                catch (GmailAppException) { credentials = null; }
                cancel.ThrowIfCancellationRequested();
                ChangeConnection(null);
                if (credentials == null) return result;
                result.WasConnected = true;
                result.Revoked = await TryRevokeAsync(credentials.RefreshToken, cancel).ConfigureAwait(false);
                return result;
            });
        }

        async Task<bool> TryRevokeAsync(string refreshToken, CancellationToken cancel)
        {
            try
            {
                await RequestAsync(RevokeEndpoint, null, new[] { GmailAppOAuth.Pair("token", refreshToken) }, cancel).ConfigureAwait(false);
                return true;
            }
            catch (GmailHttpException) { return false; }
            catch (GmailAppException) { return false; }
            catch (OperationCanceledException) { return false; }
            catch (ObjectDisposedException) { if (!IsDisposed()) throw; return false; }
        }

        public Task<GmailAppMessagePage> ListMessagesAsync(string query, string pageToken, int pageSize, CancellationToken cancellationToken)
        {
            return ListMessagesAsync(query, pageToken, pageSize, cancellationToken, null);
        }

        internal Task<GmailAppMessagePage> ListMessagesAsync(string query, string pageToken, int pageSize, CancellationToken cancellationToken, long? expectedConnection)
        {
            return RunAsync(ReadTimeout, cancellationToken, async cancel =>
            {
                VerifyConnection(expectedConnection);
                query = (query ?? "").Trim();
                pageToken = pageToken ?? "";
                if (query.Length > 1000 || (pageToken.Length != 0 && !IsPageToken(pageToken)) || pageSize < 1 || pageSize > MaxPageSize)
                    throw new GmailAppException(TextResources.Get("gmailApp.invalidSearch"));
                var fields = new List<KeyValuePair<string, string>>
                {
                    GmailAppOAuth.Pair("maxResults", pageSize.ToString(CultureInfo.InvariantCulture)), GmailAppOAuth.Pair("fields", "messages(id),nextPageToken")
                };
                if (query.Length != 0) fields.Add(GmailAppOAuth.Pair("q", query));
                if (pageToken.Length != 0) fields.Add(GmailAppOAuth.Pair("pageToken", pageToken));

                return await ReadAsync(async token =>
                {
                    var result = await RequestAsync(GmailEndpoint + "messages?" + GmailAppOAuth.Query(fields), token, null, cancel).ConfigureAwait(false);
                    var ids = GmailAppJson.ArrayField(result, "messages").Select(item => GmailAppJson.String(item as Dictionary<string, object>, "id")).ToArray();
                    var nextPageToken = GmailAppJson.String(result, "nextPageToken");
                    if (ids.Length > pageSize || ids.Any(id => !IsMessageId(id)) || (nextPageToken.Length != 0 && !IsPageToken(nextPageToken))) throw InvalidResponse();

                    var messages = new List<GmailAppMessage>(ids.Length);
                    for (int offset = 0; offset < ids.Length; offset += 5)
                    {
                        var batch = await Task.WhenAll(ids.Skip(offset).Take(5).Select(id => ReadMetadataAsync(id, token, cancel))).ConfigureAwait(false);
                        messages.AddRange(batch.Where(message => message != null));
                    }
                    return new GmailAppMessagePage(messages.ToArray(), nextPageToken);
                }, cancel).ConfigureAwait(false);
            });
        }

        async Task<GmailAppMessage> ReadMetadataAsync(string id, string token, CancellationToken cancel)
        {
            try { return await ReadMessageAsync(id, token, false, cancel).ConfigureAwait(false); }
            catch (GmailHttpException error) { if (error.Status != HttpStatusCode.NotFound) throw; return null; }
        }

        public Task<GmailAppMessage> GetMessageAsync(string messageId, CancellationToken cancellationToken)
        {
            return GetMessageAsync(messageId, cancellationToken, null);
        }

        internal Task<GmailAppMessage> GetMessageAsync(string messageId, CancellationToken cancellationToken, long? expectedConnection)
        {
            return RunAsync(ReadTimeout, cancellationToken, cancel =>
            {
                VerifyConnection(expectedConnection);
                if (!IsMessageId(messageId)) throw new GmailAppException(TextResources.Get("gmailApp.invalidMessage"));
                return ReadAsync(token => ReadMessageAsync(messageId, token, true, cancel), cancel);
            });
        }

        async Task<GmailAppMessage> ReadMessageAsync(string id, string token, bool full, CancellationToken cancel)
        {
            string query = full ? "format=full&fields=id,snippet,internalDate,payload"
                : "format=metadata&metadataHeaders=Subject&metadataHeaders=From&metadataHeaders=To&fields=id,snippet,internalDate,payload(headers)";
            var message = await RequestAsync(GmailEndpoint + "messages/" + id + "?" + query, token, null, cancel).ConfigureAwait(false);
            return GmailAppMessage.Read(id, message, full);
        }

        async Task<T> ReadAsync<T>(Func<string, Task<T>> read, CancellationToken cancel)
        {
            var client = RequireClient();
            var credentials = store.Read();
            if (credentials == null) throw new GmailAppException(TextResources.Get("gmailApp.notConnected"));
            if (credentials.ClientId != client.Id) throw new GmailAppException(TextResources.Get("gmailApp.clientChanged"));
            bool refreshed = credentials.ExpiresUtc <= DateTimeOffset.UtcNow.AddMinutes(1);
            if (refreshed) credentials = await RefreshAsync(client, credentials, cancel).ConfigureAwait(false);
            try { return await read(credentials.AccessToken).ConfigureAwait(false); }
            catch (GmailHttpException error) { if (error.Status != HttpStatusCode.Unauthorized || refreshed) throw; }
            credentials = await RefreshAsync(client, credentials, cancel).ConfigureAwait(false);
            return await read(credentials.AccessToken).ConfigureAwait(false);
        }

        async Task<GmailAppCredentials> RefreshAsync(GmailAppOAuthClient client, GmailAppCredentials current, CancellationToken cancel)
        {
            Dictionary<string, object> data;
            try
            {
                data = await RequestAsync(TokenEndpoint, null, new[]
                {
                    GmailAppOAuth.Pair("grant_type", "refresh_token"), GmailAppOAuth.Pair("refresh_token", current.RefreshToken),
                    GmailAppOAuth.Pair("client_id", client.Id), GmailAppOAuth.Pair("client_secret", client.Secret)
                }, cancel).ConfigureAwait(false);
            }
            catch (GmailHttpException error)
            {
                if (!error.InvalidGrant) throw;
                ChangeConnection(null);
                throw new GmailAppException(TextResources.Get("gmailApp.reconnect"));
            }
            var credentials = ReadTokens(data, client.Id, current);
            // Preserve a rotated refresh token once Google has returned it.
            store.Write(credentials);
            return credentials;
        }

        static GmailAppCredentials ReadTokens(Dictionary<string, object> data, string clientId, GmailAppCredentials current)
        {
            string access = GmailAppJson.String(data, "access_token"), refresh = GmailAppJson.String(data, "refresh_token");
            string scope = GmailAppJson.String(data, "scope");
            int expires = GmailAppJson.Integer(data, "expires_in");
            if (current != null && refresh.Length == 0) refresh = current.RefreshToken;
            if (!GmailAppJson.IsCredential(access) || !GmailAppJson.IsCredential(refresh) || expires <= 0 || expires > 86400
                || !String.Equals(GmailAppJson.String(data, "token_type"), "Bearer", StringComparison.OrdinalIgnoreCase)) throw InvalidResponse();
            if ((scope.Length != 0 || current == null) && !String.Equals(scope.Trim(), GmailAppOAuth.Scope, StringComparison.Ordinal))
                throw new GmailAppException(TextResources.Get("gmailApp.scopeError"));
            return new GmailAppCredentials
            {
                ClientId = clientId, AccessToken = access, RefreshToken = refresh,
                EmailAddress = current == null ? null : current.EmailAddress, ExpiresUtc = DateTimeOffset.UtcNow.AddSeconds(expires)
            };
        }

        async Task<Dictionary<string, object>> RequestAsync(string url, string token, KeyValuePair<string, string>[] form, CancellationToken cancel)
        {
            using (var request = new HttpRequestMessage(form == null ? HttpMethod.Get : HttpMethod.Post, url))
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (form != null) request.Content = new FormUrlEncodedContent(form);
                deadline.CancelAfter(RequestTimeout);
                try
                {
                    using (var response = await http.SendAsync(request, deadline.Token).ConfigureAwait(false))
                    {
                        if (url == RevokeEndpoint && response.IsSuccessStatusCode) return null;
                        if (!response.IsSuccessStatusCode)
                        {
                            bool invalidGrant = false;
                            if (url == TokenEndpoint && response.StatusCode == HttpStatusCode.BadRequest && response.Content != null)
                            {
                                var errorJson = TryParse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                                invalidGrant = GmailAppJson.String(errorJson, "error") == "invalid_grant";
                            }
                            throw new GmailHttpException(response.StatusCode, invalidGrant);
                        }
                        var data = response.Content == null ? null : TryParse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                        if (data == null) throw InvalidResponse();
                        return data;
                    }
                }
                catch (OperationCanceledException)
                {
                    cancel.ThrowIfCancellationRequested();
                    throw new GmailAppException(TextResources.Get("gmailApp.requestTimeout"));
                }
                catch (HttpRequestException)
                {
                    cancel.ThrowIfCancellationRequested();
                    throw new GmailAppException(TextResources.Get("gmailApp.networkError"));
                }
                catch (ObjectDisposedException) { cancel.ThrowIfCancellationRequested(); throw; }
            }
        }

        async Task<T> RunAsync<T>(TimeSpan timeout, CancellationToken cancellationToken, Func<CancellationToken, Task<T>> work)
        {
            CancellationTokenSource deadline;
            lock (lifecycle)
            {
                if (disposed) throw new ObjectDisposedException("GmailAppService");
                deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
            }
            using (deadline)
            {
                deadline.CancelAfter(timeout);
                var cancel = deadline.Token;
                try
                {
                    await operations.WaitAsync(cancel).ConfigureAwait(false);
                    try
                    {
                        using (EnterActivity()) return await work(cancel).ConfigureAwait(false);
                    }
                    finally { operations.Release(); }
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested || IsDisposed()) throw;
                    throw new GmailAppException(timeout == SignInTimeout ? TextResources.Get("gmailApp.authorizationTimeout") : TextResources.Get("gmailApp.requestTimeout"));
                }
                catch (GmailHttpException error)
                {
                    if (error.Status == HttpStatusCode.Unauthorized) throw new GmailAppException(TextResources.Get("gmailApp.reconnect"));
                    if (error.Status == HttpStatusCode.NotFound) throw new GmailAppException(TextResources.Get("gmailApp.messageUnavailable"));
                    throw new GmailAppException(TextResources.Get("gmailApp.requestFailed"));
                }
            }
        }

        IDisposable EnterActivity()
        {
            IDisposable activity = null;
            if (maintenance != null && !maintenance.TryEnterActivity(out activity))
                throw new GmailAppException(TextResources.Get("dataMaintenance.busy"));
            return activity;
        }

        GmailAppOAuthClient RequireClient()
        {
            var client = GmailAppOAuthClient.Read(manifest);
            if (client == null) throw new GmailAppException(TextResources.Get("gmailApp.missingConfig"));
            return client;
        }

        GmailAppStatus MarkConnected(GmailAppStatus status, GmailAppCredentials credentials)
        {
            status.HasLocalCredentials = status.IsConnected = true;
            status.EmailAddress = credentials.EmailAddress;
            lock (lifecycle) status.ConnectionVersion = connectionVersion;
            return status;
        }

        internal bool IsCurrentConnection(long version)
        {
            lock (lifecycle) return !disposed && version != 0 && version == connectionVersion;
        }

        void VerifyConnection(long? expectedConnection)
        {
            if (expectedConnection.HasValue && !IsCurrentConnection(expectedConnection.Value))
                throw new GmailAppException(TextResources.Get("gmailApp.reconnect"));
        }

        void ChangeConnection(GmailAppCredentials credentials)
        {
            lock (lifecycle)
            {
                if (credentials == null) store.Delete();
                else store.Write(credentials);
                connectionVersion++;
            }
            var changed = ConnectionChanged;
            if (changed != null) changed();
        }

        static bool IsPageToken(string token) { return token.Length <= 2048 && GmailAppJson.IsCredential(token); }

        static bool IsMessageId(string id)
        {
            if (String.IsNullOrEmpty(id) || id.Length > 160) return false;
            return id.All(character => (character >= '0' && character <= '9') || (character >= 'a' && character <= 'z')
                || (character >= 'A' && character <= 'Z') || character == '_' || character == '-');
        }

        static Dictionary<string, object> TryParse(string data)
        {
            try { return GmailAppJson.Parse(data); }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (FormatException) { return null; }
        }

        static GmailAppException InvalidResponse() { return new GmailAppException(TextResources.Get("gmailApp.invalidResponse")); }

        sealed class GmailHttpException : Exception
        {
            internal readonly HttpStatusCode Status;
            internal readonly bool InvalidGrant;
            internal GmailHttpException(HttpStatusCode status, bool invalidGrant) { Status = status; InvalidGrant = invalidGrant; }
        }

        bool IsDisposed() { lock (lifecycle) return disposed; }

        public void Dispose()
        {
            lock (lifecycle)
            {
                if (disposed) return;
                disposed = true;
            }
            lifetime.Cancel();
            http.Dispose();
            lifetime.Dispose();
        }
    }
}
