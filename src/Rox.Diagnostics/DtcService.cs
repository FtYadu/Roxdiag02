using Rox.Core;
using Rox.Uds;

namespace Rox.Diagnostics;

/// <summary>
/// DTC orchestration on top of the UDS client (FR-03, UC-01): read <c>19 02 FF</c>, decode to J2012 +
/// status bits, and the professional read → clear → read-back cycle with live-fault detection. Security
/// for clear (FR-03.6) is layered above via the security provider; here we surface the NRC.
/// </summary>
public sealed class DtcService
{
    private readonly UdsClient _client;
    private readonly IDtcDescriptionProvider _descriptions;

    public DtcService(UdsClient client, IDtcDescriptionProvider? descriptions = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _descriptions = descriptions ?? new DefaultDtcDescriptionProvider();
    }

    /// <summary>Read DTCs from one ECU (reportDTCByStatusMask, mask 0xFF).</summary>
    public async Task<DtcReadResult> ReadAsync(string ecu, byte mask = 0xFF, CancellationToken ct = default)
    {
        var resp = await _client.ReadDtcByStatusMaskAsync(mask, ct).ConfigureAwait(false);
        var entries = resp.IsPositive ? Decode(resp.Raw) : Array.Empty<DtcEntry>();
        return new DtcReadResult(ecu, entries, resp);
    }

    /// <summary>
    /// Read → clear → read-back. Captures the pre-clear list, enters extended session if requested
    /// (FR-03.4), clears all groups (<c>14 FF FF FF</c>), re-reads, and flags codes that re-set
    /// immediately as live faults distinct from cleared stale codes (FR-03.5).
    /// </summary>
    public async Task<DtcClearResult> ReadClearReadBackAsync(string ecu, bool enterExtendedSession = true, CancellationToken ct = default)
    {
        var before = (await ReadAsync(ecu, 0xFF, ct).ConfigureAwait(false)).Dtcs;

        if (enterExtendedSession)
            await _client.StartSessionAsync(0x03, ct).ConfigureAwait(false);

        var clear = await _client.ClearDiagnosticInformationAsync(0xFFFFFF, ct).ConfigureAwait(false);
        if (clear.IsNegative)
            return new DtcClearResult(ecu, before, before, ClearAccepted: false, clear);

        var after = (await ReadAsync(ecu, 0xFF, ct).ConfigureAwait(false)).Dtcs;
        return new DtcClearResult(ecu, before, after, ClearAccepted: true, clear);
    }

    /// <summary>All-ECU scan: read each provided ECU and aggregate (FR-03.3).</summary>
    public async Task<AllEcuScanResult> ScanAllAsync(IEnumerable<string> ecus, CancellationToken ct = default)
    {
        var results = new List<DtcReadResult>();
        foreach (var ecu in ecus)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await ReadAsync(ecu, 0xFF, ct).ConfigureAwait(false));
        }
        return new AllEcuScanResult(results);
    }

    private DtcEntry[] Decode(byte[] raw)
    {
        var decoded = DtcDecoder.ParseReadDtcByStatusMask(raw);
        var list = new DtcEntry[decoded.Count];
        for (int i = 0; i < decoded.Count; i++)
            list[i] = new DtcEntry(decoded[i], _descriptions.Describe(decoded[i].Code));
        return list;
    }
}
