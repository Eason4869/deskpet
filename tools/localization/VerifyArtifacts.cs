using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

internal static class VerifyArtifacts
{
    // Runs under Framework for WPF/helper, and .NET 10 for the actual published Core.
    // Only a SHA256 argument is required; no source tree or external locale is read.
    static int Main(string[] args)
    {
        try
        {
            if(args.Length<2)throw new ArgumentException("Expected catalog SHA256 and assembly paths.");
            foreach(string path in args.Length==2?new[]{args[1]}:Slice(args))
            {
                var assembly=Assembly.LoadFrom(Path.GetFullPath(path));
                using(var stream=assembly.GetManifestResourceStream("Dafeiyu.Locales.zh_CN.json"))
                {
                    if(stream==null)throw new InvalidDataException("Missing embedded locale: "+path);
                    using(var sha=SHA256.Create())
                        if(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant()!=args[0])throw new InvalidDataException("Embedded locale bytes differ: "+path);
                }
                var type=assembly.GetType("Dafeiyu.TextResources",true);
                var get=type.GetMethod("Get",new[]{typeof(string)});
                if((string)get.Invoke(null,new object[]{"common.cancel"})!="取消")throw new InvalidDataException("Standalone resource lookup failed: "+path);
                Console.WriteLine("PASS embedded locale bytes and standalone lookup: "+Path.GetFileName(path));
            }
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    static string[] Slice(string[] args){var paths=new string[args.Length-1];Array.Copy(args,1,paths,0,paths.Length);return paths;}
}
