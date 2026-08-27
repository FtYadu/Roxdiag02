using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rox.App.Services;
using Rox.Core;

namespace Rox.App.ViewModels;

public sealed partial class ExpertConsoleViewModel : PageViewModel
{
    private readonly DiagnosticSession _session;
    private bool _subscribed;
    public override string Title => "Expert Console";
    public override string Glyph => ""; // Code / terminal

    public ObservableCollection<string> Trace { get; } = new();

    [ObservableProperty] private string _requestHex = "22 F1 8C";
    [ObservableProperty] private string _lastResponse = "—";
    [ObservableProperty] private bool _isBusy;

    public ExpertConsoleViewModel(DiagnosticSession session)
    {
        _session = session;
        _session.Trace += OnTrace;
    }

    public override void OnActivated() { _subscribed = true; }

    private void OnTrace(string line)
    {
        if (!_subscribed) return;
        void Add() { Trace.Add(line); if (Trace.Count > 500) Trace.RemoveAt(0); }
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) Add(); else d.Invoke(Add);
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (!_session.IsConnected || _session.Uds is null) { LastResponse = "Not connected."; return; }
        byte[] request;
        try { request = ParseHex(RequestHex); }
        catch { LastResponse = "Invalid hex."; return; }
        if (request.Length == 0) { LastResponse = "Empty request."; return; }

        IsBusy = true;
        try
        {
            var resp = await _session.Uds.RequestAsync(request);
            LastResponse = resp.Raw.Length == 0 ? "(no response)"
                : Convert.ToHexString(resp.Raw) + (resp.IsNegative ? $"  — NRC 0x{resp.Nrc:X2} {Nrc.Describe(resp.Nrc)}" : "  — positive");
        }
        catch (Exception ex) { LastResponse = ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private void ClearTrace() => Trace.Clear();

    private static byte[] ParseHex(string s)
    {
        var clean = new string(s.Where(Uri.IsHexDigit).ToArray());
        if (clean.Length % 2 != 0) throw new FormatException();
        var b = new byte[clean.Length / 2];
        for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);
        return b;
    }
}
