using Rox.Core;
using Rox.KeyFunctions;
using Rox.Logging;
using Rox.Security;
using Rox.Simulator;
using Rox.Transport;
using Rox.Uds;
using Xunit;

public class KeyFunctionTests
{
    private static async Task<(KeyFunctionService svc, EcuSimulator sim, InMemoryAuditSink audit, ScriptedFaultTransport fault)>
        BuildAsync(IConfirmationService? confirm = null)
    {
        var sim = new EcuSimulator();
        var fault = new ScriptedFaultTransport(new LoopbackTransport(sim, "IMMO"));
        await fault.ConnectAsync();
        var client = new UdsClient(fault, new UdsClientOptions { RetryDelay = TimeSpan.Zero, KeepAliveInterval = TimeSpan.FromSeconds(30) });
        var security = new SecurityAccessService(client, new TestSecurityProvider());
        var audit = new InMemoryAuditSink();
        var svc = new KeyFunctionService(client, security, audit,
            confirm ?? new DelegateConfirmationService(_ => true));
        return (svc, sim, audit, fault);
    }

    [Fact]
    public async Task Pairing_increments_key_count_and_writes_audit()
    {
        var (svc, sim, audit, _) = await BuildAsync();
        var result = await svc.PairAsync(new KeyFunctionConfig());

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.BeforeCount);
        Assert.Equal(3, result.AfterCount);
        Assert.True(result.CountChangedAsExpected);
        Assert.Equal(3, sim.KeyCount);

        var entry = Assert.Single(audit.Entries, e => e.Operation == "Key Pairing");
        Assert.Equal(AuditResult.Success, entry.Result);
    }

    [Fact]
    public async Task Deletion_decrements_key_count_with_readback()
    {
        var (svc, sim, audit, _) = await BuildAsync();
        var result = await svc.DeleteAsync(new KeyFunctionConfig());

        Assert.True(result.Success, result.Message);
        Assert.Equal(2, result.BeforeCount);
        Assert.Equal(1, result.AfterCount);
        Assert.Equal(1, sim.KeyCount);
        Assert.Contains(audit.Entries, e => e.Operation == "Key Deletion" && e.Result == AuditResult.Success);
    }

    [Fact]
    public async Task Duplication_uses_same_skeleton_and_increments()
    {
        var (svc, sim, _, _) = await BuildAsync();
        var result = await svc.DuplicateAsync(new KeyFunctionConfig(), duplicatePayload: null);
        Assert.True(result.Success, result.Message);
        Assert.Equal(3, sim.KeyCount);
    }

    [Fact]
    public async Task Declined_confirmation_makes_no_write_and_audits_denied()
    {
        var (svc, sim, audit, _) = await BuildAsync(new DelegateConfirmationService(_ => false));
        var result = await svc.PairAsync(new KeyFunctionConfig());

        Assert.False(result.Success);
        Assert.Equal(2, sim.KeyCount); // unchanged
        Assert.Contains(audit.Entries, e => e.Result == AuditResult.Denied);
    }

    [Fact]
    public async Task Security_lockout_is_surfaced_and_no_key_written()
    {
        var (svc, sim, audit, fault) = await BuildAsync();
        fault.InjectNrc(UdsServices.SecurityAccess, Nrc.ExceededNumberOfAttempts);

        var result = await svc.PairAsync(new KeyFunctionConfig());

        Assert.False(result.Success);
        Assert.Contains("Locked", result.Message);
        Assert.Equal(2, sim.KeyCount);
        Assert.Contains(audit.Entries, e => e.Result == AuditResult.Failure);
    }

    [Fact]
    public async Task Headless_default_denies_irreversible_key_write()
    {
        var (svc, sim, _, _) = await BuildAsync(new DenyDestructiveConfirmationService());
        var result = await svc.PairAsync(new KeyFunctionConfig());
        Assert.False(result.Success);
        Assert.Equal(2, sim.KeyCount);
    }
}
