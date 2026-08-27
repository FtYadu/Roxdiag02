using System.Windows;
using Rox.FlowEngine;
using Rox.Logging;

namespace Rox.App.Services;

/// <summary>
/// WPF implementation of the confirmation + operator-prompt boundaries. Confirmations for irreversible
/// ops (key delete, VIN/config write, reflash) present the acknowledgements and require an explicit Yes
/// (NON-NEGOTIABLE #3). Runs on the UI thread regardless of the calling thread.
/// </summary>
public sealed class DialogService : IConfirmationService, IUserPrompt
{
    public Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken ct = default)
    {
        return OnUiAsync(() =>
        {
            var header = request.Risk switch
            {
                OperationRisk.BrickingRisk => "⚠ BRICKING RISK",
                OperationRisk.Irreversible => "⚠ Irreversible operation",
                OperationRisk.Elevated => "Confirm operation",
                _ => "Confirm"
            };
            var body = $"{header}\n\n{request.Operation}{(request.Ecu is null ? "" : $"  ·  {request.Ecu}")}\n\n{request.Message}\n\n"
                     + string.Join("\n", request.Acknowledgements.Select(a => $"  • {a}"))
                     + "\n\nProceed?";
            var result = MessageBox.Show(body, "ROX Diagnostic — confirmation",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            return result == MessageBoxResult.Yes;
        });
    }

    public bool Prompt(string message, int timeoutSeconds)
    {
        return OnUiAsync(() =>
        {
            var suffix = timeoutSeconds > 0 ? $"\n\n(Complete within {timeoutSeconds}s.)" : "";
            var result = MessageBox.Show(message + suffix, "ROX Diagnostic — operator action",
                MessageBoxButton.OKCancel, MessageBoxImage.Information, MessageBoxResult.OK);
            return result == MessageBoxResult.OK;
        }).GetAwaiter().GetResult();
    }

    public static void ShowError(string message, string title = "ROX Diagnostic — error")
        => Application.Current.Dispatcher.Invoke(() => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error));

    public static void ShowInfo(string message, string title = "ROX Diagnostic")
        => Application.Current.Dispatcher.Invoke(() => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information));

    private static Task<bool> OnUiAsync(Func<bool> action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            return Task.FromResult(action());
        return Task.FromResult(dispatcher.Invoke(action));
    }
}
