using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
namespace Dafeiyu
{
    internal sealed class OfficialCompanionFile
    {
        internal readonly long Length;internal readonly string Hash;
        internal OfficialCompanionFile(long length,string hash){Length=length;Hash=hash;}
    }
    // A receipt describes one generation. It is not a permanent integrity verdict:
    // every decoder must consume the bytes returned by ReadVerifiedBytes.
    public sealed class OfficialCompanionSnapshot : IDisposable
    {
        readonly Action released;
        readonly object gate=new object();readonly Dictionary<string,OfficialCompanionFile> files;readonly string dataRoot;IDisposable lease;bool disposed;
        public OfficialCompanionInstallation Installation {get;private set;}
        public string Generation {get;private set;}
        public ReadOnlyCollection<string> Files {get;private set;}
        internal OfficialCompanionSnapshot(OfficialCompanionInstallation installation,Dictionary<string,OfficialCompanionFile> files,string generation,string dataRoot,IDisposable lease,Action released=null)
        {this.released=released;Installation=installation;this.files=files;Generation=generation;this.dataRoot=dataRoot;this.lease=lease;Files=new ReadOnlyCollection<string>(files.Keys.OrderBy(x=>x,StringComparer.Ordinal).ToArray());}
        internal OfficialCompanionSnapshot Retain()
        {
            lock(gate){Alive();return new OfficialCompanionSnapshot(Installation,files,Generation,dataRoot,Protect(dataRoot,Installation.RootDirectory),released);}
        }
        internal static IDisposable Protect(string dataRoot,string directory)
        {return Maintenance.OfficialCompanionMaintenance.Managed(Path.GetFileName(directory))?Maintenance.CacheResourceLease.ProtectExisting(dataRoot,directory):null;}
        void Alive(){if(disposed)throw new ObjectDisposedException("OfficialCompanionSnapshot");}
        public byte[] ReadVerifiedBytes(string relativePath,CancellationToken cancel=default(CancellationToken))
        {
            lock(gate){Alive();cancel.ThrowIfCancellationRequested();OfficialCompanionFile file;
                if(relativePath==null||!files.TryGetValue(relativePath,out file))throw new InvalidDataException("Asset is not in this receipt.");
                byte[] bytes=ReadBounded(OfficialCompanionStorage.SafePath(Installation.RootDirectory,relativePath),file.Length,cancel);
                if(bytes.LongLength!=file.Length||Digest(bytes)!=file.Hash)throw new InvalidDataException("Companion asset integrity check failed.");cancel.ThrowIfCancellationRequested();return bytes;
            }
        }
        internal static string Digest(byte[] bytes){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
        internal static byte[] ReadBounded(string path,long maximum,CancellationToken cancel)
        {
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
                if(stream.Length>maximum||stream.Length>Int32.MaxValue)throw new InvalidDataException("Companion file exceeds its bound.");
                byte[] bytes=new byte[(int)stream.Length];int offset=0;
                while(offset<bytes.Length){cancel.ThrowIfCancellationRequested();int count=stream.Read(bytes,offset,Math.Min(65536,bytes.Length-offset));if(count==0)throw new InvalidDataException("Truncated companion file.");offset+=count;}
                if(stream.ReadByte()!=-1)throw new InvalidDataException("Companion file grew beyond its bound.");cancel.ThrowIfCancellationRequested();return bytes;
            }
        }
        public void Dispose(){lock(gate){if(disposed)return;disposed=true;if(lease!=null){lease.Dispose();lease=null;}}if(released!=null)released();}
    }
}
