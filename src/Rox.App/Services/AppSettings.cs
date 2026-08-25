using Rox.Transport;

namespace Rox.App.Services;

/// <summary>Persisted user settings (FR-07.6). Stored as settings.json; the security-module path is
/// stored separately, DPAPI-encrypted (never in this plaintext file).</summary>
public sealed class AppSettings
{
    public TransportKind TransportKind { get; set; } = TransportKind.SimulatedLoopback;

    // DoIP
    public string DoipHost { get; set; } = "127.0.0.1";
    public int DoipPort { get; set; } = 13400;
    public int DoipTesterAddress { get; set; } = 0x0E80;
    public int DoipTargetAddress { get; set; } = 0x1000;

    // CAN
    public int Baudrate { get; set; } = 500_000;
    public uint CanTxId { get; set; } = 0x7E0;
    public uint CanRxId { get; set; } = 0x7E8;

    // Timeouts
    public int TimeoutSeconds { get; set; } = 5;

    // Reflash
    public double MinReflashVoltage { get; set; } = 12.0;

    // Paths
    public string? DataPackageDirectory { get; set; }
    public string? LogDirectory { get; set; }

    // Licensing (offline)
    public string? LicenseKey { get; set; }

    // UI
    public string Theme { get; set; } = "Dark"; // "Dark" | "Light"
}
