using Rox.Core;
using Rox.Logging;
using Rox.Reflash;
using Rox.Security;
using Rox.Simulator;
using Rox.Transport;
using Rox.Transport.Can;
using Rox.Uds;
using Xunit;

public class ReflashTests
{
    private static async Task<(ReflashService svc, EcuSimulator sim, UdsClient client, InMemoryAuditSink audit, ScriptedFaultTransport fault, SimulatedVoltageProvider volts)>
        BuildAsync(double voltage = 12.6, IConfirmationService? confirm = null)
    {
        var sim = new EcuSimulator();
        var fault = new ScriptedFaultTransport(new LoopbackTransport(sim, "EMS"));
        await fault.ConnectAsync();
        var client = new UdsClient(fault, new UdsClientOptions { RetryDelay = TimeSpan.Zero, KeepAliveInterval = TimeSpan.FromSeconds(30) });
        var security = new SecurityAccessService(client, new TestSecurityProvider());
        var audit = new InMemoryAuditSink();
        var v = new SimulatedVoltageProvider(voltage);
        var svc = new ReflashService(client, security, audit, confirm ?? new DelegateConfirmationService(_ => true), v);
        return (svc, sim, client, audit, fault, v);
    }

    private static byte[] Firmware(int n)
    {
        var f = new byte[n];
        for (int i = 0; i < n; i++) f[i] = (byte)(i * 7 + 1);
        return f;
    }

    [Fact]
    public async Task Reflash_succeeds_and_verifies_checksum()
    {
        var (svc, sim, _, audit, _, _) = await BuildAsync();
        var fw = Firmware(1000);

        var result = await svc.ReflashAsync(new ReflashConfig(), fw);

        Assert.True(result.Success, result.Message);
        Assert.Equal(ReflashService.Sum32(fw), result.Checksum);
        Assert.Equal(ReflashService.Sum32(fw), sim.FlashChecksum); // sim received exactly the firmware
        Assert.Contains(audit.Entries, e => e.Operation == "MCU Reflash" && e.Result == AuditResult.Success);
    }

    [Fact]
    public async Task Block_size_comes_from_0x34_response_not_a_fixed_2kb()
    {
        var (svc, _, client, _, _, _) = await BuildAsync();
        int transferCount = 0;
        client.Traced += e => { if (e.Request.Length >= 1 && e.Request[0] == UdsServices.TransferData) Interlocked.Increment(ref transferCount); };

        // maxNumberOfBlockLength=0x0102 => 256 data bytes/block. 300 bytes => exactly 2 blocks.
        await svc.ReflashAsync(new ReflashConfig(), Firmware(300));

        Assert.Equal(2, transferCount);
    }

    [Fact]
    public async Task Block_sequence_counter_wraps_ff_to_00()
    {
        var (svc, sim, _, _, _, _) = await BuildAsync();
        // 256 * 258 bytes => 258 blocks, so BSC runs 1..255,0,1,2 — exercising the wrap.
        var fw = Firmware(256 * 258);
        var result = await svc.ReflashAsync(new ReflashConfig(), fw);

        Assert.True(result.Success, result.Message);
        Assert.Equal(ReflashService.Sum32(fw), sim.FlashChecksum);
    }

    [Fact]
    public async Task Voltage_below_threshold_blocks_reflash()
    {
        var (svc, sim, _, audit, _, _) = await BuildAsync(voltage: 11.2);
        var result = await svc.ReflashAsync(new ReflashConfig(), Firmware(500));

        Assert.False(result.Success);
        Assert.Equal(ReflashStage.VoltageCheck, result.LastStage);
        Assert.Equal(0u, sim.FlashChecksum); // nothing transferred
        Assert.Contains(audit.Entries, e => e.Result == AuditResult.Denied);
    }

    [Fact]
    public async Task Declined_confirmation_aborts_before_any_write()
    {
        var (svc, sim, _, _, _, _) = await BuildAsync(confirm: new DelegateConfirmationService(_ => false));
        var result = await svc.ReflashAsync(new ReflashConfig(), Firmware(500));

        Assert.False(result.Success);
        Assert.Equal(ReflashStage.Confirm, result.LastStage);
        Assert.Equal(0u, sim.FlashChecksum);
    }

    [Fact]
    public async Task Pending_0x78_during_transfer_is_handled()
    {
        var (svc, sim, _, _, fault, _) = await BuildAsync();
        fault.InjectPending(UdsServices.TransferData, count: 2); // first transfer gets two 0x78 then completes
        var fw = Firmware(600);

        var result = await svc.ReflashAsync(new ReflashConfig(), fw);

        Assert.True(result.Success, result.Message);
        Assert.Equal(ReflashService.Sum32(fw), sim.FlashChecksum);
    }

    [Fact]
    public async Task Wrong_block_sequence_0x73_aborts_transfer()
    {
        var (svc, _, _, audit, fault, _) = await BuildAsync();
        fault.InjectNrc(UdsServices.TransferData, Nrc.WrongBlockSequenceCounter);

        var result = await svc.ReflashAsync(new ReflashConfig(), Firmware(600));

        Assert.False(result.Success);
        Assert.Equal(ReflashStage.Transfer, result.LastStage);
        Assert.Equal(Nrc.WrongBlockSequenceCounter, result.Nrc);
        Assert.Contains(audit.Entries, e => e.Result == AuditResult.Failure);
    }

    [Fact]
    public async Task Reflash_runs_over_isotp_transport_with_framing()
    {
        // End-to-end with real ISO-TP segmentation of each 0x36 block.
        var sim = new EcuSimulator();
        await using var transport = new IsoTpLoopbackTransport(sim, "EMS");
        await transport.ConnectAsync();
        var client = new UdsClient(transport, new UdsClientOptions { RetryDelay = TimeSpan.Zero, KeepAliveInterval = TimeSpan.FromSeconds(30) });
        var security = new SecurityAccessService(client, new TestSecurityProvider());
        var svc = new ReflashService(client, security, new InMemoryAuditSink(), new DelegateConfirmationService(_ => true));

        var fw = Firmware(1024);
        var result = await svc.ReflashAsync(new ReflashConfig(), fw);

        Assert.True(result.Success, result.Message);
        Assert.Equal(ReflashService.Sum32(fw), sim.FlashChecksum);
    }
}
