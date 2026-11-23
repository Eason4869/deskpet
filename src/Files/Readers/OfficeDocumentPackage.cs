using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Xml;

namespace Dafeiyu
{
    internal sealed class OfficeDocumentPackage : IDisposable
    {
        const int MaxXmlPartBytes = 8 * 1024 * 1024;
        const int MaxXmlPackageBytes = 64 * 1024 * 1024;
        const int MaxXmlNodes = 500000;
        const int MaxParts = 20000;
        const long MaxFileBytes = 128L * 1024 * 1024;
        const string ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";
        const string PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        const string OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string StrictRelationships = "http://purl.oclc.org/ooxml/officeDocument/relationships";
        readonly ZipArchive zip;
        readonly CancellationToken token;
        readonly Dictionary<string, ZipArchiveEntry> parts = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, List<Relationship>> relationships = new Dictionary<string, List<Relationship>>(StringComparer.OrdinalIgnoreCase);
        long xmlBytes;
        int xmlNodes;

        internal OfficeDocumentPackage(string path, CancellationToken token)
        {
            this.token = token;
            token.ThrowIfCancellationRequested();
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                if (stream.Length > MaxFileBytes) throw InvalidPackage();
                zip = new ZipArchive(stream, ZipArchiveMode.Read);
                if (zip.Entries.Count > MaxParts) throw InvalidPackage();
                foreach (var entry in zip.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                    string part = ResolvePart("", entry.FullName);
                    if (part == null || parts.ContainsKey(part)) throw InvalidPackage();
                    parts.Add(part, entry);
                }
            }
            catch
            {
                if (zip != null) zip.Dispose();
                else stream.Dispose();
                throw;
            }
        }

        internal sealed class Relationship
        {
            internal string Id;
            internal string Type;
            internal string Target;
        }

        internal IEnumerable<Relationship> Relationships(string source)
        {
            List<Relationship> result;
            if (relationships.TryGetValue(source, out result)) return result;
            result = new List<Relationship>();
            relationships.Add(source, result);
            int slash = source.LastIndexOf('/');
            string linkPart = source.Substring(0, slash + 1) + "_rels/" + source.Substring(slash + 1) + ".rels";
            XmlElement root = Xml(linkPart, false);
            if (root == null) return result;
            if (root.LocalName != "Relationships" || root.NamespaceURI != PackageRelationships) throw InvalidPackage();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (XmlNode child in root.ChildNodes)
            {
                token.ThrowIfCancellationRequested();
                var link = child as XmlElement;
                if (link == null || link.LocalName != "Relationship" || link.NamespaceURI != PackageRelationships) continue;
                string mode = link.GetAttribute("TargetMode");
                if (mode.Length != 0 && mode != "Internal") continue;
                string type = link.GetAttribute("Type");
                if (!type.StartsWith(OfficeRelationships + "/", StringComparison.Ordinal) &&
                    !type.StartsWith(StrictRelationships + "/", StringComparison.Ordinal)) continue;
                string id = link.GetAttribute("Id");
                if (id.Length == 0 || !ids.Add(id)) throw InvalidPackage();
                string target = ResolvePart(source, link.GetAttribute("Target"));
                if (target != null)
                    result.Add(new Relationship { Id = id, Type = type.Substring(type.LastIndexOf('/') + 1), Target = target });
            }
            return result;
        }

        internal static string RelationshipId(XmlElement element, string name)
        {
            string id = element.GetAttribute(name, OfficeRelationships);
            return id.Length == 0 ? element.GetAttribute(name, StrictRelationships) : id;
        }

        internal void VerifyMainPartType(string part, string expected)
        {
            var root = Xml("[Content_Types].xml");
            if (root == null || root.LocalName != "Types" || root.NamespaceURI != ContentTypes) throw InvalidPackage();
            string extension = Path.GetExtension(part).TrimStart('.');
            string defaultType = null, overrideType = null;
            foreach (XmlNode child in root.ChildNodes)
            {
                token.ThrowIfCancellationRequested();
                var entry = child as XmlElement;
                if (entry == null || entry.NamespaceURI != ContentTypes) continue;
                if (entry.LocalName == "Default" && String.Equals(entry.GetAttribute("Extension"), extension, StringComparison.OrdinalIgnoreCase))
                    defaultType = entry.GetAttribute("ContentType");
                else if (entry.LocalName == "Override" && String.Equals(ResolvePart("", entry.GetAttribute("PartName")), part, StringComparison.OrdinalIgnoreCase))
                    overrideType = entry.GetAttribute("ContentType");
            }
            if (!String.Equals(overrideType ?? defaultType, expected, StringComparison.OrdinalIgnoreCase)) throw InvalidPackage();
        }

        static string ResolvePart(string source, string target)
        {
            if (target.Length == 0 || target.Length > 2048 || target.IndexOfAny(new[] { '\\', ':', '?', '#', '\0' }) >= 0 ||
                target.StartsWith("//", StringComparison.Ordinal)) return null;
            bool rooted = target[0] == '/';
            int slash = source.LastIndexOf('/');
            var segments = new List<string>();
            if (!rooted && slash >= 0) segments.AddRange(source.Substring(0, slash).Split('/'));
            foreach (string encoded in target.Substring(rooted ? 1 : 0).Split('/'))
            {
                string segment;
                try { segment = Uri.UnescapeDataString(encoded); }
                catch (UriFormatException) { return null; }
                if (segment.Length == 0 || segment.IndexOfAny(new[] { '/', '\\', ':', '?', '#', '\0' }) >= 0) return null;
                if (segment == ".") continue;
                if (segment == "..")
                {
                    if (segments.Count == 0) return null;
                    segments.RemoveAt(segments.Count - 1);
                    continue;
                }
                segments.Add(segment);
            }
            return segments.Count == 0 ? null : String.Join("/", segments);
        }

        internal XmlElement Xml(string part, bool required = true)
        {
            ZipArchiveEntry entry;
            if (part == null || !parts.TryGetValue(part, out entry))
            {
                if (!required) return null;
                throw InvalidPackage();
            }
            if (entry.Length > MaxXmlPartBytes || (xmlBytes += entry.Length) > MaxXmlPackageBytes) throw InvalidPackage();
            var document = new XmlDocument { XmlResolver = null };
            XmlNode parent = document;
            try
            {
                using (var stream = entry.Open())
                using (var reader = XmlReader.Create(stream, new XmlReaderSettings {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true,
                    IgnoreWhitespace = true, IgnoreProcessingInstructions = true, MaxCharactersInDocument = MaxXmlPartBytes }))
                {
                    while (reader.Read())
                    {
                        token.ThrowIfCancellationRequested();
                        if (reader.Depth > 96 || (xmlNodes += 1 + reader.AttributeCount) > MaxXmlNodes) throw InvalidPackage();
                        if (reader.NodeType == XmlNodeType.Element)
                        {
                            var element = document.CreateElement(reader.Prefix, reader.LocalName, reader.NamespaceURI);
                            bool empty = reader.IsEmptyElement;
                            while (reader.MoveToNextAttribute())
                            {
                                var attribute = document.CreateAttribute(reader.Prefix, reader.LocalName, reader.NamespaceURI);
                                attribute.Value = reader.Value;
                                element.Attributes.Append(attribute);
                            }
                            reader.MoveToElement();
                            parent.AppendChild(element);
                            if (!empty) parent = element;
                        }
                        else if (reader.NodeType == XmlNodeType.EndElement) parent = parent.ParentNode;
                        else if (reader.NodeType == XmlNodeType.Text || reader.NodeType == XmlNodeType.CDATA || reader.NodeType == XmlNodeType.SignificantWhitespace)
                            parent.AppendChild(document.CreateTextNode(reader.Value));
                    }
                }
            }
            catch (XmlException) { throw InvalidPackage(); }
            return document.DocumentElement;
        }

        internal static InvalidDataException InvalidPackage()
        {
            return new InvalidDataException("The Office document is malformed or exceeds the document parsing limits.");
        }

        public void Dispose() { zip.Dispose(); }
    }
}
