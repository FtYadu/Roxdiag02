# Architecture

The ROX Offline Diagnostic Suite is a layered .NET 8 solution. The **cross-platform core + service
libraries** (`net8.0`) hold all the diagnostic logic and are fully unit-tested with an in-process
simulator; the **Windows app + installer** (`net8.0-windows`, WiX) are the only OS-specific pieces.

```
              ┌─────────────────────────────────────────────────────────┐
   Windows →  │  Rox.App (WPF / MVVM)          installer/ (WiX v5 MSI)   │
              └───────────────┬─────────────────────────────────────────┘
                              │ depends on ↓ (all net8.0, cross-platform)
   ┌──────────────┬───────────┴────────┬───────────────┬──────────────┐
   │ Rox.KeyFuncs │  Rox.Reflash       │ Rox.Diagnostics│  Rox.Logging │
   └──────┬───────┴─────────┬──────────┴───────┬────────┴──────────────┘
          │                 │                  │
          └──────►  Rox.Uds (stateful UDS client)  ◄──── Rox.Security
                              │
                     Rox.Transport (ITransport: ISO-TP / DoIP / adapters)
                              │
     Rox.FlowEngine ─────► Rox.Core ◄───── Rox.Profile     Rox.Simulator (default target)
     (XML→AST→interpret)   (UDS/NRC/DTC)   (R11_Oversea)   (in-proc UDS/ECU server)
```

## Layers (data flow: User → GUI → Flow/Reflash → UDS → Transport → Vehicle)

| Layer | Project | Responsibility |
|------|---------|----------------|
| Models | `Rox.Core` | UDS service ids, NRC table + `RecommendedAction`, `UdsResponse.Parse`, `Dtc` + J2012 decode |
| Profile | `Rox.Profile` | Parse `R11_Oversea.xml` (CAN + DoIP buses/pinout) |
| Flow engine | `Rox.FlowEngine` | FlowConfiguration XML → AST → interpreter; variable store; both parser quirks; `IEcuServiceExecutor`/`ISecurityProvider`/`IUserPrompt` |
| Simulator | `Rox.Simulator` | In-proc UDS/ECU server + test-only seed-key (default target) |
| Transport | `Rox.Transport` | `ITransport` boundary; in-house ISO-TP; DoIP ISO 13400 state machine; PCAN/Kvaser/Vector adapters; `TransportEcuServiceExecutor` bridges to the flow engine |
| UDS client | `Rox.Uds` | Stateful client: session, TesterPresent keep-alive, 0x78 pending poll, retry/timeout driven by `Nrc.RecommendedAction` |
| Diagnostics | `Rox.Diagnostics` | DTC read → clear → read-back with live-fault detection |
| Security | `Rox.Security` | User-DLL provider (P/Invoke), DPAPI-stored path, session cache, lockout, manual fallback, licence stub |
| Key functions | `Rox.KeyFunctions` | Pairing / duplication / deletion on the shared skeleton, guarded + audited |
| Reflash | `Rox.Reflash` | Block sizing from 0x34, transfer loop (BSC wrap, 0x78, 0x73), checksum, voltage gate, progress |
| Logging | `Rox.Logging` | Serilog rolling files + separate audit sink; PDF/CSV reporting; audit + confirmation contracts |
| UI | `Rox.App` | WPF/MVVM shell + 7 views, DI host, settings/DPAPI |

## Key design decisions

- **One transport boundary.** Everything above `Rox.Transport` sees a single
  `Task<byte[]> SendAsync(request, ct)`; CAN-vs-DoIP is hidden. `TransportEcuServiceExecutor` presents any
  transport as the flow engine's `IEcuServiceExecutor`, so the tested `FlowInterpreter` runs unchanged over
  real hardware or the simulator.
- **Security never embeds OEM crypto.** `ISecurityProvider.ComputeKey` is the only path to a key;
  `NativeSeedKeyProvider` marshals to the operator's licensed module. The sim's `TestSecurityProvider` is
  clearly labelled and simulator-only.
- **Safety is structural.** Irreversible operations (key delete, VIN/config write, reflash) are gated by
  `IConfirmationService` and always write an `AuditEntry`; reflash is voltage-gated. The headless default
  denies destructive operations.
- **Offline by construction.** The only network I/O is DoIP TCP/UDP; `build/check-no-network.sh` fails the
  build on any banned outbound API or telemetry/auto-update package.
- **The simulator is a first-class target.** It makes the whole suite runnable and testable with no
  vehicle, and (via `ScriptedFaultTransport`) exercises every NRC error path.

## Testing

- 62 xUnit tests over the core + service libraries (NRC/DTC/UDS, profile, both flow quirks, ISO-TP
  multi-frame, real-socket DoIP, UDS pending/lockout/session, DTC live-fault, security handshake incl. a
  real native `.so`, key pair/duplicate/delete, reflash incl. BSC wrap + 0x78 + 0x73 + voltage gate,
  logging/PDF, licensing).
- Golden-trace record/replay regression (`tests/Rox.GoldenTraces`).
- CI: a Linux job builds+tests the cross-platform core and runs the offline guard; a Windows job builds
  the WPF app + WiX MSI.
