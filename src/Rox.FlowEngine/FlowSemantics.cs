namespace Rox.FlowEngine;

/// <summary>
/// The ResponseStatus enum is an OPEN item (schema 5.6). Its accepted-set and value mapping
/// are configurable rather than hard-coded. Defaults follow the working hypothesis
/// (2 = positive, 3 = positive-with-pending-resolved, 1 = negative) with {2,3} accepted.
/// </summary>
public sealed class FlowSemantics
{
    public string PositiveStatus { get; init; } = "2";
    public string PendingStatus { get; init; } = "3";
    public string NegativeStatus { get; init; } = "1";
    public HashSet<string> AcceptedResponseStatus { get; init; } =
        new(StringComparer.OrdinalIgnoreCase) { "2", "3" };
}
