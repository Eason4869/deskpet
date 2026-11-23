using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Dafeiyu
{
    internal sealed class DelimitedTableReader : IFileContentReader
    {
        readonly char delimiter;
        readonly string kind;

        internal DelimitedTableReader(char delimiter)
        {
            this.delimiter = delimiter;
            kind = delimiter == '\t' ? "tsv" : "csv";
        }

        public FileManifest Inspect(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (var stream = File.OpenRead(path))
                return new FileManifest(kind, new[] { new FileSection("rows", "Rows") }, TextFileEncoding.Select(stream, null).WebName);
        }

        public FileReadResult Read(string path, FileReadRequest request, CancellationToken token)
        {
            bool aggregate = TableAggregate.IsRequested(request);
            int end = request.Start - 1;
            int lastColumn = TabularRange.End(request.StartColumn, request.ColumnCount);
            int? next = null;
            var output = new TabularReadBuffer();
            var summary = new TableAggregate();
            using (var input = TextFileEncoding.Open(path, request.EncodingName))
            {
                for (int row = 1; ; row++)
                {
                    bool capture = row >= request.Start && row - request.Start < request.Count;
                    List<string> record;
                    if (!ReadRecord(input, capture ? request.StartColumn : 0, capture ? lastColumn : 0, token, out record)) break;
                    if (row < request.Start) continue;
                    if (!capture) { next = row; break; }
                    if (aggregate)
                    {
                        foreach (string field in record) summary.AddDelimited(field);
                    }
                    else
                    {
                        var line = new StringBuilder().Append(row).Append(": [");
                        for (int i = 0; i < record.Count; i++)
                        {
                            if (i > 0) line.Append(',');
                            TabularReadBuffer.Quote(line, record[i]);
                        }
                        if (!output.Append(line.Append("]\n").ToString())) { next = row; break; }
                    }
                    end = row;
                }
            }
            string range = TabularRange.ForRows(null, request.Start, end, request.StartColumn, lastColumn);
            return new FileReadResult(aggregate ? summary.Format(request.Operation) : output.Text, range, next);
        }

        bool ReadRecord(StreamReader input, int startColumn, int endColumn, CancellationToken token, out List<string> fields)
        {
            fields = new List<string>();
            var field = new StringBuilder();
            int column = 1, consumed = 0;
            bool quoted = false, closed = false, atFieldStart = true;
            while (true)
            {
                if ((consumed & 4095) == 0) token.ThrowIfCancellationRequested();
                int ch = input.Read();
                if (ch < 0)
                {
                    if (quoted) throw new InvalidDataException("Unterminated quoted table field.");
                    if (consumed == 0) return false;
                    AddField(fields, field, column, startColumn, endColumn);
                    return true;
                }
                if (ch == '\0') throw new InvalidDataException("A delimited table contains a NUL character.");
                consumed++;
                if (quoted)
                {
                    if (ch == '"' && input.Peek() != '"')
                    {
                        quoted = false;
                        closed = true;
                        continue;
                    }
                    if (ch == '"') input.Read();
                }
                else if (ch == delimiter || ch == '\r' || ch == '\n')
                {
                    AddField(fields, field, column, startColumn, endColumn);
                    if (ch != delimiter)
                    {
                        if (ch == '\r' && input.Peek() == '\n') input.Read();
                        return true;
                    }
                    column++;
                    field.Length = 0;
                    closed = false;
                    atFieldStart = true;
                    continue;
                }
                else if (ch == '"' && atFieldStart)
                {
                    quoted = true;
                    atFieldStart = false;
                    continue;
                }
                else if (closed)
                    throw new InvalidDataException("Unexpected characters after a quoted table field.");

                if (column >= startColumn && column <= endColumn)
                {
                    if (field.Length >= 128 * 1024)
                        throw new InvalidDataException("A selected table field exceeds the supported size.");
                    field.Append((char)ch);
                }
                atFieldStart = false;
            }
        }

        static void AddField(List<string> fields, StringBuilder field, int column, int startColumn, int endColumn)
        {
            if (column >= startColumn && column <= endColumn) fields.Add(field.ToString());
        }
    }

    internal sealed class TabularReadBuffer
    {
        const int MaxBytes = 32 * 1024;
        readonly StringBuilder output = new StringBuilder();
        int remaining;

        internal TabularReadBuffer(int reservedBytes = 0) { remaining = MaxBytes - reservedBytes; }

        internal string Text { get { return output.ToString(); } }

        // Keep full records together across pages. A single oversized record is identified explicitly.
        internal bool Append(string record)
        {
            int bytes = Encoding.UTF8.GetByteCount(record);
            if (bytes <= remaining)
            {
                output.Append(record);
                remaining -= bytes;
                return true;
            }
            if (output.Length > 0) return false;
            const string marker = "\n[record truncated]\n";
            int limit = remaining - Encoding.UTF8.GetByteCount(marker);
            int allowed = 0, used = 0;
            while (allowed < record.Length)
            {
                char ch = record[allowed];
                int chars = Char.IsHighSurrogate(ch) && allowed + 1 < record.Length && Char.IsLowSurrogate(record[allowed + 1]) ? 2 : 1;
                int width = chars == 2 ? 4 : ch <= 0x7f ? 1 : ch <= 0x7ff ? 2 : 3;
                if (used + width > limit) break;
                used += width;
                allowed += chars;
            }
            output.Append(record, 0, allowed).Append(marker);
            remaining = 0;
            return true;
        }

        internal static void Quote(StringBuilder output, string value)
        {
            output.Append('"');
            foreach (char ch in value ?? "")
            {
                switch (ch)
                {
                    case '"': output.Append("\\\""); break;
                    case '\\': output.Append("\\\\"); break;
                    case '\r': output.Append("\\r"); break;
                    case '\n': output.Append("\\n"); break;
                    case '\t': output.Append("\\t"); break;
                    default:
                        if (Char.IsControl(ch)) output.Append("\\u").Append(((int)ch).ToString("X4", CultureInfo.InvariantCulture));
                        else output.Append(ch);
                        break;
                }
            }
            output.Append('"');
        }
    }

    internal static class TabularRange
    {
        internal static int End(int start, int count)
        {
            return (int)Math.Min(Int32.MaxValue, (long)start + count - 1);
        }

        internal static string ColumnName(int column)
        {
            string address = "";
            while (column > 0)
            {
                column--;
                address = (char)('A' + column % 26) + address;
                column /= 26;
            }
            return address;
        }

        internal static string ForRows(string sheet, int start, int end, int startColumn, int endColumn)
        {
            string first = ColumnName(startColumn) + start;
            string last = ColumnName(endColumn) + end;
            string prefix = sheet == null ? "" : "'" + sheet.Replace("'", "''") + "'!";
            return prefix + (end < start ? "No rows at " + start : first + ":" + last);
        }
    }

    internal sealed class TableAggregate
    {
        int numeric, errors, nonEmpty, missingCache;
        double sum, correction;
        bool overflow;

        internal static bool IsRequested(FileReadRequest request)
        {
            if (String.IsNullOrEmpty(request.Operation)) return false;
            if (request.Operation == "count" || request.Operation == "sum") return true;
            throw new InvalidDataException("Supported table operations are count and sum.");
        }

        internal void AddDelimited(string field)
        {
            if (field.Length == 0) return;
            nonEmpty++;
            double number;
            if (TryNumber(field, out number)) AddNumber(number);
        }

        internal void AddCell(string value, bool isNumber, bool isError, bool uncalculated)
        {
            if (value != null || uncalculated) nonEmpty++;
            if (isError) errors++;
            if (uncalculated) missingCache++;
            if (!isNumber) return;
            double number;
            if (TryNumber(value, out number)) AddNumber(number);
            else errors++;
        }

        internal static bool TryNumber(string value, out double number)
        {
            return Double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) &&
                !Double.IsNaN(number) && !Double.IsInfinity(number);
        }

        void AddNumber(double number)
        {
            numeric++;
            if (overflow) return;
            double adjusted = number - correction;
            double next = sum + adjusted;
            if (Double.IsInfinity(next) || Double.IsNaN(next)) { overflow = true; return; }
            correction = (next - sum) - adjusted;
            sum = next;
        }

        internal string Format(string operation)
        {
            string result = operation == "sum" ? "SUM=" + (overflow ? "overflow" : sum.ToString("G15", CultureInfo.InvariantCulture)) : "COUNT=" + numeric;
            return result + ", NumericCells=" + numeric + ", NonEmpty=" + nonEmpty + ", Errors=" + errors + ", MissingFormulaCaches=" + missingCache + "\n";
        }
    }
}
