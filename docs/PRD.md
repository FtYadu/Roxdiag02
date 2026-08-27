# ROX Offline Diagnostic Suite — PRD (v2.0)

> Clean Markdown rendering of the authoritative PRD (`ROX_Offline_Diagnostic_Suite_PRD_v2.0`).
> The verbatim text extracted from the source PDF is kept at `docs/PRD_source_extracted.txt`.
> Where this file and the source differ in wording, the source governs.

**Version:** 2.0 · **Platform target:** `R11_Oversea` (Chinese-OEM export EV, domain/zonal E/E
architecture, L2++/L3-capable — ROX 01 / ROX Adamas / Polestones / Jishi).

## 1. Summary

A professional, self-contained **Windows desktop** application for workshops and independent repair
shops servicing the `R11_Oversea` platform. It delivers full UDS diagnostics, guided special
functions, immobilizer key programming, and secure MCU reflashing **with no internet connection**.

The suite consumes the OEM data package directly — `R11_Oversea.xml`, the per-ECU folder tree, the
XML flow scripts, the reflash sequences — and turns OEM procedures into wizard workflows driven by a
built-in flow interpreter. Security material (seed→key) is executed locally through a **user-supplied
module**; no OEM cryptographic algorithm is embedded.

## 2. Scope

**In scope:** read/clear DTCs with read-back + live-fault detection; guided special functions (IMMO
key add/delete, pairing, duplication, VIN write, config-word write, IMMO constant write, TPMS match,
window/sunroof self-learn, ECU reset); MCU reflash with block sizing from `0x34`, 0x78 handling and
checksum verify; automatic ECU discovery; CAN (ISO-TP) + DoIP (ISO 13400) transport; offline security
via a user-supplied seed-key module; guided GUI + expert raw-UDS console; logging, audit, reporting.

**Out of scope:** cloud/online services; non-`R11_Oversea` platforms (additive via XML); wireless
transport; scope/live-graphing beyond DTC/DID; multi-vehicle concurrency; any embedded OEM seed-key
algorithm.

## 3. Functional requirements (condensed)

- **FR-01 Vehicle profile** — parse `ETSData → Vehicle → Buses` (CAN H=6/L=14, 500 kbps; DoIP RX
  3/11, TX 12/13, activation 8, IPv4); discover ECU folders → registry (address + transport); manual
  ECU selection; architecture map.
- **FR-02 Transport** — interface drop-down; DoIP routing activation (`0x0005`→`0x0006`) before any
  UDS; CAN channel/baud + ISO-TP (FC frames, STmin, BlockSize); connection test via `0x3E`;
  TesterPresent keep-alive (~2 s) during long operations.
- **FR-03 DTC** — read `19 02 FF`, decode J2012 + status bitfield; all-ECU scan; clear `14 FF FF FF`
  (enter `10 03` where needed); **read → clear → read-back**, flag instantly re-set codes as live
  faults; route security if required; export PDF/CSV.
- **FR-04 Guided flows** — load flow XML; parse to AST; execute UDS per flow resolving variables;
  evaluate `If → Condition → OneCondition`, honouring the two quirks; per-step NRC decode;
  pause/resume/cancel + manual step-through; log every request/response.
- **FR-05 Reflash** — pick firmware; parse reflash flow pre-sequence (`10 02/03`, `85 02`, `0x27`,
  checkDependencies, erase); `0x34` then read `maxNumberOfBlockLength`; `0x36` blocks (BSC wraps
  0xFF→0x00, 0x78 wait, 0x73 abort); `0x37`; checksum routine; CRC verify; progress + throughput +
  ETA; multi-step confirm + bricking warning; block below voltage threshold.
- **FR-06 Security access** — intercept `27 <odd>` seed, hand to module, send `27 <even> <key>`;
  config pane for module path + self-test; per-session cache; lockout (0x36/0x37) surfaced; manual
  key entry fallback.
- **FR-07 UI** — WPF, tab/side-panel; Dashboard; Guided Flows wizard; Reflash; Expert console;
  persistent settings; dark/light theme.
- **FR-08 Logging/reporting** — auto-log sessions; PDF report; export `.txt`/`.csv`; never log
  plaintext keys or full seeds.
- **FR-09 Key functions** — Pairing (`10 03` → SecAccess → read key count → prompt → learn `31 01` →
  poll `31 03` → verify count++ → `10 01`); Duplication (same skeleton, different payload); Deletion
  (security-gated, count read-back); guard rails (before/after count, confirm, lockout); full audit.

## 4. Flow engine (schema §5)

`FlowConfiguration → Processes → Process → ChildStep(recursive)`. A `ChildStep` may hold one
`EcuService` (request + response-side `ChildStep → If`), plus the node types the interpreter adds:
**SecurityAccess**, **UserPrompt**, **Assign/Extract**, **Loop**.

Two parser rules (already implemented & tested in `Rox.FlowEngine`):

1. **Trailing `ConnectSign` is a no-op.**
2. **Consecutive bare acceptance-`If` on the same variable = an OR-set of accepted states.**
   `ResponseStatus ∈ {2,3}` means "continue"; the mapping is **configurable** (`FlowSemantics`),
   never hard-coded — the enum is open issue O-2.

## 5. UDS / NRC reference (§6)

Services: `0x10 0x11 0x14 0x19 0x22 0x27 0x28 0x2E 0x2F 0x31 0x34 0x36 0x37 0x3E 0x85`.
NRC behaviour (`Nrc.RecommendedAction`): 0x22 retry-after-precondition · 0x24 restart-sequence ·
0x33 (re)auth · 0x35 check-module · 0x36 lockout · 0x37 wait · 0x73 abort-transfer ·
**0x78 wait & re-poll (never a failure)** · 0x7E/0x7F change-session-retry · else abort.

## 6. Transport (§8)

- **CAN / ISO 15765-2**: vendor APIs via P/Invoke (PCAN-Basic reference; Kvaser/Vector stubs);
  in-house ISO-TP (First/Consecutive/Flow-Control, STmin, BlockSize). Default 500 kbps.
- **DoIP / ISO 13400**: `System.Net.Sockets`, port 13400; routing activation (`0x0005`→`0x0006`)
  before UDS; diagnostic payload `0x8001` with `0x8002`/`0x8003` ack/nack.
- One interface to the UDS client: `SendRequest(bytes) → response bytes`, hiding CAN vs DoIP.

## 7. Security (§7)

No embedded OEM seed-key algorithm. The flow's SecurityAccess node is the only boundary crossing:
`seed → ComputeKey(seed[], length) → key` via P/Invoke to a user-supplied module. DPAPI-encrypted
module path; offline licence check; full audit trail on every security-gated op; no network egress.

## 8. Non-functional (§9)

DTC read ≤ 5 s/ECU; reflash ≥ 50 KB/s; auto-retry ≤ 3× before user notification; correct 0x78
looping; 100% offline; self-contained Windows 10/11; new profiles added by dropping XML.

## 9. Use cases

- **UC-01** Read & clear DTCs (with read-back + live-fault flag).
- **UC-02** Execute a special-function flow (continue on `ResponseStatus ∈ {2,3}`).
- **UC-03** Perform an MCU reflash (block sizing from `0x34`, 0x78 auto-wait, checksum).
- **UC-04** Key pairing (count read-back + audit entry).

## 10. Open issues (§17) — blocked on one complete OEM flow file

- **O-1** Request-side element names → flips the `FlowParser` `<Request>` mapper.
- **O-2** `ResponseStatus` enum → flips `FlowSemantics` from configurable-default to confirmed.
- **O-3** Concrete XML tags for UserPrompt / Assign / Loop nodes.

These stay configurable and are listed in `docs/EXTERNAL_INPUTS.md`.
