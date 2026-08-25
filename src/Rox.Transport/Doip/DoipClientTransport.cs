using System.Net.Sockets;

namespace Rox.Transport.Doip;

/// <summary>
/// DoIP (ISO 13400) client transport over raw TCP (<see cref="System.Net.Sockets"/>), the only network
/// I/O the suite performs. Implements the mandatory routing-activation handshake (0x0005 → 0x0006)
/// before any UDS traffic; diagnostic messages are payload type 0x8001 with 0x8002/0x8003 ack/nack.
/// Works against a real vehicle's DoIP entity or the bundled <see cref="SimulatedDoipServer"/>.
/// </summary>
public sealed class DoipClientTransport : IPendingAwareTransport
{
    private readonly string _host;
    private readonly int _port;
    private readonly ushort _sourceAddress;   // tester logical address
    private readonly ushort _targetAddress;   // ECU logical address
    private readonly byte _protocolVersion;
    private TcpClient? _client;
    private NetworkStream? _stream;

    public const int DefaultPort = 13400;

    public DoipClientTransport(string host, ushort targetAddress, int port = DefaultPort,
        ushort sourceAddress = 0x0E80, byte protocolVersion = 0x02)
    {
        _host = host;
        _port = port;
        _sourceAddress = sourceAddress;
        _targetAddress = targetAddress;
        _protocolVersion = protocolVersion;
    }

    public string Name => $"DoIP {_host}:{_port} (tester 0x{_sourceAddress:X4} → ECU 0x{_targetAddress:X4})";
    public bool IsConnected { get; private set; }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _client = new TcpClient { NoDelay = true };
        await _client.ConnectAsync(_host, _port, ct).ConfigureAwait(false);
        _stream = _client.GetStream();

        // Mandatory routing activation before any UDS.
        await DoipStream.WriteMessageAsync(_stream,
            DoipMessage.RoutingActivationRequest(_sourceAddress, version: _protocolVersion), ct).ConfigureAwait(false);

        var resp = await DoipStream.ReadMessageAsync(_stream, ct).ConfigureAwait(false)
                   ?? throw new DoipException("No routing-activation response.");
        if (resp.PayloadType != DoipPayloadTypes.RoutingActivationResponse)
            throw new DoipException($"Expected routing-activation response, got payload type 0x{resp.PayloadType:X4}.");
        byte code = resp.Payload.Length >= 5 ? resp.Payload[4] : (byte)0xFF;
        if (code != DoipRoutingResponse.Success)
            throw new DoipException($"Routing activation denied (code 0x{code:X2}).");

        IsConnected = true;
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        IsConnected = false;
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null; _client = null;
        return Task.CompletedTask;
    }

    public async Task<byte[]> SendAsync(byte[] request, CancellationToken ct = default)
    {
        if (!IsConnected || _stream is null) throw new InvalidOperationException("DoIP transport not connected.");

        await DoipStream.WriteMessageAsync(_stream,
            DoipMessage.Diagnostic(_sourceAddress, _targetAddress, request, _protocolVersion), ct).ConfigureAwait(false);
        return await ReadDiagnosticAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Read the next diagnostic response without re-sending (used for 0x78 pending continuation).</summary>
    public Task<byte[]> ReceiveNextAsync(CancellationToken ct = default)
    {
        if (!IsConnected || _stream is null) throw new InvalidOperationException("DoIP transport not connected.");
        return ReadDiagnosticAsync(ct);
    }

    private async Task<byte[]> ReadDiagnosticAsync(CancellationToken ct)
    {
        // The entity sends a 0x8002 ack (or 0x8003 nack) then the diagnostic response (0x8001).
        while (true)
        {
            var msg = await DoipStream.ReadMessageAsync(_stream!, ct).ConfigureAwait(false)
                      ?? throw new DoipException("Connection closed awaiting diagnostic response.");
            switch (msg.PayloadType)
            {
                case DoipPayloadTypes.DiagnosticMessagePositiveAck:
                    continue; // ack — keep waiting for the actual UDS response
                case DoipPayloadTypes.DiagnosticMessageNegativeAck:
                    throw new DoipException($"DoIP diagnostic negative ack (code 0x{(msg.Payload.Length >= 5 ? msg.Payload[4] : 0xFF):X2}).");
                case DoipPayloadTypes.DiagnosticMessage:
                    return msg.DiagnosticUds();
                case DoipPayloadTypes.AliveCheckRequest:
                    await DoipStream.WriteMessageAsync(_stream!,
                        new DoipMessage { ProtocolVersion = _protocolVersion, PayloadType = DoipPayloadTypes.AliveCheckResponse, Payload = new byte[] { (byte)(_sourceAddress >> 8), (byte)_sourceAddress } }, ct).ConfigureAwait(false);
                    continue;
                default:
                    continue; // ignore unrelated announcements
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }
}
