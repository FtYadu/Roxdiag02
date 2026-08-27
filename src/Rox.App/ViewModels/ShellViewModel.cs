using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Rox.App.Services;

namespace Rox.App.ViewModels;

/// <summary>Root view-model: navigation between pages + the connect/disconnect command and status bar.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly DiagnosticSession _session;

    public ObservableCollection<PageViewModel> Pages { get; }

    [ObservableProperty] private PageViewModel? _currentPage;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _connectionStatus = "Disconnected";
    [ObservableProperty] private string _transportName = "—";
    [ObservableProperty] private bool _isConnected;

    public ShellViewModel(DiagnosticSession session, IEnumerable<PageViewModel> pages)
    {
        _session = session;
        Pages = new ObservableCollection<PageViewModel>(pages);
        CurrentPage = Pages.FirstOrDefault();
        _session.StateChanged += OnSessionStateChanged;
    }

    partial void OnCurrentPageChanged(PageViewModel? value) => value?.OnActivated();

    private void OnSessionStateChanged()
    {
        void Update()
        {
            IsConnected = _session.IsConnected;
            ConnectionStatus = _session.IsConnected ? "Connected" : "Disconnected";
            TransportName = _session.TransportName;
            CurrentPage?.OnActivated();
        }
        var d = Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess()) Update(); else d.Invoke(Update);
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (_session.IsConnected) return;
        IsBusy = true;
        try { await _session.ConnectAsync(); }
        catch (Exception ex) { DialogService.ShowError($"Connection failed:\n{ex.Message}"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        IsBusy = true;
        try { await _session.DisconnectAsync(); }
        finally { IsBusy = false; }
    }
}
