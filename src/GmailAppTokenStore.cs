using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Dafeiyu
{
    internal sealed class GmailAppCredentials
    {
        internal string ClientId, EmailAddress, AccessToken, RefreshToken;
        internal DateTimeOffset ExpiresUtc;
    }

    internal sealed class GmailAppTokenStore
    {
        const int MaximumBytes = 48 * 1024;
        readonly object gate = new object();
        readonly string directory, path;

        internal GmailAppTokenStore(string root)
        {
            if (String.IsNullOrWhiteSpace(root)) throw new ArgumentException("root");
            directory = Path.Combine(Path.GetFullPath(root), "gmail-app");
            path = Path.Combine(directory, "tokens.bin");
        }

        internal bool Exists { get { lock (gate) return File.Exists(path); } }

        internal GmailAppCredentials Read()
        {
            lock (gate)
            {
                byte[] plain = null;
                try
                {
                    var cipher = ReadFile(path, MaximumBytes);
                    if (cipher == null) return null;
                    plain = SecretProtection.Unprotect(cipher);
                    var json = GmailAppJson.Parse(new UTF8Encoding(false, true).GetString(plain));
                    long expires;
                    var value = new GmailAppCredentials
                    {
                        ClientId = GmailAppJson.String(json, "client_id"), EmailAddress = GmailAppJson.String(json, "email_address"),
                        AccessToken = GmailAppJson.String(json, "access_token"), RefreshToken = GmailAppJson.String(json, "refresh_token")
                    };
                    if (!GmailAppJson.IsCredential(value.ClientId) || !GmailAppJson.IsEmail(value.EmailAddress)
                        || !GmailAppJson.IsCredential(value.AccessToken) || !GmailAppJson.IsCredential(value.RefreshToken)
                        || !Int64.TryParse(GmailAppJson.String(json, "expires_utc_ticks"), NumberStyles.None, CultureInfo.InvariantCulture, out expires))
                        throw StorageError();
                    value.ExpiresUtc = new DateTimeOffset(expires, TimeSpan.Zero);
                    return value;
                }
                catch (Exception error)
                {
                    if (!IsStorageError(error)) throw;
                    throw StorageError();
                }
                finally { if (plain != null) Array.Clear(plain, 0, plain.Length); }
            }
        }

        internal void Write(GmailAppCredentials value)
        {
            lock (gate)
            {
                byte[] plain = null;
                string temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    plain = Encoding.UTF8.GetBytes(GmailAppJson.Serialize(new Dictionary<string, string>
                    {
                        { "client_id", value.ClientId }, { "email_address", value.EmailAddress },
                        { "access_token", value.AccessToken }, { "refresh_token", value.RefreshToken },
                        { "expires_utc_ticks", value.ExpiresUtc.UtcTicks.ToString(CultureInfo.InvariantCulture) }
                    }));
                    var cipher = SecretProtection.Protect(plain);
                    if (cipher.Length > MaximumBytes) throw StorageError();
                    Directory.CreateDirectory(directory);
                    using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        file.Write(cipher, 0, cipher.Length);
                        file.Flush(true);
                    }
                    AtomicFile.Commit(temporary, path);
                }
                catch (Exception error)
                {
                    if (!IsStorageError(error)) throw;
                    throw StorageError();
                }
                finally
                {
                    if (plain != null) Array.Clear(plain, 0, plain.Length);
                    try { File.Delete(temporary); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        internal void Delete()
        {
            lock (gate)
            {
                try { File.Delete(path); }
                catch (DirectoryNotFoundException) { }
                catch (Exception error)
                {
                    if (!IsStorageError(error)) throw;
                    throw StorageError();
                }
            }
        }

        internal static byte[] ReadFile(string filename, int limit)
        {
            try
            {
                using (var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                    return ReadStream(stream, limit);
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }

        internal static byte[] ReadStream(Stream stream, int limit)
        {
            long length = stream.Length;
            if (length == 0 || length > limit) throw new InvalidDataException();
            var bytes = new byte[(int)length];
            int position = 0;
            while (position < bytes.Length)
            {
                int count = stream.Read(bytes, position, bytes.Length - position);
                if (count == 0) throw new EndOfStreamException();
                position += count;
            }
            return bytes;
        }

        internal static bool IsStorageError(Exception error)
        {
            return error is IOException || error is UnauthorizedAccessException || error is CryptographicException
                || error is System.Security.SecurityException || error is ArgumentException || error is InvalidOperationException || error is FormatException;
        }

        static GmailAppException StorageError()
        {
            return new GmailAppException(TextResources.Get("gmailApp.storageError"));
        }
    }
}
