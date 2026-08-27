namespace Rox.Uds;

/// <summary>Timeouts and retry/keep-alive policy for the UDS client (PRD §6, NFR Reliability).</summary>
public sealed class UdsClientOptions
{
    /// <summary>Per-request response timeout (assumptions: default 5 s, adjustable in settings).</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Auto-retry budget before user notification (NFR: ≤ 3×).</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>Delay between retries for retryable NRCs (0x22 conditions-not-correct, session change).</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Max number of 0x78 pending polls before giving up (a stuck ECU).</summary>
    public int MaxPendingPolls { get; init; } = 60;

    /// <summary>Overall ceiling for a single request including all its pending polls.</summary>
    public TimeSpan PendingCeiling { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>TesterPresent keep-alive interval during long operations (FR-02.5, S3 ~2 s).</summary>
    public TimeSpan KeepAliveInterval { get; init; } = TimeSpan.FromSeconds(2);
}
