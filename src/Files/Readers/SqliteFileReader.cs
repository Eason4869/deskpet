using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Dafeiyu
{
    internal sealed class SqliteFileReader : IFileContentReader
    {
        const int MaxTables = 1000;
        const int MaxRows = 1000;
        const int MaxOutputBytes = 32 * 1024;
        const int MaxCellBytes = 2048;
        static readonly byte[] Header = Encoding.ASCII.GetBytes("SQLite format 3\0");

        sealed class TableColumn
        {
            internal string Name;
            internal string Type;
            internal int KeyPosition;
            internal int Hidden;
        }

        internal static void EnsureStandalone(string path)
        {
            using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                EnsureStandalone(path, source);
        }

        internal static void EnsureStandalone(string path, Stream source)
        {
            var bytes = new byte[100];
            long position = source.Position;
            int filled = 0;
            try
            {
                source.Position = 0;
                while (filled < bytes.Length)
                {
                    int read = source.Read(bytes, filled, bytes.Length - filled);
                    if (read == 0) break;
                    filled += read;
                }
            }
            finally { source.Position = position; }
            if (filled != bytes.Length || !Header.SequenceEqual(bytes.Take(Header.Length)))
                throw new InvalidDataException("SQLite 文件头无效，文件可能损坏或已加密。");
            if (bytes[18] == 2 || bytes[19] == 2 || HasSidecar(path + "-wal") ||
                HasSidecar(path + "-shm") || HasSidecar(path + "-journal"))
                throw new AttachmentFileProblem("SQLite 数据库需要日志文件才能确认一致性。请在原工具中导出独立 SQLite 文件后重选。");
            if (bytes[18] != 1 || bytes[19] != 1)
                throw new InvalidDataException("SQLite 文件格式版本不受支持。");
        }

        static bool HasSidecar(string path)
        {
            try
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) return true;
                return new FileInfo(path).Length != 0;
            }
            catch (FileNotFoundException) { return false; }
        }

        public FileManifest Inspect(string path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            EnsureStandalone(path);
            using (var db = new SqliteReadConnection(path, token))
            {
                var names = new List<string>();
                using (var rows = db.Prepare("SELECT name FROM pragma_table_list " +
                    "WHERE schema='main' AND type='table' AND name NOT GLOB 'sqlite_*' ORDER BY name LIMIT 1001"))
                {
                    while (rows.Step()) names.Add(rows.Text(0, 4096));
                }
                var sections = new List<FileSection>();
                int labelBytes = 0;
                foreach (string name in names.Take(MaxTables))
                {
                    var columns = Columns(db, name);
                    var types = columns.Where(c => c.Hidden != 2).Take(32)
                        .Select(c => Escape(c.Name) + (c.Type.Length == 0 ? "" : " " + Escape(c.Type)));
                    string label = ClipUtf8(Escape(name) + " (" + string.Join(", ", types) +
                        (columns.Count > 32 ? ", …)" : ")"), MaxCellBytes);
                    labelBytes += Encoding.UTF8.GetByteCount(label);
                    if (labelBytes > MaxOutputBytes) break;
                    sections.Add(new FileSection(name, label, columns.Count));
                }
                return new FileManifest("sqlite", sections, sections.Count < names.Count
                    ? "SQLite 表清单已达到预览上限。" : null);
            }
        }

        public FileReadResult Read(string path, FileReadRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(request.Query))
                throw new InvalidDataException("SQLite 查询须使用表、列和筛选字段。");
            EnsureStandalone(path);
            using (var db = new SqliteReadConnection(path, token))
            {
                string table = ResolveTable(db, request.Section);
                var columns = Columns(db, table);
                string operation = (request.Operation ?? "").ToLowerInvariant();
                string parameter;
                string filter = Filter(columns, request, out parameter);
                string from = " FROM main." + Identifier(table) + filter;
                if (operation == "sum" || operation == "count")
                {
                    string value = operation == "count" && string.IsNullOrEmpty(request.Column)
                        ? "*" : Identifier(ResolveColumn(columns, request.Column).Name);
                    using (var aggregate = db.Prepare("SELECT " + operation + "(" + value + ")" + from))
                    {
                        if (parameter != null) aggregate.Bind(1, parameter);
                        if (!aggregate.Step()) throw new InvalidDataException("SQLite 聚合结果不可用。");
                        return new FileReadResult(operation.ToUpperInvariant() + ": " + Cell(aggregate, 0),
                            Escape(table) + (filter.Length == 0 ? " 全表" : " 全部匹配记录"));
                    }
                }
                if (operation.Length != 0 && operation != "rows")
                    throw new InvalidDataException("SQLite 仅支持浏览记录、COUNT 和 SUM。");
                return Page(db, table, columns, from, request, parameter);
            }
        }

        static FileReadResult Page(SqliteReadConnection db, string table, List<TableColumn> columns,
            string from, FileReadRequest request, string parameter)
        {
            var visible = columns.Where(c => c.Hidden != 2).ToList();
            int firstColumn = Math.Max(1, request.StartColumn);
            var selected = string.IsNullOrEmpty(request.Column)
                ? visible.Skip(firstColumn - 1).Take(Math.Max(1, Math.Min(256, request.ColumnCount))).ToList()
                : new List<TableColumn> { ResolveColumn(columns, request.Column) };
            if (selected.Count == 0) throw new InvalidDataException("SQLite 列范围超出表结构。");
            string projection = string.Join(",", selected.Select(c => Identifier(c.Name)));
            string order = OrderBy(db, table, columns, request.SortColumn);
            int start = Math.Max(1, Math.Min(request.Start, int.MaxValue - MaxRows - 1));
            int count = Math.Min(MaxRows, Math.Max(1, request.Count));
            var output = new StringBuilder();
            string heading = string.Join("\t", selected.Select(c => Escape(c.Name)));
            output.Append(ClipUtf8(heading, MaxOutputBytes / 4));
            int bytes = Encoding.UTF8.GetByteCount(output.ToString());
            int rows = 0;
            bool more = false;
            using (var page = db.Prepare("SELECT " + projection + from + order + " LIMIT ?2 OFFSET ?3"))
            {
                if (parameter != null) page.Bind(1, parameter);
                page.Bind(2, count + 1);
                page.Bind(3, start - 1);
                while (page.Step())
                {
                    if (rows == count) { more = true; break; }
                    var cells = new string[selected.Count];
                    for (int column = 0; column < cells.Length; column++) cells[column] = Cell(page, column);
                    string row = "\n" + string.Join("\t", cells);
                    int rowBytes = Encoding.UTF8.GetByteCount(row);
                    if (bytes + rowBytes > MaxOutputBytes)
                    {
                        if (rows > 0) { more = true; break; }
                        row = ClipUtf8(row, MaxOutputBytes - bytes);
                        rowBytes = Encoding.UTF8.GetByteCount(row);
                    }
                    output.Append(row);
                    bytes += rowBytes;
                    rows++;
                }
            }
            string range = Escape(table) + (rows == 0 ? " 无匹配记录" :
                " 行 " + Number(start) + "–" + Number(start + rows - 1));
            if (string.IsNullOrEmpty(request.Column))
                range += "，列 " + Number(firstColumn) + "–" + Number(firstColumn + selected.Count - 1);
            return new FileReadResult(output.ToString(), range, more ? (int?)checked(start + rows) : null);
        }

        static string OrderBy(SqliteReadConnection db, string table, List<TableColumn> columns, string sort)
        {
            var tieBreakers = columns.Where(c => c.KeyPosition > 0).OrderBy(c => c.KeyPosition)
                .Select(c => Identifier(c.Name)).ToList();
            bool withoutRowId;
            using (var info = db.Prepare("SELECT wr FROM pragma_table_list WHERE schema='main' AND name=?1"))
            {
                info.Bind(1, table);
                withoutRowId = info.Step() && info.Integer(0) != 0;
            }
            if (!withoutRowId)
            {
                string alias = new[] { "_rowid_", "rowid", "oid" }.FirstOrDefault(candidate =>
                    !columns.Any(c => string.Equals(c.Name, candidate, StringComparison.OrdinalIgnoreCase)));
                if (alias != null) tieBreakers.Add(alias);
            }
            if (tieBreakers.Count == 0) tieBreakers.AddRange(columns.Where(c => c.Hidden != 2).Select(c => Identifier(c.Name)));
            if (!string.IsNullOrEmpty(sort)) tieBreakers.Insert(0, Identifier(ResolveColumn(columns, sort).Name));
            return " ORDER BY " + string.Join(", ", tieBreakers.Distinct());
        }

        static string Filter(List<TableColumn> columns, FileReadRequest request, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(request.FilterColumn))
            {
                if (!string.IsNullOrEmpty(request.FilterOperator) || request.FilterValue != null)
                    throw new InvalidDataException("SQLite 筛选需要已确认的列名。");
                return "";
            }
            string name = Identifier(ResolveColumn(columns, request.FilterColumn).Name);
            string operation = (request.FilterOperator ?? "=").ToLowerInvariant();
            switch (operation)
            {
                case "is_null": return " WHERE " + name + " IS NULL";
                case "not_null": return " WHERE " + name + " IS NOT NULL";
                case "eq": operation = "="; break;
                case "ne": operation = "!="; break;
                case "lt": operation = "<"; break;
                case "lte": operation = "<="; break;
                case "gt": operation = ">"; break;
                case "gte": operation = ">="; break;
                case "=": case "!=": case "<": case "<=": case ">": case ">=": break;
                default: throw new InvalidDataException("SQLite 筛选运算符不受支持。");
            }
            value = request.FilterValue;
            if (value == null || Encoding.UTF8.GetByteCount(value) > 4096)
                throw new InvalidDataException("SQLite 筛选参数缺失或超过长度上限。");
            return " WHERE " + name + " " + operation + " ?1";
        }

        static string ResolveTable(SqliteReadConnection db, string name)
        {
            if (name != null && Encoding.UTF8.GetByteCount(name) > 4096)
                throw new InvalidDataException("SQLite 表名超过长度上限。");
            string sql = "SELECT name FROM pragma_table_list WHERE schema='main' AND type='table' AND name NOT GLOB 'sqlite_*'";
            using (var rows = db.Prepare(sql + (string.IsNullOrEmpty(name) ? " ORDER BY name LIMIT 1" : " AND name=?1")))
            {
                if (!string.IsNullOrEmpty(name)) rows.Bind(1, name);
                if (rows.Step()) return rows.Text(0, 4096);
            }
            throw new InvalidDataException("SQLite 表不存在或不支持读取。");
        }

        static List<TableColumn> Columns(SqliteReadConnection db, string table)
        {
            var columns = new List<TableColumn>();
            using (var rows = db.Prepare("SELECT name,type,pk,hidden FROM pragma_table_xinfo(?1,'main') ORDER BY cid"))
            {
                rows.Bind(1, table);
                while (rows.Step()) columns.Add(new TableColumn
                {
                    Name = rows.Text(0, 4096),
                    Type = rows.Text(1, 256, true),
                    KeyPosition = (int)rows.Integer(2),
                    Hidden = (int)rows.Integer(3)
                });
            }
            return columns;
        }

        static TableColumn ResolveColumn(List<TableColumn> columns, string name)
        {
            var column = columns.Find(c => c.Hidden != 2 && c.Name == name);
            if (column == null) throw new InvalidDataException("SQLite 列不存在或不支持读取。");
            return column;
        }

        static string Cell(SqliteReadConnection.Statement row, int column)
        {
            if (row.Type(column) == 4) return "[BLOB: " + Number(row.Bytes(column)) + " bytes]";
            if (row.Type(column) == 5) return "NULL";
            return ClipUtf8(Escape(row.Text(column, MaxCellBytes, true)), MaxCellBytes);
        }

        static string ClipUtf8(string value, int limit)
        {
            if (Encoding.UTF8.GetByteCount(value) <= limit) return value;
            return FileContentService.LimitUtf8(value, limit - 3) + "…";
        }

        static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n")
                .Replace("\t", "\\t").Replace("\0", "\\0");
        }

        static string Identifier(string value) { return "\"" + value.Replace("\"", "\"\"") + "\""; }
        static string Number(long value) { return value.ToString(CultureInfo.InvariantCulture); }
    }
}
