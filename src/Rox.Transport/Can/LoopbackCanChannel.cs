using System.Threading.Channels;

namespace Rox.Transport.Can;

/// <summary>
/// An in-memory pair of <see cref="ICanChannel"/> endpoints wired back-to-back: frames written to one
/// endpoint are read from the other. Lets ISO-TP run its real First/Consecutive/Flow-Control state
/// machine against the simulator with no hardware. Not a vendor adapter — a test/loopback fabric.
/// </summary>
public sealed class LoopbackCanChannel : ICanChannel
{
    private readonly Channel<CanFrame> _outbound; // frames this endpoint writes
    private readonly Channel<CanFrame> _inbound;  // frames this endpoint reads
    public string Name { get; }
    public int Baudrate { get; }
    public bool IsOpen { get; private set; }

    private LoopbackCanChannel(string name, int baud, Channel<CanFrame> outbound, Channel<CanFrame> inbound)
    {
        Name = name; Baudrate = baud; _outbound = outbound; _inbound = inbound;
    }

    /// <summary>Create a wired tester/ECU pair sharing a virtual bus at the given baudrate.</summary>
    public static (LoopbackCanChannel tester, LoopbackCanChannel ecu) CreatePair(int baudrate = 500_000)
    {
        var aToB = Channel.CreateUnbounded<CanFrame>();
        var bToA = Channel.CreateUnbounded<CanFrame>();
        var tester = new LoopbackCanChannel("Loopback CAN (tester)", baudrate, aToB, bToA);
        var ecu = new LoopbackCanChannel("Loopback CAN (ecu)", baudrate, bToA, aToB);
        return (tester, ecu);
    }

    public Task OpenAsync(CancellationToken ct = default) { IsOpen = true; return Task.CompletedTask; }
    public Task CloseAsync(CancellationToken ct = default) { IsOpen = false; return Task.CompletedTask; }

    public Task WriteAsync(CanFrame frame, CancellationToken ct = default)
    {
        if (!IsOpen) throw new InvalidOperationException("CAN channel not open.");
        return _outbound.Writer.WriteAsync(frame, ct).AsTask();
    }

    public async Task<CanFrame> ReadAsync(CancellationToken ct = default)
        => await _inbound.Reader.ReadAsync(ct).ConfigureAwait(false);

    public ValueTask DisposeAsync()
    {
        IsOpen = false;
        _outbound.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
