#!/usr/bin/env bash
set -euo pipefail
# Run from a reviewed checkout at an exact verified commit, not a generated draft.
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
out="${1:-$root/artifacts/linux}"
mkdir -p "$out"
dotnet publish "$root/Archer.MissionControl/Archer.MissionControl.csproj" -c Release -o "$out/core"
dotnet publish "$root/Archer.PrivateGateway/Archer.PrivateGateway.csproj" -c Release -o "$out/gateway"
git -C "$root" rev-parse HEAD > "$out/SOURCE_COMMIT"
printf 'Prepared reviewed core and gateway artifacts in %s\n' "$out"
