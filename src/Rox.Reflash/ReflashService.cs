using System.Diagnostics;
using Rox.Core;
using Rox.Logging;
using Rox.Security;
using Rox.Uds;

namespace Rox.Reflash;

/// <summary>
/// MCU reflash controller (FR-05, UC-03). Pre-flash (<c>10 02</c> → <c>85 02</c> → SecurityAccess →
/// checkDependencies → erase), download (<c>0x34</c> — block size taken from the response's
/// <c>maxNumberOfBlockLength</c>, NOT a fixed 2 KB — then the <c>0x36</c> loop with BSC wrap and 0x78
/// handling), <c>0x37</c>, and a post-flash checksum routine. Voltage-gated and confirmation-gated
/// (bricking risk); writes an audit entry with the checksum.
/// </summary>
public sealed class ReflashService
{
    private readonly UdsClient _client;
    private readonly SecurityAccessService _security;
    private readonly IAuditSink _audit;
    private readonly IConfirmationService _confirm;
    private readonly IVoltageProvider _voltage;

    public ReflashService(UdsClient client, SecurityAccessService security, IAuditSink audit,
        IConfirmationService confirm, IVoltageProvider? voltage = null)
    {
        _client = client;
        _security = security;
        _audit = audit;
        _confirm = confirm;
        _voltage = voltage ?? new SimulatedVoltageProvider();
    }

    public async Task<ReflashResult> ReflashAsync(ReflashConfig cfg, byte[] firmware, IProgress<ReflashProgress>? progress = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        void Report(ReflashStage stage, long sent, string msg) =>
            progress?.Report(new ReflashProgress(stage, sent, firmware.Length,
                sent > 0 && sw.Elapsed.TotalSeconds > 0 ? sent / sw.Elapsed.TotalSeconds : 0, sw.Elapsed, msg));

        // 1) Voltage gate (FR-05.8) — before anything irreversible.
        double volts = await _voltage.ReadVoltageAsync(ct).ConfigureAwait(false);
        Report(ReflashStage.VoltageCheck, 0, $"Battery {volts:0.0} V");
        if (volts < cfg.MinVoltage)
        {
            var msg = $"Reflash blocked: battery {volts:0.0} V is below the {cfg.MinVoltage:0.0} V threshold. Attach a maintainer.";
            _audit.Write(Audit(cfg, AuditResult.Denied, msg));
            return new ReflashResult(false, ReflashStage.VoltageCheck, msg);
        }

        // 2) Multi-step confirmation with explicit bricking warning (FR-05.8, NON-NEGOTIABLE #3).
        if (!await _confirm.ConfirmAsync(new ConfirmationRequest("MCU Reflash", cfg.Ecu, OperationRisk.BrickingRisk,
                $"Reflashing {cfg.Ecu} with {firmware.Length} bytes. Interrupting this can brick the ECU.",
                new[] { "A battery maintainer is attached and voltage is stable.",
                        "The firmware image is correct for this ECU and market.",
                        "The vehicle must not be disturbed until the flash completes." }), ct).ConfigureAwait(false))
        {
            _audit.Write(Audit(cfg, AuditResult.Denied, "Operator declined reflash confirmation."));
            return new ReflashResult(false, ReflashStage.Confirm, "Cancelled — confirmation declined.");
        }

        await using var keepAlive = _client.StartKeepAlive(ct);

        // 3) Pre-programming: session + DTCs off.
        Report(ReflashStage.PreProgramming, 0, "Entering programming session");
        if ((await _client.StartSessionAsync(cfg.ProgrammingSession, ct).ConfigureAwait(false)).IsNegative)
            return Fail(cfg, ReflashStage.PreProgramming, "Could not enter programming session.");
        await _client.ControlDtcSettingAsync(on: false, ct).ConfigureAwait(false); // 85 02

        // 4) Security.
        Report(ReflashStage.Security, 0, "Security access");
        var sec = await _security.RequestAccessAsync(cfg.Ecu, cfg.RequestSeedSub, ct).ConfigureAwait(false);
        if (!sec.Granted) return Fail(cfg, ReflashStage.Security, sec.Describe());

        // 5) checkDependencies + erase.
        Report(ReflashStage.CheckDependencies, 0, "Checking pre-programming conditions");
        if (!await RunRoutineAsync(cfg, cfg.CheckDependenciesRoutineId, null, ct).ConfigureAwait(false))
            return Fail(cfg, ReflashStage.CheckDependencies, "checkDependencies routine failed.");

        Report(ReflashStage.Erase, 0, "Erasing memory");
        if (!await RunRoutineAsync(cfg, cfg.EraseRoutineId, null, ct).ConfigureAwait(false))
            return Fail(cfg, ReflashStage.Erase, "Erase routine failed.");

        // 6) RequestDownload → read maxNumberOfBlockLength.
        Report(ReflashStage.RequestDownload, 0, "RequestDownload");
        var rd = await _client.RequestDownloadAsync(BuildRequestDownload(cfg, (uint)firmware.Length), ct).ConfigureAwait(false);
        if (rd.IsNegative) return Fail(cfg, ReflashStage.RequestDownload, $"RequestDownload denied: NRC 0x{rd.Nrc:X2}", rd.Nrc);
        int maxBlockLength = ParseMaxBlockLength(rd.Raw);
        if (maxBlockLength <= 2) return Fail(cfg, ReflashStage.RequestDownload, "ECU reported an unusable maxNumberOfBlockLength.");
        int dataPerBlock = maxBlockLength - 2; // minus 0x36 SID + BSC (per ISO 14229)

        // 7) TransferData loop with BSC wrap (0xFF→0x00) and 0x78 handled by the client.
        long sent = 0;
        byte bsc = 0x01;
        for (int offset = 0; offset < firmware.Length; offset += dataPerBlock)
        {
            ct.ThrowIfCancellationRequested();
            int len = Math.Min(dataPerBlock, firmware.Length - offset);
            var block = firmware.AsSpan(offset, len).ToArray();
            var td = await _client.TransferDataAsync(bsc, block, ct).ConfigureAwait(false);
            if (td.IsNegative)
            {
                if (td.Nrc == Nrc.WrongBlockSequenceCounter)
                    return Fail(cfg, ReflashStage.Transfer, "Aborted: wrong block sequence counter (NRC 0x73).", td.Nrc);
                return Fail(cfg, ReflashStage.Transfer, $"TransferData failed: NRC 0x{td.Nrc:X2} {Nrc.Describe(td.Nrc)}", td.Nrc);
            }
            sent += len;
            bsc = (byte)((bsc + 1) & 0xFF); // wraps 0xFF -> 0x00
            Report(ReflashStage.Transfer, sent, $"Transferring… {sent}/{firmware.Length} B");
        }

        // 8) TransferExit.
        Report(ReflashStage.TransferExit, sent, "RequestTransferExit");
        if ((await _client.RequestTransferExitAsync(ct).ConfigureAwait(false)).IsNegative)
            return Fail(cfg, ReflashStage.TransferExit, "RequestTransferExit failed.");

        // 9) Post-flash checksum verify (CRC/checksum where the flow defines it).
        Report(ReflashStage.ChecksumVerify, sent, "Verifying checksum");
        uint checksum = Sum32(firmware);
        var check = await RunRoutineWithResponseAsync(cfg, cfg.CheckMemoryRoutineId, Be32(checksum), ct).ConfigureAwait(false);
        if (check.IsNegative)
        {
            var msg = $"Checksum verification failed (NRC 0x{check.Nrc:X2}). ECU left in programming session — do not power down.";
            _audit.Write(Audit(cfg, AuditResult.Failure, msg + $" checksum=0x{checksum:X8}"));
            return new ReflashResult(false, ReflashStage.ChecksumVerify, msg, checksum, check.Nrc);
        }

        await RestoreSessionAsync(cfg, ct).ConfigureAwait(false);
        Report(ReflashStage.Done, sent, "Reflash successful");
        _audit.Write(Audit(cfg, AuditResult.Success, $"Reflash successful, {firmware.Length} B, checksum=0x{checksum:X8}"));
        return new ReflashResult(true, ReflashStage.Done, "Reflash successful.", checksum);
    }

    // ---- helpers ----

    private ReflashResult Fail(ReflashConfig cfg, ReflashStage stage, string message, byte? nrc = null)
    {
        _audit.Write(Audit(cfg, AuditResult.Failure, $"{stage}: {message}"));
        return new ReflashResult(false, stage, message, Nrc: nrc);
    }

    private async Task<bool> RunRoutineAsync(ReflashConfig cfg, ushort routineId, byte[]? data, CancellationToken ct)
    {
        var start = await _client.RoutineControlAsync(0x01, routineId, data, ct).ConfigureAwait(false);
        if (start.IsNegative) return false;
        return await PollRoutineAsync(cfg, routineId, ct).ConfigureAwait(false);
    }

    private async Task<UdsResponse> RunRoutineWithResponseAsync(ReflashConfig cfg, ushort routineId, byte[]? data, CancellationToken ct)
    {
        var start = await _client.RoutineControlAsync(0x01, routineId, data, ct).ConfigureAwait(false);
        if (start.IsNegative) return start;
        await PollRoutineAsync(cfg, routineId, ct).ConfigureAwait(false);
        return start;
    }

    private async Task<bool> PollRoutineAsync(ReflashConfig cfg, ushort routineId, CancellationToken ct)
    {
        for (int i = 0; i < cfg.MaxCheckPolls; i++)
        {
            var r = await _client.RoutineControlAsync(0x03, routineId, null, ct).ConfigureAwait(false);
            if (r.IsNegative)
            {
                if (r.Nrc == Nrc.RequestSequenceError || r.Nrc == Nrc.ConditionsNotCorrect) { await Task.Delay(20, ct).ConfigureAwait(false); continue; }
                return false;
            }
            if (r.Raw.Length >= 5 && r.Raw[^1] == 0x00) return true;
            if (r.Raw.Length == 4) return true;
            await Task.Delay(20, ct).ConfigureAwait(false);
        }
        return false;
    }

    private async Task RestoreSessionAsync(ReflashConfig cfg, CancellationToken ct)
    {
        try { await _client.EcuResetAsync(0x01, ct).ConfigureAwait(false); } catch { }
    }

    private static byte[] BuildRequestDownload(ReflashConfig cfg, uint size)
    {
        // dataFormatIdentifier + addressAndLengthFormatIdentifier + memoryAddress + memorySize
        var alfi = (byte)((cfg.SizeBytes << 4) | cfg.AddressBytes);
        var list = new List<byte> { cfg.DataFormatIdentifier, alfi };
        list.AddRange(BeN(cfg.MemoryAddress, cfg.AddressBytes));
        list.AddRange(BeN(size, cfg.SizeBytes));
        return list.ToArray();
    }

    private static int ParseMaxBlockLength(byte[] rd)
    {
        // 74 <lengthFormatIdentifier> <maxNumberOfBlockLength ...>
        if (rd.Length < 3) return 0;
        int n = (rd[1] >> 4) & 0x0F;
        if (n <= 0 || rd.Length < 2 + n) return 0;
        int v = 0;
        for (int i = 0; i < n; i++) v = (v << 8) | rd[2 + i];
        return v;
    }

    private static byte[] BeN(uint value, int bytes)
    {
        var b = new byte[bytes];
        for (int i = bytes - 1; i >= 0; i--) { b[i] = (byte)(value & 0xFF); value >>= 8; }
        return b;
    }
    private static byte[] Be32(uint v) => new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };

    /// <summary>Additive 32-bit checksum matching the simulator's checkMemory routine.</summary>
    public static uint Sum32(ReadOnlySpan<byte> data) { uint s = 0; foreach (var b in data) s += b; return s; }

    private static AuditEntry Audit(ReflashConfig cfg, AuditResult result, string details)
        => new(DateTimeOffset.UtcNow, "MCU Reflash", cfg.Ecu, result, details);
}
