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

Console.WriteLine("\nDone.");
