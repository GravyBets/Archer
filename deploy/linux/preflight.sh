#!/usr/bin/env bash
# Read-only preflight for the private owner server. Run with sudo after
# provisioning. Does not print token values, start services or change firewall.
set -euo pipefail
mode="${1:---prepared}"
if [[ "$#" -gt 1 || ( "$mode" != "--prepared" && "$mode" != "--live" ) ]]; then
  printf 'Usage: sudo bash deploy/linux/preflight.sh [--prepared|--live]\n' >&2
  exit 2
fi
if [[ "${EUID:-999}" != 0 ]]; then
  printf 'FAIL: Run as root to inspect private configuration permissions.\n' >&2
  exit 2
fi
failures=0
fail(){ printf 'FAIL: %s\n' "$1"; failures=$((failures+1)); }
pass(){ printf 'PASS: %s\n' "$1"; }
warn(){ printf 'CHECK MANUALLY: %s\n' "$1"; }
require_program(){ if command -v "$1" >/dev/null 2>&1; then pass "$2 is installed"; else fail "$2 is missing"; fi; }
require_program dotnet '.NET runtime'
require_program systemctl 'systemd'
require_program curl 'curl'
require_program tailscale 'private device network client'
require_program restic 'encrypted backup client'
if command -v dotnet >/dev/null 2>&1; then
  if dotnet --list-runtimes 2>/dev/null | grep -Eq '^Microsoft\.AspNetCore\.App 10\.'; then
    pass 'ASP.NET Core 10 runtime is installed'
  else
    fail 'ASP.NET Core 10 runtime is missing'
  fi
fi
for account in archer archergateway; do
  if getent passwd "$account" >/dev/null; then
    pass "Dedicated $account service account exists"
  else
    fail "Dedicated $account service account is missing"
  fi
done
check_private_file() {
  local path="$1"
  if [[ ! -f "$path" ]]; then fail "Required private configuration file missing: $path"; return; fi
  if [[ "$(stat -c '%U' "$path")" != root ]]; then fail "Private file must be root owned: $path"; return; fi
  local perms
  perms="$(stat -c '%a' "$path")"
  if [[ ! "$perms" =~ ^[0-7]{3,4}$ ]] || (( (8#$perms & 077) != 0 )); then
    fail "Private file must have no group/other permissions: $path"
  else
    pass "Private config permissions safe: $path"
  fi
}
check_private_file /etc/archer/core.env
check_private_file /etc/archer/gateway.env
check_private_file /etc/archer/backup.env
check_private_file /etc/archer/backup-password
if [[ -d /var/lib/archer/core ]]; then
  if [[ "$(stat -c '%U' /var/lib/archer/core)" == archer &&
        "$(stat -c '%a' /var/lib/archer/core)" == 700 ]]; then
    pass 'Persistent core state is owner-only and writable by its service account'
  else
    fail 'Core state directory must be owned by archer and mode 0700'
  fi
else fail 'Persistent core state directory missing'; fi
for directory in /opt/archer/core /opt/archer/gateway; do
  if [[ -d "$directory" ]]; then pass "Published artifact directory exists: $directory"
  else fail "Published artifact directory missing: $directory"; fi
done

core=/etc/archer/core.env
gateway=/etc/archer/gateway.env
if [[ -f "$core" && -f "$gateway" ]]; then
  # Never source untrusted shell configuration or echo key content.
  read_key() { sed -n 's/^ARCHER_COMPANION_BRIDGE_KEY=//p' "$1"; }
  core_key="$(read_key "$core")"
  gateway_key="$(read_key "$gateway")"
  if [[ "$core_key" =~ ^[0-9A-Fa-f]{64}$ && "$core_key" == "$gateway_key" ]]; then
    pass 'Core and gateway share the same valid private bridge key'
  else
    fail 'Private bridge keys are missing, invalid or mismatched'
  fi
  unset core_key gateway_key
  if grep -qx 'ARCHER_PRIVATE_GATEWAY_ENABLED=true' "$gateway"; then
    pass 'Private companion gateway is explicitly enabled'
  else
    fail 'Private companion gateway not enabled'
  fi
  # Validate exact production gateway input formats without sourcing env or
  # disclosing identity/key values. Placeholder-looking values fail closed.
  read_env_value() {
    local name="$2"
    awk -v target="$name" 'index($0,target "=")==1 {print substr($0,length(target)+2)}' "$1"
  }
  check_gateway_field() {
    local name="$1" pattern="$2" value
    value="$(read_env_value "$gateway" "$name")"
    if [[ "$value" =~ $pattern && "$value" != REPLACE_* ]]; then
      pass "$name has a valid format"
    else
      fail "$name is missing, a placeholder or invalid"
    fi
  }
  check_gateway_field ARCHER_PRIVATE_HOST '^[a-z0-9][a-z0-9.-]+\\.ts\\.net
  if grep -q '^ARCHER_COMPANION_BRIDGE_KEY=' "$core"; then
    pass 'Core companion bridge is configured'
  else fail 'Core companion bridge key missing'; fi
fi

if command -v ss >/dev/null 2>&1; then
  while IFS= read -r local_address; do
    case "$local_address" in
      *:5087|*:5094)
        if [[ "$local_address" == "127.0.0.1:5087" || "$local_address" == "[::1]:5087" ||
              "$local_address" == "127.0.0.1:5094" || "$local_address" == "[::1]:5094" ]]; then
          pass 'Archer listener is restricted to loopback'
        else
          fail 'An Archer listener is exposed on a non-loopback network interface'
        fi
        ;;
    esac
  done < <(ss -lntH | awk '{print $4}')
else
  warn 'Install iproute2 and confirm no Archer listener binds outside loopback'
fi

if [[ "$mode" == "--live" ]]; then
  for service in archer-core archer-private-gateway; do
    if systemctl is-active --quiet "$service"; then pass "$service is running"
    else fail "$service is not running"; fi
  done
  if curl --silent --fail --max-time 5 http://127.0.0.1:5087/api/health >/dev/null; then
    pass 'Core health responds locally'
  else fail 'Core local health check failed'; fi
  if curl --silent --fail --max-time 5 http://127.0.0.1:5094/health/live >/dev/null; then
    pass 'Private gateway health responds locally'
  else fail 'Private gateway local health check failed'; fi
  if tailscale status >/dev/null 2>&1; then pass 'Private device network reports connected'
  else fail 'Private device network is offline'; fi
fi
warn 'Verify firewall and tailnet ACL deny all unapproved devices, and Tailscale Serve (not Funnel) uses private HTTPS'
warn 'Run an actual encrypted off-host backup and test restore before storing unique production data'
warn 'Verify Auth0 scope/audience, real signed iPhone, notifications, operational costs and rollback before production'
if (( failures > 0 )); then
  printf 'Archer server is not ready: %d required check(s) failed.\n' "$failures"
  exit 1
fi
printf 'Archer server preparation checks passed for %s. Manual validation still required.\n' "$mode"

  check_gateway_field ARCHER_PRIVATE_AUTH0_DOMAIN '^[a-z0-9][a-z0-9.-]+\\.auth0\\.com
  if grep -q '^ARCHER_COMPANION_BRIDGE_KEY=' "$core"; then
    pass 'Core companion bridge is configured'
  else fail 'Core companion bridge key missing'; fi
fi

if command -v ss >/dev/null 2>&1; then
  while IFS= read -r local_address; do
    case "$local_address" in
      *:5087|*:5094)
        if [[ "$local_address" == "127.0.0.1:5087" || "$local_address" == "[::1]:5087" ||
              "$local_address" == "127.0.0.1:5094" || "$local_address" == "[::1]:5094" ]]; then
          pass 'Archer listener is restricted to loopback'
        else
          fail 'An Archer listener is exposed on a non-loopback network interface'
        fi
        ;;
    esac
  done < <(ss -lntH | awk '{print $4}')
else
  warn 'Install iproute2 and confirm no Archer listener binds outside loopback'
fi

if [[ "$mode" == "--live" ]]; then
  for service in archer-core archer-private-gateway; do
    if systemctl is-active --quiet "$service"; then pass "$service is running"
    else fail "$service is not running"; fi
  done
  if curl --silent --fail --max-time 5 http://127.0.0.1:5087/api/health >/dev/null; then
    pass 'Core health responds locally'
  else fail 'Core local health check failed'; fi
  if curl --silent --fail --max-time 5 http://127.0.0.1:5094/health/live >/dev/null; then
    pass 'Private gateway health responds locally'
  else fail 'Private gateway local health check failed'; fi
  if tailscale status >/dev/null 2>&1; then pass 'Private device network reports connected'
  else fail 'Private device network is offline'; fi
fi
warn 'Verify firewall and tailnet ACL deny all unapproved devices, and Tailscale Serve (not Funnel) uses private HTTPS'
warn 'Run an actual encrypted off-host backup and test restore before storing unique production data'
warn 'Verify Auth0 scope/audience, real signed iPhone, notifications, operational costs and rollback before production'
if (( failures > 0 )); then
  printf 'Archer server is not ready: %d required check(s) failed.\n' "$failures"
  exit 1
fi
printf 'Archer server preparation checks passed for %s. Manual validation still required.\n' "$mode"

  check_gateway_field ARCHER_PRIVATE_NATIVE_CLIENT_ID '^[A-Za-z0-9_-]{12,128}
  if grep -q '^ARCHER_COMPANION_BRIDGE_KEY=' "$core"; then
    pass 'Core companion bridge is configured'
  else fail 'Core companion bridge key missing'; fi
fi

if command -v ss >/dev/null 2>&1; then
  while IFS= read -r local_address; do
    case "$local_address" in
      *:5087|*:5094)
        if [[ "$local_address" == "127.0.0.1:5087" || "$local_address" == "[::1]:5087" ||
              "$local_address" == "127.0.0.1:5094" || "$local_address" == "[::1]:5094" ]]; then
          pass 'Archer listener is restricted to loopback'
        else
          fail 'An Archer listener is exposed on a non-loopback network interface'
        fi
        ;;
    esac
  done < <(ss -lntH | awk '{print $4}')
else
  warn 'Install iproute2 and confirm no Archer listener binds outside loopback'
fi

if [[ "$mode" == "--live" ]]; then
  for service in archer-core archer-private-gateway; do
    if systemctl is-active --quiet "$service"; then pass "$service is running"
    else fail "$service is not running"; fi
  done
  if curl --silent --fail --max-time 5 http://127.0.0.1:5087/api/health >/dev/null; then
    pass 'Core health responds locally'
  else fail 'Core local health check failed'; fi
  if curl --silent --fail --max-time 5 http://127.0.0.1:5094/health/live >/dev/null; then
    pass 'Private gateway health responds locally'
  else fail 'Private gateway local health check failed'; fi
  if tailscale status >/dev/null 2>&1; then pass 'Private device network reports connected'
  else fail 'Private device network is offline'; fi
fi
warn 'Verify firewall and tailnet ACL deny all unapproved devices, and Tailscale Serve (not Funnel) uses private HTTPS'
warn 'Run an actual encrypted off-host backup and test restore before storing unique production data'
warn 'Verify Auth0 scope/audience, real signed iPhone, notifications, operational costs and rollback before production'
if (( failures > 0 )); then
  printf 'Archer server is not ready: %d required check(s) failed.\n' "$failures"
  exit 1
fi
printf 'Archer server preparation checks passed for %s. Manual validation still required.\n' "$mode"

  check_gateway_field ARCHER_PRIVATE_OWNER_SUBJECT '^auth0\\|[A-Za-z0-9_-]{1,120}
  if grep -q '^ARCHER_COMPANION_BRIDGE_KEY=' "$core"; then
    pass 'Core companion bridge is configured'
  else fail 'Core companion bridge key missing'; fi
fi

if command -v ss >/dev/null 2>&1; then
  while IFS= read -r local_address; do
    case "$local_address" in
      *:5087|*:5094)
        if [[ "$local_address" == "127.0.0.1:5087" || "$local_address" == "[::1]:5087" ||
              "$local_address" == "127.0.0.1:5094" || "$local_address" == "[::1]:5094" ]]; then
          pass 'Archer listener is restricted to loopback'
        else
          fail 'An Archer listener is exposed on a non-loopback network interface'
        fi
        ;;
    esac
  done < <(ss -lntH | awk '{print $4}')
else
  warn 'Install iproute2 and confirm no Archer listener binds outside loopback'
fi

if [[ "$mode" == "--live" ]]; then
  for service in archer-core archer-private-gateway; do
    if systemctl is-active --quiet "$service"; then pass "$service is running"
    else fail "$service is not running"; fi
  done
  if curl --silent --fail --max-time 5 http://127.0.0.1:5087/api/health >/dev/null; then
    pass 'Core health responds locally'
  else fail 'Core local health check failed'; fi
  if curl --silent --fail --max-time 5 http://127.0.0.1:5094/health/live >/dev/null; then
    pass 'Private gateway health responds locally'
  else fail 'Private gateway local health check failed'; fi
  if tailscale status >/dev/null 2>&1; then pass 'Private device network reports connected'
  else fail 'Private device network is offline'; fi
fi
warn 'Verify firewall and tailnet ACL deny all unapproved devices, and Tailscale Serve (not Funnel) uses private HTTPS'
warn 'Run an actual encrypted off-host backup and test restore before storing unique production data'
warn 'Verify Auth0 scope/audience, real signed iPhone, notifications, operational costs and rollback before production'
if (( failures > 0 )); then
  printf 'Archer server is not ready: %d required check(s) failed.\n' "$failures"
  exit 1
fi
printf 'Archer server preparation checks passed for %s. Manual validation still required.\n' "$mode"

  check_gateway_field ARCHER_COMPANION_DEVICE_SHA256 '^[A-Fa-f0-9]{64}
  if grep -q '^ARCHER_COMPANION_BRIDGE_KEY=' "$core"; then
    pass 'Core companion bridge is configured'
  else fail 'Core companion bridge key missing'; fi
fi

if command -v ss >/dev/null 2>&1; then
  while IFS= read -r local_address; do
    case "$local_address" in
      *:5087|*:5094)
        if [[ "$local_address" == "127.0.0.1:5087" || "$local_address" == "[::1]:5087" ||
              "$local_address" == "127.0.0.1:5094" || "$local_address" == "[::1]:5094" ]]; then
          pass 'Archer listener is restricted to loopback'
        else
          fail 'An Archer listener is exposed on a non-loopback network interface'
        fi
        ;;
    esac
  done < <(ss -lntH | awk '{print $4}')
else
  warn 'Install iproute2 and confirm no Archer listener binds outside loopback'
fi

if [[ "$mode" == "--live" ]]; then
  for service in archer-core archer-private-gateway; do
    if systemctl is-active --quiet "$service"; then pass "$service is running"
    else fail "$service is not running"; fi
  done
  if curl --silent --fail --max-time 5 http://127.0.0.1:5087/api/health >/dev/null; then
    pass 'Core health responds locally'
  else fail 'Core local health check failed'; fi
  if curl --silent --fail --max-time 5 http://127.0.0.1:5094/health/live >/dev/null; then
    pass 'Private gateway health responds locally'
  else fail 'Private gateway local health check failed'; fi
  if tailscale status >/dev/null 2>&1; then pass 'Private device network reports connected'
  else fail 'Private device network is offline'; fi
fi
warn 'Verify firewall and tailnet ACL deny all unapproved devices, and Tailscale Serve (not Funnel) uses private HTTPS'
warn 'Run an actual encrypted off-host backup and test restore before storing unique production data'
warn 'Verify Auth0 scope/audience, real signed iPhone, notifications, operational costs and rollback before production'
if (( failures > 0 )); then
  printf 'Archer server is not ready: %d required check(s) failed.\n' "$failures"
  exit 1
fi
printf 'Archer server preparation checks passed for %s. Manual validation still required.\n' "$mode"

  if grep -q '^ARCHER_COMPANION_BRIDGE_KEY=' "$core"; then
    pass 'Core companion bridge is configured'
  else fail 'Core companion bridge key missing'; fi
fi

if command -v ss >/dev/null 2>&1; then
  while IFS= read -r local_address; do
    case "$local_address" in
      *:5087|*:5094)
        if [[ "$local_address" == "127.0.0.1:5087" || "$local_address" == "[::1]:5087" ||
              "$local_address" == "127.0.0.1:5094" || "$local_address" == "[::1]:5094" ]]; then
          pass 'Archer listener is restricted to loopback'
        else
          fail 'An Archer listener is exposed on a non-loopback network interface'
        fi
        ;;
    esac
  done < <(ss -lntH | awk '{print $4}')
else
  warn 'Install iproute2 and confirm no Archer listener binds outside loopback'
fi

if [[ "$mode" == "--live" ]]; then
  for service in archer-core archer-private-gateway; do
    if systemctl is-active --quiet "$service"; then pass "$service is running"
    else fail "$service is not running"; fi
  done
  if curl --silent --fail --max-time 5 http://127.0.0.1:5087/api/health >/dev/null; then
    pass 'Core health responds locally'
  else fail 'Core local health check failed'; fi
  if curl --silent --fail --max-time 5 http://127.0.0.1:5094/health/live >/dev/null; then
    pass 'Private gateway health responds locally'
  else fail 'Private gateway local health check failed'; fi
  if tailscale status >/dev/null 2>&1; then pass 'Private device network reports connected'
  else fail 'Private device network is offline'; fi
fi
warn 'Verify firewall and tailnet ACL deny all unapproved devices, and Tailscale Serve (not Funnel) uses private HTTPS'
warn 'Run an actual encrypted off-host backup and test restore before storing unique production data'
warn 'Verify Auth0 scope/audience, real signed iPhone, notifications, operational costs and rollback before production'
if (( failures > 0 )); then
  printf 'Archer server is not ready: %d required check(s) failed.\n' "$failures"
  exit 1
fi
printf 'Archer server preparation checks passed for %s. Manual validation still required.\n' "$mode"
