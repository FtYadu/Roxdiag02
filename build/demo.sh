#!/usr/bin/env bash
# One-command acceptance demo: build, run the full cross-platform test suite, and run the end-to-end
# console walkthrough against the bundled simulator (DTC read/clear/read-back, guided Add-Key flow,
# ISO-TP + DoIP transport, 0x78 pending, guarded key pairing, checksum-verified reflash, PDF report).
set -euo pipefail
cd "$(dirname "$0")/.."

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

echo "== offline guard =="
bash build/check-no-network.sh

echo; echo "== build (Release) =="
dotnet build ROXDiagnostic.sln -c Release

echo; echo "== test (Release) =="
dotnet test ROXDiagnostic.sln -c Release

echo; echo "== end-to-end demo (simulator target) =="
dotnet run -c Release --project samples/Rox.Demo
