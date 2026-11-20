using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Dafeiyu
{
    internal static class FileCompatibilityDependencies
    {
#if !PORTABLE
        static readonly object Gate = new object();
        static readonly Dictionary<string, Assembly> Loaded = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        static bool enabled;

        static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            lock (Gate)
            {
                Assembly assembly;
                if (Loaded.TryGetValue(name, out assembly)) return assembly;
                using (var stream = typeof(FileCompatibilityDependencies).Assembly.GetManifestResourceStream("Dafeiyu.FileCompatibility." + name + ".dll"))
                {
                    if (stream == null) return null;
                    using (var buffer = new MemoryStream())
                    {
                        stream.CopyTo(buffer);
                        assembly = Assembly.Load(buffer.ToArray());
                    }
                }
                Loaded.Add(name, assembly);
                return assembly;
            }
        }
#endif

        internal static void Ensure()
        {
#if !PORTABLE
            MarkdownDependencies.Ensure();
            lock (Gate)
            {
                if (enabled) return;
                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
                enabled = true;
            }
#endif
        }
    }
}
