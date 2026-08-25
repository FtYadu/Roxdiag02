namespace Rox.FlowEngine;

/// <summary>Executes a UDS request against a target (real transport or the simulator).</summary>
public interface IEcuServiceExecutor { byte[] Execute(string? ecu, byte[] request); }

/// <summary>User-supplied seed-key module boundary. NO OEM algorithm lives in the app.</summary>
public interface ISecurityProvider { byte[] ComputeKey(byte[] seed, int length); }

/// <summary>Blocking operator prompt (insert key, cycle ignition, ...).</summary>
public interface IUserPrompt { bool Prompt(string message, int timeoutSeconds); }

public sealed class AutoContinuePrompt : IUserPrompt
{
    public bool Prompt(string message, int timeoutSeconds) => true;
}

public sealed class FlowException : Exception
{
    public FlowException(string message) : base(message) { }
}
