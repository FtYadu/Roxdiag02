namespace Rox.Logging;

public enum OperationRisk { Low, Elevated, Irreversible, BrickingRisk }

/// <summary>
/// A request to confirm an irreversible / safety-critical operation (key delete, VIN write, config-word
/// write, reflash). The suite requires multi-step confirmation (NON-NEGOTIABLE #3) before any such write.
/// </summary>
public sealed record ConfirmationRequest(
    string Operation,
    string? Ecu,
    OperationRisk Risk,
    string Message,
    IReadOnlyList<string> Acknowledgements)
{
    public bool RequiresExplicitSteps => Risk is OperationRisk.Irreversible or OperationRisk.BrickingRisk;
}

/// <summary>UI/host-provided confirmation. Implementations must genuinely block on operator consent.</summary>
public interface IConfirmationService
{
    Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken ct = default);
}

/// <summary>Denies every irreversible op by default — the safe headless default (nothing destructive runs unattended).</summary>
public sealed class DenyDestructiveConfirmationService : IConfirmationService
{
    public Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken ct = default)
        => Task.FromResult(!request.RequiresExplicitSteps);
}

/// <summary>Confirms via a supplied callback (wired to the WPF confirmation dialog, or auto-yes in tests).</summary>
public sealed class DelegateConfirmationService : IConfirmationService
{
    private readonly Func<ConfirmationRequest, CancellationToken, Task<bool>> _confirm;
    public DelegateConfirmationService(Func<ConfirmationRequest, CancellationToken, Task<bool>> confirm) => _confirm = confirm;
    public DelegateConfirmationService(Func<ConfirmationRequest, bool> confirm)
        : this((r, _) => Task.FromResult(confirm(r))) { }
    public Task<bool> ConfirmAsync(ConfirmationRequest request, CancellationToken ct = default) => _confirm(request, ct);
}
