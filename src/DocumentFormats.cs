using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;

namespace Dafeiyu
{
    // Reads and writes real Word/Excel documents. OOXML (.docx/.xlsx) uses only
    // built-in components; legacy binary .doc/.xls use the installed Word/Excel
    // or WPS through late-bound COM when it is available.
    internal static class DocumentFormats
    {
        internal const long MaxDocumentBytes = 8L * 1024 * 1024;
        const int MaxPartBytes = 4 * 1024 * 1024, MaxSheets = 8, MaxSheetColumns = 64, MaxSheetRows = 5000;
        const int MaxTextChars = 400000;
        internal static bool IsDocx(string path) { return String.Equals(Path.GetExtension(path), ".docx", StringComparison.OrdinalIgnoreCase); }
        internal static bool IsXlsx(string path) { return String.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase); }
        internal static bool IsLegacyWord(string path) { return String.Equals(Path.GetExtension(path), ".doc", StringComparison.OrdinalIgnoreCase); }
        internal static bool IsLegacyExcel(string path) { return String.Equals(Path.GetExtension(path), ".xls", StringComparison.OrdinalIgnoreCase); }
        internal static bool IsDocument(string path) { return IsDocx(path) || IsXlsx(path) || IsLegacyWord(path) || IsLegacyExcel(path); }

        internal static string ReadDocumentText(string path, byte[] bytes, CancellationToken token)
        {
            if (IsDocx(path)) return ReadDocxText(bytes, token);
            if (IsXlsx(path)) return ReadXlsxText(bytes, token);
            if (IsLegacyWord(path) || IsLegacyExcel(path)) return DocumentFormatsLegacy.ReadText(path, IsLegacyWord(path), token);
            throw new ArgumentException("这不是受支持的 Word/Excel 文档。");
        }

        // Returns the exact bytes to write. Legacy formats are produced through
        // the installed Office/WPS and re-read from a temporary file.
        internal static byte[] WriteDocumentBytes(string path, string content, CancellationToken token)
        {
            if (content == null) content = "";
            if (content.IndexOf('\0') >= 0) throw new ArgumentException("文档内容必须是文字。");
            if (IsDocx(path)) return WriteDocx(content);
            if (IsXlsx(path)) return WriteXlsx(content, token);
            if (IsLegacyWord(path) || IsLegacyExcel(path)) return DocumentFormatsLegacy.Write(path, content, IsLegacyWord(path), token);
            throw new ArgumentException("这不是受支持的 Word/Excel 文档。");
        }

        static XmlReaderSettings XmlSettings()
        {
            return new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxPartBytes, IgnoreComments = true, IgnoreProcessingInstructions = true };
        }

        static string ReadDocxText(byte[] bytes, CancellationToken token)
        {
            using (var memory = new MemoryStream(bytes, false))
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Read))
            {
                var entry = zip.GetEntry("word/document.xml");
                if (entry == null) throw new ArgumentException("不是有效的 .docx 文档（缺少 word/document.xml）。");
                if (entry.Length > MaxPartBytes) throw new ArgumentException(".docx 文字部分超过 4 MiB，请拆分文档。");
                var text = new StringBuilder(); bool inText = false, inCell = false; int paragraphs = 0;
                using (var stream = entry.Open())
                using (var reader = XmlReader.Create(stream, XmlSettings()))
                {
                    while (reader.Read())
                    {
                        token.ThrowIfCancellationRequested();
                        if (text.Length >= MaxTextChars) { text.AppendLine("[文档文字过长，已截断；请拆分后读取]"); break; }
                        if (reader.NodeType == XmlNodeType.Element)
                        {
                            string name = reader.LocalName;
                            if (name == "t") inText = true;
                            else if (name == "tab") text.Append('\t');
                            else if (name == "br") text.Append('\n');
                            else if (name == "tc") inCell = true;
                        }
                        else if (reader.NodeType == XmlNodeType.EndElement)
                        {
                            string name = reader.LocalName;
                            if (name == "t") inText = false;
                            else if (name == "tc") { inCell = false; text.Append('\t'); }
                            else if (name == "tr") text.Append('\n');
                            else if (name == "p") { paragraphs++; text.Append(inCell ? ' ' : '\n'); }
                        }
                        else if (reader.NodeType == XmlNodeType.Text && inText) text.Append(reader.Value);
                    }
                }
                if (paragraphs == 0 && text.Length == 0) return "文档没有可读取的文字。";
                var lines = text.ToString().Split('\n').Select(l => l.TrimEnd('\t', ' ')).ToList();
                while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
                return lines.Count == 0 ? "文档没有可读取的文字。" : String.Join("\n", lines);
            }
        }

        static List<string> ReadSharedStrings(ZipArchive zip, CancellationToken token)
        {
            var result = new List<string>();
            var entry = zip.GetEntry("xl/sharedStrings.xml");
            if (entry == null) return result;
            if (entry.Length > MaxPartBytes) throw new ArgumentException(".xlsx 共享字符串超过 4 MiB，请拆分表格。");
            using (var stream = entry.Open())
            using (var reader = XmlReader.Create(stream, XmlSettings()))
            {
                var current = new StringBuilder(); bool collecting = false, inText = false;
                while (reader.Read())
                {
                    token.ThrowIfCancellationRequested();
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        if (reader.LocalName == "si") { collecting = true; current.Length = 0; }
                        else if (reader.LocalName == "t") inText = true;
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        if (reader.LocalName == "t") inText = false;
                        else if (reader.LocalName == "si") { collecting = false; result.Add(current.ToString()); }
                    }
                    else if (reader.NodeType == XmlNodeType.Text && collecting && inText) current.Append(reader.Value);
                }
            }
            return result;
        }

        static List<KeyValuePair<string, string>> ReadSheetNames(ZipArchive zip)
        {
            var sheets = new List<KeyValuePair<string, string>>();
            var rels = new Dictionary<string, string>();
            var relEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
            if (relEntry != null)
            {
                using (var stream = relEntry.Open())
                using (var reader = XmlReader.Create(stream, XmlSettings()))
                    while (reader.Read())
                        if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Relationship")
                        {
                            string id = reader.GetAttribute("Id"), target = reader.GetAttribute("Target");
                            if (!String.IsNullOrEmpty(id) && !String.IsNullOrEmpty(target)) rels[id] = target;
                        }
            }
            var bookEntry = zip.GetEntry("xl/workbook.xml");
            if (bookEntry != null)
            {
                using (var stream = bookEntry.Open())
                using (var reader = XmlReader.Create(stream, XmlSettings()))
                    while (reader.Read())
                        if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "sheet")
                        {
                            string name = reader.GetAttribute("name");
                            string id = reader.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                            string target = null;
                            if (id != null) rels.TryGetValue(id, out target);
                            sheets.Add(new KeyValuePair<string, string>(name == null ? "Sheet" : name, NormalizePart(target) ?? ("xl/worksheets/sheet" + (sheets.Count + 1) + ".xml")));
                        }
            }
            if (sheets.Count == 0)
            {
                foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
                    sheets.Add(new KeyValuePair<string, string>(Path.GetFileNameWithoutExtension(entry.FullName), entry.FullName));
            }
            return sheets;
        }

        static string NormalizePart(string target)
        {
            if (String.IsNullOrEmpty(target)) return null;
            if (target.StartsWith("/", StringComparison.Ordinal)) return target.TrimStart('/');
            if (target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)) return target;
            return "xl/" + target;
        }

        static string ReadXlsxText(byte[] bytes, CancellationToken token)
        {
            using (var memory = new MemoryStream(bytes, false))
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Read))
            {
                var shared = ReadSharedStrings(zip, token);
                var names = ReadSheetNames(zip);
                if (names.Count == 0) throw new ArgumentException("不是有效的 .xlsx 表格（缺少工作表）。");
                var output = new StringBuilder(); int shown = 0;
                foreach (var sheet in names)
                {
                    if (++shown > MaxSheets) { output.AppendLine("[工作表过多，仅显示前 " + MaxSheets + " 个]"); break; }
                    var entry = zip.GetEntry(sheet.Value);
                    if (entry == null) continue;
                    if (entry.Length > MaxPartBytes) throw new ArgumentException("工作表 " + sheet.Key + " 超过 4 MiB，请拆分表格。");
                    output.Append("[工作表 ").Append(sheet.Key).AppendLine("]");
                    ReadSheet(entry, shared, output, token);
                }
                return output.ToString().TrimEnd('\n');
            }
        }

        static void ReadSheet(ZipArchiveEntry entry, List<string> shared, StringBuilder output, CancellationToken token)
        {
            var row = new StringBuilder(); int tabs = 0, next = 0, rows = 0; bool inValue = false, inInline = false; string type = null; var value = new StringBuilder(); int cellColumn = -1;
            using (var stream = entry.Open())
            using (var reader = XmlReader.Create(stream, XmlSettings()))
            {
                while (reader.Read())
                {
                    token.ThrowIfCancellationRequested();
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        string name = reader.LocalName;
                        if (name == "row") { row.Length = 0; tabs = 0; next = 0; if (++rows > MaxSheetRows) { output.AppendLine("[行数过多，已截断]"); return; } }
                        else if (name == "c") { type = reader.GetAttribute("t"); cellColumn = ColumnIndex(reader.GetAttribute("r")); value.Length = 0; }
                        else if (name == "v") inValue = true;
                        else if (name == "is") inInline = true;
                        else if (name == "t") inValue = inInline;
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        string name = reader.LocalName;
                        if (name == "v") inValue = false;
                        else if (name == "is") inInline = false;
                        else if (name == "t") { if (inInline) inValue = false; }
                        else if (name == "c")
                        {
                            int target = cellColumn >= 0 ? cellColumn : next;
                            if (target < MaxSheetColumns)
                            {
                                while (tabs < target) { row.Append('\t'); tabs++; }
                                row.Append(CellText(type, value.ToString(), shared));
                                next = target + 1;
                            }
                        }
                        else if (name == "row") output.AppendLine(row.ToString());
                    }
                    else if (reader.NodeType == XmlNodeType.Text && inValue) value.Append(reader.Value);
                }
            }
            if (row.Length > 0) output.AppendLine(row.ToString());
        }

        static string CellText(string type, string raw, List<string> shared)
        {
            if (String.IsNullOrEmpty(raw)) return "";
            string text;
            if (type == "s")
            {
                int index;
                text = Int32.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out index) && index >= 0 && index < shared.Count ? shared[index] : "";
            }
            else if (type == "b") text = raw == "1" ? "TRUE" : "FALSE";
            else text = raw;
            return text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
        }

        static int ColumnIndex(string reference)
        {
            if (String.IsNullOrEmpty(reference)) return -1;
            int index = 0, letters = 0;
            foreach (char ch in reference)
            {
                if (ch >= 'A' && ch <= 'Z') { index = index * 26 + (ch - 'A' + 1); letters++; }
                else if (ch >= 'a' && ch <= 'z') { index = index * 26 + (ch - 'a' + 1); letters++; }
                else break;
            }
            return letters == 0 ? -1 : index - 1;
        }

        static string XmlEscape(string text) { return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;"); }

        static void AddPart(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var stream = entry.Open()) using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(content);
        }

        static byte[] Package(Action<ZipArchive> build)
        {
            using (var memory = new MemoryStream())
            {
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true)) build(zip);
                return memory.ToArray();
            }
        }

        // A minimal but valid OOXML package. "# " lines become headings, "**"
        // marks bold runs, and every other line is a normal paragraph.
        internal static byte[] WriteDocx(string text)
        {
            var body = new StringBuilder();
            string[] lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string line in lines)
            {
                string title = line;
                int level = 0;
                while (level < 3 && title.StartsWith("#", StringComparison.Ordinal)) { level++; title = title.Substring(1); }
                if (level > 0 && title.StartsWith(" ", StringComparison.Ordinal))
                {
                    AppendParagraph(body, title.Substring(1), "Heading" + level);
                    continue;
                }
                AppendParagraph(body, line, null);
            }
            string document = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" + body + "<w:sectPr/></w:body></w:document>";
            return Package(delegate(ZipArchive zip)
            {
                AddPart(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                    "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/></Types>");
                AddPart(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
                AddPart(zip, "word/_rels/document.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
                AddPart(zip, "word/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                    "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>" +
                    "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:outlineLvl w:val=\"0\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"32\"/></w:rPr></w:style>" +
                    "<w:style w:type=\"paragraph\" w:styleId=\"Heading2\"><w:name w:val=\"heading 2\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:outlineLvl w:val=\"1\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"28\"/></w:rPr></w:style>" +
                    "<w:style w:type=\"paragraph\" w:styleId=\"Heading3\"><w:name w:val=\"heading 3\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:outlineLvl w:val=\"2\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"24\"/></w:rPr></w:style></w:styles>");
                AddPart(zip, "word/document.xml", document);
            });
        }

        static void AppendParagraph(StringBuilder body, string line, string style)
        {
            body.Append("<w:p>");
            if (style != null) body.Append("<w:pPr><w:pStyle w:val=\"").Append(style).Append("\"/></w:pPr>");
            if (line.Length > 0)
            {
                string[] parts = line.Split(new[] { "**" }, StringSplitOptions.None);
                bool bold = parts.Length % 2 == 1;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (parts[i].Length == 0) continue;
                    body.Append("<w:r>");
                    if (bold && i % 2 == 1) body.Append("<w:rPr><w:b/></w:rPr>");
                    body.Append("<w:t xml:space=\"preserve\">").Append(XmlEscape(parts[i])).Append("</w:t></w:r>");
                }
            }
            body.Append("</w:p>");
        }

        static string ColumnName(int index)
        {
            var name = new StringBuilder(); index++;
            while (index > 0) { int remainder = (index - 1) % 26; name.Insert(0, (char)('A' + remainder)); index = (index - 1) / 26; }
            return name.ToString();
        }

        // Accepts tab-separated text, or comma-separated text with optional
        // double quotes, and produces one worksheet.
        internal static byte[] WriteXlsx(string text, CancellationToken token)
        {
            var rows = ParseGrid(text, token);
            if (rows.Count > MaxSheetRows) throw new ArgumentException("表格超过 " + MaxSheetRows + " 行，请拆分后再写入。");
            if (rows.Any(r => r.Count > MaxSheetColumns)) throw new ArgumentException("表格超过 " + MaxSheetColumns + " 列，请拆分后再写入。");
            var sheet = new StringBuilder();
            for (int r = 0; r < rows.Count; r++)
            {
                sheet.Append("<row r=\"").Append(r + 1).Append("\">");
                for (int c = 0; c < rows[r].Count; c++)
                {
                    string value = rows[r][c];
                    string reference = ColumnName(c) + (r + 1);
                    if (IsNumber(value)) sheet.Append("<c r=\"").Append(reference).Append("\"><v>").Append(value).Append("</v></c>");
                    else sheet.Append("<c r=\"").Append(reference).Append("\" t=\"inlineStr\"><is><t xml:space=\"preserve\">").Append(XmlEscape(value)).Append("</t></is></c>");
                }
                sheet.Append("</row>");
            }
            string worksheet = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>" + sheet + "</sheetData></worksheet>";
            return Package(delegate(ZipArchive zip)
            {
                AddPart(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                    "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                    "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>");
                AddPart(zip, "_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                AddPart(zip, "xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                AddPart(zip, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
                AddPart(zip, "xl/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs></styleSheet>");
                AddPart(zip, "xl/worksheets/sheet1.xml", worksheet);
            });
        }

        static bool IsNumber(string value)
        {
            if (String.IsNullOrEmpty(value)) return false;
            if (value.Length > 1 && value[0] == '0') return false;
            return System.Text.RegularExpressions.Regex.IsMatch(value, @"^-?\d{1,15}(\.\d{1,10})?$");
        }

        static List<List<string>> ParseGrid(string text, CancellationToken token)
        {
            var rows = new List<List<string>>();
            if (String.IsNullOrEmpty(text)) return rows;
            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            bool tsv = normalized.IndexOf('\t') >= 0;
            foreach (string line in normalized.Split('\n'))
            {
                token.ThrowIfCancellationRequested();
                rows.Add(tsv ? line.Split('\t').ToList() : SplitCsv(line));
            }
            while (rows.Count > 1 && rows[rows.Count - 1].Count == 1 && rows[rows.Count - 1][0].Length == 0) rows.RemoveAt(rows.Count - 1);
            return rows;
        }

        static List<string> SplitCsv(string line)
        {
            var cells = new List<string>(); var cell = new StringBuilder(); bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (quoted)
                {
                    if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (ch == '"') quoted = false;
                    else cell.Append(ch);
                }
                else if (ch == '"') quoted = true;
                else if (ch == ',') { cells.Add(cell.ToString()); cell.Length = 0; }
                else cell.Append(ch);
            }
            cells.Add(cell.ToString());
            return cells;
        }
    }

    // Legacy .doc/.xls conversion through the installed Word/Excel or WPS.
    internal static class DocumentFormatsLegacy
    {
        static readonly string[] WordProgIds = { "KWPS.Application", "Word.Application" };
        static readonly string[] ExcelProgIds = { "KET.Application", "Excel.Application" };
        internal static bool WordAvailable { get { return Pick(WordProgIds) != null; } }
        internal static bool ExcelAvailable { get { return Pick(ExcelProgIds) != null; } }

        static string Pick(string[] ids)
        {
            foreach (string id in ids) if (Type.GetTypeFromProgID(id) != null) return id;
            return null;
        }

        static string UnavailableMessage(bool word)
        {
            return word
                ? ".doc 需要本机安装 Word 或 WPS 才能读写；也可另存为 .docx 后使用内置读写。"
                : ".xls 需要本机安装 Excel 或 WPS 才能读写；也可另存为 .xlsx 后使用内置读写。";
        }

        internal static void HardenApplication(object app,bool word)
        {
            Set(app,"Visible",false);
            Set(app,"DisplayAlerts",word?(object)0:(object)false);
            Set(app,"AutomationSecurity",3);
            if(word)SetOptional(app,"EnableEvents",false);else Set(app,"EnableEvents",false);
        }

        internal static string ReadText(string path, bool word, CancellationToken token)
        {
            object cleanupApp=null;Action cleanup=delegate{object value=Interlocked.Exchange(ref cleanupApp,null);if(value!=null){try{Invoke(value,"Quit");}catch(Exception){}ReleaseComObject(value);}};
            return RunSta(delegate(CancellationToken workToken)
            {
                string prog = Pick(word ? WordProgIds : ExcelProgIds);
                if (prog == null) throw new ArgumentException(UnavailableMessage(word));
                object app = null;
                try
                {
                    app = Activator.CreateInstance(Type.GetTypeFromProgID(prog));Interlocked.Exchange(ref cleanupApp,app);
                    HardenApplication(app, word);
                    if (word)
                    {
                        object documents = Get(app, "Documents");
                        object document = Invoke(documents, "Open", path, false, true, false);
                        try
                        {
                            object content = Get(document, "Content");
                            string text = Convert.ToString(Get(content, "Text"));
                            return NormalizeWordText(text);
                        }
                        finally { try { Invoke(document, "Close", 0); } catch (Exception) { } }
                    }
                    object books = Get(app, "Workbooks");
                    object book = Invoke(books, "Open", path, 0, true);
                    try
                    {
                        var output = new StringBuilder();
                        object sheets = Get(book, "Worksheets");
                        int count = Math.Min(Convert.ToInt32(Get(sheets, "Count")), 32);
                        for (int i = 1; i <= count; i++)
                        {
                            workToken.ThrowIfCancellationRequested();
                            object sheet = Invoke(sheets, "Item", i);
                            output.Append("[工作表 ").Append(Convert.ToString(Get(sheet, "Name"))).AppendLine("]");
                            object range = Get(sheet, "UsedRange");
                            AppendMatrix(output, Get(range, "Value2") as Array);
                        }
                        return output.ToString().TrimEnd('\n');
                    }
                    finally { try { Invoke(book, "Close", false); } catch (Exception) { } }
                }
                catch (ArgumentException) { throw; }
                catch (Exception error) { throw new ArgumentException(Failure(error)); }
                finally { cleanup(); }
            }, token, 60000, cleanup);
        }

        internal static byte[] Write(string path, string content, bool word, CancellationToken token)
        {
            object cleanupApp=null;Action cleanup=delegate{object value=Interlocked.Exchange(ref cleanupApp,null);if(value!=null){try{Invoke(value,"Quit");}catch(Exception){}ReleaseComObject(value);}};
            return RunSta(delegate(CancellationToken workToken)
            {
                string prog = Pick(word ? WordProgIds : ExcelProgIds);
                if (prog == null) throw new ArgumentException(UnavailableMessage(word));
                string temp = Path.Combine(Path.GetTempPath(), "deskpet-doc-" + Guid.NewGuid().ToString("N") + (word ? ".doc" : ".xls"));
                object app = null;
                try
                {
                    app = Activator.CreateInstance(Type.GetTypeFromProgID(prog));Interlocked.Exchange(ref cleanupApp,app);
                    HardenApplication(app, word);
                    if (word)
                    {
                        object documents = Get(app, "Documents");
                        object document = Invoke(documents, "Add");
                        try
                        {
                            object range = Get(document, "Content");
                            Set(range, "Text", content.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r"));
                            Invoke(document, "SaveAs", temp, 0);
                        }
                        finally { try { Invoke(document, "Close", 0); } catch (Exception) { } }
                    }
                    else
                    {
                        object books = Get(app, "Workbooks");
                        object book = Invoke(books, "Add");
                        try
                        {
                            object sheets = Get(book, "Worksheets");
                            object sheet = Invoke(sheets, "Item", 1);
                            var grid = GridArray(content, workToken);
                            if (grid != null)
                            {
                                object first = Invoke(sheet, "Cells", 1, 1);
                                object last = Invoke(sheet, "Cells", grid.GetLength(0), grid.GetLength(1));
                                object range = Invoke(sheet, "Range", first, last);
                                Set(range, "Value2", grid);
                            }
                            Invoke(book, "SaveAs", temp, 56);
                        }
                        finally { try { Invoke(book, "Close", false); } catch (Exception) { } }
                    }
                    return File.ReadAllBytes(temp);
                }
                catch (ArgumentException) { throw; }
                catch (Exception error) { throw new ArgumentException(Failure(error)); }
                finally
                {
                    if (app != null) { try { Invoke(app, "Quit"); } catch (Exception) { } }
                    try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }, token, 60000, cleanup);
        }

        static object[,] GridArray(string content, CancellationToken token)
        {
            var lines = (content ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var cells = new List<string[]>(); int columns = 0;
            foreach (string line in lines)
            {
                token.ThrowIfCancellationRequested();
                string[] parts = line.Split('\t');
                if (parts.Length == 1 && line.IndexOf(',') >= 0) parts = SplitCsv(line);
                columns = Math.Max(columns, parts.Length); cells.Add(parts);
            }
            while (cells.Count > 1 && cells[cells.Count - 1].Length == 1 && cells[cells.Count - 1][0].Length == 0) cells.RemoveAt(cells.Count - 1);
            if (cells.Count == 0 || columns == 0) return null;
            var grid = new object[cells.Count, columns];
            for (int r = 0; r < cells.Count; r++)
                for (int c = 0; c < columns; c++)
                    grid[r, c] = c < cells[r].Length ? cells[r][c] : "";
            return grid;
        }

        static string[] SplitCsv(string line)
        {
            var cells = new List<string>(); var cell = new StringBuilder(); bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (quoted)
                {
                    if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                    else if (ch == '"') quoted = false;
                    else cell.Append(ch);
                }
                else if (ch == '"') quoted = true;
                else if (ch == ',') { cells.Add(cell.ToString()); cell.Length = 0; }
                else cell.Append(ch);
            }
            cells.Add(cell.ToString());
            return cells.ToArray();
        }

        static void AppendMatrix(StringBuilder output, Array values)
        {
            if (values == null) return;
            for (int r = values.GetLowerBound(0); r <= values.GetUpperBound(0); r++)
            {
                var line = new StringBuilder();
                for (int c = values.GetLowerBound(1); c <= values.GetUpperBound(1); c++)
                {
                    if (c > values.GetLowerBound(1)) line.Append('\t');
                    object value = values.GetValue(r, c);
                    if (value == null) continue;
                    if (value is double) line.Append(((double)value).ToString("0.###############", CultureInfo.InvariantCulture));
                    else line.Append(Convert.ToString(value, CultureInfo.InvariantCulture).Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' '));
                }
                output.AppendLine(line.ToString().TrimEnd('\t'));
            }
        }

        static string NormalizeWordText(string text)
        {
            if (text == null) return "";
            text = text.Replace("\r\n", "\n").Replace('\r', '\n').Replace('\v', ' ').Replace("\a", "");
            var lines = text.Split('\n');
            while (lines.Length > 0 && lines[lines.Length - 1].Length == 0) Array.Resize(ref lines, lines.Length - 1);
            return String.Join("\n", lines);
        }

        static string Failure(Exception error)
        {
            Exception cause = error; while (cause.InnerException != null) cause = cause.InnerException;
            string message = cause.Message == null ? "" : cause.Message;
            return "本机 Office/WPS 无法处理此文档（可能被占用、缺少组件或已弹窗）：" + (message.Length > 160 ? message.Substring(0, 160) : message);
        }

        internal static T RunSta<T>(Func<CancellationToken,T> work,CancellationToken token,int timeoutMilliseconds,Action cleanup)
        {
            T result=default(T);Exception failure=null;var cancel=CancellationTokenSource.CreateLinkedTokenSource(token);
            var thread=new Thread(delegate(){try{result=work(cancel.Token);}catch(Exception error){failure=error;}finally{try{if(cleanup!=null)cleanup();}catch(Exception){}cancel.Dispose();}});
            thread.IsBackground=true;thread.SetApartmentState(ApartmentState.STA);thread.Start();
            DateTime deadline=DateTime.UtcNow.AddMilliseconds(Math.Max(1,timeoutMilliseconds));
            while(!thread.Join(150))
            {
                if(token.IsCancellationRequested || DateTime.UtcNow>deadline)
                {
                    try{cancel.Cancel();}catch(ObjectDisposedException){}
                    bool finished=thread.Join(100);if(!finished && cleanup!=null){var cleanupThread=new Thread(delegate(){try{cleanup();}catch(Exception){}});cleanupThread.IsBackground=true;cleanupThread.Start();}
                    if(token.IsCancellationRequested)throw new OperationCanceledException(token);
                    throw new ArgumentException("本机 Office/WPS 转换超时，请关闭相关窗口或弹窗后重试。");
                }
            }
            if(failure!=null)
            {
                var argument=failure as ArgumentException;if(argument!=null)throw argument;
                var cancelled=failure as OperationCanceledException;if(cancelled!=null && token.IsCancellationRequested)throw cancelled;
                throw new ArgumentException(Failure(failure),failure);
            }
            return result;
        }

        static void SetOptional(object target,string name,object value)
        {
            try{Set(target,name,value);}catch(Exception error){if(!(MissingMember(error) || error is ArgumentException))throw;}
        }
        static void ReleaseComObject(object value)
        {
            try{if(value!=null && System.Runtime.InteropServices.Marshal.IsComObject(value))System.Runtime.InteropServices.Marshal.FinalReleaseComObject(value);}catch(Exception){}
        }
        static object Get(object target, string name) { return target.GetType().InvokeMember(name, System.Reflection.BindingFlags.GetProperty, null, target, null); }
        static void Set(object target, string name, object value) { target.GetType().InvokeMember(name, System.Reflection.BindingFlags.SetProperty, null, target, new object[] { value }); }
        static object Invoke(object target, string name, params object[] args)
        {
            try { return target.GetType().InvokeMember(name, System.Reflection.BindingFlags.InvokeMethod, null, target, args); }
            catch (Exception error)
            {
                if (!MissingMember(error)) throw;
                // Cells and Item are parameterized properties in the Word/Excel
                // and WPS object models rather than plain methods.
                return target.GetType().InvokeMember(name, System.Reflection.BindingFlags.GetProperty, null, target, args);
            }
        }

        static bool MissingMember(Exception error)
        {
            Exception cause = error; while (cause.InnerException != null) cause = cause.InnerException;
            var com = cause as System.Runtime.InteropServices.COMException;
            return com != null && unchecked((uint)com.ErrorCode) == 0x80020003;
        }
    }
}
