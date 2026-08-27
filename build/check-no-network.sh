#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# Offline guard (NON-NEGOTIABLE #1): the suite makes no cloud / telemetry /
# analytics / auto-update / external HTTP calls. The ONLY network I/O allowed
# is DoIP TCP/UDP to the vehicle or simulator on the local link, which uses
# System.Net.Sockets. This script fails the build if a disallowed network API
# or an outbound-HTTP style dependency is introduced into the source tree.
#
# It greps C# sources (excluding bin/obj and this script) for banned symbols.
# Sockets are deliberately NOT banned — DoIP needs them.
# ---------------------------------------------------------------------------
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

# Banned API surface (case-sensitive symbols that imply outbound internet I/O).
BANNED_APIS=(
  'System\.Net\.Http'
  '\bHttpClient\b'
  '\bHttpClientFactory\b'
  '\bWebClient\b'
  '\bHttpWebRequest\b'
  '\bWebRequest\b'
  '\bFtpWebRequest\b'
  '\bSmtpClient\b'
  '\bWebSocket\b'
  '\bClientWebSocket\b'
  'System\.Net\.Mail'
)

# Banned NuGet packages (telemetry / analytics / auto-update / HTTP frameworks).
BANNED_PACKAGES=(
  'ApplicationInsights'
  'Microsoft\.AppCenter'
  'Sentry'
  'Segment'
  'GoogleAnalytics'
  'Squirrel'
  'AutoUpdater'
  'RestSharp'
  'Flurl'
  'Refit'
)

fail=0

# Search only tracked-source directories (never generated bin/obj output;
# ImplicitUsings auto-emits a global `using System.Net.Http` into obj/).
CS_FILES=$(find src samples tests installer -name '*.cs' \
  -not -path '*/bin/*' -not -path '*/obj/*' 2>/dev/null || true)

echo "== offline guard: scanning C# sources for banned network APIs =="
for pat in "${BANNED_APIS[@]}"; do
  if [ -n "$CS_FILES" ] && echo "$CS_FILES" | xargs grep -nE "$pat" 2>/dev/null; then
    echo "ERROR: banned network API matched pattern: $pat"
    fail=1
  fi
done

echo "== offline guard: scanning project files for banned packages =="
PROJ_FILES=$(find . -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' 2>/dev/null || true)
for pat in "${BANNED_PACKAGES[@]}"; do
  if [ -n "$PROJ_FILES" ] && echo "$PROJ_FILES" | xargs grep -nE "$pat" 2>/dev/null; then
    echo "ERROR: banned package matched pattern: $pat"
    fail=1
  fi
done

if [ "$fail" -ne 0 ]; then
  echo ""
  echo "OFFLINE GUARD FAILED: a disallowed network dependency was introduced."
  echo "Only DoIP TCP/UDP via System.Net.Sockets on the local link is permitted."
  exit 1
fi

echo "OFFLINE GUARD PASSED: no disallowed network I/O found."
