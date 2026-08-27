namespace Rox.Transport.Can;

/// <summary>
/// A real CAN <see cref="ITransport"/>: UDS request/response over ISO-TP on any <see cref="ICanChannel"/>
/// (a vendor adapter or the loopback fabric). Tester-side only — it segments the request and reassembles
/// the response; the ECU (real or simulated) performs the complementary framing.
/// </summary>
public sealed class IsoTpCanTransport : ITransport
{
    private readonly ICanChannel _channel;
    private readonly IsoTpChannel _isotp;
    private readonly bool _ownsChannel;

    /// <param name="channel">Raw CAN channel (vendor adapter or loopback).</param>
    /// <param name="txId">Diagnostic request CAN id (physical addressing).</param>
    /// <param name="rxId">Diagnostic response CAN id.</param>
    /// <param name="ownsChannel">Whether disposing the transport should close/dispose the channel.</param>
    public IsoTpCanTransport(ICanChannel channel, uint txId = 0x7E0, uint rxId = 0x7E8,
        IsoTpConfig? cfg = null, bool ownsChannel = true)
    {
        _channel = channel;
        _isotp = new IsoTpChannel(channel, txId, rxId, cfg);
        _ownsChannel = ownsChannel;
    }

    public string Name => $"CAN / ISO-TP via {_channel.Name} @ {_channel.Baudrate} bps";
    public bool IsConnected => _channel.IsOpen;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (!_channel.IsOpen) await _channel.OpenAsync(ct).ConfigureAwait(false);
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_channel.IsOpen) await _channel.CloseAsync(ct).ConfigureAwait(false);
    }

    public async Task<byte[]> SendAsync(byte[] request, CancellationToken ct = default)
    {
        if (!IsConnected) throw new InvalidOperationException("CAN transport not connected.");
        await _isotp.SendPduAsync(request, ct).ConfigureAwait(false);
        return await _isotp.ReceivePduAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_ownsChannel) await _channel.DisposeAsync().ConfigureAwait(false);
    }
}
