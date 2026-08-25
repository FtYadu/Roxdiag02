using Rox.Diagnostics;
using Rox.Simulator;
using Rox.Transport;
using Rox.Uds;
using Xunit;

public class DiagnosticsTests
{
    private static async Task<DtcService> ServiceAsync(EcuSimulator sim)
    {
        var transport = new LoopbackTransport(sim, "EMS");
        await transport.ConnectAsync();
        var client = new UdsClient(transport, new UdsClientOptions { RetryDelay = TimeSpan.Zero });
        return new DtcService(client, new DefaultDtcDescriptionProvider(new Dictionary<string, string>
        {
            ["P0301"] = "Cylinder 1 misfire detected"
        }));
    }

    [Fact]
    public async Task Read_decodes_dtcs_with_descriptions()
    {
        var svc = await ServiceAsync(new EcuSimulator());
        var result = await svc.ReadAsync("EMS");

        Assert.False(result.Failed);
        Assert.Equal(2, result.Dtcs.Count);
        Assert.Equal("P0301", result.Dtcs[0].Code);
        Assert.Equal("Cylinder 1 misfire detected", result.Dtcs[0].Description);
        Assert.True(result.Dtcs[0].Dtc.Confirmed);
    }

    [Fact]
    public async Task Read_clear_readback_flags_live_fault_and_cleared_stale()
    {
        // Simulator: after clear, P0301 re-sets immediately (live); the pending code clears (stale).
        var svc = await ServiceAsync(new EcuSimulator());
        var result = await svc.ReadClearReadBackAsync("EMS");

        Assert.True(result.ClearAccepted);
        Assert.Equal(1, result.LiveRemainingCount);
        Assert.Equal("P0301", result.LiveRemaining[0].Code);
        Assert.True(result.LiveRemaining[0].IsLiveFault);
        Assert.Equal(1, result.StaleClearedCount); // the pending code was genuinely cleared
        Assert.Contains("1 stale cleared, 1 live remaining", result.Summary);
    }

    [Fact]
    public async Task Clear_rejection_is_surfaced_with_nrc()
    {
        var sim = new EcuSimulator();
        var fault = new ScriptedFaultTransport(new LoopbackTransport(sim, "EMS"));
        await fault.ConnectAsync();
        fault.InjectNrc(Rox.Core.UdsServices.ClearDiagnosticInformation, Rox.Core.Nrc.SecurityAccessDenied);
        var client = new UdsClient(fault, new UdsClientOptions { RetryDelay = TimeSpan.Zero });
        var svc = new DtcService(client);

        var result = await svc.ReadClearReadBackAsync("EMS", enterExtendedSession: false);

        Assert.False(result.ClearAccepted);
        Assert.Contains("Clear rejected", result.Summary);
        Assert.Contains("0x33", result.Summary);
    }

    [Fact]
    public async Task All_ecu_scan_aggregates()
    {
        var svc = await ServiceAsync(new EcuSimulator());
        var scan = await svc.ScanAllAsync(new[] { "EMS", "IMMO", "ADCU_MCU" });

        Assert.Equal(3, scan.PerEcu.Count);
        Assert.Equal(6, scan.TotalDtcs); // 2 per ECU from the simulator
    }
}
