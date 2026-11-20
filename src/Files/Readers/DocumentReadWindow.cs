using System;
using System.Globalization;
using System.Text;

namespace Dafeiyu
{
    internal sealed class DocumentReadWindow
    {
        const int MaxOutputBytes = 32 * 1024;
        const string Truncated = "\n[This item exceeds the text limit and was shortened.]";
        readonly StringBuilder text = new StringBuilder();
        readonly string section;
        readonly int first;
        int last;
        int usedBytes;

        internal DocumentReadWindow(string section, int first)
        {
            this.section = section;
            this.first = first;
            last = first - 1;
        }

        internal bool Add(string value)
        {
            int bytes = Encoding.UTF8.GetByteCount(value);
            if (bytes + usedBytes + (text.Length == 0 ? 0 : 2) > MaxOutputBytes)
            {
                if (last >= first) return false;
                value = Shorten(value, MaxOutputBytes - Encoding.UTF8.GetByteCount(Truncated)) + Truncated;
                bytes = Encoding.UTF8.GetByteCount(value);
            }
            if (text.Length > 0)
            {
                text.Append("\n\n");
                usedBytes += 2;
            }
            text.Append(value);
            usedBytes += bytes;
            last++;
            return true;
        }

        internal FileReadResult Complete(int count)
        {
            if (last < first) return new FileReadResult("", section);
            string range = section + " " + SourceNumber(first);
            if (last != first) range += "-" + SourceNumber(last);
            return new FileReadResult(text.ToString(), range, last < count ? (int?)(last + 1) : null);
        }

        internal static string SourceNumber(int n) { return n.ToString(CultureInfo.InvariantCulture); }

        static string Shorten(string value, int maxBytes)
        {
            int low = 0, high = Math.Min(maxBytes, value.Length);
            while (low < high)
            {
                int middle = low + (high - low + 1) / 2;
                if (Encoding.UTF8.GetByteCount(value.Substring(0, middle)) <= maxBytes) low = middle;
                else high = middle - 1;
            }
            if (low > 0 && Char.IsHighSurrogate(value[low - 1])) low--;
            return value.Substring(0, low);
        }
    }
}
