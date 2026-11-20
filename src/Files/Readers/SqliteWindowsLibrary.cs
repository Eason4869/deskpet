#if !NET10_0
using System;
using System.IO;
using System.Runtime.InteropServices;
using Dafeiyu.Maintenance;

namespace Dafeiyu
{
    internal static class SqliteWindowsLibrary
    {
        const string ResourceName = "Dafeiyu.FileCompatibility.Native.e_sqlite3.dll";
        const int ResourceBytes = 1972224;
        const uint SearchLibraryDirectory = 0x100;
        const uint SearchSystem32 = 0x800;
        static readonly object Gate = new object();
        static string storageRoot;
        static bool started;

        internal static void ConfigureCacheRoot(string root)
        {
            if (string.IsNullOrEmpty(root) || !Path.IsPathRooted(root)) throw new ArgumentException("Invalid component cache path.");
            lock (Gate)
            {
                if (started) throw new InvalidOperationException("SQLite component cache is already active.");
                storageRoot = Path.GetFullPath(root);
            }
        }

        internal static IntPtr Load()
        {
            string root;
            lock (Gate)
            {
                started = true;
                root = storageRoot;
            }
            if (root == null)
            {
                string userCache = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(userCache)) throw new InvalidDataException("SQLite 本地组件缓存不可用。");
                root = Path.Combine(userCache, "DeskPet");
            }
            return LoadFromCache(root);
        }

        internal static IntPtr LoadFromCache(string root)
        {
            string directory = Path.Combine(root, "file-components", "sqlite", SqliteNative.WindowsHash);
            CachePaths.Check(directory);
            Directory.CreateDirectory(directory);
            using (CacheDirectoryPins.OpenDirectory(directory))
            {
                string path = Path.Combine(directory, "e_sqlite3.dll");
                CachePaths.Check(path);
                if (!File.Exists(path)) Extract(directory, path);
                CachePaths.Check(path);
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (file.Length != ResourceBytes) throw new InvalidDataException("SQLite 本地组件校验失败。");
                    SqliteNative.VerifyHash(file, SqliteNative.WindowsHash);
                    IntPtr library = LoadLibraryEx(CachePaths.NativePath(path), IntPtr.Zero, SearchLibraryDirectory | SearchSystem32);
                    if (library == IntPtr.Zero) throw new InvalidDataException("SQLite 本地组件无法加载。");
                    return library;
                }
            }
        }

        static void Extract(string directory, string path)
        {
            string staging = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".part");
            bool created = false;
            try
            {
                using (var resource = typeof(SqliteWindowsLibrary).Assembly.GetManifestResourceStream(ResourceName))
                {
                    if (resource == null || resource.Length != ResourceBytes)
                        throw new InvalidDataException("SQLite 本地组件缺失。");
                    using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                    {
                        created = true;
                        resource.CopyTo(output);
                        output.Flush(true);
                        output.Position = 0;
                        SqliteNative.VerifyHash(output, SqliteNative.WindowsHash);
                    }
                }
                try { File.Move(staging, path); }
                catch (IOException) { if (!File.Exists(path)) throw; }
            }
            finally
            {
                if (created) File.Delete(staging);
            }
        }

        [DllImport("kernel32.dll", EntryPoint = "LoadLibraryExW", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern IntPtr LoadLibraryEx(string path, IntPtr reserved, uint flags);
    }
}
#endif
