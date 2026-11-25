using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Mime;
using System.Text;
using System.Web.Script.Serialization;

namespace Dafeiyu
{
    public sealed class GmailAppMessagePage
    {
        public IReadOnlyList<GmailAppMessage> Messages { get; private set; }
        public string NextPageToken { get; private set; }

        internal GmailAppMessagePage(GmailAppMessage[] messages, string nextPageToken)
        {
            Messages = Array.AsReadOnly(messages);
            NextPageToken = nextPageToken;
        }
    }

    public sealed class GmailAppMessage
    {
        const int MaximumBodyCharacters = 24000;
        const int MaximumEncodedBody = MaximumBodyCharacters * 16 / 3 + 4;
        public string Id { get; private set; }
        public string Subject { get; private set; }
        public string From { get; private set; }
        public string To { get; private set; }
        public DateTimeOffset? Date { get; private set; }
        public string Snippet { get; private set; }
        public string Body { get; private set; }
        public bool HasPlainTextBody { get { return !String.IsNullOrWhiteSpace(Body); } }

        internal static GmailAppMessage Read(string id, Dictionary<string, object> json, bool includeBody)
        {
            if (id != GmailAppJson.String(json, "id")) throw new GmailAppException(TextResources.Get("gmailApp.invalidResponse"));
            var payload = GmailAppJson.ObjectField(json, "payload");
            long timestamp;
            DateTimeOffset? date = null;
            if (Int64.TryParse(GmailAppJson.String(json, "internalDate"), NumberStyles.None, CultureInfo.InvariantCulture, out timestamp)
                && timestamp <= 253402300799999) date = DateTimeOffset.FromUnixTimeMilliseconds(timestamp);
            return new GmailAppMessage
            {
                Id = id,
                Subject = SafeText(Header(payload, "Subject"), 500, false),
                From = SafeText(Header(payload, "From"), 500, false),
                To = SafeText(Header(payload, "To"), 800, false),
                Date = date,
                Snippet = SafeText(WebUtility.HtmlDecode(GmailAppJson.String(json, "snippet")), 800, false),
                Body = includeBody ? FindPlainText(payload, 0) : ""
            };
        }

        static string Header(Dictionary<string, object> payload, string name)
        {
            foreach (var item in GmailAppJson.ArrayField(payload, "headers"))
            {
                var header = item as Dictionary<string, object>;
                if (String.Equals(GmailAppJson.String(header, "name"), name, StringComparison.OrdinalIgnoreCase))
                    return GmailAppJson.String(header, "value");
            }
            return "";
        }

        static string FindPlainText(Dictionary<string, object> part, int depth)
        {
            if (part == null || depth > 16 || GmailAppJson.String(part, "filename").Length != 0
                || Header(part, "Content-Disposition").TrimStart().StartsWith("attachment", StringComparison.OrdinalIgnoreCase)) return "";
            string type = GmailAppJson.String(part, "mimeType");
            if (String.Equals(type, "text/plain", StringComparison.OrdinalIgnoreCase))
            {
                var body = GmailAppJson.ObjectField(part, "body");
                if (GmailAppJson.String(body, "attachmentId").Length != 0) return "";
                return DecodePlainText(GmailAppJson.String(body, "data"), Header(part, "Content-Type"));
            }
            if (type.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
                foreach (var child in GmailAppJson.ArrayField(part, "parts"))
                {
                    string text = FindPlainText(child as Dictionary<string, object>, depth + 1);
                    if (!String.IsNullOrWhiteSpace(text)) return text;
                }
            return "";
        }

        static string DecodePlainText(string body, string mediaType)
        {
            if (body.Length == 0) return "";
            int length = Math.Min(body.Length, MaximumEncodedBody);
            if (length < body.Length) length -= length % 4;
            string encoded = body.Substring(0, length).TrimEnd('=').Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
            byte[] bytes;
            try { bytes = Convert.FromBase64String(encoded); }
            catch (FormatException) { return ""; }

            Encoding encoding = Encoding.UTF8;
            if (mediaType.Length != 0)
            {
                try
                {
                    string charset = new ContentType(mediaType).CharSet;
                    if (!String.IsNullOrWhiteSpace(charset)) encoding = Encoding.GetEncoding(charset);
                }
                catch (FormatException) { }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
            }
            try { return SafeText(encoding.GetString(bytes).TrimStart('\uFEFF'), MaximumBodyCharacters, true); }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }

        static string SafeText(string value, int limit, bool allowNewline)
        {
            var clean = new StringBuilder(Math.Min(value.Length, limit));
            for (int index = 0; index < value.Length && clean.Length < limit; index++)
            {
                char character = value[index];
                if (Char.IsHighSurrogate(character))
                {
                    if (index + 1 < value.Length && Char.IsLowSurrogate(value[index + 1]) && clean.Length + 2 <= limit)
                        clean.Append(character).Append(value[++index]);
                    continue;
                }
                if (Char.IsLowSurrogate(character) || character == '\u061C' || character == '\u200E' || character == '\u200F'
                    || (character >= '\u202A' && character <= '\u202E') || (character >= '\u2066' && character <= '\u2069')) continue;
                if (character == '\r' || character == '\n' || character == '\t') clean.Append(allowNewline ? character : ' ');
                else if (!Char.IsControl(character)) clean.Append(character);
            }
            return clean.ToString().Trim();
        }
    }

    internal static class GmailAppJson
    {
        internal const int MaximumResponseBytes = 4 * 1024 * 1024;
        static readonly object[] EmptyArray = new object[0];
        static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = MaximumResponseBytes, RecursionLimit = 64 }; }

        internal static Dictionary<string, object> Parse(string text)
        {
            var result = Serializer().DeserializeObject(text) as Dictionary<string, object>;
            if (result == null) throw new FormatException();
            return result;
        }

        internal static string Serialize(Dictionary<string, string> value) { return Serializer().Serialize(value); }

        static object Field(Dictionary<string, object> json, string name)
        {
            object value;
            return json != null && json.TryGetValue(name, out value) ? value : null;
        }

        internal static Dictionary<string, object> ObjectField(Dictionary<string, object> json, string name)
        {
            return Field(json, name) as Dictionary<string, object>;
        }

        internal static object[] ArrayField(Dictionary<string, object> json, string name) { return Field(json, name) as object[] ?? EmptyArray; }
        internal static string String(Dictionary<string, object> json, string name) { return Field(json, name) as string ?? ""; }

        internal static int Integer(Dictionary<string, object> json, string name)
        {
            object value = Field(json, name);
            return value is int ? (int)value : 0;
        }

        internal static bool IsCredential(string value)
        {
            if (System.String.IsNullOrEmpty(value) || value.Length > 4096) return false;
            foreach (char character in value) if (character < '!' || character > '~') return false;
            return true;
        }

        internal static bool IsEmail(string value)
        {
            if (System.String.IsNullOrEmpty(value) || value.Length > 320 || value.IndexOf('@') <= 0 || value.EndsWith("@", StringComparison.Ordinal)) return false;
            foreach (char character in value) if (Char.IsWhiteSpace(character) || Char.IsControl(character)) return false;
            return true;
        }
    }
}
