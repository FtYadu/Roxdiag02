using Rox.FlowEngine;
using Rox.Transport.Adapters;
using Rox.Transport.Can;
using Rox.Transport.Doip;

namespace Rox.Transport;

public enum TransportKind
{
    /// <summary>In-process simulator, no framing (default; fastest).</summary>
    SimulatedLoopback,
    /// <summary>In-process simulator through the real ISO-TP state machine.</summary>
    SimulatedIsoTp,
    /// <summary>DoIP over a real TCP socket (against a vehicle or the simulated DoIP server).</summary>
    Doip,
    /// <summary>CAN / ISO-TP over the PCAN-Basic reference adapter (requires hardware).</summary>
    PcanCan,
    /// <summary>CAN / ISO-TP over the Kvaser CANlib adapter (stub).</summary>
    KvaserCan,
    /// <summary>CAN / ISO-TP over the Vector XL adapter (stub).</summary>
    VectorCan
}

public sealed class TransportOptions
{
    public TransportKind Kind { get; init; } = TransportKind.SimulatedLoopback;

    // Simulator target
    public IEcuServiceExecutor? Simulator { get; init; }
    public string? Ecu { get; init; }

    // CAN
    public int Baudrate { get; init; } = 500_000;
    public uint CanTxId { get; init; } = 0x7E0;
    public uint CanRxId { get; init; } = 0x7E8;
    public ushort PcanHandle { get; init; } = Pcan.PCAN_USBBUS1;

    // DoIP
    public string DoipHost { get; init; } = "127.0.0.1";
    public int DoipPort { get; init; } = DoipClientTransport.DefaultPort;
    public ushort DoipTesterAddress { get; init; } = 0x0E80;
    public ushort DoipTargetAddress { get; init; } = 0x1000;
}

/// <summary>Builds the configured <see cref="ITransport"/>. Defaults keep the app on the simulator.</summary>
public static class TransportFactory
{
    public static ITransport Create(TransportOptions options)
    {
        switch (options.Kind)
        {
            case TransportKind.SimulatedLoopback:
                return new LoopbackTransport(RequireSim(options), options.Ecu);

            case TransportKind.SimulatedIsoTp:
                return new IsoTpLoopbackTransport(RequireSim(options), options.Ecu,
                    options.CanTxId, options.CanRxId, baudrate: options.Baudrate);

            case TransportKind.Doip:
                return new DoipClientTransport(options.DoipHost, options.DoipTargetAddress,
                    options.DoipPort, options.DoipTesterAddress);

            case TransportKind.PcanCan:
                return new IsoTpCanTransport(new PcanCanChannel(options.PcanHandle, options.Baudrate),
                    options.CanTxId, options.CanRxId);

            case TransportKind.KvaserCan:
                return new IsoTpCanTransport(new KvaserCanChannel(baudrate: options.Baudrate),
                    options.CanTxId, options.CanRxId);

            case TransportKind.VectorCan:
                return new IsoTpCanTransport(new VectorCanChannel(baudrate: options.Baudrate),
                    options.CanTxId, options.CanRxId);

            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Kind, "Unknown transport kind.");
        }
    }

    private static IEcuServiceExecutor RequireSim(TransportOptions o) =>
        o.Simulator ?? throw new ArgumentException("A simulator executor is required for a simulated transport kind.");
}
