using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Dafeiyu
{
    internal static class FileOcrService
    {
        const int MaxPages = 5;
        static Func<string, int, string, CancellationToken, string> platform;

        internal static void Configure(Func<string, int, string, CancellationToken, string> recognize)
        {
            Interlocked.Exchange(ref platform, recognize);
        }

        internal static bool Available
        {
            get
            {
#if PORTABLE
                return Volatile.Read(ref platform) != null;
#else
                return Environment.OSVersion.Platform == PlatformID.Win32NT;
#endif
            }
        }

        static string Recognize(string path, int page, string language, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (String.IsNullOrWhiteSpace(language)) language =
                System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-Hans" : "en-US";
            if (language == "chi_sim" || language == "zh_CN" || language == "zh-CN") language = "zh-Hans";
            if (language == "eng" || language == "en_US" || language == "en") language = "en-US";
            if (language != "zh-Hans" && language != "en-US")
                throw new NotSupportedException(TextResources.Get("fileCompatibility.componentUnavailable"));
#if PORTABLE
            var recognizer = Volatile.Read(ref platform);
            if (recognizer == null) throw new NotSupportedException(TextResources.Get("fileCompatibility.componentUnavailable"));
            return recognizer(path, page, language, token);
#else
            return WindowsFileOcr.Recognize(path, page, language, token);
#endif
        }

        internal static FileReadResult ReadPdf(string path, FileReadRequest request, CancellationToken token)
        {
            var pdf = new PdfFileReader();
            var manifest = pdf.Inspect(path, token);
            int pages = manifest.Sections.Count == 0 ? 0 : (manifest.Sections[0].Count ?? 0);
            int start = request.Start;
            if (start > pages) return new FileReadResult("", "");
            int end = Math.Min(pages, start + Math.Min(MaxPages, request.Count) - 1);
            var blocks = new List<string>();
            int next = start;
            int length = 0;
            for (int page = start; page <= end; page++)
            {
                var original = pdf.Read(path, new FileReadRequest { Start = page, Count = 1 }, token).Text;
                string pageNumber = page.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string scanned;
                try { scanned = Recognize(path, page, request.Language, token); }
                catch (Exception error)
                {
                    if (!FileContentService.IsReadFailure(error)) throw;
                    scanned = FileContentService.UserError(error);
                }
                string addition = DistinctLines(original, scanned);
                string section = original + (addition.Length == 0 ? "" : "\n[OCR " + pageNumber + "]\n" + addition);
                if (length + section.Length > 12000 && blocks.Count > 0) break;
                blocks.Add(FileContentService.LimitUtf8(section, 24 * 1024));
                length += section.Length;
                next++;
            }
            return new FileReadResult(String.Join("\n\n", blocks), "", next <= pages ? (int?)next : null);
        }

        static string DistinctLines(string original, string scanned)
        {
            var present = new HashSet<string>(original.Split('\n').Select(Normalize), StringComparer.OrdinalIgnoreCase);
            return String.Join("\n", (scanned ?? "").Split('\n').Where(line => Normalize(line).Length > 0 && present.Add(Normalize(line))));
        }

        static string Normalize(string line) { return new String(line.Where(c => !Char.IsWhiteSpace(c)).ToArray()); }

        internal static string ReadImage(ChatAttachment image, string dataRoot, string language, CancellationToken token)
        {
            if (!image.IsImage) throw new ArgumentException("Expected an image.");
            byte[] bytes = Convert.FromBase64String(image.Data ?? "");
            if (bytes.Length > ChatAttachment.MaximumBytes) throw new ArgumentException("Image exceeds its limit.");
            string extension = image.MediaType == "image/jpeg" ? ".jpg" : "." + image.MediaType.Substring("image/".Length);
            string directory = Path.Combine(Path.GetFullPath(dataRoot), AttachmentFileStore.FolderName);
            Maintenance.CachePaths.Check(directory);
            Directory.CreateDirectory(directory);
            using (Maintenance.CacheDirectoryPins.OpenDirectory(directory))
            {
                string path = Path.Combine(directory, Guid.NewGuid().ToString("N") + extension);
                try
                {
                    token.ThrowIfCancellationRequested();
                    using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) output.Write(bytes, 0, bytes.Length);
                    return FileContentService.LimitUtf8(Recognize(path, 0, language, token), FileContentService.InitialTextBytes);
                }
                finally { if (File.Exists(path)) File.Delete(path); }
            }
        }
    }
}
