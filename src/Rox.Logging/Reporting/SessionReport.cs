namespace Rox.Logging.Reporting;

/// <summary>Aggregated data for a session summary / warranty report (FR-08.2).</summary>
public sealed class SessionReport
{
    public string Title { get; init; } = "ROX Diagnostic Session Report";
    public string? Vin { get; init; }
    public string? VehicleProfile { get; init; }
    public string? Operator { get; init; }
    public string? Odometer { get; init; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.Now;

    public List<string> EcusAccessed { get; init; } = new();
    public List<ReportDtc> DtcsRead { get; init; } = new();
    public List<ReportDtc> DtcsCleared { get; init; } = new();
    public List<string> FlowsExecuted { get; init; } = new();
    public List<ReportReflash> Reflashes { get; init; } = new();
    public List<AuditEntry> Audit { get; init; } = new();
    public string? TransportSummary { get; init; }
}

public sealed record ReportDtc(string Ecu, string Code, string Status, string RawHex, bool Live, string? Description);
public sealed record ReportReflash(string Ecu, long Bytes, string ChecksumHex, bool Success);
