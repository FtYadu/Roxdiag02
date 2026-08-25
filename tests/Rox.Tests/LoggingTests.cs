using Rox.Logging;
using Rox.Logging.Reporting;
using Xunit;

public class LoggingTests
{
    [Fact]
    public void Serilog_audit_sink_writes_entries_to_a_separate_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rox-log-" + Guid.NewGuid().ToString("N"));
        using (var logger = SerilogSetup.CreateAuditLogger(dir))
        using (var sink = new SerilogAuditSink(logger))
        {
            sink.Write(new AuditEntry(DateTimeOffset.UtcNow, "Key Pairing", "IMMO", AuditResult.Success, "key count 2 -> 3"));
        }
        var files = Directory.GetFiles(dir, "audit-*.log");
        Assert.NotEmpty(files);
        var text = File.ReadAllText(files[0]);
        Assert.Contains("Key Pairing", text);
        Assert.Contains("key count 2 -> 3", text);
        Directory.Delete(dir, true);
    }

    [Fact]
    public void Security_key_bytes_are_redacted_in_the_general_trace()
    {
        // 27 02 <key> — sendKey; key must not appear in the redacted line.
        var line = LogRedaction.RedactSecurityKey(new byte[] { 0x27, 0x02, 0xDE, 0xAD, 0xBE, 0xEF });
        Assert.Contains("redacted", line);
        Assert.DoesNotContain("DEADBEEF", line);
    }

    [Fact]
    public void Pdf_report_is_generated()
    {
        var report = new SessionReport
        {
            Vin = "TEST-VIN-0001",
            VehicleProfile = "R11_Oversea",
            Operator = "tech-1",
            EcusAccessed = { "EMS", "IMMO" },
            DtcsRead = { new ReportDtc("EMS", "P0301", "Confirmed", "030100", false, "Cylinder 1 misfire") },
            DtcsCleared = { new ReportDtc("EMS", "U0123", "Pending", "C12300", false, null) },
            FlowsExecuted = { "Add Key" },
            Reflashes = { new ReportReflash("EMS", 1000, "0x0001E240", true) },
            Audit = { new AuditEntry(DateTimeOffset.UtcNow, "MCU Reflash", "EMS", AuditResult.Success, "checksum ok") }
        };
        var path = Path.Combine(Path.GetTempPath(), "rox-report-" + Guid.NewGuid().ToString("N") + ".pdf");

        PdfReportGenerator.Generate(report, path);

        Assert.True(File.Exists(path));
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 1000);
        Assert.Equal((byte)'%', bytes[0]); // %PDF header
        Assert.Equal((byte)'P', bytes[1]);
        File.Delete(path);
    }

    [Fact]
    public void Csv_exports_dtcs_and_audit()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rox-csv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var dtcCsv = Path.Combine(dir, "dtcs.csv");
        var auditCsv = Path.Combine(dir, "audit.csv");

        CsvExporter.WriteDtcCsv(dtcCsv, new[] { new ReportDtc("EMS", "P0301", "Confirmed", "030100", true, "misfire, with comma") });
        CsvExporter.WriteAuditCsv(auditCsv, new[] { new AuditEntry(DateTimeOffset.UtcNow, "Key Deletion", "IMMO", AuditResult.Success, "2 -> 1") });

        var dtcText = File.ReadAllText(dtcCsv);
        Assert.Contains("ECU,Code,Status,RawHex,Live,Description", dtcText);
        Assert.Contains("\"misfire, with comma\"", dtcText); // quoting
        Assert.Contains("Key Deletion", File.ReadAllText(auditCsv));
        Directory.Delete(dir, true);
    }
}
