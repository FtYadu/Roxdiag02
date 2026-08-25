using Rox.Core;
using Rox.Profile;
using Rox.FlowEngine;
using Rox.Simulator;

Console.WriteLine("=== ROX Offline Diagnostic Suite - core demo (simulator target) ===\n");
string dataDir = Path.Combine(AppContext.BaseDirectory, "data");

// 1) Vehicle profile
var profile = ProfileParser.Parse(File.ReadAllText(Path.Combine(dataDir, "R11_Oversea.sample.xml")));
Console.WriteLine($"Vehicle profile: {profile.Name}");
foreach (var b in profile.Buses)
    Console.WriteLine(b.Kind == BusKind.Can
        ? $"  CAN  '{b.Name}'  {b.Baudrate} bps  CAN-H pin {b.HPin}, CAN-L pin {b.LPin}"
        : $"  DoIP '{b.Name}'  {b.IpVersion}  activation pin {b.APin} (RX {b.RHPin}/{b.RLPin}, TX {b.THPin}/{b.TLPin})");

// 2) DTC read -> clear -> read-back (live-fault detection)
var sim = new EcuSimulator();
Console.WriteLine("\n-- DTC scan (EMS) --");
foreach (var d in DtcDecoder.ParseReadDtcByStatusMask(sim.Execute("EMS", new byte[] { 0x19, 0x02, 0xFF })))
    Console.WriteLine($"  {d.Code}  [{d.StatusText}]  raw={d.RawHex}");
sim.Execute("EMS", new byte[] { 0x14, 0xFF, 0xFF, 0xFF });
var readback = DtcDecoder.ParseReadDtcByStatusMask(sim.Execute("EMS", new byte[] { 0x19, 0x02, 0xFF }));
Console.WriteLine("  cleared, re-read:");
if (readback.Count == 0) Console.WriteLine("    all cleared");
foreach (var d in readback) Console.WriteLine($"    {d.Code} persists -> LIVE FAULT (active)");

// 3) Guided flow: Add Key
Console.WriteLine("\n-- Guided flow: Add Key --");
var flow = FlowParser.Parse(File.ReadAllText(Path.Combine(dataDir, "add_key.sample.xml")));
var interp = new FlowInterpreter(sim, new TestSecurityProvider());
interp.Run(flow.Processes[0]);
foreach (var line in interp.Log) Console.WriteLine("  " + line);
interp.Variables.TryGet("keyCountBefore", out var before);
interp.Variables.TryGet("keyCountAfter", out var after);
Console.WriteLine($"  => key count {before} -> {after}  (simulator now holds {sim.KeyCount} keys)");

// 4) Transport layer: ISO-TP multi-frame + DoIP over a real socket
Console.WriteLine("\n-- Transport: ISO-TP + DoIP (against the simulator) --");
await using (var isotp = new Rox.Transport.Can.IsoTpLoopbackTransport(new EcuSimulator(), "EMS"))
{
    await isotp.ConnectAsync();
    var dtcResp = await isotp.SendAsync(new byte[] { 0x19, 0x02, 0xFF });
    Console.WriteLine($"  ISO-TP DTC read ({dtcResp.Length} B, multi-frame): {Convert.ToHexString(dtcResp)}");
}
await using (var doipServer = new Rox.Transport.Doip.SimulatedDoipServer(new EcuSimulator()))
{
    await doipServer.StartAsync();
    await using var doip = new Rox.Transport.Doip.DoipClientTransport("127.0.0.1", 0x1000, doipServer.Port);
    await doip.ConnectAsync();
    var vin = await doip.SendAsync(new byte[] { 0x22, 0xF1, 0x8C });
    Console.WriteLine($"  DoIP routing activated on port {doipServer.Port}; key-count DID -> {Convert.ToHexString(vin)}");
}

// 5) Stateful UDS client with 0x78 pending handling
Console.WriteLine("\n-- UDS client: response-pending (0x78) handling --");
var pendingSim = new EcuSimulator();
var fault = new Rox.Transport.ScriptedFaultTransport(new Rox.Transport.LoopbackTransport(pendingSim, "EMS"));
await fault.ConnectAsync();
fault.InjectPending(Rox.Core.UdsServices.ReadDataByIdentifier, 3);
var udsClient = new Rox.Uds.UdsClient(fault, new Rox.Uds.UdsClientOptions { RetryDelay = TimeSpan.Zero });
var pendResp = await udsClient.ReadDataByIdentifierAsync(0xF18C);
Console.WriteLine($"  After 3× 0x78 pending, resolved to positive: {pendResp.IsPositive} (key count {pendResp.Raw[^1]})");

// 6) Key pairing via the guarded, audited service
Console.WriteLine("\n-- Key pairing (guarded + audited) --");
var keySim = new EcuSimulator();
var keyTransport = new Rox.Transport.LoopbackTransport(keySim, "IMMO");
await keyTransport.ConnectAsync();
var keyUds = new Rox.Uds.UdsClient(keyTransport, new Rox.Uds.UdsClientOptions { RetryDelay = TimeSpan.Zero, KeepAliveInterval = TimeSpan.FromSeconds(30) });
var security = new Rox.Security.SecurityAccessService(keyUds, new TestSecurityProvider());
var audit = new Rox.Logging.InMemoryAuditSink();
var keys = new Rox.KeyFunctions.KeyFunctionService(keyUds, security, audit,
    new Rox.Logging.DelegateConfirmationService(_ => true));
var pairResult = await keys.PairAsync(new Rox.KeyFunctions.KeyFunctionConfig());
Console.WriteLine($"  {pairResult.Message}");

// 7) Simulated MCU reflash with checksum verify
Console.WriteLine("\n-- MCU reflash (simulated, checksum verified) --");
var flashSim = new EcuSimulator();
var flashTransport = new Rox.Transport.LoopbackTransport(flashSim, "EMS");
await flashTransport.ConnectAsync();
var flashUds = new Rox.Uds.UdsClient(flashTransport, new Rox.Uds.UdsClientOptions { RetryDelay = TimeSpan.Zero, KeepAliveInterval = TimeSpan.FromSeconds(30) });
var flashSec = new Rox.Security.SecurityAccessService(flashUds, new TestSecurityProvider());
var reflash = new Rox.Reflash.ReflashService(flashUds, flashSec, audit,
    new Rox.Logging.DelegateConfirmationService(_ => true), new Rox.Reflash.SimulatedVoltageProvider(12.6));
var firmware = new byte[4096];
for (int i = 0; i < firmware.Length; i++) firmware[i] = (byte)(i * 5 + 3);
var progress = new Progress<Rox.Reflash.ReflashProgress>(p => { });
var flashResult = await reflash.ReflashAsync(new Rox.Reflash.ReflashConfig(), firmware, progress);
Console.WriteLine($"  {flashResult.Message} (checksum 0x{flashResult.Checksum:X8}; sim received sum 0x{flashSim.FlashChecksum:X8})");

// 8) Audit trail + PDF session report
Console.WriteLine("\n-- Audit trail --");
foreach (var e in audit.Entries) Console.WriteLine("  " + e.ToLine());

var report = new Rox.Logging.Reporting.SessionReport
{
    VehicleProfile = profile.Name,
    TransportSummary = "Simulated loopback",
    Audit = audit.Entries.ToList(),
    Reflashes = { new Rox.Logging.Reporting.ReportReflash("EMS", firmware.Length, $"0x{flashResult.Checksum:X8}", flashResult.Success) }
};
var pdfPath = Path.Combine(Path.GetTempPath(), "rox-demo-report.pdf");
Rox.Logging.Reporting.PdfReportGenerator.Generate(report, pdfPath);
Console.WriteLine($"\nSession report written to {pdfPath}");

Console.WriteLine("\nDone.");
