using System.Text;

namespace Rox.Logging.Reporting;

/// <summary>Exports DTCs, audit trail and raw UDS traces as CSV/TXT (FR-08.3, FR-03.7).</summary>
public static class CsvExporter
{
    public static void WriteDtcCsv(string path, IEnumerable<ReportDtc> dtcs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ECU,Code,Status,RawHex,Live,Description");
        foreach (var d in dtcs)
            sb.AppendLine($"{Esc(d.Ecu)},{Esc(d.Code)},{Esc(d.Status)},{Esc(d.RawHex)},{d.Live},{Esc(d.Description ?? "")}");
        File.WriteAllText(path, sb.ToString());
    }

    public static void WriteAuditCsv(string path, IEnumerable<AuditEntry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,Result,Operation,ECU,Details,Operator");
        foreach (var e in entries)
            sb.AppendLine($"{e.Timestamp:u},{e.Result},{Esc(e.Operation)},{Esc(e.Ecu ?? "")},{Esc(e.Details)},{Esc(e.Operator ?? "")}");
        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>Raw UDS trace as plain text (one request/response line each).</summary>
    public static void WriteTraceTxt(string path, IEnumerable<string> traceLines)
        => File.WriteAllLines(path, traceLines);

    private static string Esc(string v)
        => v.Contains(',') || v.Contains('"') || v.Contains('\n')
            ? "\"" + v.Replace("\"", "\"\"") + "\""
            : v;
}
