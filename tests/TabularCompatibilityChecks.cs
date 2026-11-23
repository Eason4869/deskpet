using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;

namespace Dafeiyu.Tests
{
    internal static class TabularCompatibilityChecks
    {
        const string SpreadsheetNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";

        internal static void Run(string root)
        {
            CheckText(root);
            CheckDelimited(root);
            CheckWorkbook(root);
            CheckLegacyWorkbook();
        }

        static void CheckText(string root)
        {
            string path = Path.Combine(root, "lines.txt");
            var reader = new TextFileReader();
            File.WriteAllText(path, "甲\r\n\r乙\n丙", new UnicodeEncoding(false, true));
            var page = reader.Read(path, new FileReadRequest { Start = 2, Count = 2 }, CancellationToken.None);
            Need(page.Text == "2: \n3: 乙\n" && page.NextStart == 4, "UTF-16 BOM, blank rows, and original line cursor");
            // "甲\n编码正确\n乙" encoded as GB18030.
            File.WriteAllBytes(path, new byte[] { 0xbc, 0xd7, 0x0a, 0xb1, 0xe0, 0xc2, 0xeb, 0xd5, 0xfd, 0xc8, 0xb7, 0x0a, 0xd2, 0xd2 });
            var match = reader.Read(path, new FileReadRequest { Query = "编码", EncodingName = "GB18030" }, CancellationToken.None);
            Need(match.Text.Contains("2: 编码正确"), "GB18030 literal search keeps original line numbers");
            File.WriteAllText(path, new string('a', 70000) + "可查\n", new UTF8Encoding(false));
            var excerpt = reader.Read(path, new FileReadRequest { Query = "可查" }, CancellationToken.None);
            Need(excerpt.Text.Contains("1: [matched excerpt]") && excerpt.Text.Contains("可查"),
                "text search includes a verified match past the display break on long lines");
            File.WriteAllBytes(path, new byte[] { 0x61, 0xff, 0x62 });
            try
            {
                reader.Read(path, new FileReadRequest(), CancellationToken.None);
                throw new Exception("Invalid UTF-8 was silently decoded");
            }
            catch (DecoderFallbackException) { }
            File.WriteAllBytes(path, new byte[] { 0x61, 0, 0x62 });
            try
            {
                reader.Read(path, new FileReadRequest(), CancellationToken.None);
                throw new Exception("Binary data with NUL was accepted as text");
            }
            catch (InvalidDataException) { }
        }

        static void CheckDelimited(string root)
        {
            string path = Path.Combine(root, "rows.csv");
            File.WriteAllText(path, "id,name,empty\r\n0007,\"first\r\nsecond,\"\"quote\"\"\",\r\n0008,last,", new UTF8Encoding(false));
            var reader = new DelimitedTableReader(',');
            var page = reader.Read(path, new FileReadRequest { Start = 2, Count = 1 }, CancellationToken.None);
            Need(page.Text.Contains("\"0007\",\"first\\r\\nsecond,\\\"quote\\\"\",\"\"") && page.NextStart == 3,
                "CSV preserves quoted newlines, delimiters, leading zeroes, and empty final fields");
            string tsv = Path.Combine(root, "rows.tsv");
            File.WriteAllText(tsv, "left\t\"inside\tfield\"\tend\n", new UTF8Encoding(true));
            var tab = new DelimitedTableReader('\t').Read(tsv, new FileReadRequest { StartColumn = 2, ColumnCount = 1 }, CancellationToken.None);
            Need(tab.Text.Contains("\"inside\\tfield\"") && !tab.Text.Contains("left") && !tab.Text.Contains("end"), "TSV column selection keeps quoted tab");
        }

        static void CheckWorkbook(string root)
        {
            string path = Path.Combine(root, "wide.xlsx");
            WriteWorkbook(path, true);
            var reader = new WorkbookFileReader();
            var manifest = reader.Inspect(path, CancellationToken.None);
            Need(manifest.Sections.Count == 9, "XLSX manifest retains the ninth sheet");
            var preview = reader.Read(path, new FileReadRequest(), CancellationToken.None);
            Need(preview.Text.Contains("共同文本"), "default workbook preview finds the first nonempty sheet");
            var wide = reader.Read(path, new FileReadRequest { Section = "9", StartColumn = 65, ColumnCount = 1 }, CancellationToken.None);
            Need(wide.Range.Contains("BM") && wide.Text.Contains("BM1") && wide.Text.Contains("12.5"), "XLSX resolves the 65th column on the ninth sheet");
            var page = reader.Read(path, new FileReadRequest { Section = "9", Count = 4, ColumnCount = 7 }, CancellationToken.None);
            Need(page.Text.Contains("SUM(C1:D1)") && page.Text.Contains("SUM(C2:D2)") && page.Text.Contains("missing") &&
                page.Text.Contains("cached") && page.Text.Contains("#DIV/0!"),
                "cached, missing, and error formula values remain distinct");
            Need(page.Text.Contains("1904-01-02") && page.Text.Contains("0.0%") && page.Text.Contains("12.5%"), "XLSX 1904 date system and custom percent format display correctly");
            Need(page.Text.Contains("FFFF9900") && page.Text.Contains("E1:F2") && page.Text.Contains("shared anchor A1"), "static fill, empty merged cells, and shared formula anchor keep provenance");
            var aggregate = reader.Read(path, new FileReadRequest { Section = "9", Count = 4, ColumnCount = 4, Operation = "sum" }, CancellationToken.None);
            Need(aggregate.Text.Contains("SUM=15") && aggregate.Text.Contains("Errors=1") && aggregate.Text.Contains("MissingFormulaCaches=1"),
                "aggregation walks the selected range, reports errors, and uses only existing numeric caches");
            string other = Path.Combine(root, "1900.xlsx");
            WriteWorkbook(other, false);
            var date = reader.Read(other, new FileReadRequest { Section = "9", StartColumn = 6, ColumnCount = 1 }, CancellationToken.None);
            Need(date.Text.Contains("1900-01-01"), "XLSX 1900 date system maps serial 1 correctly");
        }

        static void CheckLegacyWorkbook()
        {
            // BIFF8 fixture generated with xlwt 1.3.0. It contains an empty first sheet and a Chinese sheet
            // with a text heading, a 12.5 quantity formatted to two places, and an Excel date.
            string path = FindLegacyFixture();
            var reader = new LegacyWorkbookReader();
            Need(LegacyWorkbookReader.CanRead(path, CancellationToken.None) &&
                reader.Inspect(path, CancellationToken.None).Sections.Count == 2, "Managed BIFF8 reader lists both XLS sheets");
            var data = reader.Read(path, new FileReadRequest(), CancellationToken.None);
            Need(data.Text.Contains("金额") && data.Text.Contains("12.50") && data.Text.Contains("2026-11-20") &&
                data.Text.Contains("#,##0.00") && data.Range.Contains("明细"), "XLS opens on first nonempty sheet and displays text, number, and date formats");
        }

        static string FindLegacyFixture()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                string path = Path.Combine(directory.FullName, "tests", "fixtures", "file-compat", "legacy.xls");
                if (File.Exists(path)) return path;
                directory = directory.Parent;
            }
            throw new FileNotFoundException("Missing managed-reader XLS test fixture.");
        }

        static void WriteWorkbook(string path, bool date1904)
        {
            using (var stream = File.Create(path))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var sheets = new StringBuilder();
                var rels = new StringBuilder();
                for (int n = 1; n <= 9; n++)
                {
                    sheets.AppendFormat("<sheet name='Sheet {0}' sheetId='{0}' r:id='ws{0}'/>", n);
                    rels.Append(Rel("ws" + n, "worksheet", "sheets/part" + (10 - n) + ".xml"));
                    string content = n == 9 ?
                        "<sheetData><row r='1'><c r='A1'><f t='shared' ref='A1:A2' si='0'>SUM(C1:D1)</f><v>15</v></c>" +
                        "<c r='B1' t='s'><v>0</v></c><c r='C1' t='e'><f>1/0</f><v>#DIV/0!</v></c>" +
                        "<c r='E1' t='inlineStr' s='1'><is><t>merge</t></is></c><c r='F1' s='2'><v>1</v></c>" +
                        "<c r='G1' s='3'><v>0.125</v></c><c r='BM1'><v>12.5</v></c></row>" +
                        "<row r='2'><c r='A2'><f t='shared' si='0'/><v/></c></row></sheetData>" +
                        "<mergeCells><mergeCell ref='E1:F2'/></mergeCells>" : "<sheetData/>";
                    Add(zip, "xl/sheets/part" + (10 - n) + ".xml", "<worksheet xmlns='" + SpreadsheetNamespace + "'>" + content + "</worksheet>");
                }
                Add(zip, "[Content_Types].xml", "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'/>" );
                Add(zip, "_rels/.rels", "<Relationships xmlns='" + PackageRelationships + "'>" + Rel("doc", "officeDocument", "xl/book.xml") + "</Relationships>");
                Add(zip, "xl/book.xml", "<workbook xmlns='" + SpreadsheetNamespace + "' xmlns:r='" + OfficeRelationships + "'>" +
                    "<workbookPr date1904='" + (date1904 ? "1" : "0") + "'/><sheets>" + sheets + "</sheets></workbook>");
                Add(zip, "xl/_rels/book.xml.rels", "<Relationships xmlns='" + PackageRelationships + "'>" + rels +
                    Rel("strings", "sharedStrings", "strings.xml") + Rel("styles", "styles", "style.xml") + "</Relationships>");
                Add(zip, "xl/strings.xml", "<sst xmlns='" + SpreadsheetNamespace + "'><si><r><t>共同</t></r><r><t>文本</t></r><rPh><t>ignored</t></rPh></si></sst>");
                Add(zip, "xl/style.xml", "<styleSheet xmlns='" + SpreadsheetNamespace + "'><numFmts><numFmt numFmtId='164' formatCode='yyyy-mm-dd'/>" +
                    "<numFmt numFmtId='165' formatCode='0.0%'/></numFmts><fills><fill><patternFill patternType='none'/></fill>" +
                    "<fill><patternFill patternType='solid'><fgColor rgb='FFFF9900'/></patternFill></fill></fills><cellXfs>" +
                    "<xf numFmtId='0' fillId='0'/><xf numFmtId='0' fillId='1'/><xf numFmtId='164' fillId='0'/><xf numFmtId='165' fillId='0'/>" +
                    "</cellXfs></styleSheet>");
            }
        }

        static string Rel(string id, string type, string target)
        {
            return "<Relationship Id='" + id + "' Type='" + OfficeRelationships + "/" + type + "' Target='" + target + "'/>";
        }

        static void Add(ZipArchive zip, string name, string content)
        {
            using (var stream = zip.CreateEntry(name).Open())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(content);
        }

        static void Need(bool ok, string requirement)
        {
            if (!ok) throw new Exception(requirement);
            Console.WriteLine("PASS " + requirement);
        }
    }
}
