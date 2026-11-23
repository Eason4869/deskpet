using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Dafeiyu
{
    internal sealed class SqliteReadConnection : IDisposable
    {
        const int SqliteReadOnly = 0x1;
        const int SqliteUri = 0x40;
        const int SqlitePrivateCache = 0x40000;
        const int SqliteNoFollow = 0x1000000;
        readonly CancellationToken token;
        readonly Stopwatch elapsed = Stopwatch.StartNew();
        readonly SqliteExports.ProgressCallback progress;
        readonly SqliteExports api;
        IntPtr handle;

        internal SqliteReadConnection(string path, CancellationToken token)
        {
            this.token = token;
            progress = Interrupted;
            token.ThrowIfCancellationRequested();
            string uri = new Uri(Path.GetFullPath(path)).AbsoluteUri + "?mode=ro&immutable=1";
            try
            {
                api = SqliteNative.Functions;
                RequireSuccess(api.Open(SqliteNative.Utf8(uri), out handle,
                    SqliteReadOnly | SqliteUri | SqlitePrivateCache | SqliteNoFollow, IntPtr.Zero));
                api.ProgressHandler(handle, 500, progress, IntPtr.Zero);
                api.Limit(handle, 1, 1024 * 1024); // SQL length
                api.Limit(handle, 3, 50); // Expression depth
                api.Limit(handle, 7, 0); // Attached databases
                RequireSuccess(api.Execute(handle, SqliteNative.Utf8(
                    "PRAGMA query_only=ON; PRAGMA trusted_schema=OFF; PRAGMA temp_store=MEMORY; PRAGMA cache_size=-2048;"),
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal Statement Prepare(string sql)
        {
            CheckDeadline();
            IntPtr statement;
            int code = api.Prepare(handle, SqliteNative.Utf8(sql), -1, out statement, IntPtr.Zero);
            if (code != 0)
            {
                if (statement != IntPtr.Zero) api.FinalizeStatement(statement);
                RequireSuccess(code);
            }
            return new Statement(this, statement);
        }

        int Interrupted(IntPtr ignored)
        {
            return token.IsCancellationRequested || elapsed.ElapsedMilliseconds >= 5000 ? 1 : 0;
        }

        void CheckDeadline()
        {
            token.ThrowIfCancellationRequested();
            if (elapsed.ElapsedMilliseconds >= 5000) throw new TimeoutException("SQLite 查询超过 5 秒上限。");
        }

        void RequireSuccess(int code)
        {
            CheckDeadline();
            if (code != 0)
                throw new InvalidDataException("SQLite 无法读取选定的数据（错误码 " +
                    code.ToString(System.Globalization.CultureInfo.InvariantCulture) + "）。");
        }

        public void Dispose()
        {
            if (handle == IntPtr.Zero) return;
            api.Close(handle);
            handle = IntPtr.Zero;
            GC.KeepAlive(progress);
        }

        internal sealed class Statement : IDisposable
        {
            readonly SqliteReadConnection owner;
            IntPtr handle;

            internal Statement(SqliteReadConnection owner, IntPtr handle)
            {
                this.owner = owner;
                this.handle = handle;
            }

            internal void Bind(int index, string value)
            {
                byte[] utf8 = SqliteNative.Utf8(value);
                owner.RequireSuccess(owner.api.BindText(handle, index, utf8, utf8.Length - 1, new IntPtr(-1)));
            }

            internal void Bind(int index, int value) { owner.RequireSuccess(owner.api.BindInteger(handle, index, value)); }
            internal int Type(int column) { return owner.api.ColumnType(handle, column); }
            internal int Bytes(int column) { return owner.api.ColumnBytes(handle, column); }
            internal long Integer(int column) { return owner.api.ColumnInteger(handle, column); }

            internal string Text(int column, int limit, bool clip = false)
            {
                IntPtr pointer = owner.api.ColumnText(handle, column);
                int length = Bytes(column);
                if (length == 0) return "";
                if (pointer == IntPtr.Zero || (!clip && length > limit))
                    throw new InvalidDataException("SQLite 数据无法读取或超过长度上限。");
                var bytes = new byte[Math.Min(length, limit)];
                Marshal.Copy(pointer, bytes, 0, bytes.Length);
                if (length <= limit) return Encoding.UTF8.GetString(bytes);
                int end = bytes.Length - 1;
                while (end > 0 && (bytes[end] & 0xc0) == 0x80) end--;
                int required = bytes[end] < 0x80 ? 1 : bytes[end] < 0xe0 ? 2 : bytes[end] < 0xf0 ? 3 : 4;
                return Encoding.UTF8.GetString(bytes, 0, bytes.Length - end < required ? end : bytes.Length) + "…";
            }

            internal bool Step()
            {
                owner.CheckDeadline();
                int code = owner.api.Step(handle);
                owner.CheckDeadline();
                if (code == 100) return true;
                if (code == 101) return false;
                owner.RequireSuccess(code);
                return false;
            }

            public void Dispose()
            {
                if (handle == IntPtr.Zero) return;
                owner.api.FinalizeStatement(handle);
                handle = IntPtr.Zero;
            }
        }
    }

    internal static class SqliteNative
    {
        internal const string WindowsHash = "6ad8e149f8ce3ed3716402b4b3a2268ebbdc7b64391b5fafed747e03bb1b9418";
        static readonly Lazy<SqliteExports> Loaded = new Lazy<SqliteExports>(() => new SqliteExports(LoadVerifiedLibrary()));
        internal static SqliteExports Functions { get { return Loaded.Value; } }

        static IntPtr LoadVerifiedLibrary()
        {
#if NET10_0
            string relative = "e_sqlite3.dll";
            string rid = "win-x64";
            string expected = WindowsHash;
            string root = AppDomain.CurrentDomain.BaseDirectory;
            Architecture arch = RuntimeInformation.ProcessArchitecture;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && arch == Architecture.X64)
                rid = "win-x64";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && (arch == Architecture.Arm64 || arch == Architecture.X64))
            {
                relative = "libe_sqlite3.dylib";
                rid = arch == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
                expected = arch == Architecture.Arm64
                    ? "2b27f45233818ae11d97016ca2a16dffcf63a53c46f60bcd52b342fc187b0c83"
                    : "20143ee08a1af0a510368e14109f512d7c7870481ba16937ae5733a415d0a201";
                // The macOS packaging step verifies vendored inputs and then signs
                // all native payloads. macOS validates the signed library on load.
                var payload = new DirectoryInfo(root);
                if (payload.Name == "MacOS" && payload.Parent != null && payload.Parent.Name == "Contents" &&
                    payload.Parent.Parent != null && payload.Parent.Parent.Name.EndsWith(".app", StringComparison.Ordinal))
                    expected = null;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && arch == Architecture.X64)
            {
                relative = "libe_sqlite3.so";
                rid = "linux-x64";
                expected = "eddcd4aa561d5b8f252db77e8272e7d1aed96bcab9fda3f177ca542f916290bf";
            }
            else throw new InvalidDataException("当前平台没有可用的 SQLite 组件。");
            string path = Path.Combine(root, "runtimes", rid, "native", relative);
            try
            {
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (expected != null) VerifyHash(file, expected);
                    return NativeLibrary.Load(path);
                }
            }
            catch (FileNotFoundException) { throw new InvalidDataException("SQLite 本地组件缺失。"); }
            catch (DirectoryNotFoundException) { throw new InvalidDataException("SQLite 本地组件缺失。"); }
            catch (DllNotFoundException) { throw new InvalidDataException("SQLite 本地组件无法加载。"); }
#else
            return SqliteWindowsLibrary.Load();
#endif
        }

        internal static void VerifyHash(Stream file, string expected)
        {
            using (var sha = SHA256.Create())
                if (BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant() != expected)
                    throw new InvalidDataException("SQLite 本地组件校验失败。");
        }

        internal static byte[] Utf8(string value) { return Encoding.UTF8.GetBytes(value + "\0"); }
    }

    internal sealed class SqliteExports
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int OpenRoutine(byte[] name, out IntPtr db, int flags, IntPtr vfs);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int HandleRoutine(IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int ExecuteRoutine(IntPtr db, byte[] sql, IntPtr callback, IntPtr state, IntPtr error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int IntegerPairRoutine(IntPtr handle, int first, int second);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate void ProgressRoutine(IntPtr db, int instructions, ProgressCallback callback, IntPtr state);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int ProgressCallback(IntPtr state);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int PrepareRoutine(IntPtr db, byte[] sql, int length, out IntPtr statement, IntPtr tail);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int BindTextRoutine(IntPtr statement, int index, byte[] text, int length, IntPtr destructor);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate int ColumnInt32Routine(IntPtr statement, int column);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate long ColumnInt64Routine(IntPtr statement, int column);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        internal delegate IntPtr ColumnTextRoutine(IntPtr statement, int column);

        internal readonly OpenRoutine Open;
        internal readonly HandleRoutine Close;
        internal readonly ExecuteRoutine Execute;
        internal readonly IntegerPairRoutine Limit;
        internal readonly ProgressRoutine ProgressHandler;
        internal readonly PrepareRoutine Prepare;
        internal readonly HandleRoutine FinalizeStatement;
        internal readonly HandleRoutine Step;
        internal readonly BindTextRoutine BindText;
        internal readonly IntegerPairRoutine BindInteger;
        internal readonly ColumnInt32Routine ColumnType;
        internal readonly ColumnInt32Routine ColumnBytes;
        internal readonly ColumnInt64Routine ColumnInteger;
        internal readonly ColumnTextRoutine ColumnText;

        internal SqliteExports(IntPtr library)
        {
            if (library == IntPtr.Zero) throw new InvalidDataException("SQLite 本地组件不可用。");
            Open = Bind<OpenRoutine>(library, "sqlite3_open_v2");
            Close = Bind<HandleRoutine>(library, "sqlite3_close_v2");
            Execute = Bind<ExecuteRoutine>(library, "sqlite3_exec");
            Limit = Bind<IntegerPairRoutine>(library, "sqlite3_limit");
            ProgressHandler = Bind<ProgressRoutine>(library, "sqlite3_progress_handler");
            Prepare = Bind<PrepareRoutine>(library, "sqlite3_prepare_v2");
            FinalizeStatement = Bind<HandleRoutine>(library, "sqlite3_finalize");
            Step = Bind<HandleRoutine>(library, "sqlite3_step");
            BindText = Bind<BindTextRoutine>(library, "sqlite3_bind_text");
            BindInteger = Bind<IntegerPairRoutine>(library, "sqlite3_bind_int");
            ColumnType = Bind<ColumnInt32Routine>(library, "sqlite3_column_type");
            ColumnBytes = Bind<ColumnInt32Routine>(library, "sqlite3_column_bytes");
            ColumnInteger = Bind<ColumnInt64Routine>(library, "sqlite3_column_int64");
            ColumnText = Bind<ColumnTextRoutine>(library, "sqlite3_column_text");
        }

        static T Bind<T>(IntPtr library, string name) where T : class
        {
#if NET10_0
            IntPtr address = NativeLibrary.GetExport(library, name);
#else
            IntPtr address = GetProcAddress(library, name);
#endif
            if (address == IntPtr.Zero) throw new InvalidDataException("SQLite 本地组件缺少必要接口。");
            return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
        }

#if !NET10_0
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern IntPtr GetProcAddress(IntPtr module, string name);
#endif
    }
}
