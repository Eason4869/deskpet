using System.Security.Cryptography;
using Dafeiyu;

namespace DeskPet.Platform.Mac;

/// <summary>Versioned AES-256-GCM envelopes; the data key is stored exclusively in Keychain.</summary>
public sealed class MacSecretProtector : ISecretProtector, IDisposable
{
    private static ReadOnlySpan<byte> Header => "DPM1"u8;
    private readonly Lazy<byte[]> key;
    private readonly object gate = new();
    private bool disposed;

    public MacSecretProtector() : this(ReadKeychainKey) { }
    // Injection permits authenticated-envelope tests without touching a real login Keychain.
    public MacSecretProtector(Func<byte[]> keyProvider) => key = new Lazy<byte[]>(() =>
    {
        var value = keyProvider();
        if (value.Length != 32) { CryptographicOperations.ZeroMemory(value); throw new CryptographicException(TextResources.Get("macSecretProtector.macSecretProtector.keyLazyByte")); }
        return value;
    });

    private static byte[] ReadKeychainKey()
    {
        MacServices.EnsureMac();
        var bytes = new byte[32];
        var status = Native.KeychainKey(bytes, bytes.Length);
        if (status != 0) { CryptographicOperations.ZeroMemory(bytes); throw new CryptographicException(TextResources.Format("macSecretProtector.readKeychainKey.template",status)); }
        return bytes;
    }

    public byte[] Protect(byte[] plain)
    {
        ArgumentNullException.ThrowIfNull(plain);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var result = new byte[checked(32 + plain.Length)];
            Header.CopyTo(result);
            RandomNumberGenerator.Fill(result.AsSpan(4, 12));
            using var aes = new AesGcm(key.Value, 16);
            aes.Encrypt(result.AsSpan(4, 12), plain, result.AsSpan(32), result.AsSpan(16, 16), Header);
            return result;
        }
    }

    public byte[] Unprotect(byte[] envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (envelope.Length < 32 || !envelope.AsSpan(0, 4).SequenceEqual(Header))
                throw new CryptographicException(TextResources.Get("macSecretProtector.unprotect.text"));
            var plain = new byte[envelope.Length - 32];
            using var aes = new AesGcm(key.Value, 16);
            try { aes.Decrypt(envelope.AsSpan(4, 12), envelope.AsSpan(32), envelope.AsSpan(16, 16), plain, Header); }
            catch { CryptographicOperations.ZeroMemory(plain); throw; }
            return plain;
        }
    }

    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; if (key.IsValueCreated) CryptographicOperations.ZeroMemory(key.Value); } }
}
