# Archer on Windows — no Visual Studio required to run

**The installer has NOT been installed or tested on the owner's own computer.**
After green `Archer Build` on a trusted push to `main`, a separate GitHub
Actions job now automatically builds an owner-downloadable package. Manual
`workflow_dispatch` remains available. Packaging is not deployment and
does not create a server, phone connection, or automatic PC updater.

## First approved installation

1. Open GitHub → Actions → **Archer Windows — Verified Owner Installer Package**
   and choose the newest **successful** run for a trusted `main` commit.
   The package is built automatically after the main CI succeeds. If needed,
   you can also choose **Run workflow** on `main` manually.
2. Download the workflow artifact named **Archer-Windows-Installer**.
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

## Voice quality rollback (November 2026)

The previous experimental System.Speech tray listener produced unreliable
recognition and a robotic Windows TTS voice on the owner PC. It is therefore
**disabled by default**. Opening Archer from the desktop shortcut once again
uses the familiar browser voice, including existing British voice and speaking
pace preferences. The browser may require clicking Speak once to enable
microphone recognition. **No-button wake-word functionality is not yet
delivered reliably**, and should not be claimed as complete.

An old tray listener from the previous installer receives a new session token
and shuts down after you relaunch Archer. If its tray icon is still present,
right-click **Exit Archer voice**. The companion code remains available only
for explicit, informed compatibility testing with
`ARCHER_ENABLE_LEGACY_VOICE=1` in the owner process environment; do not set
that flag unless willing to use lower-quality Windows voices and legacy speech
recognition. This compatibility switch does not enable a new Windows service,
remote microphone, startup task, or automatic account action.

A reliable native wake solution will need a better microphone transcription
engine **and** voice output that preserves the selected British male voice.
Neither can be verified on the owner's hardware by GitHub Actions alone.

## Native, no-button "Hey Archer" voice

When you launch the **Archer desktop shortcut** on Windows, it starts a
separate owner-session **Archer desktop voice** tray process using Microsoft's
installed local Windows Speech Recognition engine (SAPI). The voice listener
starts with Archer; **you do not click Speak or the orb**. Say **"Hey Archer"**
alone, listen for the Windows wake chime, then ask your question normally.
During the conversation, subsequent questions require no wake phrase.
After **two minutes without speech**, Archer returns to wake-only mode.
A **two-second pause** ends each spoken request.

The listener remains available if the browser tab is closed while the local
Archer server is still running. It does **not** autostart at Windows sign-in.
Open the desktop shortcut once after signing in. The microphone is used in
your Windows session, and an Archer system-tray icon appears while listening;
right-click it to **Pause microphone**, **Resume microphone**, or **Exit Archer
voice**. Closing Archer's server also stops the companion after health checks.

**Windows requirements:** an installed English Windows Speech Recognition
engine (legacy System.Speech), the selected default microphone, and desktop
app microphone permission. If no compatible speech recognizer is installed,
Archer shows **Desktop microphone unavailable** with a diagnostic message.
Windows Speech Recognition availability and dictation accuracy depend on
the PC's language packs/Windows version; this isn't the same browser voice
engine. The native voice uses installed Windows TTS voices, preferring British
English when present; its rate approximates your 1.25× browser preference
rather than exactly matching it.

**Privacy:** Before wake, only the offline two-word recognition grammar is
loaded—no ambient transcription or audio is sent to Archer/OpenAI or saved.
After wake, locally recognized requests are sent to Archer's existing
loopback API, so normal AI chat can incur the same model usage as typed chat
and remains subject to configured budget controls. The status file stores
only microphone state, timestamp and process ID, **not recordings**.
Mission creation, execution, account actions and payments still require
existing explicit on-screen approval; native voice does not approve them.

The browser Speak controls remain available as a fallback if the native
Windows speech engine fails, but they are hidden when the tray listener
is active to avoid competing microphone sessions.

## Subsequent updates

1. Review/merge the code change you want and confirm `Archer Build` passes.
2. Download and extract the artifact from the matching successful
   `Archer Windows — Verified Owner Installer Package` workflow run. **Close Archer before installing**; the
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

This is an **owner-installed package distribution** with automatic build
artifacts after trusted green `main` CI. It intentionally
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
