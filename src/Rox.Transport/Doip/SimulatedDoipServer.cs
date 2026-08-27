using System.Net;
using System.Net.Sockets;
using Rox.FlowEngine;

namespace Rox.Transport.Doip;

/// <summary>
/// A bundled DoIP entity for testing the DoIP path over a real TCP socket with no vehicle: it speaks
/// ISO 13400 (routing activation + diagnostic messages with ack) and delegates UDS to an
/// <see cref="IEcuServiceExecutor"/> (the simulator). Binds to loopback by default.
/// </summary>
public sealed class SimulatedDoipServer : IAsyncDisposable
{
    private readonly IEcuServiceExecutor _executor;
    private readonly ushort _entityAddress;
    private readonly byte _protocolVersion;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;

    public SimulatedDoipServer(IEcuServiceExecutor executor, ushort entityAddress = 0x1000, byte protocolVersion = 0x02)
    {
        _executor = executor;
        _entityAddress = entityAddress;
        _protocolVersion = protocolVersion;
    }

    public int Port { get; private set; }

    /// <summary>Start listening. Port 0 lets the OS pick a free port (returned in <see cref="Port"/>).</summary>
    public Task StartAsync(int port = 0, IPAddress? bind = null)
    {
        _listener = new TcpListener(bind ?? IPAddress.Loopback, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _cts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, ct), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            ushort testerAddress = 0x0E80;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var msg = await DoipStream.ReadMessageAsync(stream, ct).ConfigureAwait(false);
                    if (msg is null) break;

                    switch (msg.PayloadType)
                    {
                        case DoipPayloadTypes.RoutingActivationRequest:
                            if (msg.Payload.Length >= 2)
                                testerAddress = (ushort)((msg.Payload[0] << 8) | msg.Payload[1]);
                            await DoipStream.WriteMessageAsync(stream,
                                DoipMessage.RoutingActivationResponse(testerAddress, _entityAddress, DoipRoutingResponse.Success, _protocolVersion), ct).ConfigureAwait(false);
                            break;

                        case DoipPayloadTypes.DiagnosticMessage:
                        {
                            ushort source = msg.Payload.Length >= 2 ? (ushort)((msg.Payload[0] << 8) | msg.Payload[1]) : testerAddress;
                            ushort target = msg.Payload.Length >= 4 ? (ushort)((msg.Payload[2] << 8) | msg.Payload[3]) : _entityAddress;
                            // Positive ack, then compute and return the UDS response.
                            await DoipStream.WriteMessageAsync(stream,
                                DoipMessage.DiagnosticAck(_entityAddress, source, 0x00, positive: true, _protocolVersion), ct).ConfigureAwait(false);
                            var uds = _executor.Execute($"0x{target:X4}", msg.DiagnosticUds());
                            await DoipStream.WriteMessageAsync(stream,
                                DoipMessage.Diagnostic(_entityAddress, source, uds, _protocolVersion), ct).ConfigureAwait(false);
                            break;
                        }

                        case DoipPayloadTypes.AliveCheckRequest:
                            await DoipStream.WriteMessageAsync(stream,
                                new DoipMessage { ProtocolVersion = _protocolVersion, PayloadType = DoipPayloadTypes.AliveCheckResponse, Payload = new byte[] { (byte)(_entityAddress >> 8), (byte)_entityAddress } }, ct).ConfigureAwait(false);
                            break;

                        default:
                            break; // ignore
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (DoipException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();
        try { if (_acceptLoop is not null) await _acceptLoop.ConfigureAwait(false); } catch { }
        _cts?.Dispose();
    }
}
