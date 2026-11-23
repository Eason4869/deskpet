using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Dafeiyu.Tests
{
    internal static class DocumentCompatibilityChecks
    {
        const string Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        const string Presentation = "http://schemas.openxmlformats.org/presentationml/2006/main";
        const string Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
        const string OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        internal static int Run(string directory)
        {
            ReadWordSections(Path.Combine(directory, "structured.docx"));
            ReadSlidesInPresentationOrder(Path.Combine(directory, "reordered.pptx"));
            ReadPdfPages(Path.Combine(directory, "paged.pdf"));
            RejectUntrustedXml(Path.Combine(directory, "external-entity.docx"));
            ReadLegacyWord(Path.Combine(directory, "legacy-structured.doc"));
            return 21;
        }

        static void ReadLegacyWord(string path)
        {
            WriteLegacyWord(path, 0x0200);
            byte[] original = File.ReadAllBytes(path);
            var reader = new OfficeDocumentReader("doc");
            var first = reader.Read(path, new FileReadRequest { Start = 1, Count = 1 }, CancellationToken.None);
            Require(first.Text.Contains("Old ’ 旧版中文") && first.NextStart == 2 && !first.Text.Contains("原始页眉"),
                "Binary Word combines compressed and Unicode character runs while preserving the paragraph cursor.");
            var rest = reader.Read(path, new FileReadRequest { Start = 2 }, CancellationToken.None);
            Require(rest.Text.Contains("左列 | 右列") && rest.Text.Contains("尾段"), "Binary Word keeps table-cell text and the following paragraph.");
            Require(rest.Text.Contains("可见链接") && !rest.Text.Contains("HYPERLINK") && !rest.Text.Contains("never-fetch"),
                "Binary Word returns field results without evaluating field codes.");
            Require(reader.Read(path, new FileReadRequest { Section = "headers" }, CancellationToken.None).Text.Contains("原始页眉") &&
                reader.Read(path, new FileReadRequest { Section = "footers" }, CancellationToken.None).Text.Contains("原始页脚"),
                "Binary Word separates headers from footers using the story table.");
            Require(File.ReadAllBytes(path).SequenceEqual(original), "Reading a binary Word document preserves its bytes.");
            string encrypted = Path.Combine(Path.GetDirectoryName(path), "legacy-encrypted.doc");
            WriteLegacyWord(encrypted, 0x0300);
            var manifest = reader.Inspect(encrypted, CancellationToken.None);
            Require(manifest.Sections.Count == 0 && manifest.Detail.Contains("Password-protected"),
                "Password-protected binary Word documents expose an unavailable state without attempting to read their text.");
            string malformed = Path.Combine(Path.GetDirectoryName(path), "legacy-malformed.doc");
            WriteLegacyWord(malformed, 0x0200, true);
            try { reader.Inspect(malformed, CancellationToken.None); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException("Binary Word must reject an out-of-bounds piece table.");
        }

        static void WriteLegacyWord(string path, int flags, bool corrupt = false)
        {
            const string body = "旧版中文\r左列\a右列\a\a尾段\u0013 HYPERLINK http://127.0.0.1:1/never-fetch \u0014可见链接\u0015\r";
            const string header = "原始页眉\r";
            const string footer = "原始页脚\r";
            byte[] compressed = { (byte)'O', (byte)'l', (byte)'d', (byte)' ', 0x92, (byte)' ' };
            string text = body + header + footer + "\r\r";
            byte[] word = new byte[768 + text.Length * 2], table = new byte[128];
            WriteNumber(word, 0, 0xa5ec, 2);
            WriteNumber(word, 2, 0x00c1, 2);
            WriteNumber(word, 10, flags, 2);
            WriteNumber(word, 32, 14, 2);
            WriteNumber(word, 62, 22, 2);
            WriteNumber(word, 64, word.Length, 4);
            WriteNumber(word, 76, compressed.Length + body.Length, 4);
            WriteNumber(word, 84, header.Length + footer.Length + 1, 4);
            WriteNumber(word, 152, 93, 2);
            WriteNumber(word, 154 + 11 * 8, 64, 4);
            WriteNumber(word, 154 + 11 * 8 + 4, 56, 4);
            WriteNumber(word, 154 + 33 * 8 + 4, corrupt ? 256 : 33, 4);
            Array.Copy(compressed, 0, word, 720, compressed.Length);
            Array.Copy(Encoding.Unicode.GetBytes(text), 0, word, 768, text.Length * 2);
            table[0] = 2;
            WriteNumber(table, 1, 28, 4);
            WriteNumber(table, 9, compressed.Length, 4);
            WriteNumber(table, 13, compressed.Length + text.Length, 4);
            WriteNumber(table, 19, 0x40000000 | (720 * 2), 4);
            WriteNumber(table, 27, 768, 4);
            foreach (int index in new[] { 8, 9 }) WriteNumber(table, 64 + index * 4, header.Length, 4);
            foreach (int index in new[] { 10, 11, 12 }) WriteNumber(table, 64 + index * 4, header.Length + footer.Length, 4);
            using (var storage = OpenMcdf.RootStorage.Create(path, OpenMcdf.Version.V3))
            {
                using (var part = storage.CreateStream("WordDocument")) part.Write(word, 0, word.Length);
                using (var part = storage.CreateStream("1Table")) part.Write(table, 0, table.Length);
            }
        }

        static void WriteNumber(byte[] target, int at, int value, int bytes)
        {
            for (int i = 0; i < bytes; i++)
                target[at + i] = (byte)(value >> (8 * i));
        }
        static void ReadWordSections(string path)
        {
            var recorder = new TcpListener(IPAddress.Loopback, 0);
            recorder.Start();
            int requests = 0;
            recorder.BeginAcceptTcpClient(operation =>
            {
                try
                {
                    using (recorder.EndAcceptTcpClient(operation))
                        Interlocked.Increment(ref requests);
                }
                catch (ObjectDisposedException) { }
                catch (SocketException) { }
            }, null);
            try { ReadWordSections(path, (IPEndPoint)recorder.LocalEndpoint); }
            finally
            {
                bool requestedExternalContent = recorder.Pending() || Volatile.Read(ref requests) != 0;
                recorder.Stop();
                Require(!requestedExternalContent, "Office parsing must not request externally linked content.");
            }
        }

        static void ReadWordSections(string path, IPEndPoint recorder)
        {
            string external = "http://" + recorder + "/never-fetch";
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Add(zip, "[Content_Types].xml", ContentTypes("/parts/document.xml", "wordprocessingml.document"));
                Add(zip, "_rels/.rels", Relationships(Link("root", "officeDocument", "parts/document.xml")));
                Add(zip, "parts/document.xml", WordPart("document", "<w:body>"
                    + "<w:p><w:pPr><w:pStyle w:val='Heading1'/></w:pPr><w:r><w:t>中文标题</w:t></w:r></w:p>"
                    + "<w:tbl><w:tr><w:tc><w:tcPr><w:gridSpan w:val='2'/></w:tcPr><w:p><w:r><w:t>合并列</w:t></w:r></w:p>"
                    + "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>嵌套表格</w:t></w:r></w:p></w:tc></w:tr></w:tbl></w:tc></w:tr></w:tbl>"
                    + "<w:p><w:hyperlink r:id='external'><w:r><w:t>外链显示文字</w:t></w:r></w:hyperlink></w:p>"
                    + "<w:sectPr><w:headerReference w:type='default' r:id='header'/><w:headerReference w:type='first' r:id='externalHeader'/><w:footerReference w:type='default' r:id='footer'/></w:sectPr></w:body>"));
                Add(zip, "parts/_rels/document.xml.rels", Relationships(Link("header", "header", "page%20header.xml")
                    + Link("footer", "footer", "footer.xml") + Link("comments", "comments", "comments.xml")
                    + "<Relationship Id='external' Type='" + OfficeRelationships + "/hyperlink' Target='" + external + "' TargetMode='External'/>"
                    + "<Relationship Id='externalHeader' Type='" + OfficeRelationships + "/header' Target='" + external + "' TargetMode='External'/>"));
                Add(zip, "parts/page%20header.xml", WordPart("hdr", "<w:p><w:r><w:t>页眉内容</w:t></w:r></w:p>"));
                Add(zip, "parts/footer.xml", WordPart("ftr", "<w:p><w:r><w:t>页脚内容</w:t></w:r></w:p>"));
                Add(zip, "parts/comments.xml", WordPart("comments", "<w:comment w:id='4'><w:p><w:r><w:t>批注内容</w:t></w:r></w:p></w:comment>"));
            }
            var reader = new OfficeDocumentReader("docx");
            var manifest = reader.Inspect(path, CancellationToken.None);
            Require(manifest.Sections.Single(section => section.Id == "body").Count == 3 &&
                manifest.Sections.Single(section => section.Id == "headers").Count == 1,
                "The Word manifest counts only locally addressable structural units.");
            var first = reader.Read(path, new FileReadRequest { Section = "body", Start = 1, Count = 1 }, CancellationToken.None);
            Require(first.Text.Contains("中文标题") && first.NextStart == 2, "Word body advances by structural blocks.");
            var rest = reader.Read(path, new FileReadRequest { Section = "body", Start = 2, Count = 2 }, CancellationToken.None);
            Require(!rest.Text.Contains("中文标题") && rest.Text.Contains("合并列") && rest.Text.Contains("嵌套表格") && rest.Text.Contains("外链显示文字") && !rest.Text.Contains(external) && !rest.NextStart.HasValue, "Word tables and local hyperlink text remain in source order.");
            Require(reader.Read(path, new FileReadRequest { Section = "headers" }, CancellationToken.None).Text.Contains("页眉内容"), "Word header relationships are resolved locally.");
            Require(reader.Read(path, new FileReadRequest { Section = "footers" }, CancellationToken.None).Text.Contains("页脚内容"), "Word footer relationships are resolved locally.");
            Require(reader.Read(path, new FileReadRequest { Section = "notes" }, CancellationToken.None).Text.Contains("批注内容"), "Word comments have a separately readable section.");
        }

        static void ReadSlidesInPresentationOrder(string path)
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Add(zip, "[Content_Types].xml", ContentTypes("/ppt/presentation.xml", "presentationml.presentation"));
                Add(zip, "_rels/.rels", Relationships(Link("root", "officeDocument", "ppt/presentation.xml")));
                Add(zip, "ppt/presentation.xml", PresentationPart("presentation", "<p:sldIdLst><p:sldId id='256' r:id='late'/><p:sldId id='257' r:id='early'/></p:sldIdLst>"));
                Add(zip, "ppt/_rels/presentation.xml.rels", Relationships(Link("early", "slide", "slides/slide1.xml") + Link("late", "slide", "slides/slide9.xml")));
                Add(zip, "ppt/slides/slide1.xml", PresentationPart("sld", "<p:cSld><p:spTree>" + Shape("第二张") + "</p:spTree></p:cSld>"));
                Add(zip, "ppt/slides/slide9.xml", PresentationPart("sld", "<p:cSld><p:spTree>" + Shape("第一张")
                    + "<p:graphicFrame><a:graphic><a:graphicData><a:tbl><a:tr><a:tc><a:txBody><a:p><a:r><a:t>表格内容</a:t></a:r></a:p></a:txBody></a:tc></a:tr></a:tbl></a:graphicData></a:graphic></p:graphicFrame></p:spTree></p:cSld>"));
                Add(zip, "ppt/slides/_rels/slide9.xml.rels", Relationships(Link("notes", "notesSlide", "../notesSlides/notesSlide3.xml")));
                Add(zip, "ppt/notesSlides/notesSlide3.xml", PresentationPart("notes", "<p:cSld><p:spTree>" + Shape("演讲者备注") + "</p:spTree></p:cSld>"));
            }
            var reader = new OfficeDocumentReader("pptx");
            var first = reader.Read(path, new FileReadRequest { Start = 1, Count = 1 }, CancellationToken.None);
            Require(first.Text.Contains("第一张") && first.Text.Contains("表格内容") && first.Text.Contains("演讲者备注") && !first.Text.Contains("第二张") && first.NextStart == 2, "PowerPoint uses the presentation order and reads slide tables and notes.");
            var last = reader.Read(path, new FileReadRequest { Start = 2, Count = 1 }, CancellationToken.None);
            Require(last.Text.Contains("第二张") && !last.Text.Contains("第一张") && !last.NextStart.HasValue, "PowerPoint resumes at the requested slide.");
        }

        static void ReadPdfPages(string path)
        {
            WritePdf(path);
            var reader = new PdfFileReader();
            Require(reader.Inspect(path, CancellationToken.None).Sections.Single().Count == 2, "The PDF manifest exposes the physical page count.");
            var first = reader.Read(path, new FileReadRequest { Start = 1, Count = 1 }, CancellationToken.None);
            Require(first.Text.Contains("First page PDF") && first.NextStart == 2 && first.Range.Contains("1"), "PDF exposes first-page text and a continuation.");
            var second = reader.Read(path, new FileReadRequest { Start = 2, Count = 1 }, CancellationToken.None);
            Require(!String.IsNullOrWhiteSpace(second.Text) && !second.Text.Contains("First page PDF") && !second.NextStart.HasValue && second.Range.Contains("2"), "PDF identifies a missing text layer on its own page.");
            try { reader.Read(path, new FileReadRequest { Operation = "ocr" }, CancellationToken.None); }
            catch (NotSupportedException) { return; }
            throw new InvalidOperationException("PDF OCR requires an explicit unavailable result until its local component is packaged.");
        }

        static void WritePdf(string path)
        {
            var fileEncoding = Encoding.GetEncoding(28591);
            var uncompressed = Encoding.ASCII.GetBytes("BT /F1 12 Tf 72 720 Td (First page PDF) Tj ET\n");
            byte[] content;
            using (var compressed = new MemoryStream())
            {
                compressed.WriteByte(0x78);
                compressed.WriteByte(0x9c);
                using (var deflate = new DeflateStream(compressed, CompressionMode.Compress, true))
                    deflate.Write(uncompressed, 0, uncompressed.Length);
                int a = 1, b = 0;
                foreach (byte value in uncompressed)
                {
                    a = (a + value) % 65521;
                    b = (b + a) % 65521;
                }
                var checksum = new[] { (byte)(b >> 8), (byte)b, (byte)(a >> 8), (byte)a };
                compressed.Write(checksum, 0, checksum.Length);
                content = compressed.ToArray();
            }
            string[] objects = {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 6 0 R >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
                "<< /Length " + content.Length.ToString(CultureInfo.InvariantCulture) + " /Filter /FlateDecode >>\nstream\n" + fileEncoding.GetString(content) + "\nendstream"
            };
            var pdf = new StringBuilder("%PDF-1.4\n");
            var xref = new StringBuilder("0000000000 65535 f \n");
            for (int i = 0; i < objects.Length; i++)
            {
                xref.Append(pdf.Length.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
                pdf.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
            }
            int start = pdf.Length;
            pdf.Append("xref\n0 ").Append(objects.Length + 1).Append('\n').Append(xref)
                .Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(start).Append("\n%%EOF\n");
            File.WriteAllText(path, pdf.ToString(), fileEncoding);
        }

        static void RejectUntrustedXml(string path)
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Add(zip, "[Content_Types].xml", ContentTypes("/word/document.xml", "wordprocessingml.document"));
                Add(zip, "_rels/.rels", Relationships(Link("root", "officeDocument", "word/document.xml")));
                Add(zip, "word/document.xml", "<!DOCTYPE w:document [<!ENTITY other SYSTEM 'file:///missing-secret.xml'>]>"
                    + WordPart("document", "<w:body><w:p><w:r><w:t>&other;</w:t></w:r></w:p></w:body>"));
            }
            try { new OfficeDocumentReader("docx").Inspect(path, CancellationToken.None); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException("Office XML must reject external entity declarations.");
        }

        static string WordPart(string root, string xml)
        { return "<w:" + root + " xmlns:w='" + Word + "' xmlns:r='" + OfficeRelationships + "'>" + xml + "</w:" + root + ">"; }

        static string PresentationPart(string root, string xml)
        { return "<p:" + root + " xmlns:p='" + Presentation + "' xmlns:a='" + Drawing + "' xmlns:r='" + OfficeRelationships + "'>" + xml + "</p:" + root + ">"; }

        static string Shape(string text) { return "<p:sp><p:txBody><a:p><a:r><a:t>" + text + "</a:t></a:r></a:p></p:txBody></p:sp>"; }

        static string ContentTypes(string part, string type)
        { return "<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/><Default Extension='xml' ContentType='application/xml'/><Override PartName='" + part + "' ContentType='application/vnd.openxmlformats-officedocument." + type + ".main+xml'/></Types>"; }

        static string Relationships(string xml)
        { return "<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'>" + xml + "</Relationships>"; }

        static string Link(string id, string type, string target)
        { return "<Relationship Id='" + id + "' Type='" + OfficeRelationships + "/" + type + "' Target='" + target + "'/>"; }

        static void Add(ZipArchive zip, string path, string text)
        {
            using (var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false))) writer.Write(text);
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
