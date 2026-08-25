using Rox.Core;
using Rox.Simulator;
using Rox.Transport;
using Rox.Uds;
using Xunit;

public class UdsClientTests
{
    private static async Task<(UdsClient client, EcuSimulator sim, ScriptedFaultTransport fault)> ConnectedAsync()
    {
        var sim = new EcuSimulator();
        var fault = new ScriptedFaultTransport(new LoopbackTransport(sim, "EMS"));
        await fault.ConnectAsync();
        var client = new UdsClient(fault, new UdsClientOptions { RetryDelay = TimeSpan.Zero, KeepAliveInterval = TimeSpan.FromMilliseconds(20) });
        return (client, sim, fault);
    }

    [Fact]
    public async Task Positive_request_returns_positive_response()
    {
        var (client, _, _) = await ConnectedAsync();
        var r = await client.ReadDataByIdentifierAsync(0xF18C);
        Assert.True(r.IsPositive);
        Assert.Equal(0x62, r.ResponseSid);
        Assert.Equal(2, r.Raw[^1]); // key count
    }

    [Fact]
    public async Task Pending_0x78_is_polled_until_resolved_not_treated_as_failure()
    {
        var (client, _, fault) = await ConnectedAsync();
        fault.InjectPending(UdsServices.ReadDataByIdentifier, count: 3); // three 0x78 then the real answer

        var r = await client.ReadDataByIdentifierAsync(0xF18C);

        Assert.True(r.IsPositive);
        Assert.Equal(2, r.Raw[^1]);
    }

    [Fact]
    public async Task Security_denied_0x33_is_returned_as_terminal_negative()
    {
        var (client, _, fault) = await ConnectedAsync();
        fault.InjectNrc(UdsServices.RoutineControl, Nrc.SecurityAccessDenied);

        var r = await client.RoutineControlAsync(0x01, 0x0201);

        Assert.True(r.IsNegative);
        Assert.Equal(Nrc.SecurityAccessDenied, r.Nrc);
        Assert.Equal(NrcAction.Reauthenticate, Nrc.RecommendedAction(r.Nrc));
    }

    [Fact]
    public async Task Lockout_0x36_is_surfaced_immediately_without_hammering()
    {
        var (client, _, fault) = await ConnectedAsync();
        // Queue a single lockout; if the client retried it would exhaust the queue and pass — it must not.
        fault.InjectNrc(UdsServices.SecurityAccess, Nrc.ExceededNumberOfAttempts);

        var r = await client.SecurityRequestSeedAsync(0x01);

        Assert.True(r.IsNegative);
        Assert.Equal(Nrc.ExceededNumberOfAttempts, r.Nrc);
    }

    [Fact]
    public async Task Session_change_nrc_is_retried()
    {
        var (client, _, fault) = await ConnectedAsync();
        // One 0x7E then success — retryable, so the second attempt should get through.
        fault.InjectNrc(UdsServices.ReadDataByIdentifier, Nrc.SubFunctionNotSupportedInActiveSession);

        var r = await client.ReadDataByIdentifierAsync(0xF18C);

        Assert.True(r.IsPositive); // retry succeeded against the simulator
    }

    [Fact]
    public async Task StartSession_tracks_current_session()
    {
        var (client, _, _) = await ConnectedAsync();
        var r = await client.StartSessionAsync(0x03);
        Assert.True(r.IsPositive);
        Assert.Equal(0x03, client.CurrentSession);
    }

    [Fact]
    public async Task KeepAlive_sends_tester_present_periodically()
    {
        var (client, _, _) = await ConnectedAsync();
        int testerPresentCount = 0;
        client.Traced += e => { if (e.Request.Length >= 1 && e.Request[0] == UdsServices.TesterPresent) Interlocked.Increment(ref testerPresentCount); };

        await using (client.StartKeepAlive())
        {
            await Task.Delay(120); // ~6 intervals at 20 ms
        }

        Assert.True(testerPresentCount >= 2, $"expected repeated TesterPresent, saw {testerPresentCount}");
    }
}
