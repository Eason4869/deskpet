using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Xml;

namespace Dafeiyu
{
    internal sealed class WorkbookPackage : IDisposable
    {
        const long ExpansionLimit = 256L * 1024L * 1024L;
        const string OfficeRelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string StrictRelationshipNs = "http://purl.oclc.org/ooxml/officeDocument/relationships";
        readonly FileStream input;
        readonly ZipArchive zip;
        readonly CancellationToken token;
        readonly char[] valueBuffer = new char[4096];
        long bytesRead;

        internal WorkbookPackage(string path, CancellationToken token)
        {
            this.token = token;
            token.ThrowIfCancellationRequested();
            input = File.OpenRead(path);
            try { zip = new ZipArchive(input, ZipArchiveMode.Read); }
            catch { input.Dispose(); throw; }
        }

        internal XmlReader OpenXml(string path)
        {
            var part = zip.GetEntry(path);
            if (part == null) throw new InvalidDataException("A workbook package part is missing.");
            var stream = new PartStream(part.Open(), this);
            return XmlReader.Create(stream, new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true, IgnoreProcessingInstructions = true,
                MaxCharactersInDocument = ExpansionLimit, CloseInput = true
            });
        }

        internal List<Relationship> ReadRelationships(string source)
        {
            string path = "_rels/.rels";
            if (source.Length > 0)
            {
                int slash = source.LastIndexOf('/') + 1;
                path = source.Substring(0, slash) + "_rels/" + source.Substring(slash) + ".rels";
            }
            var relationships = new List<Relationship>();
            using (var xml = OpenXml(path))
                while (Read(xml))
                {
                    if (xml.NodeType != XmlNodeType.Element || xml.LocalName != "Relationship" ||
                        xml.NamespaceURI != "http://schemas.openxmlformats.org/package/2006/relationships" ||
                        String.Equals(xml.GetAttribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string target = Resolve(source, xml.GetAttribute("Target"));
                    string type = xml.GetAttribute("Type") ?? "";
                    int index = type.LastIndexOf('/');
                    string location = index >= 0 ? type.Substring(0, index) : "";
                    if (target != null && (location == OfficeRelationshipNs || location == StrictRelationshipNs))
                        relationships.Add(new Relationship(xml.GetAttribute("Id"), type.Substring(index + 1), target));
                    if (relationships.Count > 8192) throw new InvalidDataException("Too many workbook relationships.");
                }
            return relationships;
        }

        static string Resolve(string source, string target)
        {
            if (String.IsNullOrEmpty(target) || target.IndexOfAny(new[] { '\\', '?', '#', ':' }) >= 0 || target.StartsWith("//", StringComparison.Ordinal))
                return null;
            string joined = target[0] == '/' ? target.Substring(1) : source.Substring(0, source.LastIndexOf('/') + 1) + target;
            var parts = new List<string>();
            foreach (string component in joined.Split('/'))
            {
                string part = Uri.UnescapeDataString(component);
                if (part == ".") continue;
                if (part == "..")
                {
                    if (parts.Count == 0) return null;
                    parts.RemoveAt(parts.Count - 1);
                }
                else
                {
                    if (part.Length == 0 || part.IndexOfAny(new[] { '/', '\\', '\0' }) >= 0) return null;
                    parts.Add(part);
                }
            }
            return parts.Count == 0 ? null : String.Join("/", parts);
        }

        internal bool Read(XmlReader xml)
        {
            token.ThrowIfCancellationRequested();
            if (!xml.Read()) return false;
            if (xml.Depth > 64) throw new InvalidDataException("Workbook XML nesting exceeds the supported depth.");
            return true;
        }

        internal void AppendValue(XmlReader xml, StringBuilder output)
        {
            int read;
            while ((read = xml.ReadValueChunk(valueBuffer, 0, valueBuffer.Length)) != 0)
            {
                token.ThrowIfCancellationRequested();
                if (output.Length + read > 32768) throw new InvalidDataException("A workbook cell exceeds the supported text size.");
                output.Append(valueBuffer, 0, read);
            }
        }

        internal static bool IsSpreadsheet(XmlReader xml)
        {
            return xml.NamespaceURI == "http://schemas.openxmlformats.org/spreadsheetml/2006/main" ||
                xml.NamespaceURI == "http://purl.oclc.org/ooxml/spreadsheetml/main";
        }

        internal static string RelationId(XmlReader xml)
        {
            return xml.GetAttribute("id", OfficeRelationshipNs) ?? xml.GetAttribute("id", StrictRelationshipNs);
        }

        internal static int Integer(string text, int fallback)
        {
            int value;
            return Int32.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        internal static bool Address(string value, out int row, out int column)
        {
            row = column = 0;
            if (String.IsNullOrEmpty(value)) return false;
            int index = 0;
            for (; index < value.Length; index++)
            {
                char ch = Char.ToUpperInvariant(value[index]);
                if (ch < 'A' || ch > 'Z') break;
                column = column * 26 + ch - 'A' + 1;
                if (column > 16384) return false;
            }
            if (column == 0 || index == value.Length) return false;
            row = Integer(value.Substring(index), 0);
            return row > 0 && row <= 1048576;
        }

        public void Dispose() { zip.Dispose(); input.Dispose(); }

        internal sealed class Relationship
        {
            internal readonly string Id, Type, Path;
            internal Relationship(string id, string type, string path) { Id = id; Type = type; Path = path; }
        }

        sealed class PartStream : Stream
        {
            readonly Stream part;
            readonly WorkbookPackage owner;
            internal PartStream(Stream part, WorkbookPackage owner) { this.part = part; this.owner = owner; }
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { throw new NotSupportedException(); }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                owner.token.ThrowIfCancellationRequested();
                int read = part.Read(buffer, offset, count);
                owner.bytesRead += read;
                if (owner.bytesRead > ExpansionLimit) throw new InvalidDataException("Workbook expansion exceeds 256 MiB.");
                return read;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) part.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
