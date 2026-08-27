using Rox.FlowEngine;

namespace Rox.Transport;

/// <summary>
/// The default, hardware-free target: wraps an in-process <see cref="IEcuServiceExecutor"/>
/// (the <c>EcuSimulator</c>) as an <see cref="ITransport"/> with no framing. Used as the selectable
/// "vehicle" in Settings and throughout the tests. For a transport that also exercises real ISO-TP
/// segmentation against the simulator, see <see cref="Can.IsoTpLoopbackTransport"/>.
/// </summary>
public sealed class LoopbackTransport : ITransport
{
    private readonly IEcuServiceExecutor _executor;
    private readonly string? _ecu;

    public LoopbackTransport(IEcuServiceExecutor executor, string? ecu = null)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _ecu = ecu;
    }

    public string Name => "Simulated loopback (in-process)";
    public bool IsConnected { get; private set; }

    public Task ConnectAsync(CancellationToken ct = default) { IsConnected = true; return Task.CompletedTask; }
    public Task DisconnectAsync(CancellationToken ct = default) { IsConnected = false; return Task.CompletedTask; }

    public Task<byte[]> SendAsync(byte[] request, CancellationToken ct = default)
    {
        if (!IsConnected) throw new InvalidOperationException("Transport not connected.");
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_executor.Execute(_ecu, request));
    }

    public ValueTask DisposeAsync() { IsConnected = false; return ValueTask.CompletedTask; }
}
