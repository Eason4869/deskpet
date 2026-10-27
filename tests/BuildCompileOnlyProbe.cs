using System;
using System.IO;
namespace Dafeiyu.Tests
{
    // Intentionally unaware of --compile-only: only the build runner may skip Main.
    public static class BuildCompileOnlyProbe
    {
        public static int Main()
        {
            string path=Path.GetFullPath(Environment.GetEnvironmentVariable("DESKPET_COMPILE_ONLY_PROBE"));
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"compile-only-probe")+Path.DirectorySeparatorChar;
            if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Owned probe path required.");
            File.WriteAllText(path,"Main executed");return 0;
        }
    }
}
