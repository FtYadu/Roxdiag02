# External inputs required for a real vehicle

This core runs fully against the bundled simulator. Talking to an actual ROX 01 / Adamas requires
the following, which **no code can substitute** and none of which are (or should be) embedded here.

1. **Licensed seed-key module** — a native library the operator is licensed to use, exposed to the
   app through `ISecurityProvider.ComputeKey(seed, length)`. The bundled `TestSecurityProvider` is a
   simulator stand-in only and must never be used against a vehicle.

2. **OBD-II / DoIP interface + vendor SDK** — reference target is PCAN-Basic (`PCANBasic.dll`) behind
   the transport abstraction; Kvaser CANlib and Vector XL are stub adapters. A DoIP adapter is needed
   for Ethernet-side ECUs (activation pin 8 on `R11_Oversea`).

3. **Genuine firmware image(s)** for any reflash target, in the OEM's expected block format.

4. **Genuine data package** — the real `R11_Oversea.xml`, the ECU folder tree, and the OEM flow XMLs
   for the specific vehicle/market. The samples in `samples/Rox.Demo/data` mirror the observed
   structure but are illustrative.

5. **Confirmation of two open schema items**, from one complete OEM flow file:
   - the **request-side** element/attribute names (the `<Request>` element here is the reconstructed
     adapter point — swap names in `FlowParser.ParseEcuService`);
   - the **`ResponseStatus` enum** (values + which set means "continue"), currently configurable in
     `FlowSemantics` with the working hypothesis `{2,3}` accepted.

## Still Windows-side (not in this repo)
- WPF/MVVM UI (dashboard, diagnostics, guided-flow wizard, key functions, reflash, expert console)
- ISO-TP framing + DoIP state machine wired to real adapters (interfaces are here; hardware I/O is not)
- Serilog audit sink, PDF/CSV reporting, WiX v5 MSI installer
