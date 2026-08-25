# Build progress — ROX Offline Diagnostic Suite (Windows)

Legend: ✅ done · 🚧 in progress · ⬜ not started · 🔧 `TODO(hardware)` (implemented behind the
interface, validated only on the simulator; real path documented in `docs/EXTERNAL_INPUTS.md`).

| Phase | Scope | State |
|------|-------|-------|
| P0 | Adopt tested core; build+test green; CI + offline guard | ✅ |
| P1 | `Rox.Transport` — ITransport, loopback, DoIP sim + state machine, ISO-TP, vendor adapters, executor adapter | ⬜ |
| P2 | `Rox.Uds` — stateful UDS client (session, keep-alive, 0x78 poll, retry) | ⬜ |
| P3 | `Rox.Diagnostics` — DTC read→clear→read-back live-fault orchestration | ⬜ |
| P4 | `Rox.Security` — user-DLL provider, DPAPI path, cache, lockout, manual fallback | ⬜ |
| P5 | `Rox.KeyFunctions` — pairing/duplication/deletion + guardrails + audit | ⬜ |
| P6 | `Rox.Reflash` — block sizing from 0x34, transfer loop, checksum, voltage gate | ⬜ |
| P7 | `Rox.Logging` — Serilog + audit sink + PDF/CSV reporting | ⬜ |
| P8 | `Rox.App` — WPF shell + DI host + settings/DPAPI + navigation + themes | ⬜ |
| P9 | Views — Dashboard, Diagnostics, Guided Flows, Key Functions, Reflash, Expert Console, Settings | ⬜ |
| P10 | `Rox.GoldenTraces` — record/replay regression | ⬜ |
| P11 | Packaging — WiX v5 MSI, self-contained publish, bundle data, USER_MANUAL | ⬜ |
| P12 | Hardware enablement (best-effort, documented) | ⬜ |
| P13 | Acceptance & docs | ⬜ |

## P0 — Adopt foundation ✅

- Unzipped `rox-diagnostic-core.zip` unchanged into the repo (six projects).
- `dotnet build -c Release` → 0 warnings, 0 errors.
- `dotnet test -c Release` → **12/12 passing** (NRC/DTC/UDS decode, profile, both flow quirks,
  end-to-end Add-Key against the simulator, security-denied).
- `dotnet run --project samples/Rox.Demo` → profile parse + DTC read→clear→read-back with live-fault
  flagging + Add-Key flow, all against the in-process `EcuSimulator`.
- Added `.github/workflows/ci.yml`: Linux job builds+tests the cross-platform core and service
  libraries and runs the **offline guard** (`build/check-no-network.sh`); a Windows job builds the
  WPF app + WiX MSI.
- `build/check-no-network.sh` fails the build if any banned outbound-network API
  (`HttpClient`, `WebClient`, `System.Net.Http`, …) or telemetry/auto-update package is introduced.
  DoIP sockets (`System.Net.Sockets`) are deliberately allowed.

## Read-only foundation (do not rewrite)

`Rox.Core`, `Rox.Profile`, `Rox.FlowEngine`, `Rox.Simulator`, `Rox.Demo`, `Rox.Tests`. The
flow-engine schema and its two quirks are already solved and tested; the `<Request>` mapper in
`FlowParser.ParseEcuService` and the `FlowSemantics` `ResponseStatus` enum stay configurable until a
complete OEM flow confirms them (§17 open items).
