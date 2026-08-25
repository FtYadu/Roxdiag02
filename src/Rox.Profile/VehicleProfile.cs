namespace Rox.Profile;

public enum BusKind { Can, Ethernet, Unknown }

public sealed class VehicleProfile
{
    public string Name { get; init; } = "";
    public List<BusDefinition> Buses { get; init; } = new();
}

public sealed class BusDefinition
{
    public BusKind Kind { get; init; }
    public string Name { get; init; } = "";
    public int Baudrate { get; init; }
    public int NetworkType { get; init; }
    public bool IsUse { get; init; }
    // CAN
    public int? HPin { get; init; }
    public int? LPin { get; init; }
    // Ethernet / DoIP
    public int? RHPin { get; init; }
    public int? RLPin { get; init; }
    public int? THPin { get; init; }
    public int? TLPin { get; init; }
    public int? APin { get; init; }
    public string? IpVersion { get; init; }
    public int? ProtocolVersion { get; init; }
}
