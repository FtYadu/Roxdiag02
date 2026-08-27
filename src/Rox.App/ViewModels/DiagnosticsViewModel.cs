using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Rox.App.Services;
using Rox.Diagnostics;
using Rox.Logging.Reporting;

namespace Rox.App.ViewModels;

public sealed partial class DiagnosticsViewModel : PageViewModel
{
    private readonly DiagnosticSession _session;
    public override string Title => "Diagnostics";
    public override string Glyph => ""; // Repair

    public ObservableCollection<string> Ecus { get; } = new();
    public ObservableCollection<DtcRow> Dtcs { get; } = new();

    [ObservableProperty] private string? _selectedEcu;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Connect, then read DTCs.";

    public DiagnosticsViewModel(DiagnosticSession session) => _session = session;

    public override void OnActivated()
    {
        if (Ecus.Count == 0)
        {
            foreach (var e in _session.Ecus) Ecus.Add(e.Name);
            SelectedEcu ??= Ecus.FirstOrDefault();
        }
    }

    private bool Ready() => _session.IsConnected && _session.Dtc is not null;

    [RelayCommand]
    private async Task ReadDtcsAsync()
    {
        if (!Ready() || SelectedEcu is null) { Status = "Not connected."; return; }
        IsBusy = true;
        try
        {
            var result = await _session.Dtc!.ReadAsync(SelectedEcu);
            Dtcs.Clear();
            foreach (var d in result.Dtcs) Dtcs.Add(DtcRow.From(SelectedEcu, d));
            Status = result.Failed ? $"Read failed (NRC 0x{result.Nrc:X2})." : $"{result.Dtcs.Count} DTC(s) on {SelectedEcu}.";
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ClearAndReadBackAsync()
    {
        if (!Ready() || SelectedEcu is null) { Status = "Not connected."; return; }
        IsBusy = true;
        try
        {
            var result = await _session.Dtc!.ReadClearReadBackAsync(SelectedEcu);
            Dtcs.Clear();
            foreach (var d in result.LiveRemaining) Dtcs.Add(DtcRow.From(SelectedEcu, d));
            Status = result.Summary;
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ScanAllAsync()
    {
        if (!Ready()) { Status = "Not connected."; return; }
        IsBusy = true;
        try
        {
            var scan = await _session.Dtc!.ScanAllAsync(_session.Ecus.Select(e => e.Name));
            Dtcs.Clear();
            foreach (var ecu in scan.PerEcu)
                foreach (var d in ecu.Dtcs) Dtcs.Add(DtcRow.From(ecu.Ecu, d));
            Status = $"All-ECU scan: {scan.TotalDtcs} DTC(s) across {scan.PerEcu.Count} ECUs.";
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void ExportCsv()
    {
        if (Dtcs.Count == 0) { Status = "Nothing to export."; return; }
        var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "rox-dtcs.csv" };
        if (dlg.ShowDialog() == true)
        {
            CsvExporter.WriteDtcCsv(dlg.FileName, Dtcs.Select(r =>
                new ReportDtc(r.Ecu, r.Code, r.Status, r.RawHex, r.Live, r.Description)));
            Status = $"Exported {Dtcs.Count} DTC(s) to {dlg.FileName}.";
        }
    }

    [RelayCommand]
    private void ExportPdf()
    {
        var dlg = new SaveFileDialog { Filter = "PDF|*.pdf", FileName = "rox-session.pdf" };
        if (dlg.ShowDialog() != true) return;
        var report = new SessionReport
        {
            VehicleProfile = _session.Profile?.Name ?? "R11_Oversea",
            TransportSummary = _session.TransportName,
            EcusAccessed = Dtcs.Select(d => d.Ecu).Distinct().ToList(),
            DtcsRead = Dtcs.Select(r => new ReportDtc(r.Ecu, r.Code, r.Status, r.RawHex, r.Live, r.Description)).ToList(),
            Audit = _session.Audit.Entries.ToList()
        };
        PdfReportGenerator.Generate(report, dlg.FileName);
        Status = $"Report written to {dlg.FileName}.";
    }
}

public sealed record DtcRow(string Ecu, string Code, string Status, string RawHex, bool Live, string? Description)
{
    public static DtcRow From(string ecu, DtcEntry d) => new(ecu, d.Code, d.StatusText, d.RawHex, d.IsLiveFault, d.Description);
}
