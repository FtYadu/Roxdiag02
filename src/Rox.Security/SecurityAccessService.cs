using Rox.Core;
using Rox.FlowEngine;
using Rox.Uds;

namespace Rox.Security;

public enum SecurityAccessStatus
{
    Granted,
    AlreadyGranted,
    InvalidKey,        // NRC 0x35 — key wrong, check the module
    Denied,            // NRC 0x33
    Locked,            // NRC 0x36 — exceeded attempts
    WaitRequired,      // NRC 0x37 — required time delay not expired
    SeedRequestFailed,
    NoProvider
}

public sealed record SecurityAccessResult(
    SecurityAccessStatus Status,
    string Ecu,
    byte Level,
    byte? Nrc = null,
    TimeSpan? RetryAfter = null)
{
    public bool Granted => Status is SecurityAccessStatus.Granted or SecurityAccessStatus.AlreadyGranted;

    public string Describe() => Status switch
    {
        SecurityAccessStatus.Granted => $"Security access granted (level 0x{Level:X2}).",
        SecurityAccessStatus.AlreadyGranted => $"Security already granted this session (level 0x{Level:X2}).",
        SecurityAccessStatus.InvalidKey => "Invalid key — check the seed-key module (NRC 0x35).",
        SecurityAccessStatus.Denied => "Security access denied (NRC 0x33).",
        SecurityAccessStatus.Locked => $"Locked out — too many attempts (NRC 0x36). {(RetryAfter is { } d ? $"Wait ~{d.TotalSeconds:0}s." : "")}",
        SecurityAccessStatus.WaitRequired => $"Required time delay not expired (NRC 0x37). {(RetryAfter is { } d ? $"Wait ~{d.TotalSeconds:0}s." : "")}",
        SecurityAccessStatus.SeedRequestFailed => $"Seed request failed{(Nrc is { } n ? $" (NRC 0x{n:X2})" : "")}.",
        SecurityAccessStatus.NoProvider => "No seed-key module configured.",
        _ => Status.ToString()
    };
}

/// <summary>
/// Orchestrates the UDS SecurityAccess (0x27) handshake (FR-06): request seed → hand to the
/// user-supplied module → send key. Caches the granted state per session (FR-06.3) and enforces a
/// lockout back-off on 0x36/0x37 so the tool never hammers a locked ECU (FR-06.4). The key never
/// touches this class in plaintext beyond marshalling it straight into the sendKey request.
/// </summary>
public sealed class SecurityAccessService
{
    private readonly UdsClient _client;
    private readonly ISecurityProvider? _provider;
    private readonly HashSet<(string ecu, byte level)> _granted = new();
    private readonly Dictionary<(string ecu, byte level), DateTimeOffset> _lockouts = new();
    private readonly TimeSpan _defaultLockout;

    public SecurityAccessService(UdsClient client, ISecurityProvider? provider, TimeSpan? defaultLockout = null)
    {
        _client = client;
        _provider = provider;
        _defaultLockout = defaultLockout ?? TimeSpan.FromSeconds(10);
    }

    /// <summary>Whether a level is already granted this session.</summary>
    public bool IsGranted(string ecu, byte requestSeedSub = 0x01) => _granted.Contains((ecu, requestSeedSub));

    /// <summary>Forget cached grants (e.g. on session reset or disconnect).</summary>
    public void InvalidateSession() { _granted.Clear(); _lockouts.Clear(); }

    /// <summary>
    /// Ensure security access at the given level. The <paramref name="requestSeedSub"/> is the odd
    /// requestSeed sub-function; the sendKey sub-function is the next (even) value.
    /// </summary>
    public async Task<SecurityAccessResult> RequestAccessAsync(string ecu, byte requestSeedSub = 0x01, CancellationToken ct = default)
    {
        var key = (ecu, requestSeedSub);
        if (_granted.Contains(key))
            return new SecurityAccessResult(SecurityAccessStatus.AlreadyGranted, ecu, requestSeedSub);

        if (_lockouts.TryGetValue(key, out var until) && DateTimeOffset.UtcNow < until)
            return new SecurityAccessResult(SecurityAccessStatus.Locked, ecu, requestSeedSub, Nrc.ExceededNumberOfAttempts, until - DateTimeOffset.UtcNow);

        if (_provider is null)
            return new SecurityAccessResult(SecurityAccessStatus.NoProvider, ecu, requestSeedSub);

        // 1) request seed (27 <odd>)
        var seedResp = await _client.SecurityRequestSeedAsync(requestSeedSub, ct).ConfigureAwait(false);
        if (seedResp.IsNegative)
            return MapNegative(seedResp.Nrc, ecu, requestSeedSub, seedFailed: true);

        // Positive: 67 <odd> <seed...>. Raw = [0x67, echoedSub, seed...]; drop SID + echoed sub-function.
        var seed = seedResp.Raw.Length > 2 ? seedResp.Raw[2..] : Array.Empty<byte>();
        if (seed.Length == 0 || seed.All(b => b == 0))
        {
            // An all-zero seed conventionally means the ECU is already unlocked at this level.
            _granted.Add(key);
            return new SecurityAccessResult(SecurityAccessStatus.AlreadyGranted, ecu, requestSeedSub);
        }

        // 2) compute key via the user module (boundary crossing)
        byte[] keyBytes;
        try { keyBytes = _provider.ComputeKey(seed, seed.Length); }
        catch (SecurityModuleException) { return new SecurityAccessResult(SecurityAccessStatus.InvalidKey, ecu, requestSeedSub, Nrc.InvalidKey); }

        // 3) send key (27 <even>)
        byte sendKeySub = (byte)(requestSeedSub + 1);
        var keyResp = await _client.SecuritySendKeyAsync(sendKeySub, keyBytes, ct).ConfigureAwait(false);
        if (keyResp.IsPositive)
        {
            _granted.Add(key);
            _lockouts.Remove(key);
            return new SecurityAccessResult(SecurityAccessStatus.Granted, ecu, requestSeedSub);
        }
        return MapNegative(keyResp.Nrc, ecu, requestSeedSub, seedFailed: false);
    }

    private SecurityAccessResult MapNegative(byte nrc, string ecu, byte level, bool seedFailed)
    {
        switch (nrc)
        {
            case Nrc.ExceededNumberOfAttempts:
                _lockouts[(ecu, level)] = DateTimeOffset.UtcNow + _defaultLockout;
                return new SecurityAccessResult(SecurityAccessStatus.Locked, ecu, level, nrc, _defaultLockout);
            case Nrc.RequiredTimeDelayNotExpired:
                _lockouts[(ecu, level)] = DateTimeOffset.UtcNow + _defaultLockout;
                return new SecurityAccessResult(SecurityAccessStatus.WaitRequired, ecu, level, nrc, _defaultLockout);
            case Nrc.InvalidKey:
                return new SecurityAccessResult(SecurityAccessStatus.InvalidKey, ecu, level, nrc);
            case Nrc.SecurityAccessDenied:
                return new SecurityAccessResult(SecurityAccessStatus.Denied, ecu, level, nrc);
            default:
                return new SecurityAccessResult(seedFailed ? SecurityAccessStatus.SeedRequestFailed : SecurityAccessStatus.Denied, ecu, level, nrc);
        }
    }
}
