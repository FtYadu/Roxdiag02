namespace Rox.Transport;

/// <summary>
/// A transport that can deliver a follow-up response frame without re-sending the request. This is how
/// UDS response-pending (NRC 0x78) is handled correctly: the ECU emits <c>7F sid 78</c> and then, after
/// it finishes, sends the real response as a second message on the same link — the tester must keep
/// reading, not re-send. DoIP supports this natively; transports that don't implement it fall back to
/// re-sending the last request (acceptable for the simulator, which never stalls).
/// </summary>
public interface IPendingAwareTransport : ITransport
{
    Task<byte[]> ReceiveNextAsync(CancellationToken ct = default);
}
