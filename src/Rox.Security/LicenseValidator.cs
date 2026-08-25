using System.Text;

namespace Rox.Security;

public enum LicenseState { Missing, Invalid, Valid }

public sealed record LicenseStatus(LicenseState State, string Message)
{
    public bool IsValid => State == LicenseState.Valid;
}

/// <summary>
/// Offline licence-key validation (PRD §7.2 / §15). This is a self-contained, network-free format check
/// — a STUB for the real offline licensing scheme, which the operator's distribution supplies.
///
/// TODO(licensing): replace the checksum scheme below with the production offline licence verification
/// (e.g. a signed licence blob checked against an embedded public key). Documented in
/// docs/EXTERNAL_INPUTS.md. The suite runs fully against the simulator regardless of licence state; a
/// real-vehicle transport is where a production build would gate on a valid licence.
/// </summary>
public static class LicenseValidator
{
    // Format: ROX-XXXX-XXXX-XXXX-CC where CC is a 2-hex-digit checksum over the preceding groups.
    public static LicenseStatus Validate(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return new LicenseStatus(LicenseState.Missing, "No licence key configured (running in simulator mode).");

        var parts = key.Trim().ToUpperInvariant().Split('-');
        if (parts.Length != 5 || parts[0] != "ROX" || parts.Skip(1).Take(3).Any(p => p.Length != 4))
            return new LicenseStatus(LicenseState.Invalid, "Licence key format is not recognised.");

        var body = string.Join('-', parts.Take(4));
        byte expected = Checksum(body);
        if (!byte.TryParse(parts[4], System.Globalization.NumberStyles.HexNumber, null, out var given) || given != expected)
            return new LicenseStatus(LicenseState.Invalid, "Licence key checksum does not match.");

        return new LicenseStatus(LicenseState.Valid, "Licence valid.");
    }

    /// <summary>Produce a well-formed licence key for a body like "ROX-AAAA-BBBB-CCCC" (dev/testing helper).</summary>
    public static string Complete(string body) => $"{body}-{Checksum(body):X2}";

    private static byte Checksum(string body)
    {
        byte c = 0;
        foreach (var b in Encoding.ASCII.GetBytes(body)) c = (byte)((c + b) & 0xFF);
        return c;
    }
}
