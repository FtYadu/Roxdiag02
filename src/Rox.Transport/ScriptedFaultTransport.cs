using Rox.Core;

namespace Rox.Transport;

/// <summary>
/// A test/diagnostics decorator that injects negative responses in front of a real transport, so the
/// UDS client's error handling (0x78 pending poll, 0x33 re-auth, 0x36 lockout, 0x73 abort) can be
/// exercised without a misbehaving ECU. It keeps the tested foundation simulator untouched.
///
/// For a service id, you can queue:
///   • N response-pending (0x78) frames, after which the real response is returned (models the ECU
///     emitting "pending…" then the real answer — delivered via <see cref="ReceiveNextAsync"/>);
///   • a one-shot terminal NRC (e.g. 0x33/0x36/0x73) returned instead of forwarding.
/// </summary>
public sealed class ScriptedFaultTransport : IPendingAwareTransport
{
    private readonly ITransport _inner;
    private readonly Dictionary<byte, int> _pendingBySid = new();
    private readonly Dictionary<byte, Queue<byte>> _terminalNrcBySid = new();
    private byte[]? _lastRequest;
    private int _pendingRemaining;

    public ScriptedFaultTransport(ITransport inner) => _inner = inner;

    public string Name => $"Scripted-fault → {_inner.Name}";
    public bool IsConnected => _inner.IsConnected;
    public Task ConnectAsync(CancellationToken ct = default) => _inner.ConnectAsync(ct);
    public Task DisconnectAsync(CancellationToken ct = default) => _inner.DisconnectAsync(ct);

    /// <summary>Queue <paramref name="count"/> pending (0x78) responses before the real answer for a service id.</summary>
    public ScriptedFaultTransport InjectPending(byte serviceId, int count)
    {
        _pendingBySid[serviceId] = count;
        return this;
    }

    /// <summary>Queue a one-shot terminal NRC to return instead of forwarding, for a service id.</summary>
    public ScriptedFaultTransport InjectNrc(byte serviceId, byte nrc)
    {
        if (!_terminalNrcBySid.TryGetValue(serviceId, out var q)) _terminalNrcBySid[serviceId] = q = new Queue<byte>();
        q.Enqueue(nrc);
        return this;
    }

    public async Task<byte[]> SendAsync(byte[] request, CancellationToken ct = default)
    {
        _lastRequest = request;
        byte sid = request.Length > 0 ? request[0] : (byte)0x00;

        if (_terminalNrcBySid.TryGetValue(sid, out var nrcQueue) && nrcQueue.Count > 0)
            return Negative(sid, nrcQueue.Dequeue());

        if (_pendingBySid.TryGetValue(sid, out int n) && n > 0)
        {
            _pendingBySid[sid] = 0;
            _pendingRemaining = n;
            return Negative(sid, Nrc.ResponsePending);
        }

        return await _inner.SendAsync(request, ct).ConfigureAwait(false);
    }

    public async Task<byte[]> ReceiveNextAsync(CancellationToken ct = default)
    {
        byte sid = _lastRequest is { Length: > 0 } ? _lastRequest[0] : (byte)0x00;
        if (_pendingRemaining > 1)
        {
            _pendingRemaining--;
            return Negative(sid, Nrc.ResponsePending);
        }
        _pendingRemaining = 0;
        return _lastRequest is null ? Array.Empty<byte>() : await _inner.SendAsync(_lastRequest, ct).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _inner.DisposeAsync();

    private static byte[] Negative(byte sid, byte nrc) => new byte[] { UdsServices.NegativeResponseCode, sid, nrc };
}
