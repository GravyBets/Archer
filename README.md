# ARCHER — Personal Autonomous AI Operator

**Version:** 1.13.0 (local prototype)  
**Platform:** ASP.NET Core / .NET 10, vanilla HTML/CSS/JavaScript  
**License:** Personal prototype; no production deployment or multi-user authentication.

This is a **real starting codebase**, not a screenshot or a mockup. It includes:

- Animated **Archer** landing page as the default interface, with text conversation and optional browser voice
- Mission Control dashboard with mission creation and tracking
- **Developer Workspace** with allowlisted GitHub source inventory, selected-file previews, and review-mission drafts
- Opt-in **Operations** page for public HTTPS site monitoring and local outage/recovery records
- Expiring, user-declared **Presence** context with optional one-time approximate browser location
- Persistent **Inbox** for locally observed website incidents and failed mission tasks (not push or SMS)
- Local **Attention** view showing failed tasks; approvals are clearly marked as not yet integrated
- AI-generated bounded mission plans (3–5 research/analysis tasks)
- Local main-dashboard AI usage analytics (token counts, observed web searches, estimated costs)
- Configurable automatic routing between routine/chat, research/planning and code-review models
- Run **one** or **up to three** tasks sequentially, saving their reports
- Opt-in local **auto-run up to three tasks** in the background, stopping on errors or restart
- Built-in public web research through the OpenAI Responses API `web_search` tool
- Conversational assistant with recent chat and mission context
- Persistent missions, tasks, activity history, chats, and research reports (local JSON file)
- Error tracking, task retries, and recovery from interrupted runs
- Local-only HTTP server bound to `127.0.0.1` (not exposed to the internet)

**Not included yet:** SMS, phone calls, recurring scheduled execution, reliable always-on cloud autonomy, tools that modify files or GitHub, financial operations, multi-user authentication, commercial reliability, or hard API spending enforcement. The UI makes no claims that these are implemented.

## Run on your Windows development PC

1. Install or upgrade to the **.NET 10 SDK** in Visual Studio. `.NET 9` reached end of support in November 2026.
2. Extract the ZIP anywhere outside your work projects, such as `Documents\\Archer`.
3. Open `Archer.MissionControl\\Archer.MissionControl.csproj` in Visual Studio 2026. This project has no additional NuGet dependencies.
4. Run the project. Navigate to **http://127.0.0.1:5087**. Without an API key, the dashboard and mission creation work, but AI actions will display an informative error.
5. When ready to use the AI features, create an OpenAI API key on the OpenAI Platform. Set it as an **environment variable** named `OPENAI_API_KEY`, **not in source code**. Restart Visual Studio so it picks up the new variable.
6. Optional: set `ARCHER_MODEL` to a supported Responses API model. Default is `gpt-5-mini`. Enable appropriate API billing limits/alerts in your OpenAI project dashboard.
7. In Mission Control: **Use starter mission → Generate Mission Plan → Run next step**, or create your own mission.

### PowerShell: set your API key for your Windows user

Run this in **your own PowerShell console** (the command will prompt privately, without echoing the key). It persists only in your Windows user environment:

```powershell
$secure = Read-Host 'OpenAI API key' -AsSecureString
$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $key = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    [Environment]::SetEnvironmentVariable('OPENAI_API_KEY', $key, 'User')
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
    Remove-Variable key -ErrorAction SilentlyContinue
}
```

Close and reopen Visual Studio before running Archer. To run without Visual Studio, use `dotnet run --project Archer.MissionControl/Archer.MissionControl.csproj`.

> **Costs:** Responses API usage is billed separately from a ChatGPT subscription. Each plan, task, and chat request may cost money; a research task can also incur web-search tool charges. Archer limits task *count per click or explicit auto-run authorization* but does **not** yet implement a hard cost cap. Start with a low billing alert/budget and monitor usage.

## Data and privacy

By default, data is stored in `Archer.MissionControl/Data/archer-state.json`. The `Data` directory is excluded by `.gitignore`. Back up this file if you want to keep your mission and conversation history. Setting `ARCHER_DATA_DIR` lets you move data to another local folder.

Your mission descriptions, chat messages, and selected previous reports are sent to the OpenAI API when you request AI actions. Do **not** enter employer credentials, production secrets, banking logins, or confidential company data. Do not use this prototype as an always-on public service.

The UI intentionally stores output **as text**, not rendered HTML, and does not let the AI execute arbitrary shell commands. The existing research tool can access the public web, not your accounts.

## Project layout

```
Archer.MissionControl/
  Program.cs                 HTTP endpoints, bounded task execution
  Domain/Models.cs           Missions, tasks, artifacts, activity, chat
  Services/JsonFileStore.cs  Persistent single-user local state
  Services/ArcherModelRouting.cs  Task-specific model selection
  Services/ApiUsageLedger.cs  API usage and estimated cost persistence
  Services/MissionAutoRunner.cs  Opt-in bounded background research
  Services/GitHubRepositoryReader.cs  Allowlisted read-only GitHub code snapshots
  Services/ArcherAiService.cs  OpenAI Responses API integration
  wwwroot/index.html         Mission Control dashboard
  wwwroot/css/app.css        Dashboard theme
  wwwroot/js/app.js          Browser UI / API calls
```

## First goals

- [x] Create standalone Archer project
- [x] Build Mission Control interface
- [x] Implement local mission state, task history, and report artifacts
- [x] Connect a configurable AI service over the Responses API
- [x] Bound research/analysis actions to 1–3 tasks per run
- [x] Opt-in local background execution of up to three tasks
- [ ] Build + test on Windows with .NET 10 SDK
- [ ] Real cost metering / per-mission spending ceiling
- [ ] SQLite or PostgreSQL data persistence and migrations
- [ ] Reliable background worker/scheduler and reconnecting clients
- [ ] Twilio text messages with consent and number registration
- [ ] Voice calls with opt-in timing and fail-safe controls
- [ ] Sandbox coding agent with isolated branch, tests, and approvals
- [ ] Authentication before remote access or cloud hosting

## Test status

The code was created in a development sandbox that **does not have the .NET SDK**. Therefore it has **not been compiled or run against the live OpenAI API** in this environment. Static JavaScript syntax and JSON configuration can be checked; the first Windows build is still required. Do not treat this as a production-ready agent.

## Updating from v0.1.0 or v0.1.1 to v0.1.2 (mission-plan JSON fix)

1. Stop Archer/Visual Studio debugging.
2. **Back up** `Archer.MissionControl/Data/archer-state.json` if that file exists.
3. Copy the updated source files over your existing project, **without deleting or replacing the `Data` folder**. To use the smallest patch, copy only `Archer.MissionControl/Services/ArcherAiService.cs` from the update zip over the same file in your project.
4. Rebuild and run, and click **Generate Mission Plan** again on your existing mission. You do **not** need to create a new mission or API key.

What changed: planning requests use low reasoning effort with an 8,000-token shared reasoning/output budget; plans are limited to three short steps; incomplete Responses API statuses and malformed JSON are handled safely; responses that refuse the request are reported clearly. Verify `/api/config` reports `version: 0.1.2` after rebuilding. This prevents saving partial plans but cannot guarantee every request succeeds. The API still charges for generation attempts.


## GitHub workflow

Clone `https://github.com/GravyBets/Archer.git` and open `Archer.MissionControl/Archer.MissionControl.csproj` in Visual Studio 2026. Pull from `main` before rebuilding. The repository includes source code only: do not commit your `Data` directory, API keys, `.env` files, credentials, or local configuration.

If you already ran Archer from an extracted ZIP, the cloned repository is a **different local directory**. Back up `Archer.MissionControl/Data/archer-state.json` and copy it into the cloned project only while Archer is stopped, if you wish to preserve that mission. Do not push it to GitHub.


## v0.1.3 — Research execution budget fix (November 2026)

- Fixes the first research step failing immediately with `max_output_tokens`: research now explicitly requests **low reasoning effort** and has a **6,500-token combined reasoning/output budget**, rather than the previous default reasoning and 2,200-token limit.
- Web research is limited to **three web tool calls** per task and requests a focused 450–750-word report with grounded URLs. Analysis tasks use a 4,000-token budget and low reasoning effort.
- Chat now uses low reasoning effort and a 2,600-token budget so short conversations are less likely to fail before producing visible text.
- Extends the HTTP client timeout to four minutes for slower web research requests.
- Makes incomplete-response error messages apply accurately to both plans and research tasks.

### Test on Windows

1. Stop Archer, **Git → Pull** the latest `main`, and rebuild.
2. Confirm `http://127.0.0.1:5087/api/config` says `0.1.3`.
3. On the existing mission's failed first task, click **Retry** to queue it, then **Run next step**. You do **not** need to recreate the mission or change the API key.
4. Open **Deliverables** to inspect citations, useful findings, and uncertainties. A successfully completed HTTP request is not proof all research facts are correct.

This is a bounded mitigation, not a guarantee. Very long research can still hit token limits or HTTP timeouts. No automatic paid retries were added; manual Retry costs API credits. This version is **not yet tested live** against the owner's OpenAI API account.

## v0.1.4 — Private project knowledge (November 2026)

The first successful GeoShift mission revealed a grounding issue: Archer looked for the user's project name on the public web and confused it with unrelated entities. This update:

- Reminds the AI that a project may be private or unpublished; a public name match is **not** evidence that it is the user's project.
- Adds an **optional private project brief** that Archer includes in mission planning, research, analysis, and chat requests.
- Keeps the private context out of GitHub by reading it from the ignored `Data` folder. The project does not create or populate the file automatically.

### Set up Archer's local project knowledge

1. Stop Archer, pull the latest `main`, and rebuild.
2. Create the folder `Archer.MissionControl/Data` if it doesn't exist.
3. Create a plain UTF-8 text file named `Archer.MissionControl/Data/archer-knowledge.md`.
4. Describe your own software projects, target customers, definitions, important facts, and goals. For example:

```markdown
# My projects
GeoShift is my own unpublished KML/KMZ to CSV conversion utility.
GeoShift is not the Texas consultancy or any other public company with that name.
The target customer needs reliable geographic data migration and file conversion.
Investigate KML/KMZ conversion competitors, customer pain points, and pricing;
don't search for GeoShift as if it were an established public product.
```

5. Run Archer and confirm `/api/config` reports `0.1.4`.
6. Continue the existing mission. Earlier deliverables remain unchanged; the new background will inform **future** tasks only.

Optional: point `ARCHER_KNOWLEDGE_FILE` to another local file. If it is a relative path, Archer resolves it from the project content root. The first 12,000 characters are included in API requests. Don't store API keys, secrets, or employer-confidential data in this file: **the context is sent to the OpenAI API when you run AI tasks**.

This is the first small step toward longer-term memory, not a complete autonomous memory system. It does not automatically inspect private repositories, read all prior conversations, or independently verify the user's project details.

## v0.2.0 — Opt-in automatic research execution

Archer now has a local background task worker. For a mission with pending research/analysis tasks, click **Auto-run up to 3 tasks** to explicitly authorize up to three API task attempts. The worker advances the mission sequentially **without further clicks** while the local application process remains running.

- Each authorization permits at most **three task attempts**, including unsuccessful ones.
- A failed task stops automatic execution. There are no automatic paid retries.
- Use **Pause automatic execution** to prevent additional tasks. The currently running API request may finish.
- Archer stops after completing the mission, exhausting the authorized attempts, or encountering an error.
- **Every app restart disarms all missions** and never initiates new paid calls without fresh authorization.
- The UI polls for progress and keeps expanded report panels open during updates.
- The current feature supports **only research and analysis**. No unrestricted web automation, email, SMS, phone calls, deployment, financial transactions, scheduled recurrence, or automatic creation of additional missions.

**API costs:** This is an authorization limit in task attempts, **not** a hard dollar cap. Each attempt can incur model and web-search fees. Set billing limits/alerts on the OpenAI Platform and monitor actual usage.

### Try it

1. Stop Archer and pull the latest `main`.
2. Build and run in Visual Studio. Verify `http://127.0.0.1:5087/api/config` reports `version: 0.2.0`.
3. Keep your completed GeoShift revenue mission as history; create a new test mission and generate a plan.
4. Click **Auto-run up to 3 tasks** once. Leave Archer running and watch the steps and deliverables update.
5. To verify pausing, enable auto-run on another mission and press **Pause automatic execution** before the next task begins.
6. Restart Archer and confirm auto-run is off until you explicitly re-enable it.

Do not assume this is reliable unattended service yet. It only works while the local Archer process is running, and this update has not been compiled or live-tested in this tool environment.

## v0.2.1 — Read-only GitHub source inspection (November 2026)

Archer can now read an explicitly requested GitHub repository **directly**, rather than trying to infer its code from a web search. This resolves the problem where research reports said they couldn't access GeoShift's source.

**How it works:**
- A mission must contain a GitHub URL, e.g. `https://github.com/GravyBets/GeoShift`.
- The repository must be in the explicit allowlist (default: `GravyBets/GeoShift`). Other repositories require the `ARCHER_GITHUB_ALLOWLIST` environment variable, containing comma-separated `owner/repo` entries.
- Archer fetches the default branch's file inventory and a bounded sample (at most 9 eligible source files and approximately 23,000 characters), then sends those file excerpts alongside the mission task to OpenAI.
- Source snippets are cached in memory for up to ten minutes to avoid unnecessarily repeated GitHub API calls.
- Read access is **not** write access: Archer still cannot edit, commit, pull or push code, open a pull request, run build tools, deploy, or control the hosting environment.
- The selection favors GeoShift core conversion files, analysis UI and tests. Reviews must label findings as partial whenever only a sample of the repository was read.
- By design, source comments are reference material only; Archer must not obey commands embedded in repository files.

**Private repository option:** If an allowed repository is private, create a fine-grained GitHub token restricted to **only that repository** with **Contents: Read-only**. Configure it as a **user environment variable** named `ARCHER_GITHUB_READ_TOKEN` on the PC running Archer, and restart Visual Studio. **Never** put the token in GitHub or `archer-knowledge.md`. The current GeoShift repository may be publicly readable, in which case this extra token is unnecessary.

**Test WITHOUT spending OpenAI API credits:**
1. Stop debugging, pull from `main`, rebuild and start Archer.
2. Confirm `http://127.0.0.1:5087/api/config` says version `0.2.1`.
3. Open `http://127.0.0.1:5087/api/github/check?repository=https%3A%2F%2Fgithub.com%2FGravyBets%2FGeoShift`.
4. The result should say `status: readable` and list the actual source files sampled. If not, fix GitHub access first.
5. To evaluate the code, create a **new mission** referencing the GitHub URL and request focused improvements. Earlier completed reports do not retroactively change.
6. Only run the new mission after successful read-only testing. Each AI task will incur API usage charges.

**Limitations:** A bounded snapshot is not a comprehensive security audit, a full code review, proof of a successful compilation, or automated issue remediation. The application still runs only on localhost and performs no repository writes.


## v0.2.2 — Better code-review sampling

- The read-only GitHub reader now prioritizes actual GeoShift converter, analyzer, and web UI implementation files before supporting tests.
- The previous filename matching inadvertently prioritized test files such as `KmlCsvConverterTests.cs` over `KmlCsvConverter.cs`.
- No change to GitHub permissions or auto-run behavior: repository reading remains allowlisted and read-only.
- The `/api/github/check` endpoint shows which source files Archer selected; use it before paying for another code-review task.
- Restart Archer after pulling the update to clear its in-memory GitHub snapshot cache. The reader remains intentionally **partial** and cannot compile, test or edit GeoShift code.

## v0.2.3 — Source-verified code reviews

After a real GeoShift development-review mission, several confident recommendations turned out to describe protections already in the code: longitude/latitude range checks, CSV escaping, skip-count warnings, and the 10-data-row preview limit. This version strengthens the **AI's code-review instructions**:

- Verify whether the behavior is already implemented before recommending a change.
- State the inspected source file and method and classify each finding as **Verified defect**, **Possible issue**, or **Already handled**.
- Do not infer absent functionality from an incomplete source snapshot.
- Re-check earlier AI-generated reports against actual code rather than repeating unverified claims.
- Specifically examine coordinate range checks, quoted CSV output, header versus data preview rows, and whether the KMZ stream guard's extra byte is intentional.
- Never claim a build, test run, or repository modification occurred unless a real tool performed it.

**Scope:** This is a prompt-level guardrail, not an independent verifier or guarantee of correct reports. Archer still samples only part of the repository and cannot make code changes. For a fair comparison, create a **new development review mission** after upgrading to 0.2.3; previously completed reports do not regenerate automatically.

## v0.3.0 — Usage analytics and adaptive model routing

### Mission Control: usage and estimated costs

Mission Control now contains an **AI Usage & Cost** section with:
- Estimated API cost this **UTC calendar month** and across all tracked Archer requests.
- Input and output token totals, request counts, and observed web-search tool calls.
- Per-model request counts, token counts, and estimated month-to-date cost.
- The latest eight tracked API requests, including operation/model/time/token count/estimated cost.

The local endpoint `GET /api/usage` exposes the same non-secret summary. No OpenAI administration key is needed: Archer reads usage from each **Responses API result** and persists the ledger in `Archer.MissionControl/Data/archer-usage.json`. The entire `Data` folder is Git-ignored.

**Limitations that matter:**
- Usage tracking starts **after installing v0.3.0**. It cannot reconstruct earlier requests or an OpenAI account's complete usage.
- The displayed dollar amounts are **estimates, not an invoice**. They use published standard text rates from November 15, 2026 and $0.01 per observed `web_search_call`, with cached-input token discounts where reported.
- Charges may differ due to processing tier, regional pricing, cached writes, long prompts, external tools, or pricing changes. Failed requests that do not return a `usage` object cannot be measured; responses marked incomplete **are** recorded if they include usage.
- Unknown model prices show **unpriced** requests and are not silently counted as $0. API billing on the OpenAI Platform remains authoritative.
- The dashboard **does not cap spending** or create automatic retries. Do not confuse a model output-token limit with a dollar budget.

### Adaptive model selection

Adaptive routing is enabled by default and does **not** require an additional AI call to choose the model.

| Archer activity | Default model | Reasoning effort |
| --- | --- | --- |
| Routine chat | `gpt-6-luna` | low |
| Mission planning | `gpt-6.1-terra` | low |
| Public web research | `gpt-6.1-terra` | low |
| General analysis | `gpt-6.1-terra` | low |
| Code review with an allowlisted repo URL | `gpt-6.1-sol` | medium |

These models are subject to the OpenAI project's access and rate limits. Archer does not silently fall back to a second paid model on error: check the error, then change configuration if needed.

**Environment variables** (set in the Windows user environment and restart Visual Studio):

- `ARCHER_MODEL_ROUTING=adaptive` (default) enables automatic routing.
- `ARCHER_MODEL_ROUTING=fixed` uses a single model for every AI call.
- `ARCHER_MODEL=gpt-5-mini` selects the fixed model; if not set, the fixed-mode default is `gpt-5-mini`.
- `ARCHER_MODEL_CHAT`, `ARCHER_MODEL_PLAN`, `ARCHER_MODEL_RESEARCH`, `ARCHER_MODEL_ANALYSIS`, and `ARCHER_MODEL_CODE` override individual adaptive tiers.

**Cost comparison, standard input/output rates per 1M tokens (USD, November 15, 2026):**

- `gpt-5-mini`: $0.25 / $2.00
- `gpt-6-luna`: $0.10 / $0.50
- `gpt-6.1-terra`: $1.00 / $5.00
- `gpt-6.1-sol`: $2.00 / $10.00

Current pricing: https://developers.openai.com/api/docs/pricing
Project-level billing: https://platform.openai.com/settings/organization/billing/overview

### Local test

1. Stop Archer and use **Git → Pull**; rebuild.
2. Verify `http://127.0.0.1:5087/api/config` returns `version: "0.3.0"` with `routing.mode: "adaptive"`.
3. Verify `http://127.0.0.1:5087/api/usage` is available and shows zero tracked usage initially.
4. On the main Mission Control page, look for the **AI Usage & Cost** section above the mission list.
5. Send a short message in **Talk to Archer** and refresh the dashboard. The API should report a chat request using `gpt-6-luna` and usage tokens and an estimated cost.
6. Start a research or code-review mission after ensuring your API account allows the selected model. The breakdown should reflect the actual model used.
7. Check `Data/archer-usage.json` is present and **not** staged in Git. Do not upload this file publicly.

If your account lacks the newer models, set `ARCHER_MODEL_ROUTING=fixed`, keep `ARCHER_MODEL=gpt-5-mini`, restart Visual Studio, and continue using the usage dashboard. Adaptive routing can be re-enabled later.

### Acceptance

The CI build and JS syntax check cover compilation and static syntax. They cannot validate API account access or real billing. Test the live models with a small number of low-cost requests before allowing auto-run.


## v0.4.0 — Archer Presence UI, optional voice, mission oversight

The **Archer** home screen is now the default launch destination. Mission Control remains a separate tab and retains its missions, deliverables, and token/cost analytics. No changes were made to the persistent mission schema or API data paths.

### Archer landing page

- An original CSS-rendered glowing sphere animates gently while idle and changes visual behavior for **listening**, **thinking**, **text responding**, **speaking**, **mission alerts**, and **errors**.
- The sphere is a real accessible button: when the browser supports speech recognition it starts/stops **push-to-talk**; otherwise it focuses the text composer.
- Status tiles show local ready/running missions, tasks completed today **in the browser's timezone**, and locally recorded failed tasks. Click a tile to open the relevant view.
- Mission Control remains unchanged as the operational dashboard, including its usage and cost section and auto-run controls.
- Navigation: **Archer**, **Mission Control**, **Attention**, **Activity**. No backend task or safety authorization change.

### Browser voice (optional, never always-on)

- **Speak** starts the Web Speech recognition API only after you click it; browser microphone permission may be required. Dictation populates the message field **without submitting it automatically**. Review and press Send.
- **Read Archer's replies aloud** uses the browser's speech-synthesis capability (off by default); **Read last reply** starts speech on demand. **Stop speaking** cancels browser speech. Long automatic playback is limited to the first 1,800 characters and the full text always remains visible.
- Voice support differs by browser and OS. If microphone recognition is unavailable, Archer's text chat still works. On some platforms recognition may be processed by the browser vendor's cloud service. Do not dictate passwords, credentials, or sensitive information.
- Text messages and transcripts are sent to Archer's configured AI API **only when you submit** the chat form. Archer does not save audio recordings.
- This version does **not** provide outbound telephone calling, an Archer phone number, SMS, voice-API streaming, a wake word, or unattended listening.

### Attention and approvals

- **Attention** lists genuinely failed tasks in the local mission state, with navigation to those missions for inspection or manual retry.
- The **Approvals** panel is deliberately informational: no tools with approval-capable external actions are connected yet.
- No production uptime monitoring, payment processing, customer messaging, SMS, incident calling, or actual business approval queue was implemented by this UI update. Do not interpret "0 failed tasks" as "the business is healthy."

### Accessibility and privacy

- Orb and navigation are buttons; speech state is announced via a live status region.
- Press **Enter** in the chat input to send; **Shift+Enter** inserts a newline.
- Orb animations respect `prefers-reduced-motion` and can remain static when your OS requests reduced motion.
- No third-party image, icon library, front-end CDN, or audio library is required.
- The text conversation and mission data continue using Archer's existing local JSON store. The app remains bound to localhost and has **no remote authentication**.

### Manual acceptance test

1. Stop Archer, pull this release from `main`, build and run. Verify `/api/config` shows `0.4.0`.
2. The app must launch on **Archer**, showing the animated orb and conversation panel.
3. Select **Mission Control**; existing missions, analytics, artifacts and auto-run controls should all remain functional.
4. Test a short text chat: the orb should change to **Thinking**, then **Responding**. The chat history should show both sides.
5. If supported in your browser, press **Speak**, allow microphone access, dictate a short phrase, review its transcription and submit. Toggle spoken replies and test **Read last reply** and **Stop speaking**.
6. Open **Attention**. It must show only local failed tasks (if any) and explicitly say no external approval workflows are connected.
7. Use a narrow/mobile viewport and, optionally, Windows reduced-motion settings; the page should remain usable.
8. Confirm `Data/archer-state.json` and `Data/archer-usage.json` remain intact and Git-ignored.

GitHub CI validates .NET compilation, JavaScript syntax, static UI references and no-key local HTTP health. Browser speech, real microphone permissions and visual animation still require local/manual testing. This update does not incur new API charges merely by opening the home page.


## v0.4.1 — Transparent plasma orb and particle energy field

- Reworked the home sphere into a **translucent layered glass/plasma shell** with soft interior illumination rather than a solid turquoise fill.
- Added a lightweight, transparent browser Canvas layer with **76 softly orbiting particles** and **short, curved energy filaments**. No sharp lightning bolts, flashing strobe effects, external libraries, images, or API calls.
- Energy motion responds to the existing Archer state: calm orbit at idle, gentle inward pull while listening, faster cool-blue motion while thinking, bright shimmer for responses, and muted amber/red accents for alerts/errors.
- Particle rendering is capped near **30 frames/sec** and pauses when the page/tab isn't visible. It respects `prefers-reduced-motion`, rendering a static particle field for users who disable motion.
- The canvas is decorative (`aria-hidden`, `pointer-events: none`); the orb remains a real clickable/focusable microphone-or-chat control.
- No changes to API integration, chat handling, missions, model routing, cost reporting, GitHub access, or user data storage.

### Verify on your PC

1. Stop Archer, **Git → Pull**, rebuild and run. Check `/api/config` reports `0.4.1`.
2. The home sphere should be glassier and less opaque, surrounded by drifting teal motes and very faint curved filaments.
3. Talk to Archer: thinking should make energy move faster and shift bluer; responding should brighten it. If available, microphone listening should pull particles slightly inward. Failed tasks should shift the field warmer.
4. Resize the browser and switch between Archer and Mission Control; the canvas should resize and stop spending CPU on animations when hidden.
5. If your system uses reduced motion, the particles should become static while the chat remains fully operational.
6. Check that mission history, usage/cost metrics, and voice controls are unaffected.

CI checks .NET compilation, all three JavaScript files, DOM references and local API health. Visual quality and smoothness still need a Windows browser check; there is no need to create a mission or spend API credits just to see the new orb.

## v0.5.0 — Developer Workspace: inspect the actual GeoShift code

This release adds a **read-only Developer** page that gives you direct, explicit control over which repository files Archer examines.

### What it does

- Use the existing fine-grained read-only GitHub access (default allowlist: `GravyBets/GeoShift`).
- Load the default branch and list up to 300 eligible C#, Razor, project, and Markdown files directly from the GitHub API.
- Filter by filename, select **one to six files**, and preview their actual contents with source links and line numbers. Individual previews are bounded to 9,000 characters, and the combined preview is limited to 34,000 characters.
- Click **Prepare review mission** to prefill the normal new-mission dialog. You can edit or cancel it. No AI request occurs until you subsequently choose to generate and run its plan.
- When a mission contains a `SOURCE FILES: path1, path2` line, Archer's research execution fetches those **exact selected files** (bounded excerpts) instead of using its generic nine-file sample. Missing or ineligible files cause an explicit error rather than a fabricated review.
- Snapshot caching is keyed by the specific source selection, so the new selected-file review cannot accidentally reuse the earlier generic code sample.

### What it does not do

- **No code writing, commits, branches, pull requests, builds, deployments, or production access.** Those require a separately authorized coding-agent workflow.
- The file preview is **not AI analysis** and doesn't consume OpenAI API credits. Creating a mission without executing it also doesn't use API credits.
- If a mission runs, Archer sends the selected bounded source excerpts to OpenAI. Do not review employer-confidential, personal, or credential-bearing files.
- A code preview is limited; the full repository was not automatically audited. Mark unsupported defect claims as hypotheses.
- API cost tracking and limits are unchanged. This release does not implement any hard spending cap.

### Test when you're back at the PC

1. Stop Archer, **Git → Pull**, rebuild and run. Verify `/api/config` says `0.5.0`.
2. Open **Developer** in the left navigation.
3. Leave the URL as `https://github.com/GravyBets/GeoShift`; click **Load files**.
4. Filter for `KmlCsvConverter.cs`, `Analyze.razor`, or `KmzAnalyzer.cs`. Select up to six files and click **Preview selected**.
5. Confirm the preview matches GitHub and note any truncation warning.
6. Click **Prepare review mission**. Check the prefilled objective and the `SOURCE FILES:` line. You can cancel without making a mission or incurring charges.
7. Optionally create the review mission and run it to test exact-file source grounding. AI planning and task execution incur API charges.

Use `GET /api/development/repository?url=https%3A%2F%2Fgithub.com%2FGravyBets%2FGeoShift` for a raw source file inventory, and `POST /api/development/preview` with JSON `{"repository":"https://github.com/GravyBets/GeoShift","paths":["GeoShift.Core/Services/KmlCsvConverter.cs"]}` for a source preview.

The automated checks cover the .NET build, JavaScript syntax and DOM references, application startup, and rejection of non-allowlisted repositories. They don't prove private GitHub access or source correctness, which require a live test.

## v0.5.1 — More conversational Archer and British voice preference

- Archer's chat instructions now favor **normal human conversation**: direct, warm, concise (usually 30–100 words for ordinary exchanges), with no forced bullet-point essays or exaggerated British stereotypes. Detailed technical answers remain available when needed.
- Browser speech synthesis now **prefers installed British English voices**, prioritizing voices whose names suggest natural/masculine delivery (such as Ryan or George). You can select a different browser voice from the dropdown. Archer **does not guarantee a male or natural-sounding voice** if your OS/browser doesn't expose one.
- The voice selector and read-aloud preference are saved in the browser's local storage. No audio recordings are retained.
- Voice read-aloud remains **opt-in**. Click **Read last reply** for an on-demand preview; if no compatible voice is installed, the UI says so.
- This implementation uses the existing built-in browser TTS, not the soon-deprecated OpenAI Speech API. A genuinely branded natural British voice will require a future realtime speech integration and live audition.
- This release does **not** enable SMS, telephone calls, always-on listening, or unapproved outbound communications.

Test: pull, rebuild, check `/api/config` version 0.5.1; ask Archer a casual question, inspect reply length/tone, open the voice picker, and listen to a short reply. Actual voice quality depends on browser/OS and needs a Windows test.

## v0.6.0 — Presence and Operations foundation

This builds the **first secure-ish local prototype** of Archer as an operations manager, while keeping actual phone/SMS delivery and iPhone background location firmly **not connected**.

### Presence: Archer can adapt to what YOU tell him

The home page now has a **Your context** card. Choose **Available, Driving, At work, Shopping, Busy, or Other**; optionally enter a general place description; select 15 minutes, one hour, four hours, or eight hours; then Save.

- Nothing is collected until you take action. All context **expires automatically** and can be deleted immediately via **Clear**.
- **Share location once** requests browser geolocation permission once, rounds coordinates to two decimal places before saving (roughly neighbourhood-scale, accuracy varies), and never performs background tracking or stores routes.
- Coordinates are only attached when you also click **Save context**. Archer never infers that you're driving, working, or shopping from coordinates.
- Context remains in Archer's ignored local `Data/archer-state.json`. Expired data is cleared during startup recovery or periodic housekeeping when the app is running. No timeline/history is created.
- **Let Archer use this context in conversations** is unchecked by default. Only when checked and context is still active does the AI request include activity, place label and optional coarse coordinates. Without this checkbox, Archer's chat does not receive these details. The local Archer server still stores them temporarily.
- Browser location identifies the **device running that browser**, not an independently connected iPhone. Because Archer binds to `127.0.0.1`, visiting it from your phone is not currently supported. Future opt-in background phone awareness needs an authenticated native mobile companion or a supported mobile integration.
- This is **not automatic driving detection**, activity recognition, geofencing, calendar access or passive location tracking.

### Operations: explicit public website health checks

The new **Operations** page can register up to ten approved public HTTPS websites. They are added in the **stopped** state and only polled once you press **Start monitoring**.

**Before adding a site**, set the Windows user environment variable `ARCHER_MONITOR_HOSTS` to the exact comma-separated public DNS hostnames you approve, such as `yourdomain.com,api.yourdomain.com`. Restart Visual Studio/Archer after changing it. Archer will reject anything not on that explicit allowlist; hosts must use HTTPS on port 443 without credentials, query strings or fragments.

- Website checks run every five minutes **only while the Archer server is running**.
- The worker uses HTTPS GET with response headers only, no cookies, no proxy and no redirect following. It blocks obviously private/reserved address resolutions. Never monitor internal/work systems using this prototype.
- After **three consecutive failed checks** it records a local outage incident. A recovery is also recorded when the site responds again.
- Incidents can be manually **acknowledged** from Operations; an acknowledged event stays visible in the bounded local incident history.
- There are **no automatic webhooks, texts, calls, emails, payment actions, or AI calls** triggered by monitoring. A failed website check isn't evidence of a real business outage. Archer should verify before escalating.
- Operations checks only reachability. It cannot see sales, subscriptions, refunds, customer complaints or conversion quality. Those require explicit future integrations with payment processors, analytics, support and production telemetry.
- An enabled monitor remains enabled across app restarts, but no checks happen while Archer is shut down. Unlike research auto-run, monitoring itself makes no paid AI requests. Network requests to each approved site do occur when the process runs.

### Planned mobile and notification architecture

- Native iPhone companion (or authenticated supported integration), foreground user-permitted activity/location, optionally background modes only with granular consent and a visible on/off switch.
- Presence TTL, no historical GPS route logging, data-minimisation, account authentication and a way to revoke any sharing.
- Business telemetry adapters with source timestamps and evidence for alerts.
- Notification priority, quiet hours, verified phone number, SMS and outbound voice provider, explicit opt-in, spending caps, rate limiting and delivery logs.
- **Normal:** daily brief by text once the business integrations are trustworthy.
- **Urgent:** text/call according to approved escalation criteria, with duplicate suppression and false-alert protection.
- Production business automation must run on an authenticated, monitored host rather than a browser on a personal computer.

### Verify when you pull

1. Stop Archer, **Git → Pull**, rebuild, start and verify `/api/config` reports `0.6.0`.
2. On Home, set a context such as **Driving** with a short expiry and leave **Share with AI** off. Confirm the status changes and that Clear removes it. Only if you want, test one-time approximate location via browser permission.
3. Optionally check **Share with AI**, ask Archer "What did I tell you I'm doing?", and verify he uses the current, consented context without claiming to track your phone. Uncheck/clear to stop sharing.
4. On Operations, see the empty registry and the honest **Not connected** status for SMS and phone.
5. Optionally set `ARCHER_MONITOR_HOSTS` to a website you own, add its HTTPS URL, and explicitly start monitoring. Observe a timestamp after a check. Never add local, private, employer or unapproved sites.
6. Verify Mission Control, Developer Workspace, cost analytics, chat and voice still behave as before.

CI covers .NET compilation, JS/DOM wiring, local API health, coordinate rounding and clearing, and rejection of unapproved monitoring hosts. It does not prove real SMS/calling, native iOS background support, successful monitoring against your production site or accurate business-revenue data.

## v0.7.0 — Daily briefing previews and alert priorities

Archer can now turn **actual local records** into a concise, no-cost business-operations report, viewable and copyable from the **Operations** page.

### Grounded daily briefing

- Completed tasks in the previous 24 hours, current failed tasks and active/ready missions.
- Recent observed public HTTPS availability, outages and recoveries. Old website status (>20 minutes without an observed check) is explicitly **stale**, not counted as a current confirmed outage.
- Measured AI requests and estimated local API spending during the past 24 hours and the current UTC month. These costs are estimates, not OpenAI billing statements.
- **Revenue, subscriptions, customer counts, conversion rates, payment failures and support tickets are intentionally reported as NOT CONNECTED**, not guessed.
- A concise plain-text report can be copied. **No AI API call** is made to prepare or preview it, and it does not include personal GPS history.

### Suggested alerts, delivery preferences and quiet hours

- Current, recently checked site outages (three failed checks) are **Critical** suggestions; degraded websites and locally failed research tasks are **Attention**; recovered sites are informational.
- The alert preview includes the supporting source, reason, and *proposed* delivery channel (future urgent call, text, or briefing).
- Set your intended daily briefing hour, timezone, quiet hours and whether critical calls could override quiet hours **after** a verified delivery provider and phone have been configured.
- Quiet-hour routing is an **advisory preview**, never an active call or text. Changing these settings does **not** schedule a job or generate a notification.
- Supported timezones: America/Chicago, America/New_York, America/Denver, America/Los_Angeles, America/Phoenix, and UTC. Times use a 24-hour clock; equal start/end hours disable quiet hours. Default: Chicago/Central, 8:00 a.m. draft briefing, quiet 10:00 p.m. to 7:00 a.m.; quiet-hour override disabled.
- Preferences persist in Archer's existing Git-ignored local `Data/archer-state.json`. No phone number or message-provider credentials are stored.
- The Operations interface clearly says **DRAFT ONLY / NOT SENT** and always lists unconnected sources.

### Endpoints

- `GET /api/briefing` — evidence-only aggregate with `briefingText`, `metrics`, `alerts`, `deliveryStatus: "preview_only"`.
- `GET /api/briefing/preferences` — timezone choices, current preferences, and `deliveryEnabled: false`.
- `POST /api/briefing/preferences` — saves approved values, but doesn't send or schedule anything.

### Test when you're at your PC

1. Stop Archer, **Git → Pull**, rebuild and run. Check `GET /api/config` shows `0.7.0`.
2. Open **Operations** and review **Daily briefing preview**. Confirm it only contains real local counts and marks revenue/payments as unavailable.
3. Change the daily briefing hour, timezone and quiet hours; **Save preferences** and click **Refresh briefing**. Verify the quiet-hours badge changes when appropriate.
4. Click **Copy briefing** and check the content. Nothing should be sent automatically.
5. If monitoring is enabled, ensure old/stale checks aren't presented as current verified outages.
6. Confirm Archer's missions, usage/cost metrics, personal presence settings and developer workspace still work.

**Still not implemented:** background hosting, actual daily scheduled delivery, SMS provider, outbound phone calling, phone verification, production monitoring, payment/revenue integrations or autonomous corrective actions. Later integrations must apply authenticated delivery consent, event deduplication, abuse/cost controls, real-world observability checks and phone quiet hours. The current implementation intentionally remains localhost-only.

## v0.8.0 — Local AI spending safeguards

Archer now has a conservative, persistent preflight allowance for **all Archer-controlled paid OpenAI requests**, including chat, planning, research and bounded mission auto-run.

**Default controls:** $10 per UTC calendar month, $1 maximum preflight reservation for one request, and a **Pause all paid AI requests** switch on Mission Control below usage analytics. These settings are editable in Mission Control and persisted in ignored `Data/archer-usage.json`. API routes: `GET /api/spending`, `POST /api/spending/settings`. The spending page displays known estimated spending, uncertain charges after unsuccessful/unmetered requests, and costs reserved by in-flight requests.

The guard reserves a conservative allowance *before* sending an API request. Observed token/tool usage replaces the reservation when available. If a request fails without reported usage, it conservatively counts its reservation as **uncertain spend** rather than assuming it was free. Reservations remaining after a crash stay held until reconciled; they are never silently cleared. The next UTC month starts a fresh monthly budget.

**Not a guaranteed hard cost cap.** Provider price schedules, unexpected input expansions, tool pricing, unmetered failed requests, paid requests by other software, and billing lag can exceed local estimates. Set an actual platform-level API project budget/alert and ensure the account has billing controls outside Archer too. Pausing prevents new Archer requests; it does not necessarily cancel requests already in flight.

Known models are explicitly priced using the local November 2026 price reference; unknown model prices stop new requests rather than being estimated as $0. To change the model, use the existing Archer model environment settings.

**Testing:** Pull from `main`, rebuild, verify `/api/config` version `0.8.0`, check the spending guard in Mission Control, modify/save limits, toggle Pause, and confirm AI work is blocked while paused. Unpause deliberately and test one cheap chat before enabling auto-run again. No prior mission data should be lost.

**Remaining future controls:** Provider-side budget reconciliation, per-mission cost accounting, and payment-provider balance/alerts once those integrations are approved.

## v0.9.0 — Owner-reviewed AI coding drafts and opt-in draft pull requests

The **Developer** page now lets you select **one or two existing .cs/.razor files**, describe a narrow GeoShift change, and click **Generate local code proposal**. This performs one paid OpenAI code-model call *through Archer's v0.8 spending guard*. It returns strict structured full-file edits for the selected paths only, with a short summary and recommended tests. All proposed source is stored locally in Archer's ignored `Data/archer-state.json`. You can inspect the **original and proposed source for every file** before taking any further action. No code is run or published by generation alone.

### Highly controlled GitHub publishing

Archer can **optionally** open a **draft PR** on a separate `archer/draft-...` branch, only when *all* conditions are satisfied:

1. You explicitly opt in by setting the Windows user environment variable `ARCHER_CODING_ENABLED=true`.
2. You set `ARCHER_GITHUB_WRITE_REPOS=GravyBets/GeoShift` (or the exact approved owner/repository).
3. You create a fine-grained GitHub token **restricted to that one repository** with **Contents: Read and write** and **Pull requests: Read and write** permissions, and set the token as `ARCHER_GITHUB_WRITE_TOKEN` in your Windows **user environment**, NEVER in the repository, a mission, or local knowledge file.
4. You restart Visual Studio/Archer.
5. You inspect the proposed source, click **Approve and open draft PR**, and separately confirm the exact repository and paths in the confirmation dialog.

The publishing API checks the allowed repository and current GitHub blob SHAs **before** creating a branch, refuses unsupported paths and oversized edits, then creates an isolated branch and commits at most two pre-reviewed files serially. It opens a **draft** pull request (no auto-merge). Any failure disarms that publish attempt and requires manual GitHub inspection before retrying, because a branch might have been created.

**Archer cannot merge, deploy, run arbitrary shell commands, modify workflow files, or make production changes through this feature.** It doesn't run GeoShift tests locally. Existing CI in the GeoShift repository *may* execute when a PR opens; you must independently inspect those checks and the code diff. A model-generated patch can still be wrong or insecure. Inspect every line before approving a merge.

### Extra safety notes

- PR publishing is **OFF by default**, even if Github read-only previews work.
- Do not store GitHub write tokens or other credentials inside Git. They are read by Archer at runtime from a local environment variable and are not included in API results.
- The existing localhost dashboard is unauthenticated. **Never expose port 5087 to your LAN or the internet**, or grant publishing tokens in a shared/untrusted workstation.
- Explicitly generated code proposals involve **paid model usage and source disclosure to OpenAI**; browser previews and previously generated local proposals do not.
- The default v0.8 local spending preflight continues to apply to code generation.
- Maximum two files of 9,000 input characters each; generated files capped at 12,000 characters. Oversized/truncated files are rejected rather than guessed about.

### Test

1. Pull and rebuild; check `/api/config` reports `0.9.0`.
2. In Developer, load the allowed GeoShift repo, select one or two small files, write a narrow improvement request.
3. Ensure your spend allowance is sufficient and choose **Generate local code proposal**. Review the whole original/proposed content and recommended tests.
4. Without setting publishing environment variables, confirm that the draft PR button is disabled and `GET /api/development/code-proposals` reports `publishingEnabled: false`.
5. If and only if you want to test GitHub writes, configure the restricted token and explicit opt-in, regenerate or load the saved proposal, approve a draft PR, review the resulting GitHub branch/PR, and let GitHub CI run independently.
6. Do not merge or deploy until you're happy with the generated changes and actual test results.

The CI checks Archer's .NET build, JavaScript wiring, local startup, and that publishing is **disabled by default**. Actual paid code generation and token-based PR publishing require your Windows environment and explicit consent; the CI does not perform either.

## v1.0.0 — Optional SMS, British phone alerts and mobile presence ingress

This release adds an **optional, disabled-by-default Twilio delivery adapter**. Archer can now submit an explicitly authorized test text or voice call to a single configured destination. Owners may also separately opt in to scheduled daily SMS briefings and calls for critical website availability incidents. **Nothing is sent without external credentials, an exact phone number, explicit configuration AND your consent.**

### Twilio setup (only when you're ready)

Set these **Windows user environment variables** (not in source control, chat transcripts, repositories, or local project-knowledge notes):

- `ARCHER_TWILIO_ENABLED=true` (exact lowercase `true` is required)
- `TWILIO_ACCOUNT_SID=AC...` (your own account SID)
- `TWILIO_AUTH_TOKEN=...` (your own private auth token)
- `ARCHER_TWILIO_FROM=+1...` (your authorized Twilio sending number)
- `ARCHER_TWILIO_TO=+1...` (your own phone, verified/approved as required)
  
Restart Visual Studio and Archer so the server receives the variables. No keys or full phone numbers are displayed in the UI. The **Operations → SMS & phone alerts** panel shows a masked recipient and configuration status.

Click **Send one test SMS** or **Place one test call** only when you actually want a possibly billable communication; each requires a confirmation dialog. These are real provider requests, NOT dry runs. Status `Submitted` means the provider accepted an API request, **not** that the phone received it. Review Twilio's own delivery status for confirmation.

The test voice uses Twilio's `Polly.Brian-Neural` with British English (`en-GB`), a male neural voice listed by Twilio. Availability, pronunciation, naturalness and charges depend on your account. Phone calls are not currently live, two-way AI conversations—these are **outbound one-way spoken notifications**. Twilio documentation: https://www.twilio.com/docs/voice/twiml/say/text-speech

**Twilio pricing, SMS segmentation, telephone airtime and neural TTS fees are separate from Archer's OpenAI token budget.**

### Optional automatic reports and alerts

After a successful test, you can explicitly enable these **separate** settings in Operations:

- **Daily SMS briefing:** One brief, truncated summary around your configured local time on a day when Archer is actually running and not in quiet hours. The scheduler checks a five-minute window; if Archer is offline at that time it does NOT backfill or send an unexpected catch-up message.
- **Urgent website outage call:** One-way British-voiced call after **three consecutive failed public HTTPS checks**, when the last check is less than 20 minutes old, the outage has a current, unacknowledged incident, and quiet hours permit it (unless you've separately opted into critical overrides). A website reachability alarm is **not proof** that payment processing or all site functionality failed.
- Delivery attempts are durably reserved before provider calls. **Maximum three SMS attempts per UTC day and one call attempt per UTC day**, including manual tests. Each daily briefing date and outage incident has its own deduplication key across Archer restarts. Ambiguous provider failures are marked `Uncertain`, count against limits, and **are never automatically retried**. Actual provider billing may still occur.
- You can disable either form of automatic delivery at any time from Operations. The app still runs only on `127.0.0.1`, and none of this works when your PC/server is shut down.
- This does **not** integrate with GeoShift payments, analytics, customer support or customer communication. Those require separate business-specific integrations and authorization.

**Safety:** Make your first test call while you can check your phone and Twilio account. Do not initially enable unattended urgent calling without monitoring known test endpoints and understanding potential false alarms. Protect your Twilio API credentials and spending settings. Archer never accepts arbitrary phone numbers in incoming requests.

### iPhone presence: first token-gated backend step, NOT mobile tracking

An optional endpoint `POST /api/mobile/presence` has been added, but is disabled unless you set `ARCHER_MOBILE_PRESENCE_ENABLED=true` and a securely random `ARCHER_MOBILE_PRESENCE_TOKEN` of at least 32 characters. A request must use the `Authorization: Bearer ...` header. It accepts a single activity choice (`Available`, `Driving`, `Working`, `Shopping`, `Busy`, `Other`), an optional general place label (max 60 characters), expiry 15/60/120 minutes, and explicit `shareWithAi` permission. **No GPS coordinates, background routes, or movement history are ingested through this endpoint.** Every new update overwrites the previous snapshot; it expires.

The app **still listens only on the PC's loopback address**, so a phone can't call this endpoint remotely. DO NOT port-forward Archer, expose its unauthenticated dashboard, or publish the bearer token in any URL. An authenticated and encrypted mobile companion/gateway must be built and security-reviewed before any real iOS Shortcuts automation or background presence is possible. `GET /api/mobile/capabilities` reports availability without returning credentials.

### Manual verification

1. Stop Archer, pull from `main`, rebuild, and confirm `GET /api/config` reports version `1.0.0`.
2. Without any new credentials, **Operations → SMS & phone alerts** should show `NOT CONFIGURED`; the test buttons must be disabled. Daily/urgent automatic sending must be disabled. `GET /api/mobile/capabilities` must report `enabled:false`.
3. Optional live setup: configure Twilio user environment variables, restart Archer, verify the **masked** phone number, click exactly one test SMS, inspect Twilio's own delivery report and phone. Then separately test one UK-accent voice call if desired.
4. Only after confirming correct delivery and expected charges, explicitly save daily SMS and/or urgent call opt-ins. Review quiet hours and the one-call-per-day limit. Check the recent attempt log.
5. Optional mobile API testing should be done **locally with a test token**, not by exposing the app publicly. iPhone background delivery is not implemented.

GitHub CI verifies compilation, JS/DOM wiring, localhost API startup, **disabled default delivery**, required SMS confirmation, blocked mobile ingest without a token and refusal to enable delivery without Twilio. CI does **not** send a real text, make a phone call or test mobile routing.

## v1.1.0 — Unified operator inbox & mobile/voice roadmap

See **[ARCHER_MOBILE_COMMUNICATION_ROADMAP.md](ARCHER_MOBILE_COMMUNICATION_ROADMAP.md)** for the tracked, phased plan for secure remote access, a native iPhone app, APNs push, shared conversations, a natural British voice, verified emergency escalation, secure inbound text/telephone calls, optional activity awareness and eventual CarPlay entitlement. Dependencies, acceptance criteria and required owner decisions are written out in that file.

### Delivered in v1.1.0: local inbox foundation

- A new **Inbox** page provides a readable, filterable history of real locally recorded website incidents and failed mission tasks. It includes source category, evidence, priority, first-recorded time, and separate **Read** and **Acknowledge** actions.
- Items use stable source-derived keys and persist in the existing Git-ignored Archer state file. Refreshing the dashboard or restarting the app does **not** generate another alert for the same recorded incident.
- The inbox worker reconciles source events approximately every 45 seconds while Archer is running, even if the dashboard isn't open.
- If the original website records a successful recovery check, or a failed task is no longer marked Failed, its inbox event is marked **resolved** without deleting the historical record.
- Inbox history is bounded to 200 items. Read/acknowledge timestamps never silently change mission status, alter website monitors, submit GitHub changes or contact anyone.
- Website reachability incidents have priority **Urgent review** but are never called confirmed revenue/payment outages; the site must be investigated. Local status can be stale if monitoring was interrupted.
- APIs: `GET /api/inbox`, `POST /api/inbox/{id}/read`, `POST /api/inbox/{id}/acknowledge`.
- The local inbox is the **first event source for the future phone app**, not a push-notification system. It does not connect to APNs, Twilio, a new cloud server, Apple background activity, or a personal phone number. It does not incur model fees simply to show an event.

### Test after a pull

1. Stop Archer, **Git → Pull**, rebuild, launch, and verify `/api/config` reports `1.1.0`.
2. Open **Inbox** and verify existing failed mission tasks or website incidents appear **once**. If you have none, the honest empty state is expected.
3. Open the inbox repeatedly, use **Unread** / **Needs review**, mark an item read, then acknowledge. Reopen the page and verify those actions persist.
4. Review a related mission or Operations item; confirming an inbox item must not retry or change the underlying task/site by itself.
5. See the new mobile/communication roadmap file for the upcoming P0 security architecture work. **Do not expose localhost or configure background iPhone tracking** to test this version.

Automated CI validates .NET compilation, all JavaScript files, expected UI element IDs and a synthetic-source API smoke: stable deduplication, read/ack, recovery and no new Twilio attempts. A Windows visual review and a separate production security review are still required.

## v1.2.0 — Isolated mobile gateway prototype (local only)

**This is the first security step toward an iPhone companion, not a remotely accessible app.**
Archer now has two separate .NET 10 applications:

- **Archer.MissionControl** (existing UI and trusted operations) continues to bind to `http://127.0.0.1:5087`. It remains unauthenticated and **must not be exposed** to the LAN or internet.
- **Archer.MobileGateway** (new, separately runnable service) explicitly binds to `http://127.0.0.1:5091`. It has its **own data folder** and cannot reach MissionControl administration, chat, OpenAI, GitHub-writing tools, Twilio, phone numbers or location. It exposes only local health, locally authenticated enrollment/revocation/ingestion, and a scoped read/ack notification inbox.

Read the [full security and route inventory](ARCHER_MOBILE_SECURITY_REVIEW.md) and the [master mobile/voice roadmap](ARCHER_MOBILE_COMMUNICATION_ROADMAP.md) before enabling any gateway integration.

### Device access (test setup on YOUR personal Windows PC only)

Install .NET 10 SDK, then configure unique, cryptographically random **Windows user environment** secrets, **not in GitHub, code, screenshots or chat**:

- `ARCHER_GATEWAY_ADMIN_KEY`: separate 40+ character random provisioning key, used only for `/internal/devices` operations.
- `ARCHER_GATEWAY_INGEST_KEY`: another independent 40+ character random local event-ingest key. Never reuse the admin key.
- `ARCHER_GATEWAY_DATA_DIR`: optional separate writable location for the gateway's private state file (default `Archer.MobileGateway/Data`, excluded from Git).

Launch **Archer.MobileGateway** from Visual Studio. The isolated health endpoint is `GET http://127.0.0.1:5091/health/live`. It explicitly reports `publicAccessEnabled: false`.

You can use the local API with a test client to enroll a short-lived device:
- `POST /internal/devices/enroll` with header `X-Archer-Admin-Key` and JSON `{"label":"Personal iPhone test","lifetimeDays":1}` (1–7 days maximum).
- The response gives a **single random bearer token once**. Store it securely, never in a URL or clipboard history. The gateway stores its SHA-256 hash only. Maximum five active test devices.
- Use `Authorization: Bearer <token>` with `GET /v1/inbox`, or `POST /v1/inbox/{id}/read` and `/acknowledge`. These endpoints cannot start missions, deploy, pay, call or edit GitHub.
- `POST /internal/devices/{id}/revoke` with the admin header invalidates access immediately. Listing `GET /internal/devices` shows metadata, **never tokens or hashes**.
- The gateway rate limits requests and rejects cross-site browser origins and unauthorized ingress.

### Optional local source-event sync (off by default)

To make Archer's existing source-backed Inbox available through this second local API process, run both applications on the **same personal PC**. Set the **same** `ARCHER_GATEWAY_INGEST_KEY` in both processes and explicitly enable `ARCHER_GATEWAY_SYNC_ENABLED=true` in MissionControl's Windows environment. Restart the apps. The sync worker posts **only** source-backed notification ID, category, priority, short title/summary, occurrence and resolution timestamps to the fixed `127.0.0.1:5091` endpoint every minute. It never sends full chat transcripts, detailed mission artifacts, owner location, phone numbers, payment information, model keys or code proposals.

The gateway stores up to 200 events. Replaying the same source event is idempotent: existing read/ack state survives and a resolved event cannot be reopened by a stale snapshot. **Acknowledging the gateway copy currently does NOT acknowledge the MissionControl original**—that reconciliation needs another authenticated integration. No API model requests or Twilio charges occur from syncing.

If no secrets are configured, the gateway can still run for local health checks but **no device can enroll and no events can be ingested**. If you never enable sync, no extra data leaves MissionControl. If the gateway is stopped, Archer continues normally.

### What is deliberately NOT ready

- No public HTTPS endpoint, remote iPhone access, APNs push, app, realtime voice, CarPlay, remote chat, verified Apple device identity, or cloud deployment.
- No secret rotation/refresh flow, encrypted database, independent external security audit, production backups, cross-system acknowledge reconciliation, notification outbox or provider receipts.
- The gateway's bearer and shared keys are suitable only for **local development**. Do not port-forward it or place any reverse proxy in front of it until the P0 authentication, TLS and network isolation work is complete.
- Using two processes is deliberate: an iPhone app must never receive access to the desktop administrative API or unrestricted OpenAI/GitHub/Twilio credentials.

### Validation

CI builds both .NET 10 projects and uses generated **synthetic CI-only keys** to test:
unauthorized device attempts, missing admin/ingest key rejection, a one-time 256-bit device token, permitted read and acknowledgement, event deduplication, recovery without loss of acknowledgement, revocation, malformed events and persistence without raw tokens. The tests make no OpenAI API calls, GitHub writes, calls or SMS.

**When you're back at your home PC:** Pull once, rebuild/run MissionControl, check `/api/config` reports v1.2.0 and browse existing Inbox. You don't need to start or configure the mobile gateway yet; it is optional and disabled from remote access. When we're ready to test authenticated mobile interactions, we can run the new project locally and configure test keys on your personal PC.


## v1.3.0 — Zero-cost notification-routing sandbox

Archer.MobileGateway now supports a **dry-run notification planner** for safely testing how a future iPhone app would handle Archer's recorded business and mission alerts, BEFORE any real APNs push, SMS or phone calls are connected.

While the gateway is running **locally** with a test device enrolled and optional local event sync, the locally authenticated administrator can call:

- `POST http://127.0.0.1:5091/internal/delivery/simulate` with the private `X-Archer-Admin-Key` header and JSON `{"confirm":true,"maxEvents":10}`. This creates only simulation records in the gateway's local Data file.
- `GET /internal/delivery/preview` with that admin header: inspect mock routes, stable deduplication keys and reasons. No credentials are returned.
- `GET /v1/delivery-previews` with the enrolled device's short-lived `Authorization: Bearer <device token>` header: see **only** that device's mock route entries, without access to any other device.

The explicit sandbox policy currently:

- Suggests a noninterrupting **push-preview** for fresh attention or unverified urgent-review events.
- Suggests a noninterrupting **digest-preview** for routine information or stale source events.
- Suppresses an acknowledged or resolved source event from further proposed interruption.
- Labels unverified website outages as requiring a real investigation **before** any future phone escalation.
- Creates no more than one preview per source-event, device and policy revision, even if replayed. History is bounded to 200 previews.

**Important:** every preview reports `actualNotificationsSent: 0`. No real message, phone call, Apple APNs push, chat turn, geographic tracking or provider fee is incurred. This isn't a reliable delivery queue yet and doesn't provide iPhone notifications. The simulator runs only on an explicit local admin test request; it is not scheduled. The 1.2.0 authentication boundaries remain intact.

**Testing:** CI runs synthetic event ingestion, authorization rejection, explicit confirmation, duplicate prevention, receipt-free routing, preserved read/acknowledgment, device revocation and no external provider actions. See [the gateway security review](ARCHER_MOBILE_SECURITY_REVIEW.md) and [updated master to-do list](ARCHER_MOBILE_COMMUNICATION_ROADMAP.md). You can test this later on your personal home PC; no setup is needed at work.

## v1.4.0 — Local device security and audit upgrades

The separate `Archer.MobileGateway` is now more resilient to device credential loss and least-privilege access. **It remains localhost-only and is NOT approved for public hosting.**

- Local administrator can enroll either a read-only device (`["inbox:read"]`) or a read-and-ack device (`["inbox:read","inbox:ack"]`). All other scopes and acknowledgement without read permission are rejected. Existing enrollments default to read+ack for backward compatibility.
- `POST /internal/devices/{id}/rotate` with `X-Archer-Admin-Key` and JSON `{"lifetimeDays":1}` replaces an active device token atomically. The **old token stops working immediately** and the new bearer is returned **once**. Rotating a revoked or expired device is refused.
- `GET /internal/audit` uses the same local admin key and shows up to 100 recent audit entries (from a capped history of 300), containing action, device/event IDs and timestamps. It never intentionally records raw tokens, personal location or alert text. Read/ack records identify the device involved.
- The gateway rejects unexpected HTTP Host values (defense against DNS rebinding), maintains an origin restriction, and adds 30 requests/minute per Authorization digest on mobile inbox APIs, on top of its existing loopback IP rate limit.
- Rotating requires re-provisioning a future device client with the new bearer. **Do not** transmit it through a chat, source file, URL query string, SMS or unencrypted remote connection.

### API endpoints (localhost only)

- `POST http://127.0.0.1:5091/internal/devices/enroll` with private admin header and JSON `{"label":"Read-only test","lifetimeDays":1,"scopes":["inbox:read"]}`.
- `POST http://127.0.0.1:5091/internal/devices/{id}/rotate` with private admin header and JSON `{"lifetimeDays":1}`.
- `POST http://127.0.0.1:5091/internal/devices/{id}/revoke` with private admin header.
- `GET http://127.0.0.1:5091/internal/audit` with private admin header (metadata only).

Secrets must stay in private local environment settings, not GitHub or this chat. The optional MissionControl→gateway sync remains off by default and no paid AI, SMS, phone or APNs operation is introduced by this release.

**Verification:** CI compiles both .NET 10 projects and exercises wrong Host and missing admin keys, arbitrary scope rejection, read-only authorization, successful rotation, old-token denial, revocation, and audit privacy. This is local test coverage, **not** a secure remote iPhone login, mobile pairing flow or penetration test.

For production blockers and ongoing tasks, see [ARCHER_MOBILE_SECURITY_REVIEW.md](ARCHER_MOBILE_SECURITY_REVIEW.md) and [ARCHER_MOBILE_COMMUNICATION_ROADMAP.md](ARCHER_MOBILE_COMMUNICATION_ROADMAP.md).

## v1.5.0 — Shared local message journal for future iPhone conversations

Archer.MobileGateway now supports the first **device-scoped, single-owner message journal**. It is a local storage prototype only—not the live British-voiced AI, not the existing desktop Archer chat, and not remote access from an iPhone.

- **Opt-in scopes:** enroll a local test device with `["conversation:read"]` to view journal entries, or `["conversation:read","conversation:write"]` to add entries. Neither scope grants access to the mission dashboard, GitHub, AI billing, Twilio or the inbox. Existing inbox-only tokens cannot access conversation content.
- **GET `http://127.0.0.1:5091/v1/conversation/messages`** with the device bearer shows recent local owner-authored messages, time stamps, and which authorised device saved them. Responses explicitly say `savedOnly: true` and `assistantConnected: false`.
- **POST `/v1/conversation/messages`** with a conversation-write bearer and JSON like `{"clientMessageId":"<new UUID>","content":"Hi Archer"}` saves one inert text record. Same device + same UUID + same text is safely idempotent; conflicting reuse gives HTTP 409. Max 1,600 characters per message.
- **DELETE `/internal/conversation`** with the separate local `X-Archer-Admin-Key` wipes only this test journal, and logs metadata-only deletion. It does not alter desktop history.
- Messages are bounded to 200, with read visibility restricted to the most recent seven days. Older entries are pruned on subsequent writes. Records and audit live in a separate, Git-ignored local gateway JSON file.
- **Historical v1.5 limitation (addressed by v1.6):** The original prototype stored journal text unencrypted at rest. New v1.6 gateway state is encrypted, but secure remote identity, transport and backup protection remain outstanding. A future secure backend will require encrypted storage/backup, identity login, authenticated desktop sync, session ordering, transport TLS, iOS Keychain, deletion/export and retention controls.
- No model tokens or phone minutes are spent by these APIs, and **nothing sends a reply automatically**. The native app and unified two-way conversation service remain to be built.

GitHub CI tests device scope isolation, cross-device visibility, duplicate and conflicting message IDs, administrative clear, credential rotation/revocation, and ensures the journal never sends AI or phone requests. No action on your work computer is needed. This doesn't enable cloud hosting or phone connectivity.

See the [current security review](ARCHER_MOBILE_SECURITY_REVIEW.md) and [master development roadmap](ARCHER_MOBILE_COMMUNICATION_ROADMAP.md) for remaining blockers.

## v1.6.0 — Encrypted gateway state (still localhost only)

The standalone Archer.MobileGateway now writes its **entire** Git-ignored local data file using authenticated **AES-256-GCM**, including conversation text, notification descriptions, device credential hashes, previews and audit metadata. There is **no built-in, sample or default encryption key**, and this does **not** open remote access.

### Personal home PC setup (ONLY if you choose to run the separate MobileGateway)

1. Generate **32 cryptographically random bytes**, encoded as **64 lowercase hexadecimal characters**, using a trusted local password manager, OS secure random generator, or the following local PowerShell snippet. Do not paste the result into this chat, GitHub, project files or command output that will be shared:

   ```powershell
   $bytes = New-Object byte[] 32
   $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
   $rng.GetBytes($bytes)
   $rng.Dispose()
   $key = ([BitConverter]::ToString($bytes) -replace '-', '').ToLowerInvariant()
   # Store $key securely in your password manager; do not print it in a log.
   [Environment]::SetEnvironmentVariable("ARCHER_GATEWAY_DATA_KEY_HEX", $key, "User")
   Remove-Variable key, bytes
   ```

2. **Back up this key separately in a secure place.** Losing it means losing access to encrypted gateway state. The same key must be available each time the gateway runs. If you previously used the gateway on an older version, use a trusted copy of the current state and original key. No automated key-rotation or recovery system is provided yet.
3. Restart Visual Studio and any gateway process to inherit the user environment variable. When running the optional standalone gateway, `GET http://127.0.0.1:5091/health/live` remains a basic liveness check, and `GET /health/ready` shows `encryptedState:true` only when a correctly formatted key is configured.
4. On the first state read with a key, recognizable older `mobile-gateway-state.json` **plaintext v1.5** data is atomically replaced with an encrypted file **before** any data is returned. If the key is missing, the state cannot be read or overwritten. **Wrong keys and tampering do not silently reset the file.**
5. Do not upload historical plaintext data files, backups, temporary files or recovered disk snapshots. Encryption after migration does not erase older copies from prior backups or underlying disk sectors.

No key is needed to **build** Archer or use its normal MissionControl desktop app. The encryption key is required only for local gateway data operations; without one, `/health/ready` is 503. Your previous v1.5 gateway data remains untouched until you intentionally configure and access the encrypted gateway.

**Important:** This protects stored local gateway data from casual file disclosure, not from an attacker who controls your computer/account or knows your encryption key. It is not a production cloud database, a mobile app, a key vault, trusted HTTPS, an encrypted offsite backup or live AI chat. Both processes stay localhost-only. Do not port-forward or expose Archer to a public web server.

### CI / verification

GitHub Actions tests both .NET 10 builds, the existing Archer endpoints, the mobile gateway's authentication, device permissions and routing, and a new synthetic encryption harness:

- Full state contains only encrypted envelope fields, not raw message text, tokens, monitor labels or audit metadata.
- Old synthetic v1.5 plaintext state migrates with message contents intact.
- The correct original key can read a migrated state on restart.
- Wrong key, absent key and modified ciphertext cannot read, reset or overwrite the protected state.
- All existing device revocation, rate limits, and no-charge/no-delivery behavior remain unchanged.

See [ARCHER_MOBILE_SECURITY_REVIEW.md](ARCHER_MOBILE_SECURITY_REVIEW.md) and [ARCHER_MOBILE_COMMUNICATION_ROADMAP.md](ARCHER_MOBILE_COMMUNICATION_ROADMAP.md) for remaining identity, authenticated desktop sync, backup, iPhone and cloud milestones.

## v1.7.0 — Opt-in read-only desktop / phone-draft bridge (LOCAL ONLY)

Archer's local desktop app can now **preview** owner-authored messages saved in the encrypted local MobileGateway journal. This is an early integration test toward one conversation across future devices; it is **NOT** real shared AI chat, remote iPhone connectivity or two-way telephone conversation.

### Setup (only on your PERSONAL home PC, never at work)

Both Archer.MissionControl and Archer.MobileGateway must run on the **same personal computer**, each on its existing loopback-only port. The gateway must first have its own AES-256-GCM data key configured, and any test device must already be enrolled with explicit conversation permissions (see v1.5–v1.6 documentation).

1. Generate and store a **separate, cryptographically random secret of 40+ characters** for ARCHER_GATEWAY_BRIDGE_KEY. Set the same value in the private user/process environment for **both** local services. **Do not reuse** ARCHER_GATEWAY_ADMIN_KEY, ARCHER_GATEWAY_INGEST_KEY, your GitHub token, Twilio password or the AES data key. Never print, commit, upload or share secrets.
2. Only when you want drafts to appear in desktop Archer, explicitly set **ARCHER_GATEWAY_CHAT_BRIDGE_ENABLED=true** for the MissionControl process. Without both values, the feature is disabled.
3. Restart both processes so environment settings take effect; open the normal **Archer home** and find **Mobile message drafts**. It shows up to 30 owner-authored phone test drafts from the gateway, if any.
4. Click **Use in chat composer** on a draft. This **only fills the existing text box**. Read or edit it before manually clicking **Send**. If you click Send, the usual Archer model is called subject to your existing spending safeguards.
5. Disable the flag at any time and restart desktop Archer to stop fetching gateway drafts. The original gateway journal is unchanged.

### Independent, narrowly permissioned route

- Gateway **GET /internal/conversation/bridge** requires header X-Archer-Bridge-Key with the dedicated secret and stays bound to 127.0.0.1:5091. It accepts neither device bearer tokens nor administrator/ingest keys as substitutes.
- Desktop **GET /api/mobile/conversation-preview** returns disabled status without making any network call until the owner explicitly enables the integration. When active it calls only the fixed localhost gateway address with a proxy-free and redirect-free client. A bounded response is parsed into validated message previews; no browser response includes the bridge key.
- No new listener, port forwarding, reverse proxy, cloud payment, GPS, background phone monitoring, Twilio SMS/call or OpenAI model operation is introduced by simply viewing a draft. The desktop's existing chat history is **not** exported or synchronized to the gateway. Assistant replies are **not** automatically written back.
- An authenticated gateway draft is still **untrusted text**, not an instruction to execute. The operator must explicitly choose and send any draft before AI processing, and the gateway cannot merge, deploy, call contacts or manage payment accounts through this bridge.

### Verification

GitHub Actions tests the default-off desktop endpoint, missing/wrong bridge key refusal, explicit gateway read-only access, synthetic phone message visibility, and a real two-process loopback handshake where desktop Archer receives a test draft with **zero AI requests** and no automatic submission. Static UI checks verify the draft button does not call the chat API.

**Future blockers:** authenticated production identity, HTTPS and separate public-facing ingress, key management/rotation, secure device provisioning, encrypted backups, full cross-channel sessions and response sync, APNs, iOS app and real British two-way voice. Do not expose either localhost service to the internet.


## v1.8.0 — Owner-confirmed Archer replies back to the encrypted mobile journal

This is the first **controlled two-direction message exchange** between a locally enrolled test device and desktop Archer. It remains a **loopback-only prototype**, not a live iPhone app or continuously synchronized AI conversation.

1. With the separately keyed phone-draft bridge enabled on your personal HOME computer, an authorized test device saves an owner-authored draft in the encrypted MobileGateway journal.
2. Open Archer home → **Mobile message drafts**, select **Use in chat composer**, and review it. This only copies text, without an AI request.
3. Leave the original draft unchanged and press the ordinary **Send** button. Only this existing action contacts OpenAI and is governed by Archer's spending safeguards.
4. Once Archer replies, choose **Share last reply with mobile** and approve the confirmation. This makes a **second separate** local request; it does NOT make another AI model call.
5. Desktop checks that the most recent persisted user/assistant chat pair matches your selected draft. The local gateway confirms the exact owner draft still exists before saving one linked assistant reply. The gateway journal now exposes assistant replies with a replyToMessageId reference to the original owner message.
6. The sharing operation is idempotent: an identical retry returns success without a duplicate entry, while a different reply to an already-answered question is refused. Enrolled test devices with conversation:read permission can read the shared message.

### Safety, limitations and privacy

- Gateway reply publication uses the same independent 40+ character ARCHER_GATEWAY_BRIDGE_KEY, not a mobile-device bearer token. Publishing requires an exact, already-saved desktop chat response. No arbitrary browser-provided assistant text is accepted.
- Explicit confirmation is required before sharing replies. Nothing is silently exported from general desktop chat, and other private conversations are not copied.
- Only the **last desktop user/assistant pair** is eligible. Sending another desktop message first invalidates the previous share; this intentionally fails closed instead of copying the wrong answer.
- Desktop replies over 4,000 characters are not eligible for this prototype. There is no automatic transcript import, conversation session context merging, background mobile AI replies, internet access, APNs, two-way phone calls, CarPlay or cloud hosting.
- The gateway stores data encrypted with its owner-managed AES-GCM key. Authorized test devices with conversation:read may view owner messages and the linked assistant reply. This is still a single-user local development design and NOT production cloud authentication.
- Retry the **Share** button after an ambiguous local connection failure; retries do not contact the AI again. Do not port-forward either localhost service or use the employer/work PC.

### Validation

GitHub CI tests denied mobile-bearer publishing, wrong/missing service key, mismatched original messages, one reply per draft, replay conflicts, local encrypted journal reading, and a synthetic desktop→gateway integration that never calls OpenAI or Twilio. Browser visuals and personal Windows setup remain to be tested after a pull.

See the [master roadmap](ARCHER_MOBILE_COMMUNICATION_ROADMAP.md) and [security review](ARCHER_MOBILE_SECURITY_REVIEW.md) for production identity, full conversation sync, key management, push notifications, iPhone app and British voice still to do.

## v1.9.0 — Local conversation sessions, cursors and catch-up

The standalone Archer.MobileGateway now supports multiple **local single-owner conversation sessions** rather than one undifferentiated journal. This is the foundation for future desktop/iPhone conversation synchronization, **not actual remote mobile access**.

- The original Main conversation has a fixed stable `sessionId`. Existing encrypted journal messages (including owner drafts and manually shared assistant replies) automatically migrate to that session, with deterministic server-assigned `sequence` numbers. Original message IDs, reply associations, texts and encryption are retained.
- Enrolled devices with explicit `conversation:read` can list sessions via `GET /v1/conversation/sessions`. Devices with both `conversation:read` and `conversation:write` can create one with `POST /v1/conversation/sessions`, JSON `{"title":"GeoShift launch"}`. Up to ten total sessions are supported in this local prototype.
- Write a message with `POST /v1/conversation/messages`, including `clientMessageId`, `content`, and optionally `sessionId` (omitting the ID retains backward-compatible Main session behavior).
- Read incrementally with `GET /v1/conversation/messages?sessionId=<guid>&after=0&limit=50`. Returned messages include per-session, server-assigned sequence numbers; `nextAfter`, `hasMore` and `highWatermark` support bounded polling/catch-up. Devices should persist the last successful `nextAfter`, never use their local wall clock as a cursor. The default page limit is 100, and allowed limits are 1–100.
- Archive a non-Main session with `POST /v1/conversation/sessions/{id}/archive`, using a conversation-write device token. Archived history is readable, but cannot accept new owner texts or desktop assistant replies. The default Main conversation cannot be archived.
- A device's `clientMessageId` is unique across sessions; replay in another session is rejected, preventing accidental cross-thread duplication. A manually approved desktop reply inherits the session of the owner message it answers.

**Important limitations:** all sessions belong to the **same local owner**, and a device with `conversation:read` can read every local session. Cursors may skip older records discarded by the seven-day/200-message journal retention; they are not proof of complete delivery. There is no cloud identity, Internet access, device push, genuine shared AI context, multiuser tenancy, native iPhone app or long-term export/backup yet.

GitHub Actions runs a new separate synthetic session test for device permissions, session isolation, pagination, duplicates across sessions, archiving, encrypted restart and cursor preservation. Existing gateway, desktop bridge, and encryption migration tests also run. Test the interface at home after one Git → Pull; you still don't need to install anything on your work PC.

## v1.10.0 — Read-only encrypted mobile session history in desktop Archer

The Archer home page now includes a **Shared local conversation history** panel. This offers a useful view of the v1.9 session engine without writing API requests manually:

- Select the **Main conversation** or any other currently saved local session from the dropdown. Archived conversations remain visible with an archived label.
- Read actual owner-authored and explicitly shared Archer replies in order, with a timestamp and server-generated sequence number. The UI uses safe text rendering and cannot execute message content.
- Click **Load next messages** to request the next page using the server's `nextAfter` cursor; or click **Refresh history** to reload the selected session.
- When you separately and explicitly **Share last reply with mobile**, the selected history can refresh to show the saved answer. No additional model call or push is triggered.
- The browser requires the SAME v1.7 private, opt-in local bridge flag `ARCHER_GATEWAY_CHAT_BRIDGE_ENABLED=true` and separate `ARCHER_GATEWAY_BRIDGE_KEY`. Everything is disabled by default. No configuration needed until you intentionally test on your own home computer.
- Under the hood, gateway `GET /internal/conversation/sessions` and `GET /internal/conversation/history` are limited to the separately authenticated localhost bridge; desktop proxies `GET /api/mobile/sessions` and `GET /api/mobile/session-history` expose only bounded, read-only data.

**Security limitation:** Mobile journal messages become readable in your local desktop dashboard **only after** enabling this bridge. The old MissionControl dashboard itself has no remote-user login, so do not run it on a shared/employer computer or publish/port-forward either process. The browser is NOT an iPhone app, true shared AI session memory, live message delivery or public HTTPS account service.

CI verifies both .NET applications, JavaScript syntax and element wiring, an opt-in two-process session preview showing a source message then a manually shared answer, and denial of unauthenticated bridge calls. The entire upgrade adds no hosting or provider charges.

## v1.11.0 — Verified encrypted backup and safe recovery

This release introduces **Archer.GatewayBackup**, a standalone offline .NET 10 utility for creating, verifying and restoring **new** encrypted gateway state files. It does not use OpenAI, send notifications, upload data or set up cloud billing. It does NOT automatically configure backups.

**Run only on your PERSONAL home PC.** The source is the encrypted state file under Archer.MobileGateway/Data/mobile-gateway-state.json by default (or ARCHER_GATEWAY_DATA_DIR if configured). The utility requires the **same private ARCHER_GATEWAY_DATA_KEY_HEX** used to encrypt the state in v1.6; store that key separately in a safe place.

### Three commands

Run these in a terminal in the Archer repository on your home PC. Replace the sample paths; make sure destination directories already exist:

```powershell
# Verify the existing encrypted gateway state without changing it.
dotnet run --project Archer.GatewayBackup/Archer.GatewayBackup.csproj -- verify "C:\Archer\Archer.MobileGateway\Data\mobile-gateway-state.json"

# Create a NEW authenticated encrypted snapshot on a secure drive.
dotnet run --project Archer.GatewayBackup/Archer.GatewayBackup.csproj -- backup "C:\Archer\Archer.MobileGateway\Data\mobile-gateway-state.json" "D:\SecureArcherBackups\archer-2026-11-16.archerbackup"

# In a real recovery, STOP MobileGateway first. Never overwrite your existing state.
dotnet run --project Archer.GatewayBackup/Archer.GatewayBackup.csproj -- restore-new "D:\SecureArcherBackups\archer-2026-11-16.archerbackup" "C:\ArcherRecovery\mobile-gateway-state.json"
```

The utility verifies the AES-256-GCM authentication tag using the exact gateway encryption implementation. Missing/wrong keys, modified ciphertext, unsupported plaintext input, malformed authenticated data and oversized files are rejected. It never writes decrypted messages or keys to files or output.

- **No overwriting:** backup and restore destinations must be NEW. It refuses any existing destination or missing parent folder.
- **Live backup permitted:** normal gateway state writes use atomic replacement, so a backup captures one stable encrypted snapshot. Live restore is refused if anything is listening on 127.0.0.1:5091.
- **Recovery:** after restore-new creates a separate encrypted state file, intentionally configure a stopped gateway instance with that file in its own data directory and the ORIGINAL key. Verify the recovered data on localhost before relying on it.
- **Key custody matters:** lose the original 256-bit key and the backup cannot be read. Store the key separately in a trusted password manager and never share it via chat, GitHub, email or screenshots. No automatic key rotation or lost-key recovery exists.
- **Off-host backups are still your responsibility:** select a secure offsite drive/location with suitable Windows permissions, configure an actual schedule and run a real Windows restore drill. The new utility does NOT send files externally, guarantee offsite backup, enforce retention, remove historical plaintext, or replace disaster-recovery planning.

CI tests an encrypted synthetic conversation, backup while the local gateway runs, wrong/missing key refusal, ciphertext tampering, live restore and overwrite rejection, then restores to a fresh path and verifies the same conversation and device bearer survived. No real provider accounts or user data are used.

See ARCHER_MOBILE_SECURITY_REVIEW.md and ARCHER_MOBILE_COMMUNICATION_ROADMAP.md for key rotation, cloud identity, iOS, push, and actual synchronized conversations still to do.


## v1.12.0 — Offline, fail-closed gateway encryption-key rotation

Archer.GatewayBackup now supports **rotate-new**: preparing a new, independently encrypted state file with a replacement AES-256-GCM key. It NEVER overwrites the source, uploads data, changes the running gateway, sends messages or incurs model fees.

### Rotation procedure — PERSONAL HOME PC ONLY

1. Back up and verify the existing encrypted state using your ORIGINAL private ARCHER_GATEWAY_DATA_KEY_HEX. Store the backup and its old key safely and independently.
2. Completely **STOP Archer.MobileGateway**. Rotation is refused if anything listens on localhost port 5091.
3. Generate a brand-new cryptographically random 32-byte (64 hex-character) key and store it securely in your password manager. Set ARCHER_GATEWAY_NEW_DATA_KEY_HEX privately in the rotation process. Keep ARCHER_GATEWAY_DATA_KEY_HEX set to the OLD key. These keys must be distinct. Never pass keys as command-line arguments, print them to logs, commit them, or paste them into chats.
4. Prepare a new destination directory and run this sample with paths adapted to your home PC (existing source must be encrypted; destination must not exist):

    dotnet run --project Archer.GatewayBackup/Archer.GatewayBackup.csproj -- rotate-new "C:\Archer\Archer.MobileGateway\Data\mobile-gateway-state.json" "D:\SecureArcherBackups\rotated-gateway-state.json"

5. The tool authenticates the old ciphertext, encrypts a fresh copy with the NEW key, checks the new ciphertext can be decrypted correctly, and writes ONLY to a brand-new file. A wrong/missing original key, missing/malformed/equal new key, damaged source, existing destination or active gateway aborts without touching the original.
6. Privately set ARCHER_GATEWAY_DATA_KEY_HEX to the NEW key for an independent verify command on the rotated copy. The utility does NOT automatically activate it. When ready, keep the gateway stopped, create a separate gateway data directory, put ONLY the verified rotated file there named mobile-gateway-state.json, and point ARCHER_GATEWAY_DATA_DIR to that directory. Start the gateway using the NEW key, check /health/ready and inspect device access, inbox and conversation sessions.
7. Retain the ORIGINAL encrypted state, ORIGINAL key and older backups securely until you have independently verified recovery. **Old backup files still need the old key**; rotation of a new copy does not retroactively rotate old backups.

### Limits and verification

There is no in-place swap, server restart, backup retention policy, key vault, secure secret manager, emergency key recovery or background rotation schedule. An old lost key cannot be recreated. Historical copies on disks or in backups are not erased. Environment secrets can be accessed by sufficiently privileged processes on the same computer.

CI exercises two random synthetic keys, missing/equal/malformed new key, wrong original key, authenticated ciphertext tampering, active-gateway refusal, destination overwrite refusal, unchanged original, and starting a new gateway with the NEW key to verify original device credentials, messages, and monotonic session sequence survive. No provider credentials or real user data are involved.

See ARCHER_MOBILE_SECURITY_REVIEW.md and ARCHER_MOBILE_COMMUNICATION_ROADMAP.md for the remaining production-security tasks.
