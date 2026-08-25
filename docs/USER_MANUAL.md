# ROX Offline Diagnostic Suite — User Manual

A professional, **fully offline** Windows diagnostic tool for the ROX 01 / ROX Adamas
(`R11_Oversea`) platform: DTC diagnostics, guided special functions, immobilizer key programming,
and MCU reflashing. No internet connection is used at any time.

> The suite ships ready to run against a **built-in simulator** so every function can be learned and
> demonstrated with no vehicle attached. Talking to a real vehicle additionally requires the items in
> `docs/EXTERNAL_INPUTS.md` (an OBD/DoIP interface + vendor SDK, a licensed seed-key module, the
> genuine data package, and firmware images).

## 1. Install

Run `ROXDiagnostic.msi`. It installs to `C:\Program Files\ROXDiagnostic` and adds a **ROX Diagnostic**
Start-menu shortcut. Updates are manual (install a newer MSI) to preserve the offline guarantee — there
is no auto-update.

## 2. First run & connect

1. Launch **ROX Offline Diagnostic Suite**.
2. Open **Settings** and choose a **Transport**:
   - `SimulatedLoopback` / `SimulatedIsoTp` — the built-in simulator (default; no hardware).
   - `Doip` — a DoIP vehicle/entity over Ethernet (set host/port).
   - `PcanCan` — a PEAK PCAN-USB adapter (CAN / ISO-TP). Kvaser/Vector are stubs.
3. Click **Connect** (left panel). The status bar shows the connection state and active transport.

## 3. Diagnostics (DTCs)

- Pick an **ECU** and click **Read DTCs** to read and decode fault codes (SAE J2012 + status bits).
- **Clear + read-back** runs the professional cycle: it captures the codes, clears them
  (`14 FF FF FF`, entering an extended session where needed), re-reads, and flags any code that
  **re-sets immediately as a live/active fault** (distinct from a cleared stale code). The status line
  summarises "*N stale cleared, M live remaining*".
- **Scan all ECUs** loops the whole vehicle. **Export CSV / PDF** saves the results (the PDF is a
  warranty-style session report).

## 4. Guided Flows (special functions)

Guided Flows executes the OEM procedure XML files (add key, delete key, write VIN, write config word,
TPMS matching, ECU reset, …) through the built-in flow interpreter.

1. Select a flow from the list (or **Browse…** to a data-package folder).
2. Click **Run**. Follow on-screen operator prompts ("insert key, ignition ON", etc.).
3. Each UDS step and its result (with plain-language NRC decoding) appears in the log.

## 5. Key Functions

Pairing, duplication and deletion of immobilizer keys.

1. Select the immobilizer ECU (e.g. **IMMO**), click **Read key count**.
2. Click **Pair new key**, **Duplicate key**, or **Delete key**.
3. Every operation **requires explicit confirmation before any write** and records an **audit entry**.
   The before/after key count is shown; lockouts (NRC 0x36/0x37) are surfaced clearly.

## 6. Reflash

MCU firmware reflashing with safety gating.

1. Select the target ECU and **Select firmware…** (`.bin`/`.hex`/`.srec`).
2. Click **Reflash**. The tool **blocks the flash if battery voltage is below the threshold** and shows
   an explicit bricking-risk confirmation. Attach a battery maintainer first.
3. Progress, throughput and ETA are shown. The block size is taken from the ECU's own
   `maxNumberOfBlockLength` (from the `0x34` response); response-pending (0x78) is handled
   automatically; a wrong block-sequence (0x73) aborts safely. On success the checksum is verified and
   logged.

## 7. Expert Console

For engineers: type a raw UDS request in hex (e.g. `22 F1 8C`) and **Send**. The decoded response and a
live request/response trace are shown. Key material is never written to the trace or logs.

## 8. Settings reference

- **Transport / DoIP / CAN** parameters and comms **timeout**.
- **Reflash min. voltage** — the safety threshold.
- **Data package directory** — where `R11_Oversea.xml` and the flow XMLs live.
- **Seed-key module** — path to the operator's licensed native module; **Test** validates it against a
  sample seed. The path is stored **DPAPI-encrypted**, never in plaintext.
- **Licence key** — offline licence validation.
- **Theme** — dark/light.

## 9. Logs & audit

- Session logs and a **separate audit trail** roll under `%AppData%\ROXDiagnostic\Logs\`.
- Security-gated and irreversible operations always write an audit entry; keys and full seeds are never
  logged in plaintext.

## 10. Safety notes

- Reflash and key deletion are irreversible. Confirm the firmware/key is correct for the exact
  vehicle/market, keep a working key, and never interrupt a flash.
- Safety-critical ECUs (ADCU_MCU, ACU airbag, dual EPS/EPS_FD) demand extra care; validate on the
  simulator first.
