using System.IO;
using System.Text.Json;
using Rox.Security;

namespace Rox.App.Services;

/// <summary>
/// Loads/saves <see cref="AppSettings"/> as <c>settings.json</c> under <c>%AppData%\ROXDiagnostic</c>,
/// and stores the security-module path DPAPI-encrypted in a separate file (PRD §7.2, FR-07.6).
/// </summary>
public sealed class SettingsService
{
    private readonly string _dir;
    private readonly string _settingsPath;
    private readonly ProtectedValueStore _modulePathStore;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public SettingsService(string? baseDir = null)
    {
        _dir = baseDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ROXDiagnostic");
        Directory.CreateDirectory(_dir);
        _settingsPath = Path.Combine(_dir, "settings.json");
        _modulePathStore = new ProtectedValueStore(Path.Combine(_dir, "seedkey.path.dat"));
    }

    public string Directory => _dir;

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath)) ?? new AppSettings();
        }
        catch { /* corrupt file → defaults */ }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
        => File.WriteAllText(_settingsPath, JsonSerializer.Serialize(settings, JsonOpts));

    /// <summary>The DPAPI-encrypted seed-key module path (null if not configured).</summary>
    public string? SecurityModulePath
    {
        get => _modulePathStore.Load();
        set { if (string.IsNullOrWhiteSpace(value)) _modulePathStore.Clear(); else _modulePathStore.Save(value!); }
    }

    /// <summary>Whether the module path is protected by real DPAPI (true on Windows).</summary>
    public bool ModulePathEncrypted => _modulePathStore.IsRealEncryption;
}
