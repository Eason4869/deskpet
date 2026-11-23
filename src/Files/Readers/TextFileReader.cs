using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Dafeiyu
{
    internal sealed class TextFileReader : IFileContentReader
    {
        public FileManifest Inspect(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (var stream = File.OpenRead(path))
            {
                Encoding encoding = TextFileEncoding.Select(stream, null);
                return new FileManifest("text", new[] { new FileSection("lines", "Lines") }, encoding.WebName);
            }
        }

        public FileReadResult Read(string path, FileReadRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var page = new TabularReadBuffer();
            int end = request.Start - 1;
            using (var input = TextFileEncoding.Open(path, request.EncodingName))
            {
                for (int line = 1; ; line++)
                {
                    if (line - request.Start >= request.Count)
                        return new FileReadResult(page.Text, LineRange(request.Start, end), input.Peek() >= 0 ? (int?)line : null);
                    bool capture = line >= request.Start;
                    string value;
                    bool matched, clipped;
                    if (!ReadLine(input, capture, request.Query, token, out value, out matched, out clipped)) break;
                    if (!capture) continue;
                    if (!matched) { end = line; continue; }
                    string record = line.ToString(CultureInfo.InvariantCulture) + ": " + value +
                        (clipped ? " [line truncated]" : "") + "\n";
                    if (!page.Append(record)) return new FileReadResult(page.Text, LineRange(request.Start, end), line);
                    end = line;
                }
            }
            return new FileReadResult(page.Text, LineRange(request.Start, end));
        }

        static bool ReadLine(StreamReader input, bool capture, string query, CancellationToken token, out string value, out bool matched,
            out bool clipped)
        {
            const int maxLineLength = 32 * 1024;
            var line = capture ? new StringBuilder() : null;
            var search = capture && !String.IsNullOrEmpty(query) ? new StringBuilder() : null;
            int searchThreshold = search == null ? 0 : Math.Max(4096, query.Length * 2);
            string snippet = null;
            int consumed = 0;
            matched = search == null;
            clipped = false;
            while (true)
            {
                if ((consumed & 4095) == 0) token.ThrowIfCancellationRequested();
                int ch = input.Read();
                if (ch < 0 || ch == '\r' || ch == '\n')
                {
                    if (ch == '\r' && input.Peek() == '\n') input.Read();
                    if (line != null && line.Length > 0 && Char.IsHighSurrogate(line[line.Length - 1])) line.Length--;
                    value = line == null ? "" : line.ToString();
                    if (!matched) matched = FindQuery(search.ToString(), query, out snippet);
                    if (clipped && snippet != null && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                        value = "[matched excerpt] " + snippet;
                    return ch >= 0 || consumed > 0;
                }
                if (ch == '\0') throw new InvalidDataException("A text attachment contains a NUL character.");
                consumed++;
                if (line == null) continue;
                if (line.Length < maxLineLength) line.Append((char)ch);
                else clipped = true;
                if (matched) continue;
                search.Append((char)ch);
                if (search.Length >= searchThreshold && !Char.IsHighSurrogate((char)ch))
                {
                    matched = FindQuery(search.ToString(), query, out snippet);
                    if (!matched) search.Remove(0, search.Length - query.Length);
                }
            }
        }

        static bool FindQuery(string text, string query, out string snippet)
        {
            int index = text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            snippet = null;
            if (index < 0) return false;
            int start = Math.Max(0, index - 100), end = Math.Min(text.Length, index + query.Length + 100);
            if (start > 0 && Char.IsLowSurrogate(text[start])) start--;
            if (end < text.Length && Char.IsLowSurrogate(text[end])) end++;
            snippet = text.Substring(start, end - start);
            return true;
        }

        static string LineRange(int start, int end)
        {
            return end < start ? "No lines at " + start : "Lines " + start + "–" + end;
        }
    }

    internal static class TextFileEncoding
    {
        internal static void RegisterCodePages()
        {
#if PORTABLE
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
        }

        internal static StreamReader Open(string path, string requested)
        {
            var stream = File.OpenRead(path);
            try
            {
                return new StreamReader(stream, Select(stream, requested), false);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        internal static Encoding Select(Stream stream, string requested)
        {
            var header = new byte[4];
            int length = stream.Read(header, 0, header.Length);
            stream.Position = 0;
            if (length >= 4 && (header[0] == 0xff && header[1] == 0xfe && header[2] == 0 && header[3] == 0 ||
                header[0] == 0 && header[1] == 0 && header[2] == 0xfe && header[3] == 0xff))
                throw new InvalidDataException("UTF-32 is not a supported attachment encoding.");
            if (length >= 3 && header[0] == 0xef && header[1] == 0xbb && header[2] == 0xbf)
            {
                stream.Position = 3;
                return new UTF8Encoding(false, true);
            }
            if (length >= 2 && (header[0] == 0xff && header[1] == 0xfe || header[0] == 0xfe && header[1] == 0xff))
            {
                stream.Position = 2;
                return new UnicodeEncoding(header[0] == 0xfe, false, true);
            }
            string name = (requested ?? "").Trim();
            if (name.Length == 0 || name.Equals("utf-8", StringComparison.OrdinalIgnoreCase) || name.Equals("utf8", StringComparison.OrdinalIgnoreCase))
                return new UTF8Encoding(false, true);
            if (name.Equals("gb18030", StringComparison.OrdinalIgnoreCase))
            {
                RegisterCodePages();
                return Encoding.GetEncoding(54936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            }
            throw new InvalidDataException("Use UTF-8, GB18030, or a UTF-16 file with a byte-order mark.");
        }
    }
}
