using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Rox.App.Services;
using Rox.Reflash;

namespace Rox.App.ViewModels;

public sealed partial class ReflashViewModel : PageViewModel
{
    private readonly DiagnosticSession _session;
    public override string Title => "Reflash";
    public override string Glyph => ""; // Download / chip

    public ObservableCollection<string> Ecus { get; } = new();
    public ObservableCollection<string> Log { get; } = new();

    [ObservableProperty] private string _selectedEcu = "EMS";
    [ObservableProperty] private string? _firmwarePath;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _throughput = "—";
    [ObservableProperty] private string _eta = "—";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Select target ECU and firmware, then Reflash.";

    public ReflashViewModel(DiagnosticSession session) => _session = session;

    public override void OnActivated()
    {
        if (Ecus.Count == 0)
        {
            foreach (var e in _session.Ecus.Where(e => e.Domain is "Powertrain" or "ADAS" or "Chassis")) Ecus.Add(e.Name);
            if (!Ecus.Contains(SelectedEcu)) SelectedEcu = Ecus.FirstOrDefault() ?? "EMS";
        }
    }

    [RelayCommand]
    private void BrowseFirmware()
    {
        var dlg = new OpenFileDialog { Filter = "Firmware|*.bin;*.hex;*.srec;*.s19|All files|*.*" };
        if (dlg.ShowDialog() == true) { FirmwarePath = dlg.FileName; Status = $"Firmware: {Path.GetFileName(FirmwarePath)} ({new FileInfo(FirmwarePath).Length} B)."; }
    }

    [RelayCommand]
    private async Task ReflashAsync()
    {
        if (!_session.IsConnected || _session.Reflash is null) { Status = "Not connected."; return; }
        if (FirmwarePath is null || !File.Exists(FirmwarePath)) { Status = "Select a firmware image."; return; }

        IsBusy = true;
        Log.Clear();
        Progress = 0;
        try
        {
            var firmware = await File.ReadAllBytesAsync(FirmwarePath);
            var cfg = new ReflashConfig { Ecu = SelectedEcu };
            var progress = new Progress<ReflashProgress>(p =>
            {
                Progress = p.Percent;
                Throughput = p.ThroughputBytesPerSec > 0 ? $"{p.ThroughputBytesPerSec / 1024.0:0.0} KB/s" : "—";
                Eta = p.Eta > TimeSpan.Zero ? $"{p.Eta.TotalSeconds:0}s" : "—";
                AddLog($"[{p.Stage}] {p.Message}");
            });

            var result = await _session.Reflash.ReflashAsync(cfg, firmware, progress);
            Status = result.Success
                ? $"Reflash successful. Checksum 0x{result.Checksum:X8}."
                : $"Reflash failed at {result.LastStage}: {result.Message}";
            AddLog(Status);
        }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; AddLog(Status); }
        finally { IsBusy = false; }
    }

    private void AddLog(string line)
    {
        void Add() => Log.Add(line);
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) Add(); else d.Invoke(Add);
    }
}
