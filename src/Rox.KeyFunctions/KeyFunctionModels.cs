namespace Rox.KeyFunctions;

public enum KeyOperation { Pairing, Duplication, Deletion }

/// <summary>
/// OEM-specific values that drive the key flows. These come from the data package (routine IDs, DIDs),
/// NOT from application logic (PRD §7.1, FR-09). Defaults match the bundled simulator.
/// </summary>
public sealed class KeyFunctionConfig
{
    public string Ecu { get; init; } = "IMMO";
    public byte RequestSeedSub { get; init; } = 0x01;

    /// <summary>DID that returns the current learned-key count (simulator: 0xF18C).</summary>
    public ushort KeyCountDid { get; init; } = 0xF18C;

    /// <summary>RoutineControl id for the key-learn routine (simulator: 0x0201).</summary>
    public ushort LearnRoutineId { get; init; } = 0x0201;

    /// <summary>RoutineControl id for the key-delete routine (simulator: 0x0202).</summary>
    public ushort DeleteRoutineId { get; init; } = 0x0202;

    public byte ExtendedSession { get; init; } = 0x03;
    public byte DefaultSession { get; init; } = 0x01;

    /// <summary>Operator prompt timeout for "insert key / ignition ON" (0 = wait indefinitely).</summary>
    public int PromptTimeoutSeconds { get; init; } = 0;

    /// <summary>Max 0x31 03 result polls before declaring the routine stuck.</summary>
    public int MaxResultPolls { get; init; } = 30;
}

public sealed record KeyOperationResult(
    KeyOperation Operation,
    bool Success,
    int? BeforeCount,
    int? AfterCount,
    string Message)
{
    public bool CountChangedAsExpected { get; init; }
}
