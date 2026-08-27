using Rox.FlowEngine;

namespace Rox.Transport;

/// <summary>
/// Presents any <see cref="ITransport"/> as the flow engine's <see cref="IEcuServiceExecutor"/>.
/// This is the seam that lets the existing (tested) <see cref="FlowInterpreter"/> run unchanged over
/// real CAN/DoIP transport as well as over the in-process simulator.
///
/// The flow engine's executor contract is synchronous (<c>byte[] Execute(string?, byte[])</c>), so we
/// bridge to the async transport here in exactly one place, isolating the sync-over-async boundary.
/// </summary>
public sealed class TransportEcuServiceExecutor : IEcuServiceExecutor
{
    private readonly ITransport _transport;
    private readonly TimeSpan _timeout;

    public TransportEcuServiceExecutor(ITransport transport, TimeSpan? timeout = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _timeout = timeout ?? TimeSpan.FromSeconds(5); // FR/assumptions: default 5 s comms timeout
    }

    public byte[] Execute(string? ecu, byte[] request)
    {
        // The interpreter is synchronous; block on the transport with a bounded timeout.
        using var cts = new CancellationTokenSource(_timeout);
        try
        {
            return _transport.SendAsync(request, cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Transport timed out after {_timeout.TotalSeconds:0.#}s waiting for a response from {ecu ?? "(default)"}.");
        }
    }
}
