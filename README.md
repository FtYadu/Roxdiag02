# ROX Offline Diagnostic Suite — Core

Cross-platform .NET 8 core of the ROX / Polestones / Jishi **ROX 01 / Adamas** offline diagnostic
suite (profile `R11_Oversea`). This repository is the engine room: the flow-configuration
interpreter, the UDS/NRC layer, the vehicle-profile parser, and an in-process ECU simulator so the
whole thing runs and is testable **with no vehicle and no special hardware**.

> The WPF desktop UI, the real CAN/DoIP hardware adapters (PCAN/Kvaser/Vector P-Invoke), the
> user-supplied seed-key module, and firmware images are the Windows-side / external remainder.
> See `docs/EXTERNAL_INPUTS.md`. Nothing here embeds any OEM security algorithm.

## Build, test, run

```bash
# one-time: install .NET 8 SDK if needed (https://dot.net)
dotnet build -c Release
dotnet test  -c Release
dotnet run   -c Release --project samples/Rox.Demo
```

## Projects

| Project          | Responsibility |
|------------------|----------------|
| `Rox.Core`       | UDS service IDs, NRC table + recommended actions, UDS response parsing, DTC + J2012 decode |
| `Rox.Profile`    | Parse the `R11_Oversea` ETSData profile (CAN + DoIP buses / pinout) |
| `Rox.FlowEngine` | FlowConfiguration XML → AST → interpreter; variable store; all node types; both parser quirks |
| `Rox.Simulator`  | In-process UDS/ECU server + a clearly-labelled **test-only** seed-key for the sim |
| `Rox.Demo`       | Console walkthrough: profile parse, DTC read→clear→read-back, Add-Key guided flow |
| `Rox.Tests`      | xUnit: NRC/DTC/UDS decode, profile parse, both quirks, end-to-end key flow, security-denied |

## Flow engine — the two rules that matter

Derived from the real captured flow fragment (see the PRD, schema §5.4):

1. **Trailing `ConnectSign` is a no-op.** The editor emits a connector on the last `OneCondition`
   too; the interpreter never reads it.
2. **Consecutive bare acceptance-`If` blocks on the same variable = an OR-set.** One `If` per
   acceptable state. `ResponseStatus ∈ {2,3}` means "continue". The accepted-set and the
   `ResponseStatus` value mapping are **configurable** (`FlowSemantics`), because that enum is an
   open item — never hard-code it.

## Security boundary (non-negotiable)

`ISecurityProvider.ComputeKey(seed, length)` is the *only* path to a key. The app ships no OEM
algorithm. `Rox.Simulator` includes `TestSeedKey` / `TestSecurityProvider` purely so the handshake
completes against the simulator — it is labelled test-only and has no security value. A real vehicle
requires the operator's licensed module.
