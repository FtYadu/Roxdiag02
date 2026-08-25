using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Rox.App.Services;
using Rox.Security;
using Rox.Transport;
using Wpf.Ui.Appearance;

namespace Rox.App.ViewModels;

public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly SettingsService _settings;
    public override string Title => "Settings";
    public override string Glyph => ""; // Settings

    public ObservableCollection<TransportKind> TransportKinds { get; } =
        new(Enum.GetValues<TransportKind>());
    public string[] Themes { get; } = { "Dark", "Light" };

    [ObservableProperty] private TransportKind _transportKind;
    [ObservableProperty] private string _doipHost = "127.0.0.1";
    [ObservableProperty] private int _doipPort = 13400;
    [ObservableProperty] private int _baudrate = 500_000;
    [ObservableProperty] private int _timeoutSeconds = 5;
    [ObservableProperty] private double _minReflashVoltage = 12.0;
    [ObservableProperty] private string? _dataPackageDirectory;
    [ObservableProperty] private string? _securityModulePath;
    [ObservableProperty] private string _moduleProtection = "";
    [ObservableProperty] private string _theme = "Dark";
    [ObservableProperty] private string _status = "";

    public SettingsViewModel(SettingsService settings)
    {
        _settings = settings;
        Load();
    }

    private void Load()
    {
        var s = _settings.Load();
        TransportKind = s.TransportKind;
        DoipHost = s.DoipHost;
        DoipPort = s.DoipPort;
        Baudrate = s.Baudrate;
        TimeoutSeconds = s.TimeoutSeconds;
        MinReflashVoltage = s.MinReflashVoltage;
        DataPackageDirectory = s.DataPackageDirectory;
        Theme = s.Theme;
        SecurityModulePath = _settings.SecurityModulePath;
        ModuleProtection = _settings.ModulePathEncrypted ? "DPAPI-encrypted (per user)" : "Not encrypted (non-Windows dev only)";
    }

    [RelayCommand]
    private void BrowseModule()
    {
        var dlg = new OpenFileDialog { Filter = "Seed-key module|*.dll;*.so|All files|*.*" };
        if (dlg.ShowDialog() == true) SecurityModulePath = dlg.FileName;
    }

    [RelayCommand]
    private void BrowseDataPackage()
    {
        var dlg = new OpenFolderDialog();
        if (dlg.ShowDialog() == true) DataPackageDirectory = dlg.FolderName;
    }

    [RelayCommand]
    private void TestModule()
    {
        if (string.IsNullOrWhiteSpace(SecurityModulePath) || !File.Exists(SecurityModulePath))
        { Status = "No module selected."; return; }
        try
        {
            using var provider = new NativeSeedKeyProvider(SecurityModulePath!);
            var outcome = SecurityModuleTester.Test(provider, new byte[] { 0x11, 0x22, 0x33, 0x44 });
            Status = outcome.Message;
        }
        catch (Exception ex) { Status = $"Module test failed: {ex.Message}"; }
    }

    [RelayCommand]
    private void Save()
    {
        var s = _settings.Load();
        s.TransportKind = TransportKind;
        s.DoipHost = DoipHost;
        s.DoipPort = DoipPort;
        s.Baudrate = Baudrate;
        s.TimeoutSeconds = TimeoutSeconds;
        s.MinReflashVoltage = MinReflashVoltage;
        s.DataPackageDirectory = DataPackageDirectory;
        s.Theme = Theme;
        _settings.Save(s);
        _settings.SecurityModulePath = SecurityModulePath;
        ModuleProtection = _settings.ModulePathEncrypted ? "DPAPI-encrypted (per user)" : "Not encrypted (non-Windows dev only)";

        ApplicationThemeManager.Apply(Theme == "Light" ? ApplicationTheme.Light : ApplicationTheme.Dark);
        Status = "Settings saved.";
    }
}
