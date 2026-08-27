using System.Diagnostics;
using Rox.Core;
using Rox.Security;
using Rox.Simulator;
using Rox.Transport;
using Rox.Uds;
using Xunit;

public class SecurityTests
{
    private static async Task<(SecurityAccessService svc, EcuSimulator sim, ScriptedFaultTransport fault)> BuildAsync(Rox.FlowEngine.ISecurityProvider provider)
    {
        var sim = new EcuSimulator();
        var fault = new ScriptedFaultTransport(new LoopbackTransport(sim, "IMMO"));
        await fault.ConnectAsync();
        var client = new UdsClient(fault, new UdsClientOptions { RetryDelay = TimeSpan.Zero });
        return (new SecurityAccessService(client, provider, defaultLockout: TimeSpan.FromSeconds(5)), sim, fault);
    }

    [Fact]
    public async Task Handshake_grants_with_correct_provider_against_simulator()
    {
        var (svc, sim, _) = await BuildAsync(new TestSecurityProvider());
        var result = await svc.RequestAccessAsync("IMMO", 0x01);
        Assert.True(result.Granted);
        Assert.True(sim.SecurityGranted);
    }

    [Fact]
    public async Task Second_request_is_served_from_session_cache()
    {
        var (svc, _, _) = await BuildAsync(new TestSecurityProvider());
        await svc.RequestAccessAsync("IMMO", 0x01);
        var again = await svc.RequestAccessAsync("IMMO", 0x01);
        Assert.Equal(SecurityAccessStatus.AlreadyGranted, again.Status);
    }

    [Fact]
    public async Task Wrong_key_yields_invalid_key_status()
    {
        var (svc, _, _) = await BuildAsync(new WrongKeyProvider());
        var result = await svc.RequestAccessAsync("IMMO", 0x01);
        Assert.Equal(SecurityAccessStatus.InvalidKey, result.Status);
        Assert.Equal(Nrc.InvalidKey, result.Nrc);
    }

    [Fact]
    public async Task Lockout_0x36_is_surfaced_and_prevents_hammering()
    {
        var (svc, _, fault) = await BuildAsync(new TestSecurityProvider());
        fault.InjectNrc(UdsServices.SecurityAccess, Nrc.ExceededNumberOfAttempts); // seed request locked

        var first = await svc.RequestAccessAsync("IMMO", 0x01);
        Assert.Equal(SecurityAccessStatus.Locked, first.Status);
        Assert.NotNull(first.RetryAfter);

        // A subsequent call must be short-circuited by the lockout back-off, not sent to the ECU.
        var second = await svc.RequestAccessAsync("IMMO", 0x01);
        Assert.Equal(SecurityAccessStatus.Locked, second.Status);
    }

    [Fact]
    public async Task Missing_module_is_handled_gracefully()
    {
        Assert.Throws<FileNotFoundException>(() => new NativeSeedKeyProvider("/no/such/seedkey.so"));

        // With no provider at all, the service reports NoProvider rather than throwing.
        var svcNoProvider = new SecurityAccessService(new UdsClient(await ConnectedLoopback()), provider: null);
        var result = await svcNoProvider.RequestAccessAsync("IMMO");
        Assert.Equal(SecurityAccessStatus.NoProvider, result.Status);
    }

    [Fact]
    public void Manual_key_provider_returns_operator_supplied_key()
    {
        var provider = new ManualKeyProvider(seed => TestSeedKey.Compute(seed));
        var outcome = SecurityModuleTester.Test(provider, new byte[] { 0x11, 0x22, 0x33, 0x44 });
        Assert.True(outcome.Success);
        Assert.Equal(4, outcome.Key.Length);
    }

    [Fact]
    public void Protected_value_store_round_trips_the_module_path()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rox-sec-" + Guid.NewGuid().ToString("N"));
        var store = new ProtectedValueStore(Path.Combine(dir, "module.path"));
        store.Save(@"C:\ProgramData\ROX\seedkey.dll");
        Assert.Equal(@"C:\ProgramData\ROX\seedkey.dll", store.Load());
        store.Clear();
        Assert.Null(store.Load());
        Directory.Delete(dir, true);
    }

    // Genuinely exercise the native P/Invoke path by compiling a seed-key module and driving the
    // full handshake against the simulator with it. Skipped where a C toolchain is unavailable.
    [Fact]
    public async Task Native_seedkey_module_completes_handshake()
    {
        var so = TryCompileSeedKeyModule();
        if (so is null) return; // no gcc/clang — skip gracefully (covered by TestSecurityProvider tests)

        using var provider = new NativeSeedKeyProvider(so);
        var (svc, sim, _) = await BuildAsync(provider);
        var result = await svc.RequestAccessAsync("IMMO", 0x01);

        Assert.True(result.Granted, result.Describe());
        Assert.True(sim.SecurityGranted);
    }

    private static async Task<ITransport> ConnectedLoopbackTransport()
    {
        var t = new LoopbackTransport(new EcuSimulator(), "IMMO");
        await t.ConnectAsync();
        return t;
    }
    private static Task<ITransport> ConnectedLoopback() => ConnectedLoopbackTransport();

    private static string? TryCompileSeedKeyModule()
    {
        // Mirrors Rox.Simulator TestSeedKey: key[i] = (seed[i]^0x5A)+1, matching ABI in EXTERNAL_INPUTS.md.
        const string c = @"
#include <stdint.h>
int ComputeKey(const uint8_t* seed, int seedLen, uint8_t* keyBuf, int keyBufCap) {
    if (seedLen > keyBufCap) return -1;
    for (int i = 0; i < seedLen; i++) keyBuf[i] = (uint8_t)((seed[i] ^ 0x5A) + 1);
    return seedLen;
}";
        try
        {
            string dir = Path.Combine(Path.GetTempPath(), "rox-native-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string src = Path.Combine(dir, "seedkey.c");
            string outName = OperatingSystem.IsWindows() ? "seedkey.dll" : "libseedkey.so";
            string outPath = Path.Combine(dir, outName);
            File.WriteAllText(src, c);

            var cc = FindCompiler();
            if (cc is null) return null;
            var psi = new ProcessStartInfo(cc, $"-shared -fPIC -o \"{outPath}\" \"{src}\"") { RedirectStandardError = true, UseShellExecute = false };
            var p = Process.Start(psi);
            if (p is null) return null;
            p.WaitForExit(30000);
            return p.ExitCode == 0 && File.Exists(outPath) ? outPath : null;
        }
        catch { return null; }
    }

    private static string? FindCompiler()
    {
        foreach (var name in new[] { "gcc", "cc", "clang" })
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                try { var full = Path.Combine(dir, name); if (File.Exists(full)) return full; } catch { }
            }
        return null;
    }

    private sealed class WrongKeyProvider : Rox.FlowEngine.ISecurityProvider
    {
        public byte[] ComputeKey(byte[] seed, int length) => new byte[] { 0, 0, 0, 0 };
    }
}
