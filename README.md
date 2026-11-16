# ARCHER — Personal Autonomous AI Operator

**Version:** 0.6.0 (local prototype)  
**Platform:** ASP.NET Core / .NET 10, vanilla HTML/CSS/JavaScript  
**License:** Personal prototype; no production deployment or multi-user authentication.

This is a **real starting codebase**, not a screenshot or a mockup. It includes:

- Animated **Archer** landing page as the default interface, with text conversation and optional browser voice
- Mission Control dashboard with mission creation and tracking
- **Developer Workspace** with allowlisted GitHub source inventory, selected-file previews, and review-mission drafts
- Opt-in **Operations** page for public HTTPS site monitoring and local outage/recovery records
- Expiring, user-declared **Presence** context with optional one-time approximate browser location
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
