namespace Rox.Reflash;

/// <summary>Battery-voltage source for the reflash safety gate (FR-05.8, §13). Simulated by default.</summary>
public interface IVoltageProvider
{
    /// <summary>Current battery voltage in volts.</summary>
    Task<double> ReadVoltageAsync(CancellationToken ct = default);
}

/// <summary>Fixed-voltage provider for the simulator and tests.</summary>
public sealed class SimulatedVoltageProvider : IVoltageProvider
{
    private double _volts;
    public SimulatedVoltageProvider(double volts = 12.6) => _volts = volts;
    public double Volts { get => _volts; set => _volts = value; }
    public Task<double> ReadVoltageAsync(CancellationToken ct = default) => Task.FromResult(_volts);
}

/// <summary>
/// Reflash sequence parameters. Addresses, routine ids and the block-length come from the reflash flow
/// XML / data package, not embedded logic. Defaults match the bundled simulator.
/// </summary>
public sealed class ReflashConfig
{
    public string Ecu { get; init; } = "EMS";
    public byte RequestSeedSub { get; init; } = 0x01;

    public byte ProgrammingSession { get; init; } = 0x02;
    public byte DefaultSession { get; init; } = 0x01;

    public ushort CheckDependenciesRoutineId { get; init; } = 0x0203;
    public ushort EraseRoutineId { get; init; } = 0xFF00;
    public ushort CheckMemoryRoutineId { get; init; } = 0xFF01;

    /// <summary>Memory address + size for RequestDownload (0x34). Size defaults to the firmware length.</summary>
    public uint MemoryAddress { get; init; } = 0x0800_0000;
    public byte AddressBytes { get; init; } = 4;
    public byte SizeBytes { get; init; } = 4;
    public byte DataFormatIdentifier { get; init; } = 0x00; // no compression / encryption

    /// <summary>Minimum battery voltage to permit a reflash (FR-05.8).</summary>
    public double MinVoltage { get; init; } = 12.0;

    public int MaxCheckPolls { get; init; } = 50;
}

public enum ReflashStage { VoltageCheck, Confirm, PreProgramming, Security, CheckDependencies, Erase, RequestDownload, Transfer, TransferExit, ChecksumVerify, Done, Aborted }

public readonly record struct ReflashProgress(
    ReflashStage Stage,
    long BytesSent,
    long TotalBytes,
    double ThroughputBytesPerSec,
    TimeSpan Elapsed,
    string Message)
{
    public double Percent => TotalBytes == 0 ? 0 : 100.0 * BytesSent / TotalBytes;
    public TimeSpan Eta => ThroughputBytesPerSec <= 0 || TotalBytes == 0
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds((TotalBytes - BytesSent) / ThroughputBytesPerSec);
}

public sealed record ReflashResult(bool Success, ReflashStage LastStage, string Message, uint? Checksum = null, byte? Nrc = null);
