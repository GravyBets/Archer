# GeoShift development missions (A-track)

The local Mission Control Developer view now supports a bounded, owner-authorized coding batch. This is draft-only autonomy, not a production deployer or a general shell agent.

## Setup (personal PC, not work computer)

1. Pull and build Archer once the development-mission PR is merged and green. Start Archer as usual.
2. GeoShift uses the default branch **master**. Its independent **GeoShift Archer CI** workflow builds the Web app and runs the existing core tests on each PR.
3. Configure these environment variables in the local Archer process, then restart:
   - `ARCHER_GEOSHIFT_AUTONOMY_ENABLED=true`
   - `ARCHER_CODING_ENABLED=true`
   - `ARCHER_GITHUB_WRITE_REPOS=GravyBets/GeoShift`
   - `ARCHER_GITHUB_ALLOWLIST=GravyBets/GeoShift` (or include it in your existing list)
   - `ARCHER_GITHUB_WRITE_TOKEN`: a fine-grained GitHub token limited to GeoShift, Contents and Pull requests read/write, no workflow or deploy access.
   - `ARCHER_GITHUB_READ_TOKEN`: recommended separate fine-grained token limited to GeoShift with Contents, Pull requests, Actions and Checks read access. Public API calls without a token may be rate-limited.
   - Existing `OPENAI_API_KEY` and the local spending settings. Never share or commit tokens. Do not put employer data in this project.
4. Confirm configuration in Developer → GeoShift development missions. The worker refuses to arm if CI is unavailable.
5. **Use first pilot objective** proposes a real, small converter compatibility change for legacy KML files missing the standard xsi namespace declaration. Click Create task plan (one budget-checked model call), review the files and task instructions, then authorize the batch once. A planned task is not an approved code change until batch authorization.
6. Leave Archer running. It generates a bounded source proposal, opens an isolated draft PR, checks the exact published head against the required Web build and core-test job, and moves to the next independent task only after green CI. A code failure permits one repair draft with prior proposed source and limited diagnostics; earlier failed PRs are retained and must not be merged. Do not merge both drafts.
7. Inspect evidence, review each final PR, and merge only after your own review. Archer never merges or deploys. Green CI does not guarantee the requested behavior or cross-PR compatibility.

## Limits and recovery

- Up to three independent tasks; one or two existing small application source files per task; no test, workflow, entry-point, package/config or new-file edits. Ambiguous source scope is refused.
- Up to two coding attempts per task, six total, one 24-hour authorization. Every AI call goes through the existing budget ledger, which estimates costs and does not replace provider billing controls. Planning is separate, explicitly initiated and billable.
- CI must have completed successfully with both named build and test steps, not skipped or absent. CI checks time out after 45 minutes. Only bounded diagnostic codes and approved file positions, never raw logs, are used for automatic repair.
- Pausing prevents new claims; a request/write already underway might finish. A paused pre-publication edit is retained locally and blocked; do not retry ambiguous writes.
- Default restart behavior disarms the queue. Optional explicit restart authorization permits safe queued/CI work to resume without resetting the original time and attempt limits. Interrupted editing or publication is blocked, not retried.
- Drafts remain isolated, not cumulative integration. This is not a guarantee of launch readiness. No background service if the PC/app is stopped. No remote mobile approval or chat-driven execution in this change.
- The first real paid pilot must run in the owner's configured Archer instance. CI synthetic checks exercise policy and mocked GitHub evidence without credentials; they don't prove live API authorization, AI quality or owner-machine reliability.
