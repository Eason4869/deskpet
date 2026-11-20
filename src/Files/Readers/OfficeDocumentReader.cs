using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;

namespace Dafeiyu
{
    internal sealed class OfficeDocumentReader : IFileContentReader
    {
        const string Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        const string StrictWord = "http://purl.oclc.org/ooxml/wordprocessingml/main";
        const string Presentation = "http://schemas.openxmlformats.org/presentationml/2006/main";
        const string StrictPresentation = "http://purl.oclc.org/ooxml/presentationml/main";
        const string Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
        const string StrictDrawing = "http://purl.oclc.org/ooxml/drawingml/main";
        const string Compatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        const int MaxUnitChars = 40 * 1024;
        readonly string kind;

        internal OfficeDocumentReader(string kind)
        {
            if (kind != "doc" && kind != "docx" && kind != "pptx") throw new ArgumentException("Invalid Office file type.");
            this.kind = kind;
        }

        public FileManifest Inspect(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (kind == "doc") return new LegacyWordFileReader().Inspect(path, token);
            using (var package = new OfficeDocumentPackage(path, token))
            {
                string main = MainPart(package);
                if (kind == "pptx")
                    return new FileManifest(kind, new[] { new FileSection("slides", "Slides", SlideParts(package, main).Count) });

                XmlElement body = WordBody(package, main);
                var sections = new List<FileSection> { new FileSection("body", "Body blocks", WordBlocks(body).Count()) };
                int headers = HeaderFooterParts(package, main, body, "header", token).Count();
                int footers = HeaderFooterParts(package, main, body, "footer", token).Count();
                int notes = WordNotes(package, main, token).Count();
                if (headers > 0) sections.Add(new FileSection("headers", "Headers", headers));
                if (footers > 0) sections.Add(new FileSection("footers", "Footers", footers));
                if (notes > 0) sections.Add(new FileSection("notes", "Footnotes, endnotes and comments", notes));
                return new FileManifest(kind, sections);
            }
        }

        public FileReadResult Read(string path, FileReadRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (kind == "doc") return new LegacyWordFileReader().Read(path, request, token);
            string section = request.Section;
            if (String.IsNullOrEmpty(section)) section = kind == "pptx" ? "slides" : "body";
            using (var package = new OfficeDocumentPackage(path, token))
            {
                string main = MainPart(package);
                if (kind == "pptx")
                {
                    if (section != "slides") throw new ArgumentException("Select the slides section.");
                    var slides = SlideParts(package, main);
                    return ReadRange(section, request, slides.Count, 20,
                        index => SlideText(package, slides[index - 1], index, token), token);
                }

                XmlElement body = WordBody(package, main);
                if (section == "body")
                {
                    var blocks = WordBlocks(body).ToList();
                    int paragraph = 0, table = 0;
                    var numbers = new int[blocks.Count];
                    for (int i = 0; i < blocks.Count; i++)
                        numbers[i] = IsWord(blocks[i], "tbl") ? ++table : ++paragraph;
                    return ReadRange(section, request, blocks.Count, 100,
                        index => WordBlockText(blocks[index - 1], numbers[index - 1], token), token);
                }
                if (section == "headers" || section == "footers")
                {
                    string relation = section == "headers" ? "header" : "footer";
                    string element = relation == "header" ? "hdr" : "ftr";
                    var parts = HeaderFooterParts(package, main, body, relation, token).ToList();
                    return ReadRange(section, request, parts.Count, 20, index =>
                    {
                        var root = package.Xml(parts[index - 1]);
                        if (!IsWord(root, element)) throw OfficeDocumentPackage.InvalidPackage();
                        return WordContainerText(root, relation + " " + DocumentReadWindow.SourceNumber(index), token);
                    }, token);
                }
                if (section == "notes")
                {
                    var notes = WordNotes(package, main, token).ToList();
                    return ReadRange(section, request, notes.Count, 40, index =>
                    {
                        var note = notes[index - 1];
                        return WordContainerText(note, note.LocalName + " " + note.GetAttribute("id", note.NamespaceURI), token);
                    }, token);
                }
                throw new ArgumentException("Select a section of the Word document.");
            }
        }

        internal static FileReadResult ReadRange(string section, FileReadRequest request, int available, int maxCount,
            Func<int, string> read, CancellationToken token)
        {
            int first = Math.Max(1, request.Start);
            var result = new DocumentReadWindow(section, first);
            int count = Math.Min(Math.Max(1, request.Count), maxCount);
            for (int offset = 0; offset < count && first <= available - offset; offset++)
            {
                token.ThrowIfCancellationRequested();
                if (!result.Add(read(first + offset))) break;
            }
            return result.Complete(available);
        }

        string MainPart(OfficeDocumentPackage package)
        {
            var main = package.Relationships("").Where(relation => relation.Type == "officeDocument").Take(2).ToList();
            if (main.Count != 1) throw OfficeDocumentPackage.InvalidPackage();
            package.VerifyMainPartType(main[0].Target, kind == "docx"
                ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"
                : "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml");
            return main[0].Target;
        }

        static bool IsWord(XmlElement node, string name)
        { return node != null && node.LocalName == name && (node.NamespaceURI == Word || node.NamespaceURI == StrictWord); }

        static bool IsPresentation(XmlElement node, string name)
        { return node != null && node.LocalName == name && (node.NamespaceURI == Presentation || node.NamespaceURI == StrictPresentation); }

        static bool IsDrawing(XmlElement node, string name)
        { return node != null && node.LocalName == name && (node.NamespaceURI == Drawing || node.NamespaceURI == StrictDrawing); }

        static IEnumerable<XmlElement> ChildElements(XmlElement parent)
        {
            if (parent == null) yield break;
            foreach (XmlNode node in parent.ChildNodes)
            {
                var element = node as XmlElement;
                if (element == null) continue;
                if (element.NamespaceURI != Compatibility || element.LocalName != "AlternateContent")
                {
                    yield return element;
                    continue;
                }
                var choice = element.ChildNodes.OfType<XmlElement>().FirstOrDefault(child => child.NamespaceURI == Compatibility && child.LocalName == "Choice");
                var fallback = element.ChildNodes.OfType<XmlElement>().FirstOrDefault(child => child.NamespaceURI == Compatibility && child.LocalName == "Fallback");
                foreach (var alternate in ChildElements(choice ?? fallback)) yield return alternate;
            }
        }

        static IEnumerable<XmlElement> VisibleElements(XmlElement root, CancellationToken token)
        {
            foreach (var child in ChildElements(root))
            {
                token.ThrowIfCancellationRequested();
                if (IsWord(child, "del") || IsWord(child, "moveFrom")) continue;
                yield return child;
                foreach (var descendant in VisibleElements(child, token)) yield return descendant;
            }
        }

        static XmlElement WordBody(OfficeDocumentPackage package, string part)
        {
            XmlElement root = package.Xml(part);
            var body = IsWord(root, "document") ? ChildElements(root).FirstOrDefault(node => IsWord(node, "body")) : null;
            if (body == null) throw OfficeDocumentPackage.InvalidPackage();
            return body;
        }

        static bool IsWordWrapper(XmlElement node)
        {
            return IsWord(node, "sdt") || IsWord(node, "sdtContent") || IsWord(node, "customXml") ||
                IsWord(node, "ins") || IsWord(node, "moveTo");
        }

        static IEnumerable<XmlElement> WordChildren(XmlElement root)
        {
            foreach (var child in ChildElements(root))
            {
                if (!IsWordWrapper(child)) yield return child;
                else foreach (var nested in WordChildren(child)) yield return nested;
            }
        }

        static IEnumerable<XmlElement> WordBlocks(XmlElement root)
        { return WordChildren(root).Where(node => IsWord(node, "p") || IsWord(node, "tbl")); }

        static IEnumerable<string> HeaderFooterParts(OfficeDocumentPackage package, string main, XmlElement body, string type, CancellationToken token)
        {
            var available = package.Relationships(main).Where(link => link.Type == type).ToDictionary(link => link.Id, link => link.Target);
            var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (available.Count == 0) yield break;
            string reference = type + "Reference";
            foreach (var node in VisibleElements(body, token))
            {
                if (!IsWord(node, reference) || !IsWord(node.ParentNode as XmlElement, "sectPr")) continue;
                string part;
                if (available.TryGetValue(OfficeDocumentPackage.RelationshipId(node, "id"), out part) && emitted.Add(part))
                    yield return part;
            }
        }

        static IEnumerable<XmlElement> WordNotes(OfficeDocumentPackage package, string main, CancellationToken token)
        {
            foreach (var relation in package.Relationships(main))
            {
                string type = relation.Type;
                if (type != "footnotes" && type != "endnotes" && type != "comments") continue;
                var root = package.Xml(relation.Target);
                if (!IsWord(root, type)) throw OfficeDocumentPackage.InvalidPackage();
                string name = type.Substring(0, type.Length - 1);
                foreach (var note in ChildElements(root))
                {
                    token.ThrowIfCancellationRequested();
                    if (!IsWord(note, name)) continue;
                    string category = note.GetAttribute("type", note.NamespaceURI);
                    if (String.IsNullOrEmpty(category) || category == "normal") yield return note;
                }
            }
        }

        static string WordBlockText(XmlElement block, int number, CancellationToken token)
        {
            var text = new StringBuilder();
            if (IsWord(block, "tbl"))
            {
                text.Append("[Table ").Append(DocumentReadWindow.SourceNumber(number)).AppendLine("]");
                AppendWordTable(text, block, token);
                return text.ToString();
            }
            text.Append("[Paragraph ").Append(DocumentReadWindow.SourceNumber(number));
            var properties = ChildElements(block).FirstOrDefault(node => IsWord(node, "pPr"));
            var style = ChildElements(properties).FirstOrDefault(node => IsWord(node, "pStyle"));
            var outline = ChildElements(properties).FirstOrDefault(node => IsWord(node, "outlineLvl"));
            string styleName = style == null ? "" : style.GetAttribute("val", style.NamespaceURI);
            if (styleName.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) || styleName == "Title" || outline != null)
                text.Append(" / heading");
            text.AppendLine("]");
            AppendWordParagraph(text, block, token);
            return text.ToString();
        }

        static string WordContainerText(XmlElement root, string label, CancellationToken token)
        {
            var text = new StringBuilder().Append('[').Append(label).AppendLine("]");
            AppendWordBlocks(text, root, token);
            return text.ToString();
        }

        static void AppendWordBlocks(StringBuilder text, XmlElement root, CancellationToken token)
        {
            foreach (var block in WordBlocks(root))
            {
                token.ThrowIfCancellationRequested();
                if (text.Length > MaxUnitChars) break;
                if (IsWord(block, "p")) AppendWordParagraph(text, block, token);
                else AppendWordTable(text, block, token);
                text.AppendLine();
            }
        }

        static void AppendWordParagraph(StringBuilder text, XmlElement paragraph, CancellationToken token)
        {
            foreach (var node in VisibleElements(paragraph, token))
            {
                if (text.Length > MaxUnitChars) break;
                if (IsWord(node, "t")) AppendText(text, node.InnerText);
                else if (IsWord(node, "tab")) text.Append('\t');
                else if (IsWord(node, "br") || IsWord(node, "cr")) text.AppendLine();
                else if (IsWord(node, "footnoteReference") || IsWord(node, "endnoteReference") || IsWord(node, "commentReference"))
                    AppendText(text, "[" + node.LocalName + " " + node.GetAttribute("id", node.NamespaceURI) + "]");
                else if (IsDrawing(node, "blip")) AppendImageReference(text, node);
            }
        }

        static void AppendWordTable(StringBuilder text, XmlElement table, CancellationToken token)
        {
            int rowNumber = 0;
            foreach (var row in WordChildren(table).Where(node => IsWord(node, "tr")))
            {
                token.ThrowIfCancellationRequested();
                if (text.Length > MaxUnitChars) break;
                text.Append("Row ").Append(DocumentReadWindow.SourceNumber(++rowNumber)).Append(": ");
                bool first = true;
                foreach (var cell in WordChildren(row).Where(node => IsWord(node, "tc")))
                {
                    if (text.Length > MaxUnitChars) break;
                    if (!first) text.Append(" | ");
                    first = false;
                    AppendMergeDetails(text, cell);
                    foreach (var block in WordBlocks(cell))
                    {
                        if (text.Length > MaxUnitChars) break;
                        if (!IsWord(block, "tbl"))
                        {
                            AppendWordParagraph(text, block, token);
                            text.Append(' ');
                            continue;
                        }
                        text.AppendLine().AppendLine("[Nested table]");
                        AppendWordTable(text, block, token);
                        text.Append("[End nested table]");
                    }
                }
                text.AppendLine();
            }
        }

        static void AppendMergeDetails(StringBuilder text, XmlElement cell)
        {
            var properties = ChildElements(cell).FirstOrDefault(node => IsWord(node, "tcPr"));
            foreach (var property in ChildElements(properties))
            {
                if (IsWord(property, "gridSpan"))
                {
                    int span;
                    if (Int32.TryParse(property.GetAttribute("val", property.NamespaceURI), out span) && span > 1)
                        text.Append("[column span ").Append(DocumentReadWindow.SourceNumber(span)).Append("] ");
                }
                else if (IsWord(property, "vMerge"))
                    text.Append(property.GetAttribute("val", property.NamespaceURI) == "restart" ? "[vertical merge start] " : "[vertical merge continuation] ");
            }
        }

        static List<string> SlideParts(OfficeDocumentPackage package, string main)
        {
            XmlElement root = package.Xml(main);
            if (!IsPresentation(root, "presentation")) throw OfficeDocumentPackage.InvalidPackage();
            var list = ChildElements(root).FirstOrDefault(node => IsPresentation(node, "sldIdLst"));
            var relations = package.Relationships(main).Where(link => link.Type == "slide").ToDictionary(link => link.Id, link => link.Target);
            var slides = new List<string>();
            foreach (var node in ChildElements(list).Where(element => IsPresentation(element, "sldId")))
            {
                string target;
                if (!relations.TryGetValue(OfficeDocumentPackage.RelationshipId(node, "id"), out target)) throw OfficeDocumentPackage.InvalidPackage();
                slides.Add(target);
            }
            return slides;
        }

        static string SlideText(OfficeDocumentPackage package, string part, int index, CancellationToken token)
        {
            var text = new StringBuilder().Append("[Slide ").Append(DocumentReadWindow.SourceNumber(index)).AppendLine("]");
            AppendSlideShapes(text, package.Xml(part), false, token);
            var notes = package.Relationships(part).FirstOrDefault(relation => relation.Type == "notesSlide");
            if (notes != null && text.Length <= MaxUnitChars)
            {
                int before = text.Length;
                text.AppendLine("[Speaker notes]");
                int headingEnd = text.Length;
                AppendSlideShapes(text, package.Xml(notes.Target), true, token);
                if (text.Length == headingEnd) text.Length = before;
            }
            return text.ToString();
        }

        static void AppendSlideShapes(StringBuilder text, XmlElement root, bool notes, CancellationToken token)
        {
            if (!IsPresentation(root, notes ? "notes" : "sld")) throw OfficeDocumentPackage.InvalidPackage();
            var common = ChildElements(root).FirstOrDefault(node => IsPresentation(node, "cSld"));
            var tree = ChildElements(common).FirstOrDefault(node => IsPresentation(node, "spTree"));
            foreach (var shape in SlideShapes(tree))
            {
                token.ThrowIfCancellationRequested();
                if (text.Length > MaxUnitChars) break;
                if (notes && !IsSpeakerNote(shape, token)) continue;
                foreach (var node in VisibleElements(shape, token))
                {
                    if (text.Length > MaxUnitChars) break;
                    if (IsDrawing(node, "tbl"))
                    {
                        text.AppendLine("[Table]");
                        AppendSlideTable(text, node, token);
                        break;
                    }
                    if (IsDrawing(node, "p")) AppendSlideParagraph(text, node, token);
                    else if (IsDrawing(node, "blip")) AppendImageReference(text, node);
                }
            }
        }

        static IEnumerable<XmlElement> SlideShapes(XmlElement tree)
        {
            foreach (var child in ChildElements(tree))
            {
                if (IsPresentation(child, "grpSp"))
                {
                    foreach (var nested in SlideShapes(child)) yield return nested;
                }
                else if (IsPresentation(child, "sp") || IsPresentation(child, "graphicFrame") ||
                    IsPresentation(child, "pic") || IsPresentation(child, "cxnSp")) yield return child;
            }
        }

        static bool IsSpeakerNote(XmlElement shape, CancellationToken token)
        {
            var placeholder = VisibleElements(shape, token).FirstOrDefault(node => IsPresentation(node, "ph"));
            if (placeholder == null) return true;
            string type = placeholder.GetAttribute("type");
            return type.Length == 0 || type == "body" || type == "obj";
        }

        static void AppendSlideParagraph(StringBuilder text, XmlElement paragraph, CancellationToken token, bool newline = true)
        {
            int before = text.Length;
            foreach (var node in VisibleElements(paragraph, token))
            {
                if (text.Length > MaxUnitChars) break;
                if (IsDrawing(node, "t")) AppendText(text, node.InnerText);
                else if (IsDrawing(node, "br")) text.AppendLine();
            }
            if (newline && text.Length > before) text.AppendLine();
        }

        static void AppendSlideTable(StringBuilder text, XmlElement table, CancellationToken token)
        {
            int rowNumber = 0;
            foreach (var row in ChildElements(table).Where(node => IsDrawing(node, "tr")))
            {
                token.ThrowIfCancellationRequested();
                if (text.Length > MaxUnitChars) break;
                text.Append("Row ").Append(DocumentReadWindow.SourceNumber(++rowNumber)).Append(": ");
                bool first = true;
                foreach (var cell in ChildElements(row).Where(node => IsDrawing(node, "tc")))
                {
                    if (text.Length > MaxUnitChars) break;
                    if (!first) text.Append(" | ");
                    first = false;
                    foreach (var paragraph in VisibleElements(cell, token).Where(node => IsDrawing(node, "p")))
                    {
                        if (text.Length > MaxUnitChars) break;
                        AppendSlideParagraph(text, paragraph, token, false);
                        text.Append(' ');
                    }
                }
                text.AppendLine();
            }
        }

        static void AppendImageReference(StringBuilder text, XmlElement node)
        {
            string id = OfficeDocumentPackage.RelationshipId(node, "embed");
            if (id.Length != 0) AppendText(text, "[Embedded image " + id + "]");
        }

        static void AppendText(StringBuilder text, string value)
        {
            int length = Math.Min(MaxUnitChars + 1 - text.Length, value.Length);
            if (length <= 0) return;
            if (length < value.Length && Char.IsHighSurrogate(value[length - 1])) length++;
            text.Append(value, 0, length);
        }
    }
}
