using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Dafeiyu
{
    internal static class FileContentService
    {
        const int MaximumTextBytes = 32 * 1024;
        internal const int InitialTextBytes = 8 * 1024;
        internal const long DocumentLimit = 128L * 1024 * 1024;
        static readonly HashSet<string> TextExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".log", ".json", ".xml", ".yaml", ".yml", ".cs", ".py", ".js", ".ts", ".tsx",
            ".jsx", ".html", ".htm", ".css", ".sql", ".ps1", ".sh", ".toml", ".ini", ".cfg", ".conf",
            ".java", ".kt", ".c", ".h", ".cpp", ".rs", ".go", ".rb", ".php", ".swift", ".jsonl",
            ".bat", ".cmd", ".sln", ".csproj", ".props", ".targets", ".gitignore", ".editorconfig"
        };
        static readonly HashSet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".png", ".jpg", ".jpeg", ".gif", ".webp" };
        static readonly string[] StructuredExtensions =
            { ".csv", ".tsv", ".xlsx", ".xls", ".docx", ".doc", ".pptx", ".pdf", ".zip", ".tar.gz", ".sqlite", ".db", ".mp3", ".wav", ".mp4" };

        internal static string[] FilePatterns()
        {
            return StructuredExtensions.Concat(ImageExtensions).Concat(TextExtensions)
                .Select(extension => "*" + extension).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        internal static string Extension(string path)
        {
            string name = Path.GetFileName(path);
            if (name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) return ".tar.gz";
            if (TextExtensions.Contains(name)) return name.ToLowerInvariant();
            return Path.GetExtension(name).ToLowerInvariant();
        }

        internal static bool IsImage(string extension) { return ImageExtensions.Contains(extension); }
        internal static bool IsMedia(string extension) { return extension == ".mp3" || extension == ".wav" || extension == ".mp4"; }
        internal static bool IsSqlite(string extension) { return extension == ".sqlite" || extension == ".db"; }
        internal static bool CanRead(string extension) { return TextExtensions.Contains(extension) || StructuredExtensions.Contains(extension); }
        internal static bool HasStructuredReader(string extension) { return StructuredExtensions.Contains(extension); }

        static IFileContentReader Reader(string extension)
        {
            FileCompatibilityDependencies.Ensure();
            if (TextExtensions.Contains(extension)) return new TextFileReader();
            switch (extension)
            {
                case ".csv": return new DelimitedTableReader(',');
                case ".tsv": return new DelimitedTableReader('\t');
                case ".xlsx": return new WorkbookFileReader();
                case ".xls": return new LegacyWorkbookReader();
                case ".doc": case ".docx": case ".pptx": return new OfficeDocumentReader(extension.Substring(1));
                case ".pdf": return new PdfFileReader();
                case ".zip": case ".tar.gz": return new ArchiveFileReader();
                case ".sqlite": case ".db": return new SqliteFileReader();
                case ".mp3": case ".wav": case ".mp4": return new MediaFileReader();
                default: throw new ApiException(TextResources.Get("fileCompatibility.type"));
            }
        }

        internal static FileManifest InspectPath(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Reader(Extension(path)).Inspect(path, token);
        }

        internal static FileReadResult ReadPath(string path, FileReadRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (request == null) throw new ArgumentNullException("request");
            if (request.Start <= 0 || request.Count <= 0 || request.Count > 1000 ||
                request.StartColumn <= 0 || request.ColumnCount <= 0 || request.ColumnCount > 256)
                throw new ArgumentOutOfRangeException("request");
            if (Extension(path) == ".pdf" && String.Equals(request.Operation, "ocr", StringComparison.OrdinalIgnoreCase))
                return FileOcrService.ReadPdf(path, request, token);
            var result = Reader(Extension(path)).Read(path, request, token);
            string text = LimitUtf8(result.Text, MaximumTextBytes);
            if (text == result.Text) return result;
            return new FileReadResult(TextResources.Format("chatAttachment.read.fileFileSubstring", text),
                result.Range, result.NextStart, result.Image, result.ImageMediaType);
        }

        internal static string LimitUtf8(string text, int max)
        {
            if (String.IsNullOrEmpty(text) || Encoding.UTF8.GetByteCount(text) <= max) return text ?? "";
            int usedChars, usedBytes;
            bool complete;
            char[] source = text.ToCharArray();
            Encoding.UTF8.GetEncoder().Convert(source, 0, source.Length, new byte[max], 0, max, false,
                out usedChars, out usedBytes, out complete);
            return text.Substring(0, usedChars);
        }

        internal static string FormatResult(FileReadResult result)
        {
            string output = String.IsNullOrEmpty(result.Range) ? result.Text : result.Range + "\n" + result.Text;
            if (result.NextStart.HasValue)
                output += "\n" + TextResources.Format("fileCompatibility.more", result.NextStart.Value);
            return output;
        }

        internal static string FormatFrameTime(long requested, long actual)
        {
            string seconds=(requested/1000.0).ToString("0.000",System.Globalization.CultureInfo.InvariantCulture);
            if(actual>=0 && actual!=requested)
                seconds+=" → "+(actual/1000.0).ToString("0.000",System.Globalization.CultureInfo.InvariantCulture);
            return TextResources.Format("fileCompatibility.frameAt",seconds);
        }

        internal static FileManifest Inspect(AttachmentReference file, string dataRoot, CancellationToken token)
        {
            using (var lease = new AttachmentFileStore(dataRoot).Open(file,token))
                return InspectPath(lease.Path, token);
        }

        internal static FileReadResult Read(AttachmentReference file, string dataRoot, FileReadRequest request, CancellationToken token)
        {
            using (var lease = new AttachmentFileStore(dataRoot).Open(file,token))
                return ReadPath(lease.Path, request, token);
        }

        internal static void Export(AttachmentReference file,string dataRoot,string destination)
        {
            string target=Path.GetFullPath(destination);
            using(var lease=new AttachmentFileStore(dataRoot).Open(file))
            {
                if(String.Equals(target,lease.Path,Maintenance.CachePaths.Comparison))return;
                string temporary=Path.Combine(Path.GetDirectoryName(target),".deskpet-"+Guid.NewGuid().ToString("N")+".tmp");
                try
                {
                    using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                    {lease.Stream.CopyTo(output);output.Flush(true);}
                    AtomicFile.Commit(temporary,target);
                }
                finally{if(File.Exists(temporary))File.Delete(temporary);}
            }
        }

        internal static ChatAttachment Import(string path, string dataRoot, bool persistent, CancellationToken token, string ownerId = null)
        {
            token.ThrowIfCancellationRequested();
            string extension = Extension(path);
            if (IsImage(extension)) return ChatAttachment.Read(path);
            if (!CanRead(extension)) throw new ApiException(TextResources.Get("fileCompatibility.type"));
            var store = new AttachmentFileStore(dataRoot);
            var file = store.Import(path, extension, IsMedia(extension) ? AttachmentFileStore.AbsoluteFileLimit : DocumentLimit, persistent, token,
                IsSqlite(extension) ? new Action<string, Stream>(SqliteFileReader.EnsureStandalone) : null,ownerId);
            try
            {
                using (var lease = store.Open(file,token))
                {
                    string preview;
                    try
                    {
                        var manifest = InspectPath(lease.Path, token);
                        preview = manifest.Detail ?? "";
                        if (manifest.Sections.Count != 0)
                        {
                            preview += "\n" + String.Join("\n", manifest.Sections.Take(25).Select(section => section.Label));
                            if (manifest.Sections.Count > 25)
                                preview += "\n" + TextResources.Format("fileCompatibility.more", 26);
                        }
                        if (!IsMedia(extension) && extension != ".zip" && extension != ".tar.gz" && !IsSqlite(extension))
                        {
                            var first = ReadPath(lease.Path, new FileReadRequest { Count = 25 }, token);
                            preview = FormatResult(first);
                        }
                    }
                    catch (NotSupportedException)
                    {
                        preview = TextResources.Get("fileCompatibility.componentUnavailable");
                    }
                    catch (DecoderFallbackException)
                    {
                        preview = TextResources.Get("chatAttachment.read.text.alternativeB");
                    }
                    return ChatAttachment.FromImportedFile(file, LimitUtf8(preview, InitialTextBytes));
                }
            }
            catch
            {
                store.Discard(file);
                throw;
            }
        }

        internal static bool IsReadFailure(Exception error)
        {
            return error is ApiException || error is IOException || error is UnauthorizedAccessException ||
                error is InvalidDataException || error is ArgumentException || error is System.Xml.XmlException ||
                error is NotSupportedException || error is TimeoutException || error is DllNotFoundException;
        }

        internal static string UserError(Exception error)
        {
            if (error is FileNotFoundException) return TextResources.Get("fileCompatibility.missing");
            if (error is DllNotFoundException || error is NotSupportedException) return TextResources.Get("fileCompatibility.componentUnavailable");
            if (error is ApiException || error is TimeoutException || error is AttachmentFileProblem) return error.Message;
            return TextResources.Get("conversationComposer.addPathsAsync.status");
        }

        internal sealed class Batch
        {
            internal readonly List<ChatAttachment> Files = new List<ChatAttachment>();
            internal readonly List<string> Errors = new List<string>();
        }

        internal static Batch ImportBatch(IEnumerable<string> paths, string dataRoot, bool persistent,
            Func<string, ChatAttachment> folder, CancellationToken token, string ownerId = null)
        {
            var batch = new Batch();
            try
            {
                foreach (string path in paths)
                {
                    token.ThrowIfCancellationRequested();
                    try { batch.Files.Add(Directory.Exists(path) ? folder(path) : Import(path, dataRoot, persistent, token,ownerId)); }
                    catch (Exception error)
                    {
                        if (!IsReadFailure(error)) throw;
                        batch.Errors.Add(Path.GetFileName(path) + ": " + UserError(error));
                    }
                }
                token.ThrowIfCancellationRequested();
                return batch;
            }
            catch
            {
                Discard(dataRoot, batch.Files);
                throw;
            }
        }

        internal static void Discard(string dataRoot, IEnumerable<ChatAttachment> files)
        {
            var store = new AttachmentFileStore(dataRoot);
            foreach (var file in files)
                if (file.ImportedFile != null) store.Discard(file.ImportedFile);
        }

        internal static void Track(string dataRoot,IEnumerable<ChatAttachment> attachments,bool temporary)
        {
            if(String.IsNullOrEmpty(dataRoot)||attachments==null)return;
            var store=new AttachmentFileStore(dataRoot);
            foreach(var file in attachments)
                if(file!=null&&file.ImportedFile!=null)store.Track(file.ImportedFile,temporary);
        }
    }
}
