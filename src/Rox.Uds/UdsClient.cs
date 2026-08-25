using Rox.Core;
using Rox.Transport;

namespace Rox.Uds;

/// <summary>
/// Stateful UDS (ISO 14229) client over an <see cref="ITransport"/>. Owns session state, a TesterPresent
/// keep-alive, response-pending (0x78) polling, and a retry/timeout policy driven by
/// <see cref="Nrc.RecommendedAction"/>. All transport access is serialized so the keep-alive never
/// interleaves with a request on a single-stream link.
/// </summary>
public sealed class UdsClient : IAsyncDisposable
{
    private readonly ITransport _transport;
    private readonly UdsClientOptions _opt;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Action<string>? _trace;

    public byte CurrentSession { get; private set; } = 0x01;

    /// <summary>Raised for every request/response, for the Expert Console live trace and logging.</summary>
    public event Action<UdsTraceEntry>? Traced;

    public UdsClient(ITransport transport, UdsClientOptions? options = null, Action<string>? trace = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _opt = options ?? new UdsClientOptions();
        _trace = trace;
    }

    // ---- core request path ----------------------------------------------------------------

    /// <summary>
    /// Send a raw UDS request and return the resolved response. Handles 0x78 pending polling and
    /// retries retryable NRCs per the recommended-action table. Negative responses that are terminal
    /// (lockout, denied, out-of-range, …) are returned, not thrown — the caller decides.
    /// </summary>
    public async Task<UdsResponse> RequestAsync(byte[] request, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await RequestLockedAsync(request, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<UdsResponse> RequestLockedAsync(byte[] request, CancellationToken ct)
    {
        UdsResponse response = UdsResponse.Parse(Array.Empty<byte>());
        for (int attempt = 0; attempt <= _opt.MaxRetries; attempt++)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_opt.Timeout);
            byte[] raw;
            try
            {
                raw = await _transport.SendAsync(request, timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Trace(request, Array.Empty<byte>(), $"timeout (attempt {attempt + 1})");
                continue; // retry on timeout
            }

            response = UdsResponse.Parse(raw);
            response = await ResolvePendingAsync(request, response, ct).ConfigureAwait(false);
            Trace(request, response.Raw, DescribeResult(response));

            if (response.IsPositive) return response;

            var action = Nrc.RecommendedAction(response.Nrc);
            switch (action)
            {
                case NrcAction.RetryAfterPrecondition:
                case NrcAction.RestartSequence:
                case NrcAction.ChangeSessionRetry:
                    if (attempt < _opt.MaxRetries) { await Task.Delay(_opt.RetryDelay, ct).ConfigureAwait(false); continue; }
                    return response;

                // Terminal for the client — surfaced to the caller (security/reflash/UI decide).
                default:
                    return response;
            }
        }
        return response;
    }

    private async Task<UdsResponse> ResolvePendingAsync(byte[] request, UdsResponse response, CancellationToken ct)
    {
        if (!response.IsPending) return response;

        var deadline = DateTimeOffset.UtcNow + _opt.PendingCeiling;
        for (int poll = 0; poll < _opt.MaxPendingPolls; poll++)
        {
            if (DateTimeOffset.UtcNow > deadline) throw new TimeoutException("UDS response-pending (0x78) exceeded the ceiling.");
            byte[] raw;
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_opt.Timeout);

            if (_transport is IPendingAwareTransport pending)
                raw = await pending.ReceiveNextAsync(timeoutCts.Token).ConfigureAwait(false);
            else
                raw = await _transport.SendAsync(request, timeoutCts.Token).ConfigureAwait(false); // fallback: re-send

            response = UdsResponse.Parse(raw);
            if (!response.IsPending) return response;
            Trace(request, response.Raw, $"pending 0x78 (poll {poll + 1})");
        }
        throw new TimeoutException("UDS response-pending (0x78) exceeded the poll count.");
    }

    // ---- typed service helpers ------------------------------------------------------------

    public async Task<UdsResponse> StartSessionAsync(byte session, CancellationToken ct = default)
    {
        var r = await RequestAsync(new[] { UdsServices.DiagnosticSessionControl, session }, ct).ConfigureAwait(false);
        if (r.IsPositive) CurrentSession = session;
        return r;
    }

    public Task<UdsResponse> TesterPresentAsync(bool suppressPositiveResponse = true, CancellationToken ct = default)
        => RequestAsync(new[] { UdsServices.TesterPresent, (byte)(suppressPositiveResponse ? 0x80 : 0x00) }, ct);

    public Task<UdsResponse> EcuResetAsync(byte resetType = 0x01, CancellationToken ct = default)
        => RequestAsync(new[] { UdsServices.EcuReset, resetType }, ct);

    public Task<UdsResponse> ControlDtcSettingAsync(bool on, CancellationToken ct = default)
        => RequestAsync(new[] { UdsServices.ControlDtcSetting, (byte)(on ? 0x01 : 0x02) }, ct);

    public Task<UdsResponse> ReadDtcByStatusMaskAsync(byte mask = 0xFF, CancellationToken ct = default)
        => RequestAsync(new byte[] { UdsServices.ReadDtcInformation, 0x02, mask }, ct);

    public Task<UdsResponse> ClearDiagnosticInformationAsync(uint groupOfDtc = 0xFFFFFF, CancellationToken ct = default)
        => RequestAsync(new byte[] { UdsServices.ClearDiagnosticInformation, (byte)(groupOfDtc >> 16), (byte)(groupOfDtc >> 8), (byte)groupOfDtc }, ct);

    public Task<UdsResponse> ReadDataByIdentifierAsync(ushort did, CancellationToken ct = default)
        => RequestAsync(new byte[] { UdsServices.ReadDataByIdentifier, (byte)(did >> 8), (byte)did }, ct);

    public Task<UdsResponse> WriteDataByIdentifierAsync(ushort did, byte[] data, CancellationToken ct = default)
    {
        var req = new byte[3 + data.Length];
        req[0] = UdsServices.WriteDataByIdentifier; req[1] = (byte)(did >> 8); req[2] = (byte)did;
        Array.Copy(data, 0, req, 3, data.Length);
        return RequestAsync(req, ct);
    }

    public Task<UdsResponse> RoutineControlAsync(byte type, ushort routineId, byte[]? data = null, CancellationToken ct = default)
    {
        data ??= Array.Empty<byte>();
        var req = new byte[4 + data.Length];
        req[0] = UdsServices.RoutineControl; req[1] = type; req[2] = (byte)(routineId >> 8); req[3] = (byte)routineId;
        Array.Copy(data, 0, req, 4, data.Length);
        return RequestAsync(req, ct);
    }

    public Task<UdsResponse> SecurityRequestSeedAsync(byte subFunction, CancellationToken ct = default)
        => RequestAsync(new[] { UdsServices.SecurityAccess, subFunction }, ct);

    public Task<UdsResponse> SecuritySendKeyAsync(byte subFunction, byte[] key, CancellationToken ct = default)
    {
        var req = new byte[2 + key.Length];
        req[0] = UdsServices.SecurityAccess; req[1] = subFunction;
        Array.Copy(key, 0, req, 2, key.Length);
        return RequestAsync(req, ct);
    }

    public Task<UdsResponse> RequestDownloadAsync(byte[] payload, CancellationToken ct = default)
    {
        var req = new byte[1 + payload.Length];
        req[0] = UdsServices.RequestDownload;
        Array.Copy(payload, 0, req, 1, payload.Length);
        return RequestAsync(req, ct);
    }

    public Task<UdsResponse> TransferDataAsync(byte blockSequenceCounter, byte[] data, CancellationToken ct = default)
    {
        var req = new byte[2 + data.Length];
        req[0] = UdsServices.TransferData; req[1] = blockSequenceCounter;
        Array.Copy(data, 0, req, 2, data.Length);
        return RequestAsync(req, ct);
    }

    public Task<UdsResponse> RequestTransferExitAsync(CancellationToken ct = default)
        => RequestAsync(new[] { UdsServices.RequestTransferExit }, ct);

    // ---- keep-alive -----------------------------------------------------------------------

    /// <summary>
    /// Start a TesterPresent keep-alive for the duration of a long operation. Dispose the returned
    /// scope to stop it. The keep-alive shares the request gate, so it never interleaves on the wire.
    /// </summary>
    public KeepAliveScope StartKeepAlive(CancellationToken ct = default) => new(this, _opt.KeepAliveInterval, ct);

    public sealed class KeepAliveScope : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cts;
        private readonly Task _loop;

        internal KeepAliveScope(UdsClient client, TimeSpan interval, CancellationToken ct)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _cts.Token;
            _loop = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(interval, token).ConfigureAwait(false);
                        try { await client.TesterPresentAsync(true, token).ConfigureAwait(false); }
                        catch (OperationCanceledException) { }
                        catch { /* keep-alive is best-effort; the next request surfaces real errors */ }
                    }
                }
                catch (OperationCanceledException) { }
            });
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { await _loop.ConfigureAwait(false); } catch { }
            _cts.Dispose();
        }
    }

    // ---- trace ----------------------------------------------------------------------------

    private void Trace(byte[] request, byte[] response, string note)
    {
        var entry = new UdsTraceEntry(DateTimeOffset.UtcNow, request, response, note);
        _trace?.Invoke(entry.ToString());
        Traced?.Invoke(entry);
    }

    private static string DescribeResult(UdsResponse r) =>
        r.IsPositive ? "positive"
        : r.IsNegative ? $"negative NRC 0x{r.Nrc:X2} {Nrc.Describe(r.Nrc)} → {Nrc.RecommendedAction(r.Nrc)}"
        : "empty";

    public async ValueTask DisposeAsync()
    {
        _gate.Dispose();
        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>One request/response pair for the live trace and logs. Never carries decoded key material.</summary>
public readonly record struct UdsTraceEntry(DateTimeOffset Timestamp, byte[] Request, byte[] Response, string Note)
{
    public override string ToString()
        => $"{Timestamp:HH:mm:ss.fff}  →{Convert.ToHexString(Request)}  ←{(Response.Length == 0 ? "(none)" : Convert.ToHexString(Response))}  [{Note}]";
}
