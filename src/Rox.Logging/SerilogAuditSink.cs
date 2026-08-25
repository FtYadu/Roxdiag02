using Serilog;

namespace Rox.Logging;

/// <summary>
/// Writes audit entries to the dedicated Serilog audit logger. Entries are already redacted by
/// construction (no plaintext keys/seeds ever reach an <see cref="AuditEntry"/>, FR-08.4).
/// </summary>
public sealed class SerilogAuditSink : IAuditSink, IDisposable
{
    private readonly ILogger _logger;
    private readonly bool _ownsLogger;

    public SerilogAuditSink(ILogger auditLogger, bool ownsLogger = false)
    {
        _logger = auditLogger;
        _ownsLogger = ownsLogger;
    }

    public void Write(AuditEntry entry) => _logger.Information("{AuditLine}", entry.ToLine());

    public void Dispose()
    {
        if (_ownsLogger && _logger is IDisposable d) d.Dispose();
    }
}

/// <summary>Guards against writing secrets into logs (defence-in-depth over the audit contract).</summary>
public static class LogRedaction
{
    /// <summary>Redacts any SecurityAccess key material from a raw UDS line for the general trace log.</summary>
    public static string RedactSecurityKey(byte[] request)
    {
        // 27 <even> <key...> — replace the key bytes with a placeholder in the general log.
        if (request.Length >= 3 && request[0] == 0x27 && (request[1] & 0x01) == 0)
            return $"27{request[1]:X2}<key redacted, {request.Length - 2} bytes>";
        return Convert.ToHexString(request);
    }
}
