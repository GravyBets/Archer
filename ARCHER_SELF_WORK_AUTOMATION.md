# Archer working on Archer — supervised self-coding workflow

**Status: Review PR, not yet active.** No paid server or home-PC uptime is needed. The GitHub Actions workflow is **disabled by default** and will not call a model or write a branch until the owner explicitly enables it after review. This workflow does not run inside Archer Pocket and does not expose the desktop API.

## Concrete end-to-end workflow

1. The owner creates a focused **GitHub Issue** in `GravyBets/Archer` describing one small bug or feature, e.g. "Voice reply is too wordy."
2. The owner adds the GitHub issue label `archer-approved`. This is a **human approval for one bounded issue with up to two model attempts**, not consent to change the server, expose secrets or merge.
3. Once the owner has opted in, GitHub Actions' **Archer Self-Work** workflow can run daily or manually. It selects only an open, `archer-approved` issue without `archer-attempted`, blocks another pending Archer self-work PR, and marks the issue `archer-attempted` *before spending money*.
4. The unprivileged model job reads **only one allowlisted pair of files** for the selected task type. It can generate at most four unique, exact-text replacements. The model cannot choose workflows, hidden files, secrets, server authentication, billing files or arbitrary tool calls. The existing `tests/ValidateUi.py` suite is supplied as **read-only** context and cannot be edited by the model; this prevents a patch from weakening its own UI validation.
5. A separate, unprivileged GitHub Actions job applies the patch, runs JavaScript syntax/UI/safety tests and builds the .NET 10 Mission Control application. It receives **no OpenAI key and no GitHub write token**.
6. If the first patch fails isolated CI, Archer may use **one additional** billable model request with bounded failure feedback to propose a replacement patch against the original approved files. The second patch is independently tested with no model or GitHub write secrets. There are **no further automatic retries**; the issue remains attempted even on failure.\n7. Only if either attempt passes does the publishing job, which has repository-scoped GitHub permission but no model key, apply the same bounded patch and create a **draft pull request**. It never merges, releases, deploys, or grants authorization.
8. The owner reviews the actual diff, checks CI, and decides whether to merge. A failed issue remains attempted; future retries need fresh owner approval.

## How to enable (only when ready)

After reviewing and merging the workflow PR:

1. In repository **Settings → Secrets and variables → Actions → Variables**, add
   `ARCHER_SELF_WORK_ENABLED` with value **`true`**. Optional `ARCHER_SELF_WORK_MODEL` must be a valid model name, e.g. `gpt-5-mini` if available in your own API project.
2. In **Actions secrets**, add a dedicated, tightly budgeted
   `ARCHER_SELF_WORK_OPENAI_API_KEY`. This must be a personal
   API project key with independent provider-side project budget/alerts.
   **Never paste it into GitHub issues, this chat, source, logs or screenshots.**
3. Under repository **Settings → Actions → General → Workflow permissions**, ensure
   GitHub Actions is allowed to create pull requests, if the repository policy permits. The individual jobs already use narrow `GITHUB_TOKEN` permissions.
4. Create the GitHub label **`archer-approved`**. The automation creates
   its internal `archer-attempted` label when necessary.
5. Create a focused Issue, label it `archer-approved`, and manually trigger
   **Actions → Archer Self-Work → Run workflow** for the first trial.
6. Examine its draft PR; repeat only after you deliberately approve another
   issue. The scheduled daily run may then pick up one approved backlog item.

To stop all future runs, set `ARCHER_SELF_WORK_ENABLED=false` or remove it.
Already running jobs may need cancellation from GitHub Actions. Remove the
dedicated provider key if you want an immediate additional spend stop.
GitHub's own workflow quota and external model API charges still apply;
there is no unlimited free autonomous model processing.

### Initial limited source categories

| Issue wording | Files worker may edit |
|---|---|
| voice / speech / microphone / pause | `Archer.MissionControl/wwwroot/js/archer-home.js` and optionally `tests/ValidateUi.py` |
| chat / messages / composer | `Archer.MissionControl/wwwroot/js/app.js` and optionally `tests/ValidateUi.py` |
| coding / code proposal / draft PR | `Archer.MissionControl/wwwroot/js/coding.js` and optionally `tests/ValidateUi.py` |
| developer / repository browsing | `Archer.MissionControl/wwwroot/js/developer.js` and optionally `tests/ValidateUi.py` |
| inbox / alerts | `Archer.MissionControl/wwwroot/js/inbox.js` and optionally `tests/ValidateUi.py` |

A worker may edit the application source file only. Existing UI regression
tests are read-only, cannot be removed or weakened by the model, and must still
pass in the isolated validation job. Unsupported, ambiguous or oversized
tasks should fail without touching source. This starter worker will not
implement complex cross-file features or fix its own workflow implementation;
those will require additional engineering and a separately audited privilege
boundary.

## What's next before genuine self-development autonomy

The current workflow can own a **single bounded change (up to two model calls per owner-approved issue)** from approved issue
through independent tests to a draft PR. It still cannot decide its own
roadmap, generate arbitrary new files, read the full architecture, interact
with deployed Archer, or autonomously diagnose arbitrary CI failures. It can retry one bounded patch after failure, but broader debugging remains a separate
milestones and must have strong resource, permissions and review policies.

No remote server should be purchased merely to turn on this workflow.
Its model API use may cost money, but the owner can disable it entirely.

**A green build is not proof that AI-generated code is safe or correct.**
Independent tests and human code review remain required.
