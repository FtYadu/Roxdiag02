using Rox.Core;

namespace Rox.Diagnostics;

/// <summary>A decoded DTC plus presentation/analysis fields used by the diagnostics UI and reports.</summary>
public sealed record DtcEntry(Dtc Dtc, string? Description = null)
{
    public string Code => Dtc.Code;
    public string StatusText => Dtc.StatusText;
    public string RawHex => Dtc.RawHex;

    /// <summary>True when this code re-set immediately after a clear — an active (live) fault, not stale.</summary>
    public bool IsLiveFault { get; init; }
}

/// <summary>Result of a single DTC read.</summary>
public sealed record DtcReadResult(string Ecu, IReadOnlyList<DtcEntry> Dtcs, UdsResponse Response)
{
    public bool Failed => Response.IsNegative;
    public byte? Nrc => Response.IsNegative ? Response.Nrc : null;
}

/// <summary>
/// Result of the professional read → clear → read-back cycle (FR-03.5, UC-01). Distinguishes codes that
/// were cleared (stale) from codes that re-set immediately (live/active faults).
/// </summary>
public sealed record DtcClearResult(
    string Ecu,
    IReadOnlyList<DtcEntry> Before,
    IReadOnlyList<DtcEntry> After,
    bool ClearAccepted,
    UdsResponse? ClearResponse)
{
    /// <summary>Codes present before that are gone after — genuinely cleared stale codes.</summary>
    public IReadOnlyList<DtcEntry> Cleared { get; } =
        Before.Where(b => After.All(a => a.Code != b.Code)).ToList();

    /// <summary>Codes still present after the clear — active faults (flagged live).</summary>
    public IReadOnlyList<DtcEntry> LiveRemaining { get; } =
        After.Select(a => a with { IsLiveFault = true }).ToList();

    public int StaleClearedCount => Cleared.Count;
    public int LiveRemainingCount => LiveRemaining.Count;

    /// <summary>UC-01 step 6 summary line.</summary>
    public string Summary => ClearAccepted
        ? $"DTCs cleared ({StaleClearedCount} stale cleared, {LiveRemainingCount} live remaining)"
        : $"Clear rejected{(ClearResponse?.IsNegative == true ? $" — NRC 0x{ClearResponse.Nrc:X2} {Nrc.Describe(ClearResponse.Nrc)}" : "")}";
}

/// <summary>Aggregated all-ECU scan (FR-03.3).</summary>
public sealed record AllEcuScanResult(IReadOnlyList<DtcReadResult> PerEcu)
{
    public int TotalDtcs => PerEcu.Sum(e => e.Dtcs.Count);
    public IEnumerable<DtcEntry> AllDtcs => PerEcu.SelectMany(e => e.Dtcs);
}

/// <summary>User-editable DTC description source (FR-03.2). Default returns a generic label.</summary>
public interface IDtcDescriptionProvider
{
    string? Describe(string code);
}

public sealed class DefaultDtcDescriptionProvider : IDtcDescriptionProvider
{
    private readonly Dictionary<string, string> _map;
    public DefaultDtcDescriptionProvider(IDictionary<string, string>? seed = null)
        => _map = new(seed ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);

    public void Set(string code, string description) => _map[code] = description;
    public string? Describe(string code) => _map.TryGetValue(code, out var d) ? d : null;
}
