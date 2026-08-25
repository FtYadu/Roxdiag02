using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Rox.App.Services;
using Rox.App.ViewModels;
using Rox.App.Views;
using Wpf.Ui.Appearance;

namespace Rox.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                // Infrastructure
                services.AddSingleton<SettingsService>();
                services.AddSingleton<DialogService>();
                services.AddSingleton<DiagnosticSession>();

                // Page view-models (concrete singletons)
                services.AddSingleton<DashboardViewModel>();
                services.AddSingleton<DiagnosticsViewModel>();
                services.AddSingleton<GuidedFlowsViewModel>();
                services.AddSingleton<KeyFunctionsViewModel>();
                services.AddSingleton<ReflashViewModel>();
                services.AddSingleton<ExpertConsoleViewModel>();
                services.AddSingleton<SettingsViewModel>();

                // Shell (fixed page order) + main window
                services.AddSingleton<ShellViewModel>(sp => new ShellViewModel(
                    sp.GetRequiredService<DiagnosticSession>(),
                    new PageViewModel[]
                    {
                        sp.GetRequiredService<DashboardViewModel>(),
                        sp.GetRequiredService<DiagnosticsViewModel>(),
                        sp.GetRequiredService<GuidedFlowsViewModel>(),
                        sp.GetRequiredService<KeyFunctionsViewModel>(),
                        sp.GetRequiredService<ReflashViewModel>(),
                        sp.GetRequiredService<ExpertConsoleViewModel>(),
                        sp.GetRequiredService<SettingsViewModel>(),
                    }));
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();

        // Apply the saved theme before showing the window.
        var settings = _host.Services.GetRequiredService<SettingsService>().Load();
        ApplicationThemeManager.Apply(settings.Theme == "Light" ? ApplicationTheme.Light : ApplicationTheme.Dark);

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.DataContext = _host.Services.GetRequiredService<ShellViewModel>();
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
