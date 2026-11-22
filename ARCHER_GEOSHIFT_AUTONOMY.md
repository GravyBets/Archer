# Archer managed-project autonomy

This replaces the earlier GeoShift-specific prototype. GeoShift is one optional registered project, not an execution dependency. Archer can manage another repository without code changes to Archer.

## Start on the personal Archer machine

1. Pull the merged and verified Archer release, build, and run Mission Control.
2. In Developer → Managed projects, register the project name, exact GitHub owner/repository, technology, editable application directory prefixes, independent CI workflow **filename**, verification job display name, and required build and regression-test step names. Priority is 1–5; 5 is highest. Set a rolling 24-hour coding-attempt limit (1–12). Registration is local metadata, not GitHub authorization.
3. Only enable repositories you own and have reviewed. Set `ARCHER_PROJECT_AUTONOMY_ENABLED=true` and `ARCHER_CODING_ENABLED=true`. Include each authorized repository in both comma-separated `ARCHER_GITHUB_ALLOWLIST` and `ARCHER_GITHUB_WRITE_REPOS`. Set `OPENAI_API_KEY` and existing global AI spend preferences.
4. The project panel shows a stable credential prefix derived from its repository identity (uppercase first 16 hex characters of SHA-256 of lowercase owner/repo). Configure separate environment variables `<prefix>_READ_TOKEN` and `<prefix>_WRITE_TOKEN` for that repository. Use separate fine-grained GitHub credentials: Contents, Pull requests, Actions and Checks read-only for the reader; Contents and Pull requests read/write for publication only. No workflow, deployment, billing, account-wide or organization admin rights. Never enter tokens in memory, objectives, or GitHub.
5. Ensure the owner's independent PR CI job actually builds and runs tests. **The CI executing generated source must be isolated, read-only and have no secrets or deployment privileges.** Review repository workflows and branch protection; verify CI runs on draft pull requests, and that the build and test gates cannot be replaced by generated changes. Do not enable repositories whose untrusted PR code runs on privileged runners.
6. The worker requires both scoped credentials, both explicit allowlists, the autonomy switches, an AI key and a working CI profile. Registering another project does not create or modify its repository.

## Workflows

- Create a persistent objective and optionally link it to an earlier objective in the same project. Owner acceptance releases dependencies. Accepted means the owner says it is satisfactory; it is not an Archer deployment claim.
- **Review-first:** select an objective or enter a small instruction, request a budget-checked plan, inspect the exact eligible file paths, authorize the batch once.
- **Delegate to Archer:** explicitly authorize one paid planning attempt and bounded draft coding for a saved objective and its registered source roots for 24 hours. Archer selects ready objectives by priority after outstanding CI, refuses unresolved dependencies, and creates and arms a batch without per-task approval. Cancel delegation or pause the project at any time. No automatic renewal.
- Review final and earlier failed draft PRs. Required independent CI must pass on the exact recorded head. A verified draft still needs human integration and release review. Archer cannot merge, deploy, or transact.
- Project memory stores owner notes, architectural decisions, preferences and lessons with timestamps and optional AI sharing. CI evidence and blockers are recorded automatically in the relevant project. The planner and project code model retrieve only opted-in entries from that project. Chat retrieves notes only when exactly one project is explicitly named; project names and status summaries are included as general context. Delete local memory entries from the panel. Never put secrets or employer information in memory.

## Current operating limits

Workspace source: .cs, .razor, .js, .jsx, .ts, .tsx, .py inside explicit application roots, up to four editable files and six separate read-only context files per task. Existing files are limited to 9,000 bytes; total generated content is capped at 36,000 characters. New application source files (up to two within the four-file limit) require explicit project registration opt-in and must not collide with any existing tree entry. The worker reads one immutable snapshot and publishes a single commit on a new isolated branch, refusing a stale base tree. Inventory is bounded at 12,000 tree entries and 1,200 reviewable files. Test directories, workflow files, scripts, entrypoints and common sensitive/config files remain excluded. Larger refactors, test authoring, cumulative integration branches, automatic release/deploy, isolated shell execution and additional languages need further implementation. CI profiles are owner-defined; their semantics are not independently proven by Archer. Source review is not security validation.

One or two attempts per task, at most three independent tasks / six attempts per batch, per-project rolling attempt quota, and the existing shared local AI budget ledger. This is not an independent dollar budget per project or a provider-wide billing cap. The worker runs locally while Archer runs; multiple projects may queue, but a single worker executes serially. Pausing cancels queued authorization, not necessarily an already running request. Restart disarms by default; optional explicit restart permission preserves only safe unclaimed work and CI polling within the original deadline. Ambiguous interrupted paid planning or publication is blocked, not retried.

Archer's project registry is still backed by the local state store. It is not the production multi-device database, hardened secrets vault or a full autonomous coding sandbox. The phone app has not been modified in this batch.

## Example profile (only if the owner chooses to register it)

GeoShift: `GravyBets/GeoShift`, .NET, roots `GeoShift.Core, GeoShift.Web`, workflow `archer-ci.yml`, job `GeoShift build and tests`, required steps `Build Web` and `Run core regression tests`. This profile is illustrative; no default project is seeded. Do not run the first paid autonomous mission until the new Archer build is verified and configured.
