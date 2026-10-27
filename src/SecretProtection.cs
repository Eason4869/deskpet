using System;
using System.Security.Cryptography;
using System.Threading;

namespace Dafeiyu
{
    public interface ISecretProtector
    {
        byte[] Protect(byte[] plain);
        byte[] Unprotect(byte[] cipher);
    }

    // Register the platform protector once, before opening settings, history or queues.
    // Portable builds deliberately have no plaintext or machine-local-key fallback.
    public static class SecretProtection
    {
#if PORTABLE
        static ISecretProtector current;
#else
        static ISecretProtector current=new WindowsProtector();
        sealed class WindowsProtector : ISecretProtector
        {
            public byte[] Protect(byte[] plain){return ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser);}
            public byte[] Unprotect(byte[] cipher){return ProtectedData.Unprotect(cipher,null,DataProtectionScope.CurrentUser);}
        }
#endif
        public static void Configure(ISecretProtector protector)
        {
            if(protector==null)throw new ArgumentNullException("protector");
            Interlocked.Exchange(ref current,protector);
        }
        static ISecretProtector Current
        {
            get
            {
                var protector=Volatile.Read(ref current);
                if(protector==null)throw new InvalidOperationException(TextResources.Get("secretProtection.current.text"));
                return protector;
            }
        }
        public static byte[] Protect(byte[] plain){return Current.Protect(plain);}
        public static byte[] Unprotect(byte[] cipher){return Current.Unprotect(cipher);}
    }
}
