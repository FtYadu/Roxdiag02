using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Rox.App.Services;
using Rox.Security;

namespace Rox.App.ViewModels;

public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly DiagnosticSession _session;
    private readonly SettingsService _settings;
    public override string Title => "Dashboard";
    public override string Glyph => ""; // Home

    public ObservableCollection<EcuInfo> Ecus { get; } = new();

    [ObservableProperty] private string _vehicle = "R11_Oversea";
    [ObservableProperty] private string _connection = "Disconnected";
    [ObservableProperty] private string _transport = "—";
    [ObservableProperty] private string _profileSummary = "No profile loaded";
    [ObservableProperty] private string _license = "";

    public DashboardViewModel(DiagnosticSession session, SettingsService settings)
    {
        _session = session;
        _settings = settings;
    }

    public override void OnActivated()
    {
        Connection = _session.IsConnected ? "Connected" : "Disconnected";
        Transport = _session.TransportName;
        License = LicenseValidator.Validate(_settings.Load().LicenseKey).Message;
        Ecus.Clear();
        foreach (var e in _session.Ecus) Ecus.Add(e);

        if (_session.Profile is { } p)
        {
            var buses = string.Join("; ", p.Buses.Select(b =>
                b.Kind == Rox.Profile.BusKind.Can
                    ? $"CAN {b.Baudrate}bps (H{b.HPin}/L{b.LPin})"
                    : $"DoIP {b.IpVersion} act.pin {b.APin}"));
            ProfileSummary = $"{p.Name}: {buses}";
        }
        else ProfileSummary = "No profile loaded (connect to load the data package).";
    }
}
