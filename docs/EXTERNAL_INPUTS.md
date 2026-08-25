# External inputs & hardware enablement

The suite runs **fully against the bundled simulator** — build, tests, guided flows, key functions and
reflash are all validated with no vehicle and no special hardware (62 tests green; see `docs/PROGRESS.md`).
Talking to an actual ROX 01 / Adamas requires the items below, which **no code can substitute** and none
of which are (or should be) embedded here. Every hardware-dependent path is implemented behind an
interface, wired to the simulator, and marked `TODO(hardware)`.

## 1. Licensed seed-key module (required for any security-gated operation)

A native library the operator is licensed to use, called across the security boundary through
`ISecurityProvider.ComputeKey(seed, length)` (`Rox.Security.NativeSeedKeyProvider`). **No OEM algorithm
is embedded in the app.** The bundled `TestSecurityProvider` is a simulator stand-in only and must never
be used against a vehicle.

**Export ABI** (`NativeSeedKeyProvider`, Cdecl):

```c
// Returns the number of key bytes written, or a negative value on error.
int ComputeKey(const uint8_t* seed, int seedLen, uint8_t* keyBuf, int keyBufCap);
```

The default export name is `ComputeKey` (overridable). The module path is stored **DPAPI-encrypted**
(`ProtectedValueStore`), and **Settings → Test** validates the module against a sample seed.

## 2. OBD-II / DoIP interface + vendor SDK

- **PCAN-Basic (reference)** — `Rox.Transport.Adapters.PcanCanChannel`, real P/Invoke to `PCANBasic.dll`.
  `TODO(hardware)`: validate against a physical PCAN-USB adapter on the `R11_Oversea` CAN bus
  (H=pin6/L=pin14, 500 kbps).
- **Kvaser CANlib / Vector XL** — `StubCanChannels.cs` stubs with the correct API shape.
  `TODO(hardware)`: implement `canOpenChannel/canBusOn/canWrite/canReadWait` (canlib32.dll) and
  `xlOpenPort/xlActivateChannel/xlCanTransmit/xlReceive` (vxlapi64.dll), then validate on hardware.
- **DoIP** — `Rox.Transport.Doip.DoipClientTransport` is a complete ISO 13400 client (routing activation
  `0x0005`→`0x0006`, diagnostic `0x8001` with `0x8002`/`0x8003`) over real TCP, validated against the
  bundled `SimulatedDoipServer` over a real socket. `TODO(hardware)`: validate against the vehicle's
  DoIP entity on the activation line (pin 8) with the real logical addresses.
- **Battery voltage** — `Rox.Reflash.IVoltageProvider` (simulated by default). `TODO(hardware)`: read the
  real pack/12 V voltage via a DID or the adapter before gating a reflash.

ISO-TP (ISO 15765-2) segmentation and the DoIP state machine are implemented in-house and are
hardware-independent; only the raw CAN frame I/O (`ICanChannel`) is vendor-specific.

## 3. Genuine firmware image(s)

Real `.bin`/`.hex`/`.srec` firmware for any reflash target, in the OEM's expected block format. The
reflash controller sizes blocks from the ECU's own `maxNumberOfBlockLength` (0x34 response) and verifies
the post-flash checksum routine.

## 4. Genuine data package

The real `R11_Oversea.xml`, the ECU folder tree, and the OEM flow XMLs for the specific vehicle/market.
The files under `data/` mirror the observed structure but are **illustrative** — the profile parser and
flow interpreter are real, the sample data is not the OEM's.

## 5. Two open schema items (PRD §17) — confirm from one complete OEM flow file

- **O-1 — request-side element names.** The `<Request>` element in `FlowParser.ParseEcuService` is the
  reconstructed adapter point. One complete flow (`Write VIN.xml`, `Write configword.xml`, or a full
  `adds new key.xml`) confirms the exact `ServiceId`/`SubFunction`/`RequestData` names — change them in
  that one method to freeze the parser.
- **O-2 — `ResponseStatus` enum.** Configurable in `FlowSemantics` with the working hypothesis
  `{2,3}` accepted (`2` = positive, `3` = positive-with-pending-resolved). The reflash flow's pending
  loop settles it definitively; update `FlowSemantics` once confirmed.
- **O-3 — node tags** for UserPrompt / Assign / Loop (their existence is certain; the exact tag names
  are not) — confirm and adjust `FlowParser`.

## 6. Licensing

`Rox.Security.LicenseValidator` is an offline format/checksum stub. `TODO(licensing)`: replace with the
production offline licence-verification scheme (e.g. a signed licence blob checked against an embedded
public key). The suite runs against the simulator regardless of licence state.

## Still Windows-side (built on the Windows CI job, not the Linux core job)

- The WPF/MVVM UI (`Rox.App`, `net8.0-windows`) — dashboard, diagnostics, guided-flow wizard, key
  functions, reflash, expert console, settings.
- The WiX v5 MSI (`installer/Rox.Installer`).

Everything else — `Rox.Core`, `Rox.Profile`, `Rox.FlowEngine`, `Rox.Simulator`, `Rox.Transport`,
`Rox.Uds`, `Rox.Diagnostics`, `Rox.Security`, `Rox.KeyFunctions`, `Rox.Reflash`, `Rox.Logging` — is
cross-platform `net8.0` and builds + tests on Linux and Windows alike.
