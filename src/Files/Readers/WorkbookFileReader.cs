using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;

namespace Dafeiyu
{
    internal sealed class WorkbookFileReader : IFileContentReader
    {
        const int MaxColumn = 16384, MaxRow = 1048576;
        static readonly Regex StringEscape = new Regex("_x([0-9a-fA-F]{4})_", RegexOptions.CultureInvariant);
        static readonly Regex FormulaToken = new Regex(@"(?<![\p{L}\p{N}_.])(\$?)([A-Za-z]{1,3})(\$?)([1-9][0-9]{0,6})(?![\p{L}\p{N}_.]|\s*\()", RegexOptions.CultureInvariant);
        static readonly Regex EntireRowOrColumn = new Regex(@"(?<![\p{L}\p{N}_.])(?:(?:\$?[A-Za-z]{1,3}\s*:\s*\$?[A-Za-z]{1,3})|(?:\$?[0-9]+\s*:\s*\$?[0-9]+))(?![\p{L}\p{N}_.])",
            RegexOptions.CultureInvariant);

        public FileManifest Inspect(string path, CancellationToken token)
        {
            using (var package = new WorkbookPackage(path, token))
            {
                var book = ReadBook(package);
                return new FileManifest("xlsx", book.Sheets.Select(sheet => new FileSection(sheet.Id, sheet.Name)),
                    book.Date1904 ? "1904 date system" : "1900 date system");
            }
        }

        public FileReadResult Read(string path, FileReadRequest request, CancellationToken token)
        {
            bool aggregate = TableAggregate.IsRequested(request);
            if (request.StartColumn < 1 || request.StartColumn > MaxColumn || request.Start < 1 || request.Start > MaxRow)
                throw new InvalidDataException("The requested XLSX range is outside the worksheet.");
            int lastColumn = Math.Min(MaxColumn, TabularRange.End(request.StartColumn, request.ColumnCount));
            int requestedEnd = Math.Min(MaxRow, TabularRange.End(request.Start, request.Count));
            using (var package = new WorkbookPackage(path, token))
            {
                var book = ReadBook(package);
                Sheet sheet = null;
                SheetMetadata metadata = null;
                if (String.IsNullOrEmpty(request.Section))
                {
                    foreach (var candidate in book.Sheets)
                    {
                        var candidateMetadata = ReadMetadata(package, candidate, request.Start, requestedEnd, request.StartColumn, lastColumn);
                        if (sheet == null || candidateMetadata.HasContent)
                        {
                            sheet = candidate;
                            metadata = candidateMetadata;
                        }
                        if (candidateMetadata.HasContent) break;
                    }
                }
                else
                {
                    sheet = book.Sheets.FirstOrDefault(s => s.Id == request.Section) ??
                        book.Sheets.FirstOrDefault(s => String.Equals(s.Name, request.Section, StringComparison.OrdinalIgnoreCase));
                    if (sheet == null) throw new InvalidDataException("The requested worksheet does not exist.");
                    metadata = ReadMetadata(package, sheet, request.Start, requestedEnd, request.StartColumn, lastColumn);
                }
                if (sheet == null || metadata == null) throw new InvalidDataException("The workbook has no worksheets.");
                int lastRow = Math.Min(requestedEnd, metadata.LastRow);
                var styles = WorkbookStyles.Read(package, aggregate ? null : book.Styles);
                var strings = ReadSharedStrings(package, book.SharedStrings);
                return ReadRange(package, request, book, sheet, metadata, styles, strings, lastRow, lastColumn, aggregate, token);
            }
        }

        static FileReadResult ReadRange(WorkbookPackage package, FileReadRequest request, Book book, Sheet sheet, SheetMetadata metadata,
            WorkbookStyles styles, IList<string> strings, int lastRow, int lastColumn, bool aggregate, CancellationToken token)
        {
            string conditionalNote = metadata.HasConditionalFormatting && !aggregate
                ? "ConditionalFormatting=present (static Fill displayed; conditional rules not evaluated)\n" : "";
            var output = new TabularReadBuffer(Encoding.UTF8.GetByteCount(conditionalNote));
            var summary = new TableAggregate();
            var merges = metadata.Merges.OrderBy(merge => merge.FirstRow).ToList();
            var activeMerges = new List<CellRange>();
            int mergeIndex = 0, end = request.Start - 1;
            int? next = lastRow < metadata.LastRow ? (int?)(lastRow + 1) : null;
            using (var cells = ReadCells(package, sheet, request.Start, lastRow, request.StartColumn, lastColumn).GetEnumerator())
            {
                bool hasCell = lastRow >= request.Start && cells.MoveNext();
                for (int row = request.Start; row <= lastRow; row++)
                {
                    token.ThrowIfCancellationRequested();
                    while (mergeIndex < merges.Count && merges[mergeIndex].FirstRow <= row)
                        activeMerges.Add(merges[mergeIndex++]);
                    activeMerges.RemoveAll(merge => merge.LastRow < row);
                    var line = new StringBuilder();
                    for (int column = request.StartColumn; column <= lastColumn; column++)
                    {
                        Cell cell = null;
                        if (hasCell && cells.Current.Row == row && cells.Current.Column == column)
                        {
                            cell = cells.Current;
                            hasCell = cells.MoveNext();
                        }
                        CellRange merge = aggregate ? null : activeMerges.FirstOrDefault(m => m.FirstColumn <= column && m.LastColumn >= column);
                        if (cell == null && merge == null) continue;
                        if (cell == null) cell = new Cell { Row = row, Column = column };
                        bool error;
                        string value = cell.Value(strings, out error);
                        bool numeric = !error && cell.IsNumeric && value != null;
                        if (aggregate) summary.AddCell(value, numeric, error, cell.HasFormula && !cell.HasCache);
                        else if (line.Length < 32 * 1024)
                            AppendCell(line, cell, value, numeric, error, styles, book.Date1904, metadata, merge);
                        else if (line.Length <= 64 * 1024)
                        {
                            line.Append("[row fields truncated. Select a narrower column range.]\n");
                            break;
                        }
                    }
                    // Consume the remainder of the row if its rendered cells exceeded the output budget.
                    while (hasCell && cells.Current.Row == row) hasCell = cells.MoveNext();
                    if (!aggregate && line.Length > 0 && !output.Append(line.ToString())) { next = row; break; }
                    end = row;
                }
            }
            string range = TabularRange.ForRows(sheet.Name, request.Start, end, request.StartColumn, lastColumn);
            string text = aggregate ? summary.Format(request.Operation) : conditionalNote + output.Text;
            return new FileReadResult(text, range, next);
        }

        static void AppendCell(StringBuilder line, Cell cell, string value, bool numeric, bool error, WorkbookStyles styles, bool date1904,
            SheetMetadata metadata, CellRange merge)
        {
            string numberFormat, fill;
            styles.Lookup(cell.Style, out numberFormat, out fill);
            double number;
            DateTime date;
            object formatValue = numeric && TableAggregate.TryNumber(value, out number) ? (object)number : value;
            if (cell.Type == "d" && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out date))
                formatValue = date;
            string display = error ? value : cell.Type == "b" && value != null ? (value == "1" ? "TRUE" : "FALSE") :
                styles.Display(value, numberFormat, formatValue, date1904);
            line.Append(TabularRange.ColumnName(cell.Column)).Append(cell.Row);
            Field(line, "RawValue", value);
            Field(line, "DisplayValue", display);
            Field(line, "NumberFormat", numberFormat);
            if (error) Field(line, "CellType", "error");
            if (fill != null) Field(line, "Fill", fill);
            if (merge != null) Field(line, "MergeRange", merge.Reference);
            if (cell.HasFormula)
            {
                string formula = cell.Formula;
                SharedFormula anchor = null;
                if (cell.FormulaType == "shared" && cell.SharedIndex != null)
                {
                    metadata.Formulas.TryGetValue(cell.SharedIndex, out anchor);
                    if (anchor != null && String.IsNullOrEmpty(formula))
                        formula = anchor.Translate(cell.Row, cell.Column);
                }
                if (!String.IsNullOrEmpty(formula)) Field(line, "Formula", formula);
                Field(line, "FormulaCacheState", cell.HasCache ? "cached" : "missing");
                if (cell.HasCache) Field(line, "CachedValue", value);
                if (cell.FormulaType == "shared")
                {
                    string provenance = anchor != null
                        ? "shared anchor " + anchor.Address + " in " + anchor.Range
                        : "shared anchor unavailable";
                    Field(line, "FormulaProvenance", provenance);
                    if (anchor != null && String.IsNullOrEmpty(formula)) Field(line, "SharedFormulaTemplate", anchor.Template);
                }
            }
            line.Append('\n');
        }

        static void Field(StringBuilder output, string name, string value)
        {
            output.Append(" | ").Append(name).Append('=');
            if (value == null) { output.Append("null"); return; }
            if (value.Length > 8192)
            {
                int end = Char.IsHighSurrogate(value[8191]) ? 8191 : 8192;
                value = value.Substring(0, end) + " [cell value truncated]";
            }
            TabularReadBuffer.Quote(output, value);
        }

        static Book ReadBook(WorkbookPackage package)
        {
            var office = package.ReadRelationships("").FirstOrDefault(r => r.Type == "officeDocument");
            if (office == null) throw new InvalidDataException("The workbook root relationship is missing.");
            var relations = package.ReadRelationships(office.Path);
            var book = new Book();
            var strings = relations.FirstOrDefault(r => r.Type == "sharedStrings");
            var styles = relations.FirstOrDefault(r => r.Type == "styles");
            book.SharedStrings = strings == null ? null : strings.Path;
            book.Styles = styles == null ? null : styles.Path;
            var worksheets = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var relation in relations)
                if (relation.Type == "worksheet" && !String.IsNullOrEmpty(relation.Id))
                    worksheets.Add(relation.Id, relation.Path);
            using (var xml = package.OpenXml(office.Path))
                while (package.Read(xml))
                {
                    if (xml.NodeType != XmlNodeType.Element || !WorkbookPackage.IsSpreadsheet(xml)) continue;
                    if (xml.Depth == 1 && xml.LocalName == "workbookPr")
                    {
                        string date1904 = xml.GetAttribute("date1904");
                        book.Date1904 = date1904 == "1" || date1904 == "true";
                    }
                    else if (xml.Depth == 2 && xml.LocalName == "sheet")
                    {
                        string id = WorkbookPackage.RelationId(xml), name = xml.GetAttribute("name"), target;
                        if (id != null && worksheets.TryGetValue(id, out target))
                            book.Sheets.Add(new Sheet((book.Sheets.Count + 1).ToString(CultureInfo.InvariantCulture), name, target));
                    }
                }
            if (book.Sheets.Count == 0) throw new InvalidDataException("The workbook has no worksheets.");
            return book;
        }

        static SheetMetadata ReadMetadata(WorkbookPackage package, Sheet sheet, int start, int end, int startColumn, int endColumn)
        {
            var result = new SheetMetadata();
            int sheetData = -1, row = 0, column = 0;
            using (var xml = package.OpenXml(sheet.Path))
                while (package.Read(xml))
                {
                    if (xml.NodeType == XmlNodeType.EndElement && xml.Depth == sheetData) sheetData = -1;
                    if (xml.NodeType != XmlNodeType.Element || !WorkbookPackage.IsSpreadsheet(xml)) continue;
                    if (xml.Depth == 1 && xml.LocalName == "sheetData" && !xml.IsEmptyElement) sheetData = xml.Depth;
                    else if (sheetData >= 0 && xml.Depth == sheetData + 1 && xml.LocalName == "row")
                    {
                        row = ReadRowIndex(xml, row);
                        result.LastRow = row;
                        column = 0;
                    }
                    else if (sheetData >= 0 && xml.Depth == sheetData + 2 && xml.LocalName == "c")
                        ReadColumn(xml, row, ref column);
                    else if (sheetData >= 0 && xml.Depth == sheetData + 3)
                    {
                        if (!result.HasContent && (xml.LocalName == "v" || xml.LocalName == "is") && !xml.IsEmptyElement)
                            result.HasContent = ReadText(package, xml, xml.LocalName == "is").Length > 0;
                        if (xml.LocalName == "f")
                        {
                            result.HasContent = true;
                            if (xml.GetAttribute("t") != "shared" || xml.IsEmptyElement) continue;
                            string index = xml.GetAttribute("si"), bounds = xml.GetAttribute("ref");
                            if (index != null && bounds != null && column > 0)
                            {
                                string template = ReadText(package, xml, false);
                                result.Formulas[index] = new SharedFormula(TabularRange.ColumnName(column) + row, bounds, template, row, column);
                            }
                        }
                    }
                    else if (xml.Depth == 2 && xml.LocalName == "mergeCell")
                    {
                        CellRange merge = CellRange.Parse(xml.GetAttribute("ref"));
                        if (merge == null) continue;
                        result.LastRow = Math.Max(result.LastRow, merge.LastRow);
                        if (merge.LastRow >= start && merge.FirstRow <= end && merge.LastColumn >= startColumn && merge.FirstColumn <= endColumn)
                            result.Merges.Add(merge);
                    }
                    else if (xml.Depth == 1 && xml.LocalName == "conditionalFormatting") result.HasConditionalFormatting = true;
                    if (result.Merges.Count > 65536 || result.Formulas.Count > 65536)
                        throw new InvalidDataException("The workbook has too many overlapping merges or shared formulas.");
                }
            return result;
        }

        static IEnumerable<Cell> ReadCells(WorkbookPackage package, Sheet sheet, int start, int end, int startColumn, int endColumn)
        {
            int sheetData = -1, row = 0, column = 0;
            if (end < start) yield break;
            using (var xml = package.OpenXml(sheet.Path))
                while (package.Read(xml))
                {
                    if (xml.NodeType == XmlNodeType.EndElement && xml.Depth == sheetData) yield break;
                    if (xml.NodeType != XmlNodeType.Element || !WorkbookPackage.IsSpreadsheet(xml)) continue;
                    if (xml.Depth == 1 && xml.LocalName == "sheetData" && !xml.IsEmptyElement) sheetData = xml.Depth;
                    else if (sheetData >= 0 && xml.Depth == sheetData + 1 && xml.LocalName == "row")
                    {
                        row = ReadRowIndex(xml, row);
                        if (row > end) yield break;
                        column = 0;
                    }
                    else if (sheetData >= 0 && xml.Depth == sheetData + 2 && xml.LocalName == "c" && row >= start)
                    {
                        int index = ReadColumn(xml, row, ref column);
                        if (index >= startColumn && index <= endColumn) yield return ReadCell(package, xml, row, column);
                    }
                }
        }

        static int ReadRowIndex(XmlReader xml, int previous)
        {
            int row = WorkbookPackage.Integer(xml.GetAttribute("r"), previous + 1);
            if (row <= previous || row > MaxRow) throw new InvalidDataException("Invalid XLSX row coordinates.");
            return row;
        }

        static int ReadColumn(XmlReader xml, int row, ref int previous)
        {
            int index = previous + 1, givenRow, givenColumn;
            string reference = xml.GetAttribute("r");
            if (reference != null)
            {
                if (!WorkbookPackage.Address(reference, out givenRow, out givenColumn) || givenRow != row)
                    throw new InvalidDataException("Invalid XLSX cell coordinates.");
                index = givenColumn;
            }
            if (index <= previous || index > MaxColumn) throw new InvalidDataException("XLSX cells are out of coordinate order.");
            previous = index;
            return index;
        }

        static Cell ReadCell(WorkbookPackage package, XmlReader xml, int row, int col)
        {
            var cell = new Cell { Row = row, Column = col, Type = xml.GetAttribute("t"), Style = WorkbookPackage.Integer(xml.GetAttribute("s"), 0) };
            if (xml.IsEmptyElement) return cell;
            using (var content = xml.ReadSubtree())
                while (package.Read(content))
                {
                    if (content.Depth != 1 || content.NodeType != XmlNodeType.Element || !WorkbookPackage.IsSpreadsheet(content)) continue;
                    if (content.LocalName == "v") cell.Raw = ReadText(package, content, false);
                    else if (content.LocalName == "is") cell.Raw = ReadText(package, content, true);
                    else if (content.LocalName == "f")
                    {
                        cell.HasFormula = true;
                        cell.FormulaType = content.GetAttribute("t");
                        cell.SharedIndex = content.GetAttribute("si");
                        cell.Formula = ReadText(package, content, false);
                    }
                }
            return cell;
        }

        static IList<string> ReadSharedStrings(WorkbookPackage package, string path)
        {
            var values = new List<string>();
            if (path == null) return values;
            using (var xml = package.OpenXml(path))
                while (package.Read(xml))
                    if (xml.Depth == 1 && xml.NodeType == XmlNodeType.Element && xml.LocalName == "si" && WorkbookPackage.IsSpreadsheet(xml))
                    {
                        values.Add(ReadText(package, xml, true));
                        if (values.Count > 2000000) throw new InvalidDataException("The workbook has too many shared strings.");
                    }
            return values;
        }

        static string ReadText(WorkbookPackage package, XmlReader element, bool rich)
        {
            if (element.IsEmptyElement) return "";
            var value = new StringBuilder();
            int textDepth = rich ? -1 : 0, phoneticDepth = -1;
            using (var xml = element.ReadSubtree())
                while (package.Read(xml))
                {
                    if (xml.NodeType == XmlNodeType.Element && !xml.IsEmptyElement && WorkbookPackage.IsSpreadsheet(xml))
                    {
                        if (xml.LocalName == "rPh") phoneticDepth = xml.Depth;
                        if (rich && xml.LocalName == "t" && phoneticDepth < 0) textDepth = xml.Depth;
                    }
                    else if (xml.NodeType == XmlNodeType.EndElement)
                    {
                        if (xml.Depth == phoneticDepth) phoneticDepth = -1;
                        if (xml.Depth == textDepth) textDepth = -1;
                    }
                    else if (textDepth >= 0 && xml.Depth == textDepth + 1 && (xml.NodeType == XmlNodeType.Text || xml.NodeType == XmlNodeType.CDATA ||
                        xml.NodeType == XmlNodeType.SignificantWhitespace || xml.NodeType == XmlNodeType.Whitespace))
                        package.AppendValue(xml, value);
                }
            return rich ? Decode(value.ToString()) : value.ToString();
        }

        static string Decode(string text)
        {
            return StringEscape.Replace(text, match => ((char)Int32.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());
        }

        sealed class Book
        {
            internal readonly List<Sheet> Sheets = new List<Sheet>();
            internal string SharedStrings, Styles;
            internal bool Date1904;
        }

        sealed class Sheet
        {
            internal readonly string Id, Name, Path;
            internal Sheet(string id, string name, string path) { Id = id; Name = name ?? id; Path = path; }
        }

        sealed class SheetMetadata
        {
            internal readonly List<CellRange> Merges = new List<CellRange>();
            internal readonly Dictionary<string, SharedFormula> Formulas = new Dictionary<string, SharedFormula>(StringComparer.Ordinal);
            internal int LastRow;
            internal bool HasContent, HasConditionalFormatting;
        }

        sealed class SharedFormula
        {
            internal readonly string Address, Range, Template;
            readonly int row, column;

            internal SharedFormula(string address, string range, string template, int row, int column)
            {
                Address = address; Range = range; Template = template; this.row = row; this.column = column;
            }

            internal string Translate(int targetRow, int targetColumn)
            {
                var area = CellRange.Parse(Range);
                if (area == null || targetRow < area.FirstRow || targetRow > area.LastRow || targetColumn < area.FirstColumn ||
                    targetColumn > area.LastColumn || String.IsNullOrEmpty(Template)) return null;
                var text = new StringBuilder();
                // Split Excel string literals away from references. Adjacent quotes preserve escaped quotes.
                var segments = Template.Split('"');
                if (segments.Length % 2 == 0) return null;
                for (int i = 0; i < segments.Length; i++)
                {
                    if (i > 0) text.Append('"');
                    string segment = segments[i];
                    if ((i & 1) == 1) { text.Append(segment); continue; }
                    // Preserve the anchor template for cross-sheet, structured, and entire-row/column references.
                    if (segment.IndexOfAny(new[] { '\'', '!', '[', ']', '@', '#', '{', '}' }) >= 0 || EntireRowOrColumn.IsMatch(segment))
                        return null;
                    text.Append(FormulaToken.Replace(segment, match =>
                    {
                        string columnPin = match.Groups[1].Value, rowPin = match.Groups[3].Value;
                        int oldRow, oldColumn;
                        if (!WorkbookPackage.Address(match.Groups[2].Value + match.Groups[4].Value, out oldRow, out oldColumn))
                            return match.Value;
                        int shiftedRow = rowPin.Length == 0 ? oldRow + targetRow - row : oldRow;
                        int shiftedColumn = columnPin.Length == 0 ? oldColumn + targetColumn - column : oldColumn;
                        if (shiftedRow < 1 || shiftedRow > MaxRow || shiftedColumn < 1 || shiftedColumn > MaxColumn) return "#REF!";
                        return columnPin + TabularRange.ColumnName(shiftedColumn) + rowPin + shiftedRow.ToString(CultureInfo.InvariantCulture);
                    }));
                }
                return text.ToString();
            }
        }

        sealed class CellRange
        {
            internal int FirstRow, LastRow, FirstColumn, LastColumn;
            internal string Reference;

            internal static CellRange Parse(string reference)
            {
                if (String.IsNullOrEmpty(reference)) return null;
                var pair = reference.Split(':');
                int firstRow, firstColumn, lastRow, lastColumn;
                if (pair.Length != 2 || !WorkbookPackage.Address(pair[0], out firstRow, out firstColumn) ||
                    !WorkbookPackage.Address(pair[1], out lastRow, out lastColumn) || lastRow < firstRow || lastColumn < firstColumn) return null;
                return new CellRange { FirstRow = firstRow, LastRow = lastRow, FirstColumn = firstColumn, LastColumn = lastColumn, Reference = reference };
            }
        }

        sealed class Cell
        {
            internal int Row, Column, Style;
            internal string Raw, Type, Formula, FormulaType, SharedIndex;
            internal bool HasFormula;
            internal bool HasCache { get { return Raw != null && (Raw.Length != 0 || Type == "str"); } }
            internal bool IsNumeric { get { return String.IsNullOrEmpty(Type) || Type == "n"; } }

            internal string Value(IList<string> strings, out bool error)
            {
                error = Type == "e";
                if (Raw == null || Raw.Length == 0 && IsNumeric || HasFormula && !HasCache) return null;
                if (Type == "s")
                {
                    int index = WorkbookPackage.Integer(Raw, -1);
                    if (index >= 0 && index < strings.Count) return strings[index];
                    error = true;
                    return Raw;
                }
                if (Type == "str") return Decode(Raw);
                return Raw;
            }
        }
    }
}
