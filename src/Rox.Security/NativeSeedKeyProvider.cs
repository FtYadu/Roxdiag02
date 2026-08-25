using System.Runtime.InteropServices;
using Rox.FlowEngine;

namespace Rox.Security;

/// <summary>
/// Loads the operator's licensed seed-key module (a native DLL) and calls it across the security
/// boundary. NO OEM algorithm lives in the app — this is the only path to a real key (PRD §7).
///
/// Expected export ABI (documented in docs/EXTERNAL_INPUTS.md):
/// <code>
///   // returns the number of key bytes written, or a negative value on error
///   int ComputeKey(const uint8_t* seed, int seedLen, uint8_t* keyBuf, int keyBufCap);
/// </code>
/// The default export name is <c>ComputeKey</c>; override if the module uses a different symbol.
/// </summary>
public sealed class NativeSeedKeyProvider : ISecurityProvider, IDisposable
{
    private readonly IntPtr _handle;
    private readonly ComputeKeyDelegate _computeKey;
    private readonly int _maxKeyBytes;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ComputeKeyDelegate(byte[] seed, int seedLen, byte[] keyBuf, int keyBufCap);

    public string ModulePath { get; }

    public NativeSeedKeyProvider(string modulePath, string exportName = "ComputeKey", int maxKeyBytes = 64)
    {
        ModulePath = modulePath ?? throw new ArgumentNullException(nameof(modulePath));
        if (!File.Exists(modulePath))
            throw new FileNotFoundException($"Seed-key module not found: {modulePath}", modulePath);

        _maxKeyBytes = maxKeyBytes;
        _handle = NativeLibrary.Load(modulePath);
        try
        {
            var fn = NativeLibrary.GetExport(_handle, exportName);
            _computeKey = Marshal.GetDelegateForFunctionPointer<ComputeKeyDelegate>(fn);
        }
        catch
        {
            NativeLibrary.Free(_handle);
            throw;
        }
    }

    public byte[] ComputeKey(byte[] seed, int length)
    {
        var buf = new byte[_maxKeyBytes];
        int written = _computeKey(seed, seed.Length, buf, buf.Length);
        if (written < 0)
            throw new SecurityModuleException($"Seed-key module returned error code {written}.");
        if (written > buf.Length)
            throw new SecurityModuleException($"Seed-key module reported {written} bytes, exceeding the {buf.Length}-byte buffer.");
        return buf.AsSpan(0, written).ToArray();
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero) NativeLibrary.Free(_handle);
    }
}

/// <summary>Manual key entry fallback for offline engineering when no module is supplied (FR-06.5).</summary>
public sealed class ManualKeyProvider : ISecurityProvider
{
    private readonly Func<byte[], byte[]> _keyForSeed;

    /// <param name="keyForSeed">Callback that prompts the operator and returns the key bytes for a seed.</param>
    public ManualKeyProvider(Func<byte[], byte[]> keyForSeed) => _keyForSeed = keyForSeed;

    public byte[] ComputeKey(byte[] seed, int length) => _keyForSeed(seed);
}

public sealed class SecurityModuleException : Exception
{
    public SecurityModuleException(string message) : base(message) { }
}
