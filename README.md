# ROX Offline Diagnostic Suite (Windows)

A professional, **fully offline** Windows desktop diagnostic suite for the ROX / Polestones / Jishi
**ROX 01 / Adamas** platform (profile `R11_Oversea`): full UDS diagnostics, guided special functions,
immobilizer key programming, and MCU reflashing — with **no internet connection**.

The suite ships with a built-in **simulator**, so every function builds, tests, and demonstrates
end-to-end with no vehicle and no special hardware. Talking to a real vehicle additionally requires the
items in [`docs/EXTERNAL_INPUTS.md`](docs/EXTERNAL_INPUTS.md).

## Layout

```
ROXDiagnostic.sln            cross-platform core + service libraries (net8.0) — builds on Linux & Windows
 ├─ src/
 │   ├─ Rox.Core            UDS/NRC/DTC primitives
 │   ├─ Rox.Profile         R11_Oversea profile parser
 │   ├─ Rox.FlowEngine      FlowConfiguration XML → AST → interpreter (both quirks)
 │   ├─ Rox.Simulator       in-proc UDS/ECU server (default target)
 │   ├─ Rox.Transport       ITransport: in-house ISO-TP, DoIP state machine, PCAN/Kvaser/Vector adapters
 │   ├─ Rox.Uds             stateful UDS client (session, keep-alive, 0x78 poll, retry)
 │   ├─ Rox.Diagnostics     DTC read→clear→read-back + live-fault detection
 │   ├─ Rox.Security        user-DLL seed-key provider, DPAPI, lockout, licence
 │   ├─ Rox.KeyFunctions    pairing / duplication / deletion + guardrails + audit
 │   ├─ Rox.Reflash         block sizing from 0x34, transfer loop, checksum, voltage gate
 │   ├─ Rox.Logging         Serilog + audit sink + PDF/CSV reporting
 │   └─ Rox.App             WPF/MVVM app (net8.0-windows) — builds on Windows
 ├─ tests/Rox.Tests         62 xUnit tests
 ├─ tests/Rox.GoldenTraces  record/replay regression fixtures
 ├─ installer/Rox.Installer WiX v5 MSI (builds on Windows)
 ├─ data/                   default R11_Oversea data package (profile + flow XMLs)
 └─ docs/                   PRD, ARCHITECTURE, EXTERNAL_INPUTS, USER_MANUAL, PROGRESS
```

## Build, test, demo (any OS with the .NET 8 SDK)

```bash
dotnet build ROXDiagnostic.sln -c Release
dotnet test  ROXDiagnostic.sln -c Release      # 62 tests
dotnet run   -c Release --project samples/Rox.Demo
# or the one-command runner:
bash build/demo.sh        # (build/demo.cmd on Windows)
```

## Build & run the Windows app + MSI (Windows only)

```powershell
dotnet build   src/Rox.App/Rox.App.csproj -c Release
dotnet publish src/Rox.App/Rox.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
dotnet build   installer/Rox.Installer/Rox.Installer.wixproj -c Release   # produces ROXDiagnostic.msi
```

The WPF UI (`net8.0-windows`) and the WiX MSI are Windows-only; the cross-platform core and all service
libraries build and test on Linux and Windows alike. See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## Non-negotiables

- **Offline only** — the only network I/O is DoIP TCP/UDP; `build/check-no-network.sh` fails the build on
  any banned outbound API or telemetry/auto-update package.
- **No embedded OEM crypto** — keys come only from a user-supplied module via
  `ISecurityProvider.ComputeKey`; the sim's `TestSecurityProvider` is clearly labelled and simulator-only.
- **Safety on irreversible ops** — key delete, VIN/config write, and reflash require explicit confirmation
  and a mandatory audit entry; reflash is voltage-gated.
