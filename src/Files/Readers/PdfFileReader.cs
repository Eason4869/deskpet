using System;
using System.IO;
using System.Threading;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace Dafeiyu
{
    internal sealed class PdfFileReader : IFileContentReader
    {
        const int MaxPagesPerRead = 20;
        const int MaxLettersPerPage = 75000;
        const long MaxInputBytes = 128L * 1024 * 1024;
        const string Encrypted = "Password-protected PDF documents are unavailable for local text extraction.";
        const string OcrUnavailable = "Local PDF rendering and OCR are unavailable.";

        public FileManifest Inspect(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                return WithDocument(path, token, document =>
                    new FileManifest("pdf", new[] { new FileSection("pages", "Pages", document.NumberOfPages) }));
            }
            catch (PdfDocumentEncryptedException)
            {
                return new FileManifest("pdf", new FileSection[0], Encrypted);
            }
        }

        public FileReadResult Read(string path, FileReadRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (String.Equals(request.Operation, "ocr", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(request.Operation, "image", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException(OcrUnavailable);
            if (!String.IsNullOrEmpty(request.Section) && request.Section != "pages")
                throw new ArgumentException("Select the pages section.");
            try
            {
                return WithDocument(path, token, document => ReadPages(document, request, token));
            }
            catch (PdfDocumentEncryptedException)
            {
                throw new NotSupportedException(Encrypted);
            }
        }

        static T WithDocument<T>(string path, CancellationToken token, Func<PdfDocument, T> read)
        {
            using (var stream = new DocumentReadStream(path, token))
            {
                if (stream.Length > MaxInputBytes) throw new InvalidDataException("The PDF exceeds the file size limit.");
                var options = new ParsingOptions {
                    MaxStackDepth = 128, UseLenientParsing = true, SkipMissingFonts = true,
                    FilterProvider = new PdfStreamFilters(token)
                };
                try
                {
                    using (var document = PdfDocument.Open(stream, options))
                    {
                        token.ThrowIfCancellationRequested();
                        if (document.IsEncrypted) throw new PdfDocumentEncryptedException();
                        return read(document);
                    }
                }
                catch (PdfDocumentFormatException)
                {
                    token.ThrowIfCancellationRequested();
                    throw new InvalidDataException("The PDF is malformed or exceeds the parsing limits.");
                }
            }
        }

        static FileReadResult ReadPages(PdfDocument document, FileReadRequest request, CancellationToken token)
        {
            int first = Math.Max(1, request.Start);
            int count = Math.Min(Math.Max(1, request.Count), MaxPagesPerRead);
            var result = new DocumentReadWindow("pages", first);
            for (int offset = 0; offset < count && first <= document.NumberOfPages - offset; offset++)
            {
                token.ThrowIfCancellationRequested();
                int number = first + offset;
                string label = "[Page " + DocumentReadWindow.SourceNumber(number) + "]\n";
                if (!result.Add(label + PageText(document, number, token))) break;
            }
            return result.Complete(document.NumberOfPages);
        }

        static string PageText(PdfDocument document, int number, CancellationToken token)
        {
            try
            {
                var page = document.GetPage(number);
                token.ThrowIfCancellationRequested();
                if (page.Letters.Count > MaxLettersPerPage) return "[This page exceeds the PDF text extraction limit.]";
                string text = ContentOrderTextExtractor.GetText(page).Trim();
                return text.Length == 0 ? "[No readable text layer. Local OCR is unavailable.]" : text;
            }
            catch (PdfDocumentFormatException)
            {
                token.ThrowIfCancellationRequested();
                return "[This page is malformed or exceeds the PDF parsing limits.]";
            }
            catch (NotSupportedException)
            {
                token.ThrowIfCancellationRequested();
                return "[The PDF encoding on this page is unsupported.]";
            }
        }

    }
}
