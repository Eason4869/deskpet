using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
#if !NET10_0
using System.Diagnostics;
using System.Threading.Tasks;
#endif

namespace Dafeiyu.Tests
{
    public static class ContainerCompatibilityChecks
    {
        public static int Run(string directory)
        {
            Directory.CreateDirectory(directory);
            return CheckZip(directory) + CheckNestedZip(directory) + CheckTar(directory) +
                CheckTarLimit(directory) + CheckSqlite(directory);
        }

        static int CheckZip(string directory)
        {
            string path = Path.Combine(directory, "duplicate.zip");
            using (var file = File.Create(path))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                WriteZipEntry(zip, "数据.csv", "name,total\nfirst,11\n");
                WriteZipEntry(zip, "数据.csv", "name,total\nsecond,27\n");
                WriteZipEntry(zip, "../outside.csv", "escaped");
                var link = zip.CreateEntry("linked.csv");
                link.ExternalAttributes = unchecked((int)0xa1ff0000);
                using (var writer = new StreamWriter(link.Open())) writer.Write("数据.csv");
                WriteZipEntry(zip, "marked-encrypted.csv", "unavailable");
                using (var stream = zip.CreateEntry("large.csv").Open())
                {
                    var block = new byte[1024 * 1024];
                    for (int i = 0; i < 65; i++) stream.Write(block, 0, block.Length);
                }
            }
            MarkEntryEncrypted(path, "marked-encrypted.csv");
            var reader = new ArchiveFileReader();
            var sections = reader.Inspect(path, CancellationToken.None).Sections.ToArray();
            Require(sections.Length == 2 && sections[0].Label.Contains("数据.csv") &&
                sections[1].Label.Contains("数据.csv"), "ZIP lists only usable regular entries");
            Require(sections[0].Id != sections[1].Id &&
                reader.Inspect(path, CancellationToken.None).Sections.First().Id == sections[0].Id,
                "ZIP duplicate entry IDs are stable and unique");
            Require(reader.Read(path, new FileReadRequest { Section = sections[0].Id }, CancellationToken.None).Text.Contains("first") &&
                reader.Read(path, new FileReadRequest { Section = sections[1].Id }, CancellationToken.None).Text.Contains("second"),
                "ZIP selection reads the requested duplicate entry through the CSV reader");
            string other = Path.Combine(directory, "other.zip");
            using (var file = File.Create(other))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create)) WriteZipEntry(zip, "数据.csv", "other");
            Expect<InvalidDataException>(() => reader.Read(other, new FileReadRequest { Section = sections[0].Id }, CancellationToken.None),
                "ZIP entry IDs cannot select a file in a different archive");
            Require(!File.Exists(Path.Combine(directory, "outside.csv")), "ZIP entry never writes outside archive scratch storage");
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Expect<OperationCanceledException>(() => reader.Inspect(path, cancellation.Token), "ZIP honors cancellation");
            }
            return 6;
        }

        static void MarkEntryEncrypted(string path, string name)
        {
            byte[] bytes = File.ReadAllBytes(path);
            byte[] key = Encoding.ASCII.GetBytes(name);
            int matched = 0;
            for (int offset = 0; offset < bytes.Length - 46; offset++)
            {
                if (bytes[offset] != 0x50 || bytes[offset + 1] != 0x4b) continue;
                bool local = bytes[offset + 2] == 3 && bytes[offset + 3] == 4;
                bool central = bytes[offset + 2] == 1 && bytes[offset + 3] == 2;
                if (!local && !central) continue;
                int nameOffset = offset + (local ? 30 : 46);
                if (!bytes.Skip(nameOffset).Take(key.Length).SequenceEqual(key)) continue;
                bytes[offset + (local ? 6 : 8)] |= 1;
                matched++;
            }
            Require(matched == 2, "Fixture marks both ZIP headers as encrypted");
            File.WriteAllBytes(path, bytes);
        }

        static int CheckNestedZip(string directory)
        {
            byte[] inner = Package(zip => WriteZipEntry(zip, "cells.csv", "name,total\ninner,62\n"));
            byte[] middle = Package(zip => WriteZipBytes(zip, "inner.zip", inner));
            string path = Path.Combine(directory, "nested.zip");
            File.WriteAllBytes(path, Package(zip =>
            {
                WriteZipBytes(zip, "inner.zip", inner);
                WriteZipBytes(zip, "middle.zip", middle);
                WriteZipBytes(zip, "记录.docx", DocumentFormats.WriteDocumentBytes("entry.docx", "归档正文", CancellationToken.None));
            }));
            var reader = new ArchiveFileReader();
            var sections = reader.Inspect(path, CancellationToken.None).Sections;
            string innerId = reader.Read(path, new FileReadRequest { Section = sections[0].Id },
                CancellationToken.None).Text.Split('\t')[0];
            var result = reader.Read(path, new FileReadRequest { Section = sections[0].Id, NestedSection = innerId },
                CancellationToken.None);
            Require(result.Text.Contains("inner") && result.Text.Contains("62") && result.Range.Contains("cells.csv"),
                "Nested ZIP lists stable entry IDs and reads with attribution for both entries");
            string middleId = reader.Read(path, new FileReadRequest { Section = sections[1].Id },
                CancellationToken.None).Text.Split('\t')[0];
            Expect<InvalidDataException>(() => reader.Read(path,
                new FileReadRequest { Section = sections[1].Id, NestedSection = middleId }, CancellationToken.None),
                "ZIP stops at two archive layers");
            Require(reader.Read(path, new FileReadRequest { Section = sections[2].Id }, CancellationToken.None).Text.Contains("归档正文"),
                "ZIP reads attributed text through the DOCX reader");
            return 3;
        }

        static byte[] Package(Action<ZipArchive> build)
        {
            using (var stream = new MemoryStream())
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true)) build(zip);
                return stream.ToArray();
            }
        }

        static void WriteZipBytes(ZipArchive zip, string name, byte[] value)
        {
            using (var stream = zip.CreateEntry(name).Open()) stream.Write(value, 0, value.Length);
        }

        static void WriteZipEntry(ZipArchive zip, string name, string text)
        {
            using (var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false))) writer.Write(text);
        }

        static int CheckTar(string directory)
        {
            string path = Path.Combine(directory, "fixture.tar.gz");
            using (var file = File.Create(path))
            using (var gzip = new GZipStream(file, CompressionMode.Compress))
            {
                WriteTarEntry(gzip, "notes.csv", (byte)'0', "", "name,total\nnote,42\n");
                WriteTarEntry(gzip, "link.csv", (byte)'2', "notes.csv", "");
                WriteTarEntry(gzip, "pipe.csv", (byte)'6', "", "");
                gzip.Write(new byte[1024], 0, 1024);
            }
            var reader = new ArchiveFileReader();
            var sections = reader.Inspect(path, CancellationToken.None).Sections.ToArray();
            Require(sections.Length == 1 && sections[0].Label.Contains("notes.csv"), "TAR.GZ skips links and special nodes");
            Require(reader.Read(path, new FileReadRequest { Section = sections[0].Id }, CancellationToken.None).Text.Contains("42"),
                "TAR.GZ reads the selected regular file");
            string damaged = Path.Combine(directory, "damaged.tar.gz");
            using (var file = File.Create(damaged))
            using (var gzip = new GZipStream(file, CompressionMode.Compress))
                gzip.Write(new byte[512], 0, 512);
            Expect<InvalidDataException>(() => reader.Inspect(damaged, CancellationToken.None),
                "TAR.GZ rejects an incomplete end marker");
            return 3;
        }

        static int CheckTarLimit(string directory)
        {
            string path = Path.Combine(directory, "bounded.tar.gz");
            using (var file = File.Create(path))
            using (var gzip = new GZipStream(file, CompressionMode.Compress))
            {
                WriteTarEntry(gzip, "first.csv", (byte)'0', "", "name,total\nfirst,1\n");
                for (int index = 0; index < 9999; index++)
                    WriteTarEntry(gzip, "directory/", (byte)'5', "", "");
                WriteTarEntry(gzip, "uncovered.csv", (byte)'0', "", "name,total\nlate,2\n");
                gzip.Write(new byte[1024], 0, 1024);
            }
            var reader = new ArchiveFileReader();
            var manifest = reader.Inspect(path, CancellationToken.None);
            Require(manifest.Sections.Count == 1 && manifest.Sections[0].Label == "first.csv" &&
                !string.IsNullOrEmpty(manifest.Detail), "TAR.GZ reports readable sections before the archive entry limit");
            return 1;
        }

        static void WriteTarEntry(Stream stream, string name, byte type, string link, string value)
        {
            byte[] header = new byte[512];
            byte[] data = Encoding.UTF8.GetBytes(value);
            Copy(name, header, 0);
            Copy("0000644\0", header, 100);
            Copy("0000000\0", header, 108);
            Copy("0000000\0", header, 116);
            Copy(Convert.ToString(data.Length, 8).PadLeft(11, '0') + "\0", header, 124);
            Copy("00000000000\0", header, 136);
            Copy("        ", header, 148);
            header[156] = type;
            Copy(link, header, 157);
            Copy("ustar\0", header, 257);
            Copy("00", header, 263);
            Copy(Convert.ToString(header.Sum(b => (int)b), 8).PadLeft(6, '0') + "\0 ", header, 148);
            stream.Write(header, 0, header.Length);
            stream.Write(data, 0, data.Length);
            int padding = (512 - data.Length % 512) % 512;
            if (padding != 0) stream.Write(new byte[padding], 0, padding);
        }

        static void Copy(string value, byte[] destination, int offset)
        {
            byte[] data = Encoding.UTF8.GetBytes(value);
            Buffer.BlockCopy(data, 0, destination, offset, data.Length);
        }

        static int CheckSqlite(string directory)
        {
            int checks = 13;
#if !NET10_0
            checks += CheckWindowsNativeComponent(directory);
#endif
            string path = Path.Combine(directory, "fixture #报价.db");
            IntPtr db;
            var api = SqliteNative.Functions;
            Require(api.Open(SqliteNative.Utf8(path), out db, 6, IntPtr.Zero) == 0, "SQLite fixture opens");
            try
            {
                Execute(db, "CREATE TABLE \"报\"\"价\" (id INTEGER PRIMARY KEY, name TEXT, cost REAL, detail BLOB);" +
                    "INSERT INTO \"报\"\"价\" VALUES (1, '其他', NULL, NULL), (2, 'O''Neil', 2.5, x'010203'), (3, 'O''Neil', 7, x'00');" +
                    "CREATE VIEW ignored_view AS SELECT * FROM \"报\"\"价\";");
            }
            finally { api.Close(db); }

            string original = Digest(path);
            var reader = new SqliteFileReader();
            var sections = reader.Inspect(path, CancellationToken.None).Sections.ToArray();
            Require(sections.Length == 1 && sections[0].Id == "报\"价" && sections[0].Label.Contains("cost"),
                "SQLite lists quoted ordinary table identifiers and columns");
            var request = new FileReadRequest { Section = "报\"价", FilterColumn = "name", FilterValue = "O'Neil", Column = "cost", Operation = "sum" };
            Require(reader.Read(path, request, CancellationToken.None).Text.Contains("9.5"),
                "SQLite sums the full filtered result using quoted parameter values");
            request.Operation = null;
            request.Column = null;
            request.SortColumn = "id";
            request.Count = 1;
            var first = reader.Read(path, request, CancellationToken.None);
            Require(first.Text.Contains("O'Neil") && first.Text.Contains("BLOB") && first.Text.Contains("3") &&
                !first.Text.Contains("其他") && !first.Text.Contains("010203") && first.NextStart == 2,
                "SQLite applies parameterized filtering and paging and reports BLOB length");
            var nulls = reader.Read(path, new FileReadRequest { Section = request.Section, FilterColumn = "cost",
                FilterOperator = "is_null" }, CancellationToken.None);
            Require(nulls.Text.Contains("其他") && nulls.Text.Contains("NULL") && !nulls.Text.Contains("O'Neil"),
                "SQLite can filter and display NULL values");
            Expect<InvalidDataException>(() => reader.Read(path, new FileReadRequest { Section = "ignored_view" },
                CancellationToken.None), "SQLite rejects views outside the ordinary table schema");
            request.Operation = "update";
            Expect<InvalidDataException>(() => reader.Read(path, request, CancellationToken.None), "SQLite disallows write operations");
            Expect<InvalidDataException>(() => reader.Read(path,
                new FileReadRequest { Section = request.Section, Query = "SELECT count(*) FROM a_table" }, CancellationToken.None),
                "SQLite refuses free-form SQL as a query argument");
            Require(original == Digest(path), "SQLite standalone query preserves original database bytes");

            Require(api.Open(SqliteNative.Utf8(path), out db, 2, IntPtr.Zero) == 0, "SQLite WAL fixture reopens");
            try
            {
                Execute(db, "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; INSERT INTO \"报\"\"价\" VALUES (4, 'later', 99, NULL);");
                string main = Digest(path);
                string wal = Digest(path + "-wal");
                string shared = Digest(path + "-shm");
                Expect<AttachmentFileProblem>(() => reader.Inspect(path, CancellationToken.None), "SQLite rejects active WAL before reading stale data");
                var batch = FileContentService.ImportBatch(new[] { path }, Path.Combine(directory, "wal-import"), false, null, CancellationToken.None);
                Require(batch.Files.Count == 0 && batch.Errors.Count == 1 && batch.Errors[0].Contains("导出独立 SQLite 文件"),
                    "SQLite WAL attachment explains how to export a consistent standalone copy");
                Require(main == Digest(path) && wal == Digest(path + "-wal") && shared == Digest(path + "-shm"),
                    "SQLite rejection preserves active database and sidecar bytes");
            }
            finally { api.Close(db); }
            Expect<AttachmentFileProblem>(() => reader.Inspect(path, CancellationToken.None), "SQLite rejects WAL-mode copies missing sidecars");
            return checks;
        }

#if !NET10_0
        static int CheckWindowsNativeComponent(string directory)
        {
            string cache = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "file-test-cache");
            SqliteWindowsLibrary.ConfigureCacheRoot(cache);
            var concurrent = new SqliteExports[8];
            Parallel.For(0, concurrent.Length, index => concurrent[index] = SqliteNative.Functions);
            Require(concurrent.All(api => api != null && ReferenceEquals(api, concurrent[0])),
                "SQLite initializes a shared native module across concurrent first reads");
            string installed = NativeComponentPath(cache);
            using (var file = File.OpenRead(installed))
            using (var sha = SHA256.Create())
            {
                string hash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
                Require(hash == "6ad8e149f8ce3ed3716402b4b3a2268ebbdc7b64391b5fafed747e03bb1b9418",
                    "SQLite cached module matches the pinned embedded build");
            }
            using (var process = Process.GetCurrentProcess())
                Require(process.Modules.Cast<ProcessModule>().Any(module => string.Equals(
                    module.FileName, installed, StringComparison.OrdinalIgnoreCase)),
                    "SQLite is loaded from the validated cache path");
            string tamperedCache = Path.Combine(directory, "tampered-native");
            string tampered = NativeComponentPath(tamperedCache);
            Directory.CreateDirectory(Path.GetDirectoryName(tampered));
            using (var file = File.Create(tampered)) file.SetLength(1972224);
            Expect<InvalidDataException>(() => SqliteWindowsLibrary.LoadFromCache(tamperedCache),
                "SQLite rejects a tampered cache entry even when a valid module is already loaded");
            return 4;
        }

        static string NativeComponentPath(string cache)
        {
            return Path.Combine(cache, "file-components", "sqlite", SqliteNative.WindowsHash, "e_sqlite3.dll");
        }
#endif

        static string Digest(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(stream));
        }

        static void Execute(IntPtr db, string sql)
        {
            int code = SqliteNative.Functions.Execute(db, SqliteNative.Utf8(sql), IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            Require(code == 0, "SQLite fixture statement succeeds (" + code.ToString(CultureInfo.InvariantCulture) + ")");
        }

        static void Expect<TException>(Action action, string message) where TException : Exception
        {
            try { action(); }
            catch (TException) { return; }
            throw new InvalidOperationException(message);
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
