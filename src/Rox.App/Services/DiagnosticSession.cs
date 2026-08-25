using System.IO;
using Rox.Diagnostics;
using Rox.FlowEngine;
using Rox.KeyFunctions;
using Rox.Logging;
using Rox.Profile;
using Rox.Reflash;
using Rox.Security;
using Rox.Simulator;
using Rox.Transport;
using Rox.Uds;

namespace Rox.App.Services;

/// <summary>
/// The live diagnostic session: builds the transport + UDS client + all service layers from settings,
/// connects/disconnects, and exposes them to the view-models. The simulator is the default target, so
/// the whole UI is functional with no vehicle.
/// </summary>
public sealed class DiagnosticSession
{
    private readonly SettingsService _settings;
    private readonly DialogService _dialogs;

    public DiagnosticSession(SettingsService settings, DialogService dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;
        Audit = new InMemoryAuditSink();
    }

    public EcuSimulator Simulator { get; private set; } = new();
    public InMemoryAuditSink Audit { get; }
    public IAuditSink AuditSink { get; private set; } = new InMemoryAuditSink();

    public ITransport? Transport { get; private set; }
    public UdsClient? Uds { get; private set; }
    public DtcService? Dtc { get; private set; }
    public SecurityAccessService? Security { get; private set; }
    public KeyFunctionService? Keys { get; private set; }
    public ReflashService? Reflash { get; private set; }

    public ISecurityProvider? SecurityProvider { get; private set; }
    public VehicleProfile? Profile { get; private set; }
    public bool IsConnected => Transport?.IsConnected == true;
    public string TransportName => Transport?.Name ?? "(not connected)";

    public IReadOnlyList<EcuInfo> Ecus => EcuRegistry.R11Oversea;

    public event Action? StateChanged;
    public event Action<string>? Trace;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        var s = _settings.Load();
        Simulator = new EcuSimulator(); // fresh state each connect

        var options = new TransportOptions
        {
            Kind = s.TransportKind,
            Simulator = Simulator,
            Baudrate = s.Baudrate,
            CanTxId = s.CanTxId,
            CanRxId = s.CanRxId,
            DoipHost = s.DoipHost,
            DoipPort = s.DoipPort,
            DoipTesterAddress = (ushort)s.DoipTesterAddress,
            DoipTargetAddress = (ushort)s.DoipTargetAddress,
        };

        var transport = TransportFactory.Create(options);
        await transport.ConnectAsync(ct).ConfigureAwait(false);

        var uds = new UdsClient(transport, new UdsClientOptions { Timeout = TimeSpan.FromSeconds(s.TimeoutSeconds) });
        uds.Traced += e => Trace?.Invoke(e.ToString());

        var provider = ResolveSecurityProvider(s);
        SecurityProvider = provider;
        var security = new SecurityAccessService(uds, provider);
        var audit = BuildAuditSink(s);

        Transport = transport;
        Uds = uds;
        Dtc = new DtcService(uds);
        Security = security;
        Keys = new KeyFunctionService(uds, security, audit, _dialogs, _dialogs);
        Reflash = new ReflashService(uds, security, audit, _dialogs,
            new SimulatedVoltageProvider(12.6)); // real target: read voltage via a DID/adapter (TODO(hardware))
        AuditSink = audit;

        LoadProfile(s);
        StateChanged?.Invoke();
    }

    public async Task DisconnectAsync()
    {
        if (Transport is not null) await Transport.DisconnectAsync().ConfigureAwait(false);
        Transport = null; Uds = null; Dtc = null; Security = null; Keys = null; Reflash = null;
        StateChanged?.Invoke();
    }

    private ISecurityProvider? ResolveSecurityProvider(AppSettings s)
    {
        var path = _settings.SecurityModulePath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try { return new NativeSeedKeyProvider(path!); }
            catch (Exception ex) { DialogService.ShowError($"Could not load seed-key module:\n{ex.Message}\nFalling back to the simulator provider."); }
        }
        // On the simulator target, the labelled test provider completes the handshake.
        if (s.TransportKind is TransportKind.SimulatedLoopback or TransportKind.SimulatedIsoTp)
            return new TestSecurityProvider();
        return null; // real target with no module → manual key / NoProvider
    }

    private IAuditSink BuildAuditSink(AppSettings s)
    {
        try
        {
            var logDir = string.IsNullOrWhiteSpace(s.LogDirectory) ? SerilogSetup.DefaultLogDirectory : s.LogDirectory!;
            var serilog = new SerilogAuditSink(SerilogSetup.CreateAuditLogger(logDir), ownsLogger: true);
            return new CompositeAuditSink(Audit, serilog);
        }
        catch
        {
            return Audit; // fall back to in-memory only if the file sink can't be created
        }
    }

    private void LoadProfile(AppSettings s)
    {
        try
        {
            var dir = string.IsNullOrWhiteSpace(s.DataPackageDirectory)
                ? Path.Combine(AppContext.BaseDirectory, "data")
                : s.DataPackageDirectory!;
            var file = Path.Combine(dir, "R11_Oversea.xml");
            if (!File.Exists(file)) file = Path.Combine(dir, "R11_Oversea.sample.xml");
            if (File.Exists(file)) Profile = ProfileParser.Parse(File.ReadAllText(file));
        }
        catch { Profile = null; }
    }
}
