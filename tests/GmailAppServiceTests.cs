using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu.Tests
{
    public static class GmailAppServiceTests
    {
        public static int Main()
        {
            try
            {
                RunAsync().GetAwaiter().GetResult();
                Console.WriteLine("Gmail app service tests passed.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }

        static async Task RunAsync()
        {
            string root = Path.Combine(Environment.CurrentDirectory, "work", "deskpet-gmail-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string manifest = Path.Combine(root, "deskpet-gmail-oauth.json");
                string data = Path.Combine(root, "data");
                using (var unavailable = new GmailAppService(data, manifest, new GoogleFixture(), _ =>
                    { throw new InvalidOperationException("Unconfigured Gmail opened a browser."); }))
                {
                    Assert(!unavailable.GetStatus().IsConfigured, "Absent manifest must disable sign-in.");
                    try
                    {
                        await unavailable.ConnectAsync(CancellationToken.None);
                        throw new InvalidOperationException("Unconfigured Gmail accepted sign-in.");
                    }
                    catch (GmailAppException) { }
                }

                File.WriteAllText(manifest, "{\"installed\":{\"client_id\":\"123-example.apps.googleusercontent.com\",\"client_secret\":\"desktop-example\",\"token_uri\":\"https://example.invalid\"}}");
                await CheckAccountAsync(data, manifest);
                await CheckConnectionChangeAsync(data, manifest);
                await CheckCancelAsync(data, manifest);
                await CheckScopeAsync(data, manifest);
            }
            finally { Directory.Delete(root, true); }
        }

        static async Task CheckConnectionChangeAsync(string data, string manifest)
        {
            var google = new GoogleFixture();
            Task callback = null;
            using (var service = new GmailAppService(data, manifest, google, uri => { callback = CompleteBrowserAsync(uri, google); }))
            {
                var first = await service.ConnectAsync(CancellationToken.None);
                await callback;
                int changes = 0;
                service.ConnectionChanged += () => changes++;
                await service.DisconnectAsync(CancellationToken.None);
                Assert(changes == 1 && !service.IsCurrentConnection(first.ConnectionVersion),
                    "Disconnect must invalidate the open mailbox connection once.");
                google.EmailAddress = "other@example.com";
                var second = await service.ConnectAsync(CancellationToken.None);
                await callback;
                Assert(changes == 2 && second.EmailAddress == "other@example.com" && service.IsCurrentConnection(second.ConnectionVersion),
                    "Reconnecting must issue a distinct mailbox connection.");
                int before = google.RequestCount;
                foreach (var stale in new Func<Task>[]
                {
                    () => service.GetMessageAsync("test123", CancellationToken.None, first.ConnectionVersion),
                    () => service.ListMessagesAsync("old search", null, 12, CancellationToken.None, first.ConnectionVersion)
                })
                {
                    try { await stale(); throw new InvalidOperationException("A stale mailbox read used the new connection."); }
                    catch (GmailAppException) { }
                }
                Assert(google.RequestCount == before, "Stale mailbox requests must not reach Gmail.");
                var message = await service.GetMessageAsync("test123", CancellationToken.None, second.ConnectionVersion);
                Assert(message.HasPlainTextBody, "Current mailbox requests still read messages.");
                await service.DisconnectAsync(CancellationToken.None);
            }
        }

        static async Task CheckAccountAsync(string data, string manifest)
        {
            var google = new GoogleFixture();
            Task callback = null;
            using (var service = new GmailAppService(data, manifest, google, authorizeUri =>
                { callback = CompleteBrowserAsync(authorizeUri, google); }))
            {
                var connected = await service.ConnectAsync(CancellationToken.None);
                Assert(callback != null, "Sign-in must open the system browser.");
                await callback;
                Assert(connected.IsConnected && connected.EmailAddress == "pet@example.com", "Gmail profile must identify the connected account.");
                var files = Directory.GetFiles(data, "*", SearchOption.AllDirectories);
                Assert(files.Length == 1, "Only one encrypted token file must be persisted.");
                Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(files[0])).Contains("refresh-example"), "Token must not be stored as plaintext.");
            }

            google = new GoogleFixture();
            using (var service = new GmailAppService(data, manifest, google, _ =>
                { throw new InvalidOperationException("Stored Gmail authorization opened a browser."); }))
            {
                Assert(service.GetStatus().EmailAddress == "pet@example.com", "Account identity must survive a restart.");
                var page = await service.ListMessagesAsync("from:pet@example.com & label:inbox", "before/+", 10, CancellationToken.None);
                Assert(google.RefreshCount == 1, "An unauthorized request must refresh and retry once.");
                Assert(page.NextPageToken == "after/+" && page.Messages.Count == 1, "Search must preserve paging and load message metadata.");
                Assert(page.Messages[0].Subject == "Schedule" && page.Messages[0].From == "Friend <friend@example.com>", "Metadata must contain recipient-visible headers.");

                var plain = await service.GetMessageAsync("test123", CancellationToken.None);
                Assert(plain.HasPlainTextBody && plain.Body == "Safe plain text", "Read must select inline text/plain and ignore attachments and HTML.");
                var html = await service.GetMessageAsync("html123", CancellationToken.None);
                Assert(!html.HasPlainTextBody && String.IsNullOrEmpty(html.Body) && html.Snippet == "Fallback", "HTML-only content must expose its inert snippet.");
                int before = google.RequestCount;
                try
                {
                    await service.GetMessageAsync("../profile", CancellationToken.None);
                    throw new InvalidOperationException("Invalid message ID reached Gmail.");
                }
                catch (GmailAppException) { }
                Assert(google.RequestCount == before, "Invalid message IDs must not leave the app.");

                google.FailRevoke = true;
                var disconnected = await service.DisconnectAsync(CancellationToken.None);
                Assert(disconnected.WasConnected && !disconnected.Revoked, "Failed Google revocation must be reported as local-only disconnect.");
                Assert(!service.GetStatus().IsConnected && Directory.GetFiles(data, "*", SearchOption.AllDirectories).Length == 0,
                    "Disconnect must delete local credentials even when Google is unavailable.");
            }

            google = new GoogleFixture();
            using (var service = new GmailAppService(data, manifest, google, uri => { callback = CompleteBrowserAsync(uri, google); }))
            {
                await service.ConnectAsync(CancellationToken.None);
                await callback;
                var disconnected = await service.DisconnectAsync(CancellationToken.None);
                Assert(disconnected.WasConnected && disconnected.Revoked, "A successful Google revocation must be reported.");
            }
        }

        static async Task CheckCancelAsync(string data, string manifest)
        {
            using (var cancel = new CancellationTokenSource())
            using (var service = new GmailAppService(data, manifest, new GoogleFixture(), _ => cancel.Cancel()))
            {
                try
                {
                    await service.ConnectAsync(cancel.Token);
                    throw new InvalidOperationException("Cancelled sign-in must stop awaiting the callback.");
                }
                catch (OperationCanceledException) { }
                Assert(!service.GetStatus().IsConnected, "Cancelled sign-in must leave no credentials.");
            }
        }

        static async Task CheckScopeAsync(string data, string manifest)
        {
            var google = new GoogleFixture { GrantScope = "https://www.googleapis.com/auth/gmail.readonly https://www.googleapis.com/auth/gmail.send" };
            Task callback = null;
            using (var service = new GmailAppService(data, manifest, google, uri => { callback = CompleteBrowserAsync(uri, google); }))
            {
                try
                {
                    await service.ConnectAsync(CancellationToken.None);
                    throw new InvalidOperationException("A broader authorization scope was accepted.");
                }
                catch (GmailAppException) { }
                await callback;
                Assert(google.RequestCount == 1 && !service.GetStatus().HasLocalCredentials,
                    "A non-readonly grant must be rejected before using or persisting credentials.");
            }
        }

        static async Task CompleteBrowserAsync(Uri authorizeUri, GoogleFixture google)
        {
            Assert(authorizeUri.GetLeftPart(UriPartial.Path) == "https://accounts.google.com/o/oauth2/v2/auth", "Browser must open Google's official OAuth page.");
            var query = Fields(authorizeUri.Query);
            Assert(query["scope"] == "https://www.googleapis.com/auth/gmail.readonly", "Sign-in scope must be exactly Gmail readonly.");
            Assert(query["code_challenge_method"] == "S256" && query["state"].Length >= 43, "Sign-in must use PKCE S256 and a random state.");
            Assert(!query.ContainsKey("client_secret"), "The desktop client secret must not enter the browser URL.");
            google.Challenge = query["code_challenge"];
            string redirect = query["redirect_uri"];
            Assert(new Uri(redirect).Host == "127.0.0.1", "OAuth callback must bind to loopback.");
            using (var browser = new HttpClient(new HttpClientHandler { UseProxy = false }))
            {
                var invalid = await browser.GetAsync(redirect + "?state=wrong&code=ignored");
                Assert(invalid.StatusCode == HttpStatusCode.BadRequest, "Wrong state must be ignored by the callback listener.");
                var valid = await browser.GetAsync(redirect + "?state=" + query["state"] + "&code=code-example");
                Assert(valid.IsSuccessStatusCode, "Correct state must complete the callback.");
                Assert(!(await valid.Content.ReadAsStringAsync()).Contains("code-example"), "Callback must not echo the authorization code.");
            }
        }

        sealed class GoogleFixture : HttpMessageHandler
        {
            internal string Challenge;
            internal string GrantScope = "https://www.googleapis.com/auth/gmail.readonly";
            internal string EmailAddress = "pet@example.com";
            internal int RequestCount, RefreshCount;
            internal bool FailRevoke;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref RequestCount);
                Assert(request.RequestUri.Scheme == "https", "Provider requests must use HTTPS.");
                if (request.RequestUri.Host == "oauth2.googleapis.com")
                {
                    Assert(request.Method == HttpMethod.Post, "OAuth requests must submit credentials in the body.");
                    var fields = Fields(await request.Content.ReadAsStringAsync());
                    if (request.RequestUri.AbsolutePath == "/revoke")
                    {
                        Assert(fields["token"] == "refresh-example", "Disconnect must revoke the refresh token.");
                        return FailRevoke ? Response(HttpStatusCode.ServiceUnavailable, "{}") : Response(HttpStatusCode.OK, "{}");
                    }
                    Assert(request.RequestUri.AbsolutePath == "/token" && fields["client_secret"] == "desktop-example", "Token exchange must use the installed client at Google's endpoint.");
                    if (fields["grant_type"] == "refresh_token")
                    {
                        Interlocked.Increment(ref RefreshCount);
                        Assert(fields["refresh_token"] == "refresh-example", "Refresh must use the persisted token.");
                        return Response(HttpStatusCode.OK, "{\"access_token\":\"refreshed-example\",\"expires_in\":3600,\"token_type\":\"Bearer\"}");
                    }
                    Assert(fields["grant_type"] == "authorization_code" && fields["code"] == "code-example", "Only the verified browser callback may exchange a code.");
                    Assert(fields["code_verifier"].Length >= 43, "Token exchange must present the original verifier.");
                    using (var sha = SHA256.Create())
                    {
                        var encoded = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(fields["code_verifier"]))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
                        Assert(encoded == Challenge, "Token exchange verifier must match the browser challenge.");
                    }
                    return Response(HttpStatusCode.OK, "{\"access_token\":\"access-example\",\"refresh_token\":\"refresh-example\",\"expires_in\":3600,\"token_type\":\"Bearer\",\"scope\":\"" + GrantScope + "\"}");
                }

                Assert(request.RequestUri.Host == "gmail.googleapis.com" && request.Method == HttpMethod.Get && request.Headers.Authorization.Scheme == "Bearer",
                    "Mailbox requests must be reads with a bearer header at Google's Gmail endpoint.");
                string path = request.RequestUri.AbsolutePath;
                if (path == "/gmail/v1/users/me/profile")
                    return Response(HttpStatusCode.OK, "{\"emailAddress\":\"" + EmailAddress + "\"}");
                var query = Fields(request.RequestUri.Query);
                if (path == "/gmail/v1/users/me/messages")
                {
                    Assert(query["q"] == "from:pet@example.com & label:inbox" && query["pageToken"] == "before/+" && query["maxResults"] == "10", "Gmail must receive the exact bounded search and page token.");
                    if (request.Headers.Authorization.Parameter == "access-example") return Response(HttpStatusCode.Unauthorized, "{}");
                    return Response(HttpStatusCode.OK, "{\"messages\":[{\"id\":\"test123\"}],\"nextPageToken\":\"after/+\"}");
                }

                const string Meta = "\"id\":\"test123\",\"snippet\":\"Fallback\",\"internalDate\":\"1740000000000\"";
                if (path == "/gmail/v1/users/me/messages/html123")
                    return Response(HttpStatusCode.OK, "{\"id\":\"html123\",\"snippet\":\"Fallback\",\"payload\":{\"mimeType\":\"text/html\",\"body\":{\"data\":\"PGltZyBzcmM9J2h0dHBzOi8vZXhhbXBsZS5jb20nPg\"}}}");
                Assert(path == "/gmail/v1/users/me/messages/test123", "No attachment or unexpected endpoint is allowed.");
                if (query["format"] == "metadata")
                    return Response(HttpStatusCode.OK, "{" + Meta + ",\"payload\":{\"headers\":[{\"name\":\"Subject\",\"value\":\"Schedule\"},{\"name\":\"From\",\"value\":\"Friend <friend@example.com>\"}]}}");
                Assert(query["format"] == "full", "Explicit message read must request message content.");
                return Response(HttpStatusCode.OK, "{" + Meta + ",\"payload\":{\"mimeType\":\"multipart/mixed\",\"parts\":[{\"mimeType\":\"text/plain\",\"filename\":\"secret.txt\",\"body\":{\"data\":\"bm8\"}},{\"mimeType\":\"multipart/alternative\",\"parts\":[{\"mimeType\":\"text/html\",\"body\":{\"data\":\"bm8\"}},{\"mimeType\":\"text/plain\",\"body\":{\"data\":\"U2FmZSBwbGFpbiB0ZXh0\"}}]}]}}");
            }

            static HttpResponseMessage Response(HttpStatusCode code, string json)
            {
                return new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            }
        }

        static Dictionary<string, string> Fields(string text)
        {
            return text.TrimStart('?').Split('&').Select(field => field.Split(new[] { '=' }, 2)).GroupBy(part => WebUtility.UrlDecode(part[0]))
                .ToDictionary(group => group.Key, group => WebUtility.UrlDecode(group.First().Last()));
        }

        static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
