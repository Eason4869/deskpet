using System;
using System.IO;
namespace Dafeiyu
{
    // A missing authoritative file is fresh only when no owned interrupted
    // commit survives. Do not guess which candidate to promote on restart.
    internal static class RelationshipWriteRecovery
    {
        internal static bool AuthoritativeExists(string path)
        {
            try{return (File.GetAttributes(path)&FileAttributes.Directory)==0;}
            catch(FileNotFoundException){return false;}
            catch(DirectoryNotFoundException){return false;}
        }
        internal static void RequireNoCandidate(string directory,bool state)
        {
            if(!Directory.Exists(directory))return;
            string prefix=state?".state-":"",suffix=state?".tmp":".pending.tmp";
            foreach(string path in Directory.GetFiles(directory,state?".state-*.tmp":"*.pending.tmp",SearchOption.TopDirectoryOnly)){
                string name=Path.GetFileName(path);Guid transaction;
                if(name.Length!=prefix.Length+32+suffix.Length||!name.StartsWith(prefix,StringComparison.Ordinal)||!name.EndsWith(suffix,StringComparison.Ordinal)||!Guid.TryParseExact(name.Substring(prefix.Length,32),"N",out transaction))continue;
                CompanionPackStorage.AssetPath(directory,name); // Reject links and foreign ancestors.
                throw new IOException("An interrupted relationship write requires recovery; retained bytes were not overwritten.");
            }
        }
    }
}
