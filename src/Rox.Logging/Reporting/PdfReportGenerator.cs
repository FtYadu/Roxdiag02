using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Rox.Logging.Reporting;

/// <summary>
/// Generates the session-summary PDF (FR-08.2) with QuestPDF — a self-contained, offline PDF engine
/// (no network, no native GDI dependency). Suitable for warranty claims.
/// </summary>
public static class PdfReportGenerator
{
    private static bool _licenseSet;

    public static void EnsureLicense()
    {
        if (_licenseSet) return;
        QuestPDF.Settings.License = LicenseType.Community; // free community licence
        _licenseSet = true;
    }

    public static void Generate(SessionReport report, string outputPath)
    {
        EnsureLicense();
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text(report.Title).FontSize(18).Bold();
                    col.Item().Text($"Generated {report.GeneratedAt:yyyy-MM-dd HH:mm}  •  Offline diagnostic session").FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    col.Spacing(12);

                    col.Item().Column(v =>
                    {
                        v.Item().Text("Vehicle").FontSize(13).Bold();
                        v.Item().Text($"VIN: {report.Vin ?? "—"}");
                        v.Item().Text($"Profile: {report.VehicleProfile ?? "—"}");
                        v.Item().Text($"Odometer: {report.Odometer ?? "—"}");
                        v.Item().Text($"Operator: {report.Operator ?? "—"}");
                        v.Item().Text($"Transport: {report.TransportSummary ?? "—"}");
                        v.Item().Text($"ECUs accessed: {(report.EcusAccessed.Count == 0 ? "—" : string.Join(", ", report.EcusAccessed))}");
                    });

                    Section(col, $"DTCs read ({report.DtcsRead.Count})", () =>
                        DtcTable(col, report.DtcsRead), report.DtcsRead.Count > 0);

                    Section(col, $"DTCs cleared ({report.DtcsCleared.Count})", () =>
                        DtcTable(col, report.DtcsCleared), report.DtcsCleared.Count > 0);

                    if (report.FlowsExecuted.Count > 0)
                        Section(col, "Guided flows executed", () =>
                        {
                            foreach (var f in report.FlowsExecuted) col.Item().Text($"• {f}");
                        }, true);

                    if (report.Reflashes.Count > 0)
                        Section(col, "Reflash operations", () =>
                        {
                            foreach (var r in report.Reflashes)
                                col.Item().Text($"• {r.Ecu}: {r.Bytes} B, checksum {r.ChecksumHex} — {(r.Success ? "SUCCESS" : "FAILED")}");
                        }, true);

                    Section(col, $"Audit trail ({report.Audit.Count})", () =>
                    {
                        foreach (var a in report.Audit) col.Item().Text(a.ToLine()).FontSize(8);
                    }, report.Audit.Count > 0);
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("ROX Offline Diagnostic Suite — page ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf(outputPath);
    }

    private static void Section(ColumnDescriptor col, string title, Action body, bool hasContent)
    {
        col.Item().Text(title).FontSize(13).Bold();
        if (hasContent) body();
        else col.Item().Text("None").FontColor(Colors.Grey.Medium);
    }

    private static void DtcTable(ColumnDescriptor col, IReadOnlyList<ReportDtc> dtcs)
    {
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(1.4f); c.RelativeColumn(1); c.RelativeColumn(2); });
            foreach (var h in new[] { "ECU", "Code", "Status", "Live", "Description" })
                table.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text(h).Bold();
            foreach (var d in dtcs)
            {
                table.Cell().Padding(3).Text(d.Ecu);
                table.Cell().Padding(3).Text(d.Code);
                table.Cell().Padding(3).Text(d.Status);
                table.Cell().Padding(3).Text(d.Live ? "LIVE" : "");
                table.Cell().Padding(3).Text(d.Description ?? "");
            }
        });
    }
}
