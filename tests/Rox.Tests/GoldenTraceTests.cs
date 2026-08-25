using Rox.Diagnostics;
using Rox.GoldenTraces;
using Rox.Simulator;
using Rox.Transport;
using Rox.Uds;
using Xunit;

public class GoldenTraceTests
{
    private static string TracesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ROXDiagnostic.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "tests", "Rox.GoldenTraces", "traces");
    }

    [Fact]
    public async Task Recorded_dtc_cycle_replays_identically()
    {
        // Record a fresh DTC read->clear->read-back against the simulator.
        var rec = new RecordingTransport(new LoopbackTransport(new EcuSimulator(), "EMS"));
        await rec.ConnectAsync();
        var liveFromSim = await new DtcService(new UdsClient(rec, new UdsClientOptions { RetryDelay = TimeSpan.Zero }))
            .ReadClearReadBackAsync("EMS");

        // Replay the recorded trace with no simulator; the higher layers must produce the same result.
        await using var replay = new ReplayTransport(rec.Trace);
        await replay.ConnectAsync();
        var liveFromReplay = await new DtcService(new UdsClient(replay, new UdsClientOptions { RetryDelay = TimeSpan.Zero }))
            .ReadClearReadBackAsync("EMS");

        Assert.True(replay.AtEnd);
        Assert.Equal(liveFromSim.LiveRemainingCount, liveFromReplay.LiveRemainingCount);
        Assert.Equal(liveFromSim.LiveRemaining[0].Code, liveFromReplay.LiveRemaining[0].Code);
    }

    [Fact]
    public async Task Committed_dtc_trace_replays_and_flags_live_fault()
    {
        var trace = GoldenTrace.Load(Path.Combine(TracesDir(), "dtc_read_clear_readback.json"));
        await using var replay = new ReplayTransport(trace);
        await replay.ConnectAsync();

        var result = await new DtcService(new UdsClient(replay, new UdsClientOptions { RetryDelay = TimeSpan.Zero }))
            .ReadClearReadBackAsync("EMS");

        Assert.True(replay.AtEnd);
        Assert.Equal(1, result.LiveRemainingCount);
        Assert.Equal("P0301", result.LiveRemaining[0].Code);
        Assert.True(result.LiveRemaining[0].IsLiveFault);
    }

    [Fact]
    public async Task Committed_key_count_trace_replays()
    {
        var trace = GoldenTrace.Load(Path.Combine(TracesDir(), "read_key_count.json"));
        await using var replay = new ReplayTransport(trace);
        await replay.ConnectAsync();
        var client = new UdsClient(replay, new UdsClientOptions { RetryDelay = TimeSpan.Zero });

        await client.StartSessionAsync(0x03);
        var r = await client.ReadDataByIdentifierAsync(0xF18C);

        Assert.True(r.IsPositive);
        Assert.Equal(2, r.Raw[^1]);
    }

    [Fact]
    public async Task Divergent_request_is_detected_as_regression()
    {
        var trace = GoldenTrace.Load(Path.Combine(TracesDir(), "read_key_count.json"));
        await using var replay = new ReplayTransport(trace);
        await replay.ConnectAsync();

        // First recorded request is 1003 (StartSession). Sending something else must be flagged.
        await Assert.ThrowsAsync<GoldenTraceRegression>(() => replay.SendAsync(new byte[] { 0x22, 0xF1, 0x8C }));
    }
}
