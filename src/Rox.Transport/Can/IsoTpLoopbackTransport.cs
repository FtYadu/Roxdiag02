using Rox.FlowEngine;

namespace Rox.Transport.Can;

/// <summary>
/// A hardware-free CAN transport that runs the ISO-TP state machine end to end against the simulator:
/// a tester endpoint plus a background ECU responder that reassembles each request, hands it to an
/// <see cref="IEcuServiceExecutor"/>, and segments the response back. Unlike <see cref="LoopbackTransport"/>
/// (no framing), this proves multi-frame First/Consecutive/Flow-Control round-trips — DTC lists and DIDs
/// that exceed a single 8-byte frame actually get segmented and reassembled.
/// </summary>
public sealed class IsoTpLoopbackTransport : ITransport
{
    private readonly LoopbackCanChannel _testerChannel;
    private readonly LoopbackCanChannel _ecuChannel;
    private readonly IsoTpCanTransport _tester;
    private readonly IsoTpChannel _ecuIso;
    private readonly IEcuServiceExecutor _executor;
    private readonly string? _ecu;
    private readonly CancellationTokenSource _cts = new();
    private Task? _responder;

    public IsoTpLoopbackTransport(IEcuServiceExecutor executor, string? ecu = null,
        uint txId = 0x7E0, uint rxId = 0x7E8, IsoTpConfig? cfg = null, int baudrate = 500_000)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _ecu = ecu;
        (_testerChannel, _ecuChannel) = LoopbackCanChannel.CreatePair(baudrate);
        _tester = new IsoTpCanTransport(_testerChannel, txId, rxId, cfg, ownsChannel: false);
        // ECU side mirrors the ids: it receives on the request id and replies on the response id.
        _ecuIso = new IsoTpChannel(_ecuChannel, txId: rxId, rxId: txId, cfg);
    }

    public string Name => "Simulated CAN / ISO-TP loopback";
    public bool IsConnected { get; private set; }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _testerChannel.OpenAsync(ct).ConfigureAwait(false);
        await _ecuChannel.OpenAsync(ct).ConfigureAwait(false);
        await _tester.ConnectAsync(ct).ConfigureAwait(false);
        _responder ??= Task.Run(() => RespondLoopAsync(_cts.Token));
        IsConnected = true;
    }

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        IsConnected = false;
        await _testerChannel.CloseAsync(ct).ConfigureAwait(false);
        await _ecuChannel.CloseAsync(ct).ConfigureAwait(false);
    }

    public Task<byte[]> SendAsync(byte[] request, CancellationToken ct = default)
    {
        if (!IsConnected) throw new InvalidOperationException("Transport not connected.");
        return _tester.SendAsync(request, ct);
    }

    private async Task RespondLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var request = await _ecuIso.ReceivePduAsync(ct).ConfigureAwait(false);
                var response = _executor.Execute(_ecu, request);
                await _ecuIso.SendPduAsync(response, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }
        catch (Exception) when (ct.IsCancellationRequested) { /* shutdown race */ }
    }

    public async ValueTask DisposeAsync()
    {
        IsConnected = false;
        _cts.Cancel();
        try { if (_responder is not null) await _responder.ConfigureAwait(false); } catch { /* ignore */ }
        await _testerChannel.DisposeAsync().ConfigureAwait(false);
        await _ecuChannel.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
