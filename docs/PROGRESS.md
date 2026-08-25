# Build progress — ROX Offline Diagnostic Suite (Windows)

Legend: ✅ done · 🚧 in progress · ⬜ not started · 🔧 `TODO(hardware)` (implemented behind the
interface, validated only on the simulator; real path documented in `docs/EXTERNAL_INPUTS.md`).

| Phase | Scope | State |
|------|-------|-------|
| P0 | Adopt tested core; build+test green; CI + offline guard | ✅ |
| P1 | `Rox.Transport` — ITransport, loopback, DoIP sim + state machine, ISO-TP, vendor adapters, executor adapter | ✅ |
| P2 | `Rox.Uds` — stateful UDS client (session, keep-alive, 0x78 poll, retry) | ✅ |
| P3 | `Rox.Diagnostics` — DTC read→clear→read-back live-fault orchestration | ✅ |
| P4 | `Rox.Security` — user-DLL provider, DPAPI path, cache, lockout, manual fallback | ✅ |
| P5 | `Rox.KeyFunctions` — pairing/duplication/deletion + guardrails + audit | ✅ |
| P6 | `Rox.Reflash` — block sizing from 0x34, transfer loop, checksum, voltage gate | ✅ |
| P7 | `Rox.Logging` — Serilog + audit sink + PDF/CSV reporting | ✅ |
| P8 | `Rox.App` — WPF shell + DI host + settings/DPAPI + navigation + themes | ✅ (Windows-only build) |
| P9 | Views — Dashboard, Diagnostics, Guided Flows, Key Functions, Reflash, Expert Console, Settings | ✅ (Windows-only build) |
| P10 | `Rox.GoldenTraces` — record/replay regression | ✅ |
| P11 | Packaging — WiX v5 MSI, self-contained publish, bundle data, USER_MANUAL | ✅ (MSI builds on Windows) |
| P12 | Hardware enablement (best-effort, documented) | ✅ |
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

## P1 — Transport abstraction ✅

`Rox.Transport` (net8.0, cross-platform):

- `ITransport` — the single `Task<byte[]> SendAsync(request, ct)` boundary; CAN vs DoIP hidden.
- `TransportEcuServiceExecutor` — adapts any `ITransport` to the flow engine's `IEcuServiceExecutor`,
  so the existing `FlowInterpreter` runs unchanged over real transport.
- `LoopbackTransport` — in-process simulator, no framing (default target).
- **ISO-TP (ISO 15765-2)** — `IsoTpChannel` implements SF/FF/CF/FC with BlockSize + STmin;
  `LoopbackCanChannel` gives an in-memory tester/ECU CAN pair; `IsoTpCanTransport` (tester) and
  `IsoTpLoopbackTransport` (tester + background ECU responder over the simulator).
- **DoIP (ISO 13400)** — `DoipMessage` framing, `DoipClientTransport` (real TCP, routing activation
  0x0005→0x0006 before UDS, 0x8001/0x8002/0x8003), `SimulatedDoipServer` for hardware-free socket tests.
- Vendor adapters — `PcanCanChannel` (real PCAN-Basic P/Invoke, `TODO(hardware)`), Kvaser + Vector
  stubs; `TransportFactory` builds the configured kind.

Tests (6 new, 18 total green): 40-byte ISO-TP multi-frame round-trip; multi-frame DTC response via
ISO-TP loopback; Add-Key flow over both the plain loopback and the ISO-TP transport; DoIP routing
activation + diagnostic round-trip over a real localhost socket; DoIP connect failure path.

## P2 — UDS client ✅

`Rox.Uds` (net8.0):

- `UdsClient` over `ITransport`: session tracking, retry/timeout policy driven by
  `Nrc.RecommendedAction`, and **0x78 response-pending polling that is never a failure** (uses
  `IPendingAwareTransport.ReceiveNextAsync` where available; re-send fallback otherwise).
- TesterPresent **keep-alive** scope (`StartKeepAlive`, ~2 s) that shares the request gate so it never
  interleaves on the wire; all transport access serialized via a semaphore.
- Typed helpers for every service in §6 (0x10/0x11/0x14/0x19/0x22/0x27/0x2E/0x31/0x34/0x36/0x37/0x3E/0x85).
- `UdsTraceEntry` event feed for the Expert Console live trace / logging (never carries decoded keys).
- `ScriptedFaultTransport` decorator (in `Rox.Transport`) injects 0x78/0x33/0x36/0x73 to exercise
  error paths without touching the foundation simulator.

Tests (7 new, 25 total green): positive path; 0x78 polled to resolution; 0x33 terminal; 0x36 lockout
surfaced without hammering; session-change NRC retried; session tracking; keep-alive fires repeatedly.

## P3 — DTC service ✅

`Rox.Diagnostics` (net8.0): `DtcService` over `UdsClient`.

- `ReadAsync` — `19 02 FF`, decode J2012 + status bits, attach user-editable descriptions.
- `ReadClearReadBackAsync` — capture pre-clear list → enter `10 03` if needed → `14 FF FF FF` →
  re-read; codes gone are **stale cleared**, codes that re-set immediately are flagged **live faults**;
  produces the UC-01 summary ("N stale cleared, M live remaining").
- `ScanAllAsync` — all-ECU aggregation. Clear rejection surfaced with decoded NRC.

Tests (4 new, 29 total green): decode + descriptions; read→clear→read-back live/stale split;
clear-rejection NRC surfacing; all-ECU aggregate.

## P4 — Security provider ✅

`Rox.Security` (net8.0):

- `NativeSeedKeyProvider : ISecurityProvider` — loads the operator's licensed module via
  `NativeLibrary` + a Cdecl `ComputeKey(seed, seedLen, keyBuf, cap)` export. **No OEM algorithm in the app.**
- `ManualKeyProvider` — offline manual-key fallback (FR-06.5); `SecurityModuleTester` self-test (FR-06.2).
- `SecurityAccessService` — full 0x27 handshake (request seed → module → send key), per-session grant
  cache (FR-06.3), lockout back-off on 0x36/0x37 that prevents hammering (FR-06.4), all-zero-seed =
  already-unlocked convention; typed `SecurityAccessResult` with plain-language NRC.
- DPAPI: `DpapiSecretProtector` (Windows) + `ProtectedValueStore` for the module path/licence;
  clearly-labelled non-secure passthrough on non-Windows for dev/CI only.

Tests (8 new, 37 total green): grant against sim; session cache; wrong key → invalid; 0x36 lockout
surfaced + no hammering; missing module handled; manual provider; DPAPI store round-trip; **and a real
native `.so` compiled at runtime driving the full handshake through the P/Invoke path**.

## P5 — Key functions ✅

`Rox.Logging` (contracts) + `Rox.KeyFunctions` (net8.0):

- Audit + safety contracts (`Rox.Logging`): `AuditEntry`/`IAuditSink` (in-memory/null/composite),
  `ConfirmationRequest`/`IConfirmationService` (deny-destructive headless default; delegate for UI/tests).
- `KeyFunctionService` — pairing, duplication, deletion on the shared skeleton (`10 03` → SecurityAccess
  → read key count → operator prompt → `31 01` learn/delete → poll `31 03` → verify count → `10 01`),
  with TesterPresent keep-alive during the operation. **Every write is confirmation-gated and audited**;
  before/after key counts are surfaced; security lockout (0x36/0x37) surfaced without hammering.
- Routine ids / DIDs are `KeyFunctionConfig` data-package values, never embedded logic.
- Simulator extended (additive) with a key-delete routine (0x0202) so deletion read-back is testable.

Tests (6 new, 43 total green): pairing increments + audit; deletion decrements with read-back;
duplication skeleton; declined confirmation makes no write (audited Denied); lockout surfaced with no
write; headless default denies irreversible writes.

## P6 — Reflash controller ✅

`Rox.Reflash` (net8.0): `ReflashService` (FR-05, UC-03).

- Pre-flash: voltage gate (blocks below threshold, FR-05.8) → bricking-risk confirmation → programming
  session (`10 02`) → DTCs off (`85 02`) → SecurityAccess → checkDependencies → erase.
- Download: `0x34` then **block size taken from the response's `maxNumberOfBlockLength` (NOT a fixed
  2 KB)**; `0x36` loop with **BSC wrap 0xFF→0x00**, 0x78 handled by the client, **0x73 aborts**;
  `0x37`; post-flash checksum routine with a 32-bit sum matching the simulator.
- `IProgress<ReflashProgress>` with throughput + ETA + percent; audit entry carries the checksum.
- Simulator extended (additive): accumulates transferred firmware + a `checkMemory` routine (0xFF01).

Tests (8 new, 51 total green): success + checksum verify; block size from 0x34 (2 blocks for 300 B);
BSC wrap across 258 blocks; voltage-below-threshold blocks with nothing transferred; declined
confirmation aborts pre-write; 0x78-during-transfer handled; 0x73 aborts; full reflash over ISO-TP framing.

## P7 — Logging / audit / reporting ✅

`Rox.Logging` (net8.0) expanded:

- `SerilogSetup` — two independent rolling-file sinks under `%AppData%\ROXDiagnostic\Logs\`: a general
  session log (daily) and a **separate audit sink** (monthly, longer retention). No network sinks.
- `SerilogAuditSink : IAuditSink`; `LogRedaction` scrubs SecurityAccess key bytes from the general
  trace (never log plaintext keys/seeds, FR-08.4) — audit entries are redacted by construction.
- Reporting: `SessionReport` model; `PdfReportGenerator` (QuestPDF, offline community licence) for the
  warranty session summary; `CsvExporter` for DTC/audit CSV and raw UDS trace TXT.

Tests (4 new, 55 total green): audit written to a separate file; key bytes redacted; a real PDF
produced (%PDF header, >1 KB); CSV export with correct quoting.

## P8 / P9 — WPF app + views ✅ (builds on Windows)

`Rox.App` (`net8.0-windows`, WPF + WPF-UI Fluent + CommunityToolkit.Mvvm + Microsoft.Extensions.Hosting):

- Generic-host DI (`App.xaml.cs`) wiring `SettingsService`, `DialogService`, `DiagnosticSession` and all
  view-models; `FluentWindow` shell with DataTemplate-driven navigation (`ShellViewModel`), dark/light
  theme via `ApplicationThemeManager`, connect/disconnect + status bar.
- `SettingsService`: `settings.json` under `%AppData%\ROXDiagnostic` + the seed-key module path stored
  DPAPI-encrypted in a separate file; `DialogService` implements the confirmation + operator-prompt
  boundaries (irreversible ops require an explicit Yes).
- `DiagnosticSession` builds transport → UDS client → DTC/security/key/reflash services from settings,
  defaulting to the simulator; `EcuRegistry` carries the full R11_Oversea ECU map from the vehicle scan.
- Seven MVVM views bound to the real services: **Dashboard** (vehicle + ECU map), **Diagnostics** (read /
  clear+read-back / all-ECU scan / CSV+PDF export), **Guided Flows** (drives `FlowInterpreter` over the
  transport), **Key Functions** (pair/duplicate/delete + audit), **Reflash** (firmware picker, progress +
  throughput + ETA, voltage/bricking gate), **Expert Console** (raw UDS + live trace), **Settings**
  (transport, module path + self-test, theme).

The WPF app targets `net8.0-windows` and builds on the **Windows CI job** (the Windows Desktop SDK is not
available on Linux, so the Linux job builds only the cross-platform core + service libraries). Bundled
data package added under `data/` (profile + guided-flow XMLs).

## P10 — Golden-trace regression ✅

`tests/Rox.GoldenTraces` (net8.0):

- `RecordingTransport` captures every request/response exchange; `ReplayTransport` replays a recorded
  trace with no target and **throws on any divergence** (changed bytes, order, extra/missing request) —
  exactly how a regression in the UDS/DTC/flow layers is caught in CI.
- Committed fixtures under `traces/` (DTC read→clear→read-back, key-count read) recorded from the
  simulator and copied to test output.

Tests (4 new, 59 total green): recorded DTC cycle replays identically to the live sim; committed DTC
trace replays and flags the live fault; committed key-count trace replays; a divergent request is
detected as a regression.

## P11 — Packaging ✅ (MSI builds on Windows)

- `installer/Rox.Installer` — WiX v5 (`WixToolset.Sdk/5.0.2`) MSI: installs the self-contained WPF app
  + bundled `data/` to `Program Files\ROXDiagnostic`, Start-menu shortcut, clean major-upgrade,
  InstallDir UI, no network/auto-update components. Harvests the publish output via the `<Files>` element.
  Built by the Windows CI job (`dotnet publish` self-contained win-x64 single-file → `dotnet build` wixproj).
- `LicenseValidator` (Rox.Security) — offline licence-key format/checksum validation (stub for the
  production scheme), surfaced on the Dashboard and configurable in Settings.
- `docs/USER_MANUAL.md` — full operator guide (install, connect, DTCs, guided flows, key functions,
  reflash, expert console, settings, logs/audit, safety).

Tests (3 new, 62 total green): licence missing/valid/tampered/malformed.

## P12 — Hardware enablement ✅ (documented, simulator-validated)

- PCAN-Basic adapter is real P/Invoke; Kvaser/Vector are correctly-shaped stubs; the DoIP client is a
  complete ISO 13400 implementation validated over a real socket against the bundled server. All
  hardware-specific paths sit behind `ICanChannel` / `ITransport` / `IVoltageProvider` and are marked
  `TODO(hardware)`.
- `docs/EXTERNAL_INPUTS.md` rewritten: the seed-key ABI, every `TODO(hardware)`/`TODO(licensing)` marker,
  the vendor SDKs, firmware/data-package requirements, and the three open schema items (O-1/O-2/O-3).
- Compiles + loopback stays green (62 tests); the real path is documented as requiring physical
  validation, never faked.

## Read-only foundation (do not rewrite)

`Rox.Core`, `Rox.Profile`, `Rox.FlowEngine`, `Rox.Simulator`, `Rox.Demo`, `Rox.Tests`. The
flow-engine schema and its two quirks are already solved and tested; the `<Request>` mapper in
`FlowParser.ParseEcuService` and the `FlowSemantics` `ResponseStatus` enum stay configurable until a
complete OEM flow confirms them (§17 open items).
