namespace Rox.Transport;

/// <summary>
/// The single boundary the UDS client and flow engine talk to. It hides whether the underlying
/// link is CAN (ISO-TP over a vendor adapter) or DoIP (routing-activated TCP sockets): the caller
/// hands over a complete UDS request PDU and gets back the complete UDS response PDU.
/// </summary>
public interface ITransport : IAsyncDisposable
{
    /// <summary>Human-readable adapter name (e.g. "Simulated loopback", "PCAN-USB / ISO-TP", "DoIP").</summary>
    string Name { get; }

    bool IsConnected { get; }

    /// <summary>Open the link (CAN channel or DoIP TCP + routing activation). Idempotent.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Close the link.</summary>
    Task DisconnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Send one UDS request PDU and await the corresponding response PDU. Segmentation
    /// (ISO-TP) or DoIP framing is handled below this method; the bytes in and out are raw UDS.
    /// A negative response (7F ...) is returned as data, not thrown — the UDS client decides.
    /// </summary>
    Task<byte[]> SendAsync(byte[] request, CancellationToken ct = default);
}
