using Rox.Core;
using Rox.FlowEngine;
using Rox.Simulator;
using Rox.Transport;
using Rox.Transport.Can;
using Rox.Transport.Doip;
using Xunit;

public class TransportTests
{
    // ISO-TP must segment and reassemble a PDU larger than a single 8-byte CAN frame.
    [Fact]
    public async Task IsoTp_round_trips_a_multi_frame_pdu()
    {
        var (tester, ecu) = LoopbackCanChannel.CreatePair();
        await tester.OpenAsync();
        await ecu.OpenAsync();
        var testerIso = new IsoTpChannel(tester, txId: 0x7E0, rxId: 0x7E8);
        var ecuIso = new IsoTpChannel(ecu, txId: 0x7E8, rxId: 0x7E0);

        // 40-byte payload => First Frame + several Consecutive Frames + a Flow Control.
        var pdu = new byte[40];
        for (int i = 0; i < pdu.Length; i++) pdu[i] = (byte)(i + 1);

        var receive = ecuIso.ReceivePduAsync();
        await testerIso.SendPduAsync(pdu);
        var got = await receive;

        Assert.Equal(pdu, got);
    }

    // A UDS response longer than 8 bytes (the DTC list) exercises reassembly through IsoTpLoopbackTransport.
    [Fact]
    public async Task IsoTp_loopback_transport_reads_multi_frame_dtc_response()
    {
        await using var transport = new IsoTpLoopbackTransport(new EcuSimulator(), "EMS");
        await transport.ConnectAsync();

        var response = await transport.SendAsync(new byte[] { 0x19, 0x02, 0xFF });
        var dtcs = DtcDecoder.ParseReadDtcByStatusMask(response);

        Assert.True(response.Length > 8, "DTC response should require ISO-TP multi-frame.");
        Assert.Equal(2, dtcs.Count);
        Assert.Equal("P0301", dtcs[0].Code);
    }

    // The existing Add-Key flow must run over a real transport (executor adapter), not just in-proc.
    [Fact]
    public async Task AddKey_flow_runs_over_loopback_transport()
    {
        var sim = new EcuSimulator();
        await using var transport = new LoopbackTransport(sim, "IMMO");
        await transport.ConnectAsync();
        var executor = new TransportEcuServiceExecutor(transport);

        var interp = new FlowInterpreter(executor, new TestSecurityProvider());
        interp.Run(FlowParser.Parse(AddKeyFlowXml).Processes[0]);

        Assert.True(sim.SecurityGranted);
        Assert.Equal(3, sim.KeyCount);
    }

    // Same flow over ISO-TP transport: proves the flow engine is transport-agnostic through framing.
    [Fact]
    public async Task AddKey_flow_runs_over_isotp_transport()
    {
        var sim = new EcuSimulator();
        await using var transport = new IsoTpLoopbackTransport(sim, "IMMO");
        await transport.ConnectAsync();
        var executor = new TransportEcuServiceExecutor(transport);

        var interp = new FlowInterpreter(executor, new TestSecurityProvider());
        interp.Run(FlowParser.Parse(AddKeyFlowXml).Processes[0]);

        Assert.Equal(3, sim.KeyCount);
    }

    // DoIP over a real TCP socket: routing activation handshake + diagnostic message round-trip.
    [Fact]
    public async Task Doip_routing_activation_and_diagnostic_round_trip()
    {
        var sim = new EcuSimulator();
        await using var server = new SimulatedDoipServer(sim);
        await server.StartAsync();

        await using var client = new DoipClientTransport("127.0.0.1", targetAddress: 0x1000, port: server.Port);
        await client.ConnectAsync();  // performs 0x0005 -> 0x0006
        Assert.True(client.IsConnected);

        var response = await client.SendAsync(new byte[] { 0x22, 0xF1, 0x8C }); // read key count DID
        Assert.Equal(0x62, response[0]);
        Assert.Equal(0xF1, response[1]);
        Assert.Equal(0x8C, response[2]);
        Assert.Equal(2, response[3]); // simulator starts at 2 keys
    }

    // DoIP transport must reject when routing activation is refused.
    [Fact]
    public async Task Doip_connect_fails_without_server()
    {
        await using var client = new DoipClientTransport("127.0.0.1", targetAddress: 0x1000, port: 1); // nothing listening
        await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync(new CancellationTokenSource(2000).Token));
    }

    private const string AddKeyFlowXml = @"<FlowConfiguration><Processes><Process Name='Add Key'>
      <ChildStep><EcuService Ecu='IMMO'><Request Service='0x10' Sub='0x03'/>
        <ChildStep><If Description='null'><Condition><OneCondition>
          <LeftValue IsVariable='true' Value='ResponseStatus'/><OpSign Value='Equal'/>
          <RightValue IsVariable='false' Value='2'/><ConnectSign Value='OR'/>
        </OneCondition></Condition></If></ChildStep></EcuService></ChildStep>
      <SecurityAccess Ecu='IMMO' SeedSub='0x01' KeySub='0x02'/>
      <EcuService Ecu='IMMO'><Request Service='0x22'><Data IsVariable='false' Value='F18C'/></Request>
        <ChildStep><Assign Var='before' From='response[3..4]'/></ChildStep></EcuService>
      <EcuService Ecu='IMMO'><Request Service='0x31' Sub='0x01'><Data IsVariable='false' Value='0201'/></Request>
        <ChildStep><If Description='null'><Condition><OneCondition><LeftValue IsVariable='true' Value='ResponseStatus'/><OpSign Value='Equal'/><RightValue IsVariable='false' Value='2'/><ConnectSign Value='OR'/></OneCondition></Condition></If></ChildStep></EcuService>
      <EcuService Ecu='IMMO'><Request Service='0x22'><Data IsVariable='false' Value='F18C'/></Request>
        <ChildStep><Assign Var='after' From='response[3..4]'/></ChildStep></EcuService>
    </Process></Processes></FlowConfiguration>";
}
