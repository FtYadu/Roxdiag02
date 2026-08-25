using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Rox.Security;

/// <summary>Encrypts/decrypts small secrets (the security-module path, licence) at rest.</summary>
public interface ISecretProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
    bool IsRealEncryption { get; }
}

/// <summary>
/// Windows DPAPI protector (PRD §7.2 / §15): the security-module path is stored encrypted per current
/// user. Windows-only at runtime; use <see cref="SecretProtector.Create"/> to get the right implementation.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiSecretProtector : ISecretProtector
{
    private readonly byte[]? _entropy;
    public DpapiSecretProtector(byte[]? entropy = null) => _entropy = entropy;
    public bool IsRealEncryption => true;

    public byte[] Protect(byte[] plaintext) => ProtectedData.Protect(plaintext, _entropy, DataProtectionScope.CurrentUser);
    public byte[] Unprotect(byte[] ciphertext) => ProtectedData.Unprotect(ciphertext, _entropy, DataProtectionScope.CurrentUser);
}

/// <summary>
/// Non-Windows fallback so the code builds and runs cross-platform in dev/CI. It does NOT provide real
/// encryption — it is clearly labelled and must never be relied on for the shipping Windows product,
/// which always uses DPAPI.
/// </summary>
public sealed class PassthroughSecretProtector : ISecretProtector
{
    public bool IsRealEncryption => false;
    // Light obfuscation only (XOR), never a security control.
    private const byte Mask = 0x5A;
    public byte[] Protect(byte[] plaintext) => Xor(plaintext);
    public byte[] Unprotect(byte[] ciphertext) => Xor(ciphertext);
    private static byte[] Xor(byte[] b) { var o = new byte[b.Length]; for (int i = 0; i < b.Length; i++) o[i] = (byte)(b[i] ^ Mask); return o; }
}

public static class SecretProtector
{
    /// <summary>DPAPI on Windows; a clearly non-secure passthrough elsewhere (dev/CI only).</summary>
    public static ISecretProtector Create(byte[]? entropy = null)
        => OperatingSystem.IsWindows() ? new DpapiSecretProtector(entropy) : new PassthroughSecretProtector();
}

/// <summary>A DPAPI-protected key/value store persisted to a file (used for the module path + licence).</summary>
public sealed class ProtectedValueStore
{
    private readonly string _path;
    private readonly ISecretProtector _protector;

    public ProtectedValueStore(string path, ISecretProtector? protector = null)
    {
        _path = path;
        _protector = protector ?? SecretProtector.Create();
    }

    public bool IsRealEncryption => _protector.IsRealEncryption;

    public void Save(string value)
    {
        var dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var cipher = _protector.Protect(Encoding.UTF8.GetBytes(value));
        File.WriteAllText(_path, Convert.ToBase64String(cipher));
    }

    public string? Load()
    {
        if (!File.Exists(_path)) return null;
        try
        {
            var cipher = Convert.FromBase64String(File.ReadAllText(_path));
            return Encoding.UTF8.GetString(_protector.Unprotect(cipher));
        }
        catch (CryptographicException) { return null; } // wrong user / corrupted
        catch (FormatException) { return null; }
    }

    public void Clear() { if (File.Exists(_path)) File.Delete(_path); }
}
