#!/bin/sh
# Trusted image entrypoint. The owner chooses fixed targets at image build time.
# Never evaluate source/model text as a shell command. All repository commands
# run inside the constrained container, never on the Archer host.
set -eu
umask 077
cd /work
mkdir -p src home nuget
export HOME=/work/home DOTNET_CLI_HOME=/work/home NUGET_PACKAGES=/work/nuget
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
tar --extract --gzip --file=- --strip-components=1 --no-same-owner --no-same-permissions --directory=/work/src || exit 30
cd /work/src
# Trusted image configuration, not repository-provided commands.
case "${ARCHER_BUILD_TARGET:-}" in /*|*..*|""|[-]*) exit 30;; esac
case "${ARCHER_TEST_TARGET:-}" in /*|*..*|""|[-]*) exit 30;; esac
test -f "$ARCHER_BUILD_TARGET" && test -f "$ARCHER_TEST_TARGET" || exit 30
dotnet restore "$ARCHER_BUILD_TARGET" --configfile /opt/archer/NuGet.Config --disable-parallel || exit 30
dotnet restore "$ARCHER_TEST_TARGET" --configfile /opt/archer/NuGet.Config --disable-parallel || exit 30
dotnet build "$ARCHER_BUILD_TARGET" --no-restore --configuration Release --disable-build-servers || exit 10
dotnet test "$ARCHER_TEST_TARGET" --no-restore --configuration Release --disable-build-servers || exit 20
