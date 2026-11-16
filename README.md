# ARCHER — Personal Autonomous AI Operator

**Version:** 0.3.0 (local prototype)  
**Platform:** ASP.NET Core / .NET 10, vanilla HTML/CSS/JavaScript  
**License:** Personal prototype; no production deployment or multi-user authentication.

This is a **real starting codebase**, not a screenshot or a mockup. It includes:

- Mission Control dashboard with mission creation and tracking
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
