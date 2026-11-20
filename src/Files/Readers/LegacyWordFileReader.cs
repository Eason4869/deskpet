using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using OpenMcdf;

namespace Dafeiyu
{
    internal sealed class LegacyWordFileReader : IFileContentReader
    {
        const int MaxTextCharacters = 2 * 1024 * 1024;
        const int MaxBlocks = 100000;
        const long MaxFileBytes = 128L * 1024 * 1024;
        const string Detail = "Word 97–2003 text. Table cell text is preserved in source order.";

        sealed class Section
        {
            internal readonly string Id, Label;
            internal readonly List<string> Blocks = new List<string>();

            internal Section(string id, string label) { Id = id; Label = label; }
        }

        sealed class WordDocumentUnavailableException : NotSupportedException
        {
            internal WordDocumentUnavailableException(string message) : base(message) { }
        }

        public FileManifest Inspect(string path, CancellationToken token)
        {
            try
            {
                var sections = new List<FileSection>();
                foreach (var section in Load(path, token))
                    if (section.Id == "body" || section.Blocks.Count > 0)
                        sections.Add(new FileSection(section.Id, section.Label, section.Blocks.Count));
                return new FileManifest("doc", sections, Detail);
            }
            catch (WordDocumentUnavailableException error)
            {
                return new FileManifest("doc", new FileSection[0], error.Message);
            }
        }

        public FileReadResult Read(string path, FileReadRequest request, CancellationToken token)
        {
            string id = String.IsNullOrEmpty(request.Section) ? "body" : request.Section;
            foreach (var section in Load(path, token))
            {
                if (section.Id != id) continue;
                return OfficeDocumentReader.ReadRange(id, request, section.Blocks.Count, 100,
                    index => "[" + section.Label + " " + DocumentReadWindow.SourceNumber(index) + "]\n" + section.Blocks[index - 1], token);
            }
            throw new ArgumentException("Select a section of the Word document.");
        }

        static IEnumerable<Section> Load(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using (var stream = new DocumentReadStream(path, token))
                {
                    if (stream.Length < 512 || stream.Length > MaxFileBytes) throw InvalidDocument();
                    using (var storage = RootStorage.Open(stream, StorageModeFlags.LeaveOpen))
                    using (var word = OpenPart(storage, "WordDocument"))
                    {
                        if (word.ReadUInt16() != 0xa5ec) throw InvalidDocument();
                        int version = word.ReadUInt16();
                        if (version < 0xc1 || version > 0x112)
                            throw new WordDocumentUnavailableException("Only Word 97–2003 binary documents are supported.");
                        word.BaseStream.Position = 10;
                        int flags = word.ReadUInt16();
                        if ((flags & 0x8100) != 0)
                            throw new WordDocumentUnavailableException("Password-protected Word documents are unavailable.");
                        using (var table = OpenPart(storage, (flags & 0x0200) == 0 ? "0Table" : "1Table"))
                            return ReadStories(word, table, token);
                    }
                }
            }
            catch (OpenMcdf.FileFormatException) { throw InvalidDocument(); }
            catch (EndOfStreamException) { throw InvalidDocument(); }
        }

        static BinaryReader OpenPart(RootStorage storage, string name)
        {
            CfbStream part;
            if (!storage.TryOpenStream(name, out part)) throw InvalidDocument();
            return new BinaryReader(part);
        }

        static IEnumerable<Section> ReadStories(BinaryReader word, BinaryReader table, CancellationToken token)
        {
            // MS-DOC FIB: FibRgW is followed by FibRgLw97, then offset/size pairs in FibRgFcLcb97.
            word.BaseStream.Position = 32;
            int shortCount = word.ReadUInt16();
            word.BaseStream.Position += 2 * shortCount;
            int longCount = word.ReadUInt16();
            long longStart = word.BaseStream.Position;
            if (longCount < 11) throw InvalidDocument();
            long wordSize = word.ReadUInt32();
            long pairsOffset = longStart + 4 * longCount;
            if (pairsOffset + 2 + 34 * 8 > wordSize || wordSize > word.BaseStream.Length) throw InvalidDocument();
            word.BaseStream.Position = longStart + 12;
            int body = word.ReadInt32(), footnotes = word.ReadInt32(), headers = word.ReadInt32();
            word.ReadInt32();  // Reserved.
            int comments = word.ReadInt32(), endnotes = word.ReadInt32(), textboxes = word.ReadInt32(), headerTextboxes = word.ReadInt32();
            long total = 0;
            foreach (int count in new[] { body, footnotes, headers, comments, endnotes, textboxes, headerTextboxes })
            {
                if (count < 0) throw InvalidDocument();
                total += count;
            }
            if (total >= MaxTextCharacters) throw InvalidDocument();
            word.BaseStream.Position = pairsOffset;
            int pairs = word.ReadUInt16();
            if (pairs <= 33) throw InvalidDocument();
            long pairsStart = word.BaseStream.Position;
            word.BaseStream.Position = pairsStart + 11 * 8;
            long headersOffset = word.ReadUInt32(), headersBytes = word.ReadUInt32();
            word.BaseStream.Position = pairsStart + 33 * 8;
            long clxOffset = word.ReadUInt32(), clxBytes = word.ReadUInt32();
            char[] characters = ReadPieces(word, table, wordSize, clxOffset, clxBytes, (int)total, token);
            var result = new List<Section>();
            var main = new Section("body", "Body block");
            AppendStory(main, characters, 0, body, token);
            result.Add(main);
            var headerSection = new Section("headers", "Header");
            var footerSection = new Section("footers", "Footer");
            if (headers > 0)
                ReadHeaders(table, headersOffset, headersBytes, characters, body + footnotes, headers, headerSection, footerSection, token);
            result.Add(headerSection);
            result.Add(footerSection);
            var notes = new Section("notes", "Note");
            AppendStory(notes, characters, body, footnotes, token);
            AppendStory(notes, characters, body + footnotes + headers, comments + endnotes, token);
            result.Add(notes);
            var boxes = new Section("textboxes", "Textbox paragraph");
            AppendStory(boxes, characters, body + footnotes + headers + comments + endnotes, textboxes + headerTextboxes, token);
            result.Add(boxes);
            return result;
        }

        static char[] ReadPieces(BinaryReader word, BinaryReader table, long wordSize, long offset, long length,
            int count, CancellationToken token)
        {
            if (length < 9 || length > 4L * 1024 * 1024 || offset + length > table.BaseStream.Length) throw InvalidDocument();
            table.BaseStream.Position = offset;
            byte[] contents = table.ReadBytes((int)length);
            if (contents.Length != length) throw InvalidDocument();
            using (var pieces = new BinaryReader(new MemoryStream(contents, false)))
                return ReadPieces(word, pieces, wordSize, count, token);
        }

        static char[] ReadPieces(BinaryReader word, BinaryReader layout, long wordSize, int count, CancellationToken token)
        {
            long end = layout.BaseStream.Length;
            int marker = layout.ReadByte();
            while (marker == 1)
            {
                token.ThrowIfCancellationRequested();
                if (layout.BaseStream.Position + 2 >= end) throw InvalidDocument();
                int propertyBytes = layout.ReadUInt16();
                layout.BaseStream.Position += propertyBytes;
                if (layout.BaseStream.Position >= end) throw InvalidDocument();
                marker = layout.ReadByte();
            }
            if (marker != 2 || layout.BaseStream.Position + 4 > end) throw InvalidDocument();
            long plcBytes = layout.ReadUInt32();
            long plc = layout.BaseStream.Position;
            if (plcBytes < 4 || (plcBytes - 4) % 12 != 0 || plc + plcBytes != end) throw InvalidDocument();
            int pieces = (int)((plcBytes - 4) / 12);
            if (pieces > MaxBlocks) throw InvalidDocument();
            int previous = layout.ReadInt32();
            if (previous != 0) throw InvalidDocument();
            var result = new char[count];
            for (int i = 0; i < pieces; i++)
            {
                token.ThrowIfCancellationRequested();
                layout.BaseStream.Position = plc + 4 * (i + 1L);
                int next = layout.ReadInt32();
                if (next <= previous || next > count + 1) throw InvalidDocument();
                layout.BaseStream.Position = plc + 4 * (pieces + 1L) + i * 8L + 2;
                uint encoded = layout.ReadUInt32();
                bool compressed = (encoded & 0x40000000) != 0;
                long fc = encoded & 0x3fffffff;
                int bytesPerCharacter = compressed ? 1 : 2;
                if (compressed) fc /= 2;
                int byteCount = (Math.Min(next, count) - previous) * bytesPerCharacter;
                if (byteCount < 0 || fc + byteCount > wordSize) throw InvalidDocument();
                word.BaseStream.Position = fc;
                byte[] bytes = word.ReadBytes(byteCount);
                if (bytes.Length != byteCount) throw InvalidDocument();
                for (int c = 0; c < byteCount / bytesPerCharacter; c++)
                    result[previous + c] = compressed ? DecodeCompressed(bytes[c]) : (char)(bytes[c * 2] | bytes[c * 2 + 1] << 8);
                previous = next;
            }
            if (previous < count) throw InvalidDocument();
            return result;
        }

        static char DecodeCompressed(byte value)
        {
            // FcCompressed uses 8-bit Unicode with the exceptions from MS-DOC §2.9.73.
            const string exceptions = "\u0080\u0081‚ƒ„…†‡ˆ‰Š‹Œ\u008d\u008e\u008f\u0090‘’“”•–—˜™š›œ\u009d\u009eŸ";
            return value >= 0x80 && value < 0xa0 ? exceptions[value - 0x80] : (char)value;
        }

        static void ReadHeaders(BinaryReader table, long offset, long length, char[] text, int start, int count,
            Section headers, Section footers, CancellationToken token)
        {
            // Plcfhdd begins with six footnote/endnote separators. Later entries appear in groups of six per section.
            if (length < 32 || length > 4 * MaxBlocks || length % 4 != 0 || offset + length > table.BaseStream.Length)
                throw InvalidDocument();
            table.BaseStream.Position = offset;
            int previous = table.ReadInt32();
            if (previous != 0) throw InvalidDocument();
            for (int i = 0; i < length / 4 - 2; i++)
            {
                token.ThrowIfCancellationRequested();
                int next = table.ReadInt32();
                if (next < previous || next >= count) throw InvalidDocument();
                if (i >= 6 && next > previous)
                {
                    int occurrence = (i - 6) % 6;
                    var section = occurrence == 2 || occurrence == 3 || occurrence == 5 ? footers : headers;
                    AppendStory(section, text, start + previous, next - previous, token);
                }
                previous = next;
            }
        }

        static void AppendStory(Section section, char[] text, int start, int count, CancellationToken token)
        {
            int hiddenFields = 0, depth = 0;
            var fieldCodes = new bool[64];
            var block = new StringBuilder();
            for (int i = start; i < start + count; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                char ch = text[i];
                if (ch == '\u0013')
                {
                    if (depth == fieldCodes.Length) throw InvalidDocument();
                    fieldCodes[depth++] = true;
                    hiddenFields++;
                }
                else if (ch == '\u0014' && depth > 0 && fieldCodes[depth - 1])
                {
                    fieldCodes[depth - 1] = false;
                    hiddenFields--;
                }
                else if (ch == '\u0015' && depth > 0)
                {
                    if (fieldCodes[--depth]) hiddenFields--;
                }
                else if (hiddenFields == 0)
                {
                    if (ch == '\r' || ch == '\f') EndBlock(section, block);
                    else if (ch == '\a')
                    {
                        if (i + 1 < start + count && text[i + 1] == '\a') { EndBlock(section, block); i++; }
                        else block.Append(" | ");
                    }
                    else if (ch == '\v') block.AppendLine();
                    else if (ch == '\u0001' || ch == '\u0008') block.Append("[Embedded object]");
                    else if (!Char.IsControl(ch) || ch == '\t') block.Append(ch);
                }
            }
            EndBlock(section, block);
        }

        static void EndBlock(Section section, StringBuilder block)
        {
            string value = block.ToString().Trim();
            if (value.Length > 0)
            {
                if (section.Blocks.Count >= MaxBlocks) throw InvalidDocument();
                section.Blocks.Add(value);
            }
            block.Length = 0;
        }

        static InvalidDataException InvalidDocument()
        {
            return new InvalidDataException("The binary Word document is malformed or exceeds the document parsing limits.");
        }
    }
}
