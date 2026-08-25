using System.Runtime.InteropServices;
using Rox.Transport.Can;

namespace Rox.Transport.Adapters;

/// <summary>
/// Kvaser CANlib adapter (stub). The P/Invoke entry points below mirror the real CANlib surface so the
/// shape is right, but the adapter is not wired to hardware.
///
/// TODO(hardware): implement canOpenChannel / canBusOn / canWrite / canReadWait against canlib32.dll
/// and validate on a physical Kvaser interface. See docs/EXTERNAL_INPUTS.md.
/// </summary>
public sealed class KvaserCanChannel : ICanChannel
{
    public KvaserCanChannel(int channel = 0, int baudrate = 500_000) { Baudrate = baudrate; }
    public string Name => "Kvaser CANlib (stub)";
    public int Baudrate { get; }
    public bool IsOpen => false;

    public Task OpenAsync(CancellationToken ct = default) => throw NotWired();
    public Task CloseAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task WriteAsync(CanFrame frame, CancellationToken ct = default) => throw NotWired();
    public Task<CanFrame> ReadAsync(CancellationToken ct = default) => throw NotWired();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static NotSupportedException NotWired() =>
        new("Kvaser CANlib adapter is a stub (TODO(hardware)). Use the PCAN adapter or the simulated loopback.");

    // Real CANlib surface (bound at call time; unused until the stub is finished).
    [DllImport("canlib32.dll")] internal static extern int canOpenChannel(int channel, int flags);
    [DllImport("canlib32.dll")] internal static extern int canBusOn(int handle);
}

/// <summary>
/// Vector XL Driver adapter (stub), mirroring the XL API shape.
///
/// TODO(hardware): implement xlOpenPort / xlActivateChannel / xlCanTransmit / xlReceive against
/// vxlapi64.dll and validate on a physical Vector interface. See docs/EXTERNAL_INPUTS.md.
/// </summary>
public sealed class VectorCanChannel : ICanChannel
{
    public VectorCanChannel(int channel = 0, int baudrate = 500_000) { Baudrate = baudrate; }
    public string Name => "Vector XL (stub)";
    public int Baudrate { get; }
    public bool IsOpen => false;

    public Task OpenAsync(CancellationToken ct = default) => throw NotWired();
    public Task CloseAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task WriteAsync(CanFrame frame, CancellationToken ct = default) => throw NotWired();
    public Task<CanFrame> ReadAsync(CancellationToken ct = default) => throw NotWired();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static NotSupportedException NotWired() =>
        new("Vector XL adapter is a stub (TODO(hardware)). Use the PCAN adapter or the simulated loopback.");

    [DllImport("vxlapi64.dll")] internal static extern int xlOpenDriver();
    [DllImport("vxlapi64.dll")] internal static extern int xlCloseDriver();
}
