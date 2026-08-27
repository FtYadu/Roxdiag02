using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rox.App.Services;
using Rox.KeyFunctions;

namespace Rox.App.ViewModels;

public sealed partial class KeyFunctionsViewModel : PageViewModel
{
    private readonly DiagnosticSession _session;
    public override string Title => "Key Functions";
    public override string Glyph => ""; // Permissions / key

    public ObservableCollection<string> Ecus { get; } = new();
    public ObservableCollection<string> AuditLog { get; } = new();

    [ObservableProperty] private string _selectedEcu = "IMMO";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _keyCount = "—";
    [ObservableProperty] private string _status = "Connect, then pair / duplicate / delete a key.";

    public KeyFunctionsViewModel(DiagnosticSession session) => _session = session;

    public override void OnActivated()
    {
        if (Ecus.Count == 0)
        {
            foreach (var e in _session.Ecus.Where(e => e.Name is "IMMO" or "PMS" or "CCU" or "BMS")) Ecus.Add(e.Name);
            if (Ecus.Count == 0) foreach (var e in _session.Ecus) Ecus.Add(e.Name);
            if (!Ecus.Contains(SelectedEcu)) SelectedEcu = Ecus.FirstOrDefault() ?? "IMMO";
        }
    }

    private bool Ready() => _session.IsConnected && _session.Keys is not null;
    private KeyFunctionConfig Config() => new() { Ecu = SelectedEcu };

    [RelayCommand]
    private async Task ReadCountAsync()
    {
        if (!_session.IsConnected || _session.Uds is null) { Status = "Not connected."; return; }
        IsBusy = true;
        try
        {
            var r = await _session.Uds.ReadDataByIdentifierAsync(new KeyFunctionConfig().KeyCountDid);
            KeyCount = r.IsPositive && r.Raw.Length >= 4 ? r.Raw[3].ToString() : "unknown";
            Status = $"Current key count: {KeyCount}.";
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand] private Task PairAsync() => RunAsync(() => _session.Keys!.PairAsync(Config()));
    [RelayCommand] private Task DuplicateAsync() => RunAsync(() => _session.Keys!.DuplicateAsync(Config()));
    [RelayCommand] private Task DeleteAsync() => RunAsync(() => _session.Keys!.DeleteAsync(Config()));

    private async Task RunAsync(Func<Task<KeyOperationResult>> op)
    {
        if (!Ready()) { Status = "Not connected."; return; }
        IsBusy = true;
        try
        {
            var result = await op();
            KeyCount = result.AfterCount?.ToString() ?? KeyCount;
            Status = result.Message;
            RefreshAudit();
        }
        catch (Exception ex) { Status = ex.Message; }
        finally { IsBusy = false; }
    }

    private void RefreshAudit()
    {
        AuditLog.Clear();
        foreach (var e in _session.Audit.Entries.Reverse().Take(30)) AuditLog.Add(e.ToLine());
    }
}
