using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using ExcelDataReader;
using ExcelDataReader.Exceptions;

namespace Dafeiyu
{
    internal sealed class LegacyWorkbookReader : IFileContentReader
    {
        internal static bool CanRead(string path, CancellationToken token)
        {
            try
            {
                using (var book = Open(path, token)) return book.ResultsCount > 0;
            }
            catch (InvalidPasswordException) { return true; }
            catch (ExcelReaderException) { return false; }
        }

        public FileManifest Inspect(string path, CancellationToken token)
        {
            using (var book = Open(path, token))
            {
                var sheets = new List<FileSection>();
                do
                {
                    token.ThrowIfCancellationRequested();
                    sheets.Add(new FileSection(SectionId(sheets.Count + 1), book.Name, book.RowCount));
                }
                while (book.NextResult());
                return new FileManifest("xls", sheets);
            }
        }

        public FileReadResult Read(string path, FileReadRequest request, CancellationToken token)
        {
            bool aggregate = TableAggregate.IsRequested(request);
            using (var book = Open(path, token))
            {
                Select(book, request.Section, token);
                int lastColumn = Math.Min(256, TabularRange.End(request.StartColumn, request.ColumnCount));
                int requestedEnd = TabularRange.End(request.Start, request.Count);
                if (request.StartColumn < 1 || lastColumn < request.StartColumn || request.Start < 1 || request.Start > 65536)
                    throw new InvalidDataException("The requested XLS range is outside the worksheet.");
                int end = request.Start - 1;
                int? next = null;
                var summary = new TableAggregate();
                var output = new TabularReadBuffer();
                var formats = new WorkbookStyles();
                for (int row = 1; book.Read(); row++)
                {
                    token.ThrowIfCancellationRequested();
                    if (row < request.Start) continue;
                    if (row > requestedEnd) { next = row; break; }
                    var line = new StringBuilder();
                    for (int col = request.StartColumn; col <= Math.Min(lastColumn, book.FieldCount); col++)
                    {
                        object value = book.GetValue(col - 1);
                        var error = book.GetCellError(col - 1);
                        if (value == null && !error.HasValue) continue;
                        string raw = error.HasValue ? ErrorValue(error.Value) : RawValue(value);
                        if (aggregate)
                        {
                            summary.AddCell(raw, value is double || value is int, error.HasValue, false);
                            continue;
                        }
                        if (line.Length >= 32 * 1024) { line.Append("[row fields truncated. Select a narrower column range.]\n"); break; }
                        string format = book.GetNumberFormatString(col - 1) ?? "General";
                        string display = formats.Display(raw, format, value is bool ? null : value, false);
                        line.Append(TabularRange.ColumnName(col)).Append(row).Append(" | RawValue=");
                        TabularReadBuffer.Quote(line, raw);
                        line.Append(" | DisplayValue=");
                        TabularReadBuffer.Quote(line, display);
                        line.Append(" | NumberFormat=");
                        TabularReadBuffer.Quote(line, format);
                        line.Append('\n');
                    }
                    if (!aggregate && line.Length > 0 && !output.Append(line.ToString())) { next = row; break; }
                    end = row;
                }
                string range = TabularRange.ForRows(book.Name, request.Start, end, request.StartColumn, lastColumn);
                return new FileReadResult(aggregate ? summary.Format(request.Operation) : output.Text, range, next);
            }
        }

        static void Select(IExcelDataReader book, string section, CancellationToken token)
        {
            int current = 1;
            // Select the first sheet with actual data for an unqualified preview.
            if (String.IsNullOrEmpty(section))
            {
                int first = 0;
                do
                {
                    while (first == 0 && book.Read())
                    {
                        token.ThrowIfCancellationRequested();
                        for (int col = 0; col < book.FieldCount; col++)
                            if (book.GetValue(col) != null || book.GetCellError(col).HasValue) { first = current; break; }
                    }
                    if (first != 0) break;
                    current++;
                }
                while (book.NextResult());
                book.Reset();
                for (current = 1; current < first; current++) book.NextResult();
                return;
            }
            int index;
            bool byIndex = Int32.TryParse(section, NumberStyles.None, CultureInfo.InvariantCulture, out index);
            do
            {
                token.ThrowIfCancellationRequested();
                if (byIndex ? current == index : String.Equals(book.Name, section, StringComparison.OrdinalIgnoreCase)) return;
                current++;
            }
            while (book.NextResult());
            throw new InvalidDataException("The requested worksheet does not exist.");
        }

        static string SectionId(int index) { return index.ToString(CultureInfo.InvariantCulture); }

        static string RawValue(object value)
        {
            if (value is DateTime) return ((DateTime)value).ToString("o", CultureInfo.InvariantCulture);
            if (value is bool) return (bool)value ? "TRUE" : "FALSE";
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        static string ErrorValue(CellError error)
        {
            switch (error)
            {
                case CellError.NULL: return "#NULL!";
                case CellError.DIV0: return "#DIV/0!";
                case CellError.VALUE: return "#VALUE!";
                case CellError.REF: return "#REF!";
                case CellError.NAME: return "#NAME?";
                case CellError.NUM: return "#NUM!";
                case CellError.NA: return "#N/A";
                default: return "#GETTING_DATA";
            }
        }

        static IExcelDataReader Open(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            TextFileEncoding.RegisterCodePages();
            var stream = new CancellableFileStream(path, token);
            try { return ExcelReaderFactory.CreateBinaryReader(stream); }
            catch { stream.Dispose(); throw; }
        }

        sealed class CancellableFileStream : FileStream
        {
            readonly CancellationToken token;

            internal CancellableFileStream(string path, CancellationToken token) : base(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            {
                this.token = token;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                token.ThrowIfCancellationRequested();
                return base.Read(buffer, offset, count);
            }

            public override int ReadByte()
            {
                token.ThrowIfCancellationRequested();
                return base.ReadByte();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                token.ThrowIfCancellationRequested();
                return base.Seek(offset, origin);
            }
        }
    }
}
