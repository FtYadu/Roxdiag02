using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Rox.Logging;

/// <summary>
/// Configures Serilog for the suite (PRD §11, FR-08). Two independent rolling-file sinks under
/// <c>%AppData%\ROXDiagnostic\Logs\</c>: a general session log and a **separate audit sink**. No
/// network sinks — the offline guarantee forbids any remote logging.
/// </summary>
public static class SerilogSetup
{
    public static string DefaultLogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ROXDiagnostic", "Logs");

    /// <summary>The general session logger (rolling by day, capped).</summary>
    public static Logger CreateSessionLogger(string? logDir = null)
    {
        logDir ??= DefaultLogDirectory;
        Directory.CreateDirectory(logDir);
        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(logDir, "session-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    /// <summary>The dedicated audit logger — separate file, longer retention, tamper-evident ordering.</summary>
    public static Logger CreateAuditLogger(string? logDir = null)
    {
        logDir ??= DefaultLogDirectory;
        Directory.CreateDirectory(logDir);
        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logDir, "audit-.log"),
                rollingInterval: RollingInterval.Month,
                retainedFileCountLimit: 24,
                outputTemplate: "{Message:lj}{NewLine}")
            .CreateLogger();
    }
}
