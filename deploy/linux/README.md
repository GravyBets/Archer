# Archer private Linux deployment candidate

This is an owner-only staging layout, not an internet-facing production launch. The core listens on loopback 5087, the separate companion gateway on loopback 5094. Use a private mesh VPN with HTTPS (Tailscale Serve, never Funnel), and a private SSH tunnel to the desktop admin page. Do not expose 5087, do not reverse proxy general administrative endpoints, do not open 5094 to the internet. The companion gateway independently requires a validated Auth0 owner token and a one-time P-256 signature from the one owner-approved phone. No API or GitHub key goes to the gateway or phone.

## Before provisioning
- A supported Linux host, .NET 10 SDK for publishing and ASP.NET Core 10 runtime for hosting, systemd, persistent SSD storage, private VPN and SSH key access.
- Review the exact commit and required CI. Keep known-good releases separately. Prepare a replacement machine or encrypted off-host restore.
- Native signed iPhone application: owner-selected Auth0 Native Client ID, configured API audience/scopes, fixed private VPN hostname in Gateway.plist, Secure Enclave key. Apple signing and physical testing are still required.
- Keep branch protection, per-project scoped tokens, provider budgets and owner-only reviews. No generated draft is installed automatically.

## Install a reviewed release
Build with `bash deploy/linux/publish.sh /tmp/archer-release` in a trusted checkout. Record SOURCE_COMMIT with the artifacts. Create distinct non-login system users `archer` and `archergateway`, root-owned release directories under /opt/archer/core and /opt/archer/gateway, and /var/lib/archer/core owned by archer mode 0700. Copy the published files (root owned, read/execute for service users). Install the two unit files from this directory under /etc/systemd/system. Use root-owned mode-0600 environment files /etc/archer/core.env and gateway.env from the examples. Generate a shared 32-byte random bridge key locally; use the same hex value in both files. Keep OpenAI and repo keys ONLY in core.env; do not place them on the gateway or phone.

Start only the core first: `sudo systemctl daemon-reload`; `sudo systemctl enable --now archer-core`; verify `curl --fail http://127.0.0.1:5087/api/health`. Check the journal and stop if startup or state migration fails. If migrating your existing desktop state, stop both old and new processes first, make an independent backup, copy the owner-approved state directory with restrictive permissions, and do not run two writers against it. Reauthorize only intentional work after restart. There is one core writer, no clustered database or automatic failover.

## Private phone access
Connect the server and the iPhone to your private device network with restrictive owner-only ACLs. Configure Auth0 Native app as documented in ios/ArcherMobile; use the exact owner subject and native authorized party, not email or an arbitrary token. Create a hardware key on the signed iPhone and compare its full public fingerprint on both devices. Set the matching ARCHER_COMPANION_DEVICE_SHA256 in gateway.env, along with private hostname, Auth0 settings and bridge key. Set enabled=true only after reviewing this configuration. Start archer-private-gateway and check /health/live locally on 5094.

Use the mesh provider's private HTTPS reverse proxy to forward only to loopback:5094. Configure private HTTPS hostname and verify it matches the signed mobile build's Gateway.plist. For Tailscale Serve, use the current official Serve instructions to configure the local 5094 service and verify it is tailnet-only. Never use Funnel or a public reverse proxy. Default-deny firewall for 5087/5094. For desktop admin use SSH local forwarding e.g. `ssh -N -L 5087:127.0.0.1:5087 <owner-host>` (do not run a competing local Archer on port 5087). Then browse http://127.0.0.1:5087 on the desktop.

If the phone is lost, stop the gateway, revoke the Auth0 session/device, remove its pinned fingerprint, and provision a new one. Restarting the gateway erases outstanding proof challenges. The current gateway intentionally supports one pre-approved hardware key. No remote admin/token-management interface.

## Backup, rollback and health
Set up restic on the host and an owner-controlled encrypted off-host repository, with root-only backup.env and separate recovery password. Initialize and test an actual repository before enabling the optional archer-backup timer. The timer takes a per-file backup of current atomic state; for a consistent release migration/restore, deliberately stop Archer after confirming no paid work is in flight and create a verified offline backup. Verify a restore into a private separate directory before switching. Never restore over the sole live copy. Preserve version and config backups securely and independently of the state backup; the timer excludes secrets. Keep current and prior reviewed software artifacts. Stop services before rollback, verify compatible state and restart core before gateway.

Check service logs, memory/disk, backup status, provider bill and spend safeguards regularly. A rootless sandbox for untrusted generated code requires a separate disposable runner VM; do not enable it on this core/gateway host. This candidate does not yet implement HA, multi-node persistence, automatic certificate/credential recovery, remote push, a real physical-device acceptance test or independent production security review.

## Read-only preflight before accepting production data

After provisioning (and again after every server migration), run the included Go/No-Go check **on the server**, as root. It never starts services, exposes tokens or changes firewall configuration.

```bash
sudo bash deploy/linux/preflight.sh --prepared
# After core and private gateway are running:
sudo bash deploy/linux/preflight.sh --live
```

A successful preflight means only the configured runtime, service accounts, file permissions, paired bridge key, private binding and local health checks passed. **It is not a security audit, a verified public-internet firewall posture, proof of working Auth0/device pairing, or a successful offsite disaster recovery test.** Confirm no public port exposure, the owner's private network ACL, TLS, and an independently restored backup manually. Stop and correct any failure before processing live Archer business or private data.

## Downloadable, tested Linux staging package (never auto-installed)

After **Archer Build** succeeds on a trusted push to protected `main`, the separate **Archer Linux — Verified Private Staging Bundle** GitHub Actions workflow packages the **exact same tested source commit** into the owner-downloadable `Archer-Linux-Staging` artifact. This is only a software bundle: no provider credentials, database, state, approval or remote access are included. The bundle contains the compiled framework-dependent .NET 10 core and separate private gateway, service templates, a read-only readiness check and `SOURCE_COMMIT`. It expires from GitHub Actions after 14 days; download it from your private repository's Actions page when actually provisioning.

Only take the artifact from an official successful run in `GravyBets/Archer`. Verify `SOURCE_COMMIT` against the trusted workflow commit and run `sha256sum -c SHA256SUMS` **before extracting** the bundled tarball. A checksum kept beside a file detects corruption but is not independent proof of authorship; the GitHub Actions run and reviewed commit provide that identity. The outer GitHub artifact download adds another ZIP layer. Do not trust packages sent by email or public websites. Stage the release in fresh owner-restricted directories, retain the prior reviewed release, and stop old workers before migrating any state. Follow the manual systemd and private network steps above. The package does **not** provision the server, install dependencies, apply secrets, open firewall ports or deploy automatically.

The read-only `preflight.sh` now checks that private DNS, Auth0 owner/client identification and device fingerprint values match their exact required formats, not just nonempty placeholders. Even a clean result is not proof of real Auth0 login, phone enrollment, off-site restore or Internet perimeter isolation. Do those acceptance tests before enabling business operations.

## Before moving the Windows Archer history to a server

**Do not migrate or reinstall the owner state by guessing which Data folder is active.** Visual Studio Archer can use `Archer.MissionControl/Data/archer-state.json`, while the standalone installer uses `%LOCALAPPDATA%/Archer/Data/archer-state.json`. Different instances can contain different conversations, missions and approvals. Only migrate one explicitly selected verified snapshot; never overwrite a newer file with a stale copy or blindly merge JSON arrays.

At home, pull the reviewed `main` and **double-click `tools/Archer-CheckData.cmd`**. It runs the checked-in read-only PowerShell audit and leaves the results on screen. For advanced use, run `powershell -NoProfile -File tools/Check-ArcherState.ps1` from the checkout. It reads but does not print saved content, reports which data files exist and whether their histories differ, and warns if Archer appears to be listening on port 5087. It **does not** create a backup, change permissions, copy anything, or upload data to GitHub. If there is more than one different state file, stop and reconcile which is truly active before migration. Also verify the installer has not created a second data folder. The check may display local filesystem paths but not credentials or conversation text.

When ready to migrate, deliberately stop Archer and all voice/helper processes, take an **encrypted off-machine backup** of the selected original data folder, perform a test restore into a separate private location, and verify content counts before starting the new host. Never include the source state in a GitHub issue, pull request, email, chat upload or installer artifact. Running two processes against copied state is not synchronized, and the destination's startup recovery deliberately pauses potentially paid work. Keep the original snapshot until the new server is verified.
