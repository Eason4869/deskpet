using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu
{
    internal sealed class GmailAppOAuthClient
    {
        const string ClientResourceName = "Dafeiyu.GmailOAuthClient";
        const int MaximumClientBytes = 16 * 1024;
        internal string Id, Secret;

        internal static GmailAppOAuthClient Read(string manifest)
        {
            try
            {
                var bytes = ReadClientBytes(manifest);
                if (bytes == null) return null;
                try
                {
                    var json = GmailAppJson.Parse(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'));
                    var installed = GmailAppJson.ObjectField(json, "installed");
                    string id = GmailAppJson.String(installed, "client_id");
                    string secret = GmailAppJson.String(installed, "client_secret");
                    if (!GmailAppJson.IsCredential(id) || !id.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal)
                        || !GmailAppJson.IsCredential(secret)) throw InvalidClient();
                    return new GmailAppOAuthClient { Id = id, Secret = secret };
                }
                finally { Array.Clear(bytes, 0, bytes.Length); }
            }
            catch (Exception error)
            {
                if (!GmailAppTokenStore.IsStorageError(error)) throw;
                throw InvalidClient();
            }
        }

        static byte[] ReadClientBytes(string fixturePath)
        {
            if (fixturePath != null) return GmailAppTokenStore.ReadFile(fixturePath, MaximumClientBytes);
            using (var resource = typeof(GmailAppOAuthClient).Assembly.GetManifestResourceStream(ClientResourceName))
                return resource == null ? null : GmailAppTokenStore.ReadStream(resource, MaximumClientBytes);
        }

        static GmailAppException InvalidClient()
        {
            return new GmailAppException(TextResources.Get("gmailApp.invalidConfig"));
        }
    }

    internal sealed class GmailAppAuthorization
    {
        internal string Code, Verifier, RedirectUri;
    }

    internal static class GmailAppOAuth
    {
        internal const string Scope = "https://www.googleapis.com/auth/gmail.readonly";
        const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        const int MaximumCallbackBytes = 8192;

        internal static void OpenBrowser(Uri address)
        {
            using (Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true })) { }
        }

        internal static async Task<GmailAppAuthorization> AuthorizeAsync(GmailAppOAuthClient client, Action<Uri> openBrowser, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            string state = RandomValue(), verifier = RandomValue(), challenge;
            using (var sha = SHA256.Create()) challenge = Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));

            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.ExclusiveAddressUse = true;
                listener.Start();
            }
            catch (SocketException) { throw new GmailAppException(TextResources.Get("gmailApp.callbackError")); }
            using (cancel.Register(listener.Stop))
            try
            {
                var redirect = new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture) + "/");
                var url = new Uri(AuthorizeEndpoint + "?" + Query(new[]
                {
                    Pair("client_id", client.Id), Pair("redirect_uri", redirect.AbsoluteUri), Pair("response_type", "code"),
                    Pair("scope", Scope), Pair("state", state), Pair("code_challenge", challenge), Pair("code_challenge_method", "S256"),
                    Pair("access_type", "offline"), Pair("prompt", "select_account consent")
                }));
                cancel.ThrowIfCancellationRequested();
                try { openBrowser(url); }
                catch (Win32Exception) { throw BrowserError(); }
                catch (InvalidOperationException) { throw BrowserError(); }
                catch (System.Security.SecurityException) { throw BrowserError(); }

                while (true)
                {
                    using (var connection = await listener.AcceptTcpClientAsync().ConfigureAwait(false))
                    {
                        var response = await ReadCallbackAsync(connection, redirect, state, cancel).ConfigureAwait(false);
                        if (response == null) continue;
                        if (response.ContainsKey("error")) throw new GmailAppException(TextResources.Get("gmailApp.authorizationDenied"));
                        return new GmailAppAuthorization { Code = response["code"], Verifier = verifier, RedirectUri = redirect.AbsoluteUri };
                    }
                }
            }
            catch (ObjectDisposedException) { cancel.ThrowIfCancellationRequested(); throw; }
            catch (InvalidOperationException) { cancel.ThrowIfCancellationRequested(); throw; }
            catch (SocketException) { cancel.ThrowIfCancellationRequested(); throw new GmailAppException(TextResources.Get("gmailApp.callbackError")); }
            finally { listener.Stop(); }
        }

        static async Task<Dictionary<string, string>> ReadCallbackAsync(TcpClient connection, Uri redirect, string state, CancellationToken cancel)
        {
            Dictionary<string, string> response = null;
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(3));
                using (deadline.Token.Register(connection.Close))
                try
                {
                    var stream = connection.GetStream();
                    string headers = await ReadHeadersAsync(stream, deadline.Token).ConfigureAwait(false);
                    response = ParseCallback(headers, redirect, state);
                    string message = response == null ? TextResources.Get("gmailApp.callbackInvalid")
                        : response.ContainsKey("error") ? TextResources.Get("gmailApp.callbackDenied") : TextResources.Get("gmailApp.callbackComplete");
                    var body = Encoding.UTF8.GetBytes(message);
                    string status = response == null ? "400 Bad Request" : "200 OK";
                    string header = "HTTP/1.1 " + status + "\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\n"
                        + "Referrer-Policy: no-referrer\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\nContent-Length: "
                        + body.Length.ToString(CultureInfo.InvariantCulture) + "\r\n\r\n";
                    var prefix = Encoding.ASCII.GetBytes(header);
                    await stream.WriteAsync(prefix, 0, prefix.Length, deadline.Token).ConfigureAwait(false);
                    await stream.WriteAsync(body, 0, body.Length, deadline.Token).ConfigureAwait(false);
                }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { if (!deadline.IsCancellationRequested) throw; }
                catch (OperationCanceledException) { }
            }
            cancel.ThrowIfCancellationRequested();
            return response;
        }

        static async Task<string> ReadHeadersAsync(NetworkStream stream, CancellationToken cancel)
        {
            var bytes = new byte[MaximumCallbackBytes];
            int total = 0;
            while (total < bytes.Length)
            {
                int count = await stream.ReadAsync(bytes, total, bytes.Length - total, cancel).ConfigureAwait(false);
                if (count == 0) break;
                int scan = Math.Max(0, total - 3);
                total += count;
                for (; scan + 3 < total; scan++)
                    if (bytes[scan] == 13 && bytes[scan + 1] == 10 && bytes[scan + 2] == 13 && bytes[scan + 3] == 10)
                        return Encoding.ASCII.GetString(bytes, 0, scan);
            }
            return null;
        }

        static Dictionary<string, string> ParseCallback(string headers, Uri redirect, string state)
        {
            if (headers == null) return null;
            var lines = headers.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var request = lines[0].Split(' ');
            if (request.Length != 3 || request[0] != "GET" || !request[1].StartsWith("/?", StringComparison.Ordinal)
                || (request[2] != "HTTP/1.0" && request[2] != "HTTP/1.1")) return null;
            var host = lines.Skip(1).Where(line => line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (host.Length != 1 || !String.Equals(host[0].Substring(5).Trim(), redirect.Authority, StringComparison.OrdinalIgnoreCase)) return null;
            var query = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in request[1].Substring(2).Split('&'))
            {
                int equals = field.IndexOf('=');
                if (equals < 1) return null;
                string key = WebUtility.UrlDecode(field.Substring(0, equals));
                if (query.ContainsKey(key)) return null;
                query.Add(key, WebUtility.UrlDecode(field.Substring(equals + 1)));
            }
            string returnedState, code, error;
            if (!query.TryGetValue("state", out returnedState) || !SameState(state, returnedState)) return null;
            bool gotCode = query.TryGetValue("code", out code);
            bool gotError = query.TryGetValue("error", out error);
            if (gotCode == gotError) return null;
            return (gotCode ? GmailAppJson.IsCredential(code) : !String.IsNullOrWhiteSpace(error)) ? query : null;
        }

        static bool SameState(string expected, string received)
        {
            if (received == null || expected.Length != received.Length) return false;
            int difference = 0;
            for (int index = 0; index < expected.Length; index++) difference |= expected[index] ^ received[index];
            return difference == 0;
        }

        static string RandomValue()
        {
            var data = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(data);
            return Base64Url(data);
        }

        static string Base64Url(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        internal static string Query(IEnumerable<KeyValuePair<string, string>> values)
        {
            return String.Join("&", values.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        }

        internal static KeyValuePair<string, string> Pair(string name, string value)
        {
            return new KeyValuePair<string, string>(name, value);
        }

        static GmailAppException BrowserError()
        {
            return new GmailAppException(TextResources.Get("gmailApp.browserError"));
        }
    }
}
