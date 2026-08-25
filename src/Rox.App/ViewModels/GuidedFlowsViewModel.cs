using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Rox.App.Services;
using Rox.FlowEngine;
using Rox.Transport;

namespace Rox.App.ViewModels;

public sealed partial class GuidedFlowsViewModel : PageViewModel
{
    private readonly DiagnosticSession _session;
    private readonly SettingsService _settings;
    private readonly DialogService _dialogs;
    public override string Title => "Guided Flows";
    public override string Glyph => ""; // List

    public ObservableCollection<string> FlowFiles { get; } = new();
    public ObservableCollection<string> Log { get; } = new();

    [ObservableProperty] private string? _selectedFlow;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Select a flow XML, then Run.";

    public GuidedFlowsViewModel(DiagnosticSession session, SettingsService settings, DialogService dialogs)
    {
        _session = session;
        _settings = settings;
        _dialogs = dialogs;
    }

    public override void OnActivated()
    {
        if (FlowFiles.Count == 0) RefreshFlows();
    }

    [RelayCommand]
    private void RefreshFlows()
    {
        FlowFiles.Clear();
        foreach (var dir in FlowDirectories())
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir, "*.xml", SearchOption.AllDirectories))
                FlowFiles.Add(f);
        }
        SelectedFlow ??= FlowFiles.FirstOrDefault();
        Status = FlowFiles.Count == 0 ? "No flow XML found — Browse to load one." : $"{FlowFiles.Count} flow(s) found.";
    }

    [RelayCommand]
    private void Browse()
    {
        var dlg = new OpenFileDialog { Filter = "Flow XML|*.xml", Multiselect = false };
        if (dlg.ShowDialog() == true) { FlowFiles.Add(dlg.FileName); SelectedFlow = dlg.FileName; }
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (SelectedFlow is null || !File.Exists(SelectedFlow)) { Status = "Select a flow file."; return; }
        if (!_session.IsConnected || _session.Transport is null) { Status = "Not connected."; return; }

        IsBusy = true;
        Log.Clear();
        try
        {
            var xml = await File.ReadAllTextAsync(SelectedFlow);
            var config = FlowParser.Parse(xml);
            if (config.Processes.Count == 0) { Status = "No processes in flow."; return; }

            var executor = new TransportEcuServiceExecutor(_session.Transport);
            var interp = new FlowInterpreter(executor, _session.SecurityProvider, prompt: _dialogs);

            await Task.Run(() =>
            {
                foreach (var proc in config.Processes) interp.Run(proc);
            });

            foreach (var line in interp.Log) Log.Add(line);
            Status = $"Flow '{Path.GetFileName(SelectedFlow)}' completed.";
        }
        catch (FlowException fx)
        {
            Status = $"Flow stopped: {fx.Message}";
        }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    private IEnumerable<string> FlowDirectories()
    {
        var s = _settings.Load();
        if (!string.IsNullOrWhiteSpace(s.DataPackageDirectory)) yield return s.DataPackageDirectory!;
        yield return Path.Combine(AppContext.BaseDirectory, "data");
        yield return Path.Combine(AppContext.BaseDirectory, "data", "flows");
    }
}
