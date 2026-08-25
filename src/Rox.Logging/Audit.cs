namespace Rox.Logging;

public enum AuditResult { Success, Failure, Denied, Cancelled, Warning }

/// <summary>
/// One audit-trail entry for a security-gated or irreversible operation (PRD §7.2, §11, FR-08.4).
/// Records the event and its result only — NEVER plaintext keys or full seeds.
/// </summary>
public sealed record AuditEntry(
    DateTimeOffset Timestamp,
    string Operation,
    string? Ecu,
    AuditResult Result,
    string Details,
    string? Operator = null)
{
    public string ToLine() =>
        $"{Timestamp:u} | {Result,-9} | {Operation}{(Ecu is null ? "" : $" @ {Ecu}")} | {Details}{(Operator is null ? "" : $" | by {Operator}")}";
}

/// <summary>Sink for audit entries. The Serilog-backed file sink is added in Rox.Logging (P7).</summary>
public interface IAuditSink
{
    void Write(AuditEntry entry);
}

/// <summary>Keeps entries in memory (tests, and the in-app audit view). Thread-safe.</summary>
public sealed class InMemoryAuditSink : IAuditSink
{
    private readonly List<AuditEntry> _entries = new();
    private readonly object _lock = new();

    public IReadOnlyList<AuditEntry> Entries { get { lock (_lock) return _entries.ToList(); } }

    public void Write(AuditEntry entry) { lock (_lock) _entries.Add(entry); }
}

/// <summary>Discards entries (used where auditing is intentionally off, e.g. some unit tests).</summary>
public sealed class NullAuditSink : IAuditSink
{
    public static readonly NullAuditSink Instance = new();
    public void Write(AuditEntry entry) { }
}

/// <summary>Fans an entry out to several sinks (e.g. in-memory view + Serilog file).</summary>
public sealed class CompositeAuditSink : IAuditSink
{
    private readonly IReadOnlyList<IAuditSink> _sinks;
    public CompositeAuditSink(params IAuditSink[] sinks) => _sinks = sinks;
    public void Write(AuditEntry entry) { foreach (var s in _sinks) s.Write(entry); }
}
