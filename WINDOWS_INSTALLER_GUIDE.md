# Archer on Windows — no Visual Studio required to run

**Review-only. The Windows installer has NOT been published, downloaded, or run on the owner's computer.**
The packaging workflow is a GitHub Actions artifact builder, not a deployment
to your PC. This change does **not** create a server, phone integration, or
automatic self-updater.

## First approved installation

1. Review and merge the installer PR to the `main` branch.
2. Open GitHub → Actions → **Archer Windows — Owner-Approved Installer Package**
   → **Run workflow** on `main`.
3. Download the workflow artifact named **Archer-Windows-Installer**.
4. Extract the GitHub artifact ZIP once, then extract the contained
   `Archer-Windows-Installer.zip` if present. You should see
   `Archer-Install.cmd`, `Install-Archer.ps1`, `archer-app.zip`,
   `archer-app.sha256`, `Archer-Rollback.cmd`, and
   `HOW-TO-INSTALL.txt`. **Do not extract `archer-app.zip` manually.**
5. Double-click `Archer-Install.cmd`. The script verifies the SHA256,
   checks the archive cannot escape the staging folder, and installs into
   `%LOCALAPPDATA%\Archer\current`. It creates the `Archer` desktop shortcut.
6. Open the Archer shortcut. It launches the local ASP.NET Core app at
   `http://127.0.0.1:5087/`, waits for its health endpoint, and opens your
   browser. No new cloud service or Visual Studio installation is needed.
7. Keep the installed package folder until you are confident the app works.
   **These are unsigned local scripts; use only artifacts from your own
   approved Archer repository.** A matching SHA256 guards accidental
   corruption but is **not** cryptographic proof of who published the ZIP.

## Subsequent updates

1. Review/merge the code change you want.
2. Manually run the same owner-approved Windows package workflow on `main`.
3. Download and extract the artifact. **Close Archer before installing**; the
   installer refuses to replace a running localhost server.
4. Double-click `Archer-Install.cmd`. The old app is moved into
   `%LOCALAPPDATA%\Archer\previous`, and the new app replaces only
   `current`. Your persistent data remains separate.
5. If the new version fails, close Archer and use `Archer-Rollback.cmd`
   from the downloaded installer folder. This swaps the app binaries back.
   The data folder is untouched, but if the app changes persistent data
   schemas in the future, a binary rollback alone may not undo migrations.
   Back up your data before a schema-changing update.

## Mission/history migration from Visual Studio

The current source-tree prototype stores conversations and missions under
`Archer.MissionControl\Data\archer-state.json` unless
`ARCHER_DATA_DIR` was already set. The installer does **not** silently move
or delete it.

To retain this history, **stop both Archer processes**, make a backup of the
old `Data` directory, and copy the files into
`%LOCALAPPDATA%\Archer\Data` **only if no newer installed data would be
overwritten**. This may include spend-ledger and gateway state files, not
just the conversation JSON. Verify the state in Archer afterward. Personal
API keys remain user environment variables, never in ZIP artifacts.

## Troubleshooting

- If Archer says port 5087 is in use, close the currently running Archer
  window/process first; no other processes are force-stopped.
- If the SHA256 check fails, re-download from your own workflow. Do not
  disable the verification.
- If Windows blocks unsigned scripts, review the source and your Windows
  policy. Do not lower company or managed-device execution policies.
- If your browser opens before a healthy local API responds, launch
  `current\Archer.MissionControl.exe` directly for diagnostic console
  output and ensure no other Archer instance is running.
- If the installer is missing files, make sure you extracted the GitHub
  artifact and the outer installer bundle but left `archer-app.zip` intact.

## Security and scope

This is an **owner-approved manual package distribution**. It intentionally
does NOT:
- auto-fetch releases, auto-install GitHub code, merge PRs, deploy a server,
  grant Archer developer-admin permissions, or bypass local model budgets;
- run installed code as a service, require administrator rights, or open router
  ports / expose port 5087 to the public internet;
- modify or delete the separate `Data` folder.

The eventual production updater should use publisher signing, signed
release manifests, incremental updates and a health-check rollback agent.
This simple reproducible approach removes the repetitive `git pull` /
`dotnet publish` workflow without hiding which version gets installed.
