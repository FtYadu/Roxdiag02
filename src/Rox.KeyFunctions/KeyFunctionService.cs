using Rox.Core;
using Rox.FlowEngine;
using Rox.Logging;
using Rox.Security;
using Rox.Uds;

namespace Rox.KeyFunctions;

/// <summary>
/// Immobilizer key functions (FR-09, UC-04): pairing, duplication, deletion. All share one skeleton —
/// <c>10 03</c> → SecurityAccess → read key count → operator prompt → routine (<c>31 01</c>) → poll
/// (<c>31 03</c>) → verify count → <c>10 01</c> — and every one is gated by explicit confirmation and
/// writes a full audit entry. Routine ids / DIDs are data-package values, never embedded logic.
/// </summary>
public sealed class KeyFunctionService
{
    private readonly UdsClient _client;
    private readonly SecurityAccessService _security;
    private readonly IAuditSink _audit;
    private readonly IConfirmationService _confirm;
    private readonly IUserPrompt _prompt;

    public KeyFunctionService(UdsClient client, SecurityAccessService security, IAuditSink audit,
        IConfirmationService confirm, IUserPrompt? prompt = null)
    {
        _client = client;
        _security = security;
        _audit = audit;
        _confirm = confirm;
        _prompt = prompt ?? new AutoContinuePrompt();
    }

    public Task<KeyOperationResult> PairAsync(KeyFunctionConfig cfg, CancellationToken ct = default)
        => LearnAsync(KeyOperation.Pairing, cfg, "Insert the new key and switch ignition ON.", learnPayload: null, expectDelta: +1, ct);

    public Task<KeyOperationResult> DuplicateAsync(KeyFunctionConfig cfg, byte[]? duplicatePayload = null, CancellationToken ct = default)
        => LearnAsync(KeyOperation.Duplication, cfg, "Insert the duplicate key blank and switch ignition ON.", duplicatePayload, expectDelta: +1, ct);

    private async Task<KeyOperationResult> LearnAsync(KeyOperation op, KeyFunctionConfig cfg, string promptMsg, byte[]? learnPayload, int expectDelta, CancellationToken ct)
    {
        var opName = op.ToString();

        if (!await _confirm.ConfirmAsync(new ConfirmationRequest(opName, cfg.Ecu, OperationRisk.Irreversible,
                $"{opName} writes a new key identity to the immobilizer.",
                new[] { "A new key will be learned to this vehicle.", "The operation cannot be undone without deleting the key." }), ct).ConfigureAwait(false))
        {
            _audit.Write(Entry(opName, cfg.Ecu, AuditResult.Denied, "Operator declined confirmation."));
            return new KeyOperationResult(op, false, null, null, "Cancelled — confirmation declined.");
        }

        await _client.StartSessionAsync(cfg.ExtendedSession, ct).ConfigureAwait(false);
        await using var keepAlive = _client.StartKeepAlive(ct);

        var sec = await _security.RequestAccessAsync(cfg.Ecu, cfg.RequestSeedSub, ct).ConfigureAwait(false);
        if (!sec.Granted)
        {
            _audit.Write(Entry(opName, cfg.Ecu, AuditResult.Failure, $"SecurityAccess: {sec.Describe()}"));
            await RestoreSessionAsync(cfg, ct).ConfigureAwait(false);
            return new KeyOperationResult(op, false, null, null, sec.Describe());
        }

        int? before = await ReadKeyCountAsync(cfg, ct).ConfigureAwait(false);

        if (!_prompt.Prompt(promptMsg, cfg.PromptTimeoutSeconds))
        {
            _audit.Write(Entry(opName, cfg.Ecu, AuditResult.Cancelled, "Operator cancelled at key-insert prompt."));
            await RestoreSessionAsync(cfg, ct).ConfigureAwait(false);
            return new KeyOperationResult(op, false, before, before, "Cancelled at key-insert prompt.");
        }

        var learn = await _client.RoutineControlAsync(0x01, cfg.LearnRoutineId, learnPayload, ct).ConfigureAwait(false);
        if (learn.IsNegative)
        {
            var msg = $"Learn routine failed: NRC 0x{learn.Nrc:X2} {Nrc.Describe(learn.Nrc)}";
            _audit.Write(Entry(opName, cfg.Ecu, AuditResult.Failure, msg));
            await RestoreSessionAsync(cfg, ct).ConfigureAwait(false);
            return new KeyOperationResult(op, false, before, before, msg);
        }

        var pollOk = await PollRoutineAsync(cfg, cfg.LearnRoutineId, ct).ConfigureAwait(false);
        int? after = await ReadKeyCountAsync(cfg, ct).ConfigureAwait(false);
        await RestoreSessionAsync(cfg, ct).ConfigureAwait(false);

        bool asExpected = before is int b && after is int a && a == b + expectDelta;
        var result = new KeyOperationResult(op, pollOk && asExpected, before, after,
            asExpected ? $"{opName} complete: key count {before} → {after}."
                       : $"{opName} did not confirm: key count {before} → {after}.")
        { CountChangedAsExpected = asExpected };

        _audit.Write(Entry(opName, cfg.Ecu, result.Success ? AuditResult.Success : AuditResult.Warning,
            $"key count {before} → {after}; routine {(pollOk ? "complete" : "poll incomplete")}"));
        return result;
    }

    public async Task<KeyOperationResult> DeleteAsync(KeyFunctionConfig cfg, byte[]? keyIdPayload = null, CancellationToken ct = default)
    {
        const KeyOperation op = KeyOperation.Deletion;
        const string opName = nameof(KeyOperation.Deletion);

        if (!await _confirm.ConfirmAsync(new ConfirmationRequest(opName, cfg.Ecu, OperationRisk.Irreversible,
                "Key deletion permanently removes a key identity from the immobilizer.",
                new[] { "The selected key will stop starting this vehicle.", "This cannot be undone; keep at least one working key." }), ct).ConfigureAwait(false))
        {
            _audit.Write(Entry(opName, cfg.Ecu, AuditResult.Denied, "Operator declined confirmation."));
            return new KeyOperationResult(op, false, null, null, "Cancelled — confirmation declined.");
        }

        await _client.StartSessionAsync(cfg.ExtendedSession, ct).ConfigureAwait(false);
        await using var keepAlive = _client.StartKeepAlive(ct);

        var sec = await _security.RequestAccessAsync(cfg.Ecu, cfg.RequestSeedSub, ct).ConfigureAwait(false);
        if (!sec.Granted)
        {
            _audit.Write(Entry(opName, cfg.Ecu, AuditResult.Failure, $"SecurityAccess: {sec.Describe()}"));
            await RestoreSessionAsync(cfg, ct).ConfigureAwait(false);
            return new KeyOperationResult(op, false, null, null, sec.Describe());
        }

        int? before = await ReadKeyCountAsync(cfg, ct).ConfigureAwait(false);
        var del = await _client.RoutineControlAsync(0x01, cfg.DeleteRoutineId, keyIdPayload, ct).ConfigureAwait(false);
        if (del.IsNegative)
        {
            var msg = $"Delete routine failed: NRC 0x{del.Nrc:X2} {Nrc.Describe(del.Nrc)}";
            _audit.Write(Entry(opName, cfg.Ecu, AuditResult.Failure, msg));
            await RestoreSessionAsync(cfg, ct).ConfigureAwait(false);
            return new KeyOperationResult(op, false, before, before, msg);
        }

        var pollOk = await PollRoutineAsync(cfg, cfg.DeleteRoutineId, ct).ConfigureAwait(false);
        int? after = await ReadKeyCountAsync(cfg, ct).ConfigureAwait(false);
        await RestoreSessionAsync(cfg, ct).ConfigureAwait(false);

        bool asExpected = before is int b && after is int a && a == b - 1;
        var result = new KeyOperationResult(op, pollOk && asExpected, before, after,
            asExpected ? $"Deletion complete: key count {before} → {after}."
                       : $"Deletion did not confirm: key count {before} → {after}.")
        { CountChangedAsExpected = asExpected };

        _audit.Write(Entry(opName, cfg.Ecu, result.Success ? AuditResult.Success : AuditResult.Warning,
            $"key count {before} → {after}; routine {(pollOk ? "complete" : "poll incomplete")}"));
        return result;
    }

    private async Task<int?> ReadKeyCountAsync(KeyFunctionConfig cfg, CancellationToken ct)
    {
        var r = await _client.ReadDataByIdentifierAsync(cfg.KeyCountDid, ct).ConfigureAwait(false);
        // Positive: 62 <didHi> <didLo> <count...>; count is the first data byte.
        return r.IsPositive && r.Raw.Length >= 4 ? r.Raw[3] : null;
    }

    private async Task<bool> PollRoutineAsync(KeyFunctionConfig cfg, ushort routineId, CancellationToken ct)
    {
        for (int i = 0; i < cfg.MaxResultPolls; i++)
        {
            var r = await _client.RoutineControlAsync(0x03, routineId, null, ct).ConfigureAwait(false);
            if (r.IsNegative)
            {
                if (r.Nrc == Nrc.RequestSequenceError || r.Nrc == Nrc.ConditionsNotCorrect) { await Task.Delay(50, ct).ConfigureAwait(false); continue; }
                return false;
            }
            // Positive 31 03 result: trailing status byte 0x00 = complete (simulator convention).
            if (r.Raw.Length >= 5 && r.Raw[^1] == 0x00) return true;
            if (r.Raw.Length == 4) return true; // no status field => treat as complete
            await Task.Delay(50, ct).ConfigureAwait(false);
        }
        return false;
    }

    private async Task RestoreSessionAsync(KeyFunctionConfig cfg, CancellationToken ct)
    {
        try { await _client.StartSessionAsync(cfg.DefaultSession, ct).ConfigureAwait(false); } catch { }
    }

    private static AuditEntry Entry(string op, string ecu, AuditResult result, string details)
        => new(DateTimeOffset.UtcNow, $"Key {op}", ecu, result, details);
}
