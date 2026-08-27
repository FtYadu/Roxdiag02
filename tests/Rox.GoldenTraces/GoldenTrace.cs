using System.Text.Json;
using Rox.Transport;

namespace Rox.GoldenTraces;

/// <summary>One recorded request/response exchange.</summary>
public sealed record TraceExchange(string Req, string Resp);

/// <summary>A recorded golden trace: an ordered list of UDS exchanges, persisted as pretty JSON.</summary>
public sealed class GoldenTrace
{
    public string Name { get; set; } = "";
    public List<TraceExchange> Exchanges { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));
    }

    public static GoldenTrace Load(string path)
        => JsonSerializer.Deserialize<GoldenTrace>(File.ReadAllText(path)) ?? new GoldenTrace();
}

/// <summary>
/// Wraps a transport and records every request/response exchange, so a real (or simulated) session can
/// be captured once and replayed as a regression fixture (PRD §13 golden traces).
/// </summary>
public sealed class RecordingTransport : ITransport
{
    private readonly ITransport _inner;
    public GoldenTrace Trace { get; }

    public RecordingTransport(ITransport inner, string name = "trace")
    {
        _inner = inner;
        Trace = new GoldenTrace { Name = name };
    }

    public string Name => $"Recording → {_inner.Name}";
    public bool IsConnected => _inner.IsConnected;
    public Task ConnectAsync(CancellationToken ct = default) => _inner.ConnectAsync(ct);
    public Task DisconnectAsync(CancellationToken ct = default) => _inner.DisconnectAsync(ct);

    public async Task<byte[]> SendAsync(byte[] request, CancellationToken ct = default)
    {
        var resp = await _inner.SendAsync(request, ct).ConfigureAwait(false);
        Trace.Exchanges.Add(new TraceExchange(Convert.ToHexString(request), Convert.ToHexString(resp)));
        return resp;
    }

    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}

/// <summary>
/// Replays a recorded golden trace with no live target. Each request must match the recorded request in
/// order — any divergence (changed bytes, changed order, an extra/missing request) throws, which is
/// exactly how a regression in the UDS/flow layers is caught in CI.
/// </summary>
public sealed class ReplayTransport : IPendingAwareTransport
{
    private readonly GoldenTrace _trace;
    private int _index;

    public ReplayTransport(GoldenTrace trace) => _trace = trace;

    public string Name => $"Replay [{_trace.Name}]";
    public bool IsConnected { get; private set; }
    public Task ConnectAsync(CancellationToken ct = default) { IsConnected = true; return Task.CompletedTask; }
    public Task DisconnectAsync(CancellationToken ct = default) { IsConnected = false; return Task.CompletedTask; }

    public Task<byte[]> SendAsync(byte[] request, CancellationToken ct = default)
    {
        if (_index >= _trace.Exchanges.Count)
            throw new GoldenTraceRegression($"Unexpected extra request {Convert.ToHexString(request)} beyond the recorded trace (len {_trace.Exchanges.Count}).");
        var expected = _trace.Exchanges[_index];
        var actual = Convert.ToHexString(request);
        if (!string.Equals(expected.Req, actual, StringComparison.OrdinalIgnoreCase))
            throw new GoldenTraceRegression($"Trace divergence at step {_index}: expected request {expected.Req}, got {actual}.");
        _index++;
        return Task.FromResult(Convert.FromHexString(expected.Resp));
    }

    // Pending continuation replays the next recorded exchange without consuming a new request slot mismatch.
    public Task<byte[]> ReceiveNextAsync(CancellationToken ct = default)
    {
        if (_index >= _trace.Exchanges.Count)
            throw new GoldenTraceRegression("Pending continuation ran past the recorded trace.");
        return Task.FromResult(Convert.FromHexString(_trace.Exchanges[_index++].Resp));
    }

    public ValueTask DisposeAsync() { IsConnected = false; return ValueTask.CompletedTask; }

    public bool AtEnd => _index >= _trace.Exchanges.Count;
}

public sealed class GoldenTraceRegression : Exception
{
    public GoldenTraceRegression(string message) : base(message) { }
}
