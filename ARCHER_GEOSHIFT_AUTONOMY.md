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

Workspace source: .cs, .razor, .js, .jsx, .ts, .tsx, .py inside explicit application roots, up to four editable files and six separate read-only context files per task. Existing files are limited to 9,000 bytes; total generated content is capped at 36,000 characters. New application source files (up to two within the four-file limit) require explicit project registration opt-in and must not collide with any existing tree entry. The worker reads one immutable snapshot and publishes a single commit on a new isolated branch, refusing a stale base tree. Inventory is bounded at 12,000 tree entries and 1,200 reviewable files. Test directories, workflow files, scripts, entrypoints and common sensitive/config files remain excluded. Larger refactors, automatic release/deploy, isolated shell execution and additional languages need further implementation. CI profiles are owner-defined; their semantics are not independently proven by Archer. Source review is not security validation.

One or two attempts per task, at most three independent tasks / six attempts per batch, per-project rolling attempt quota, and the existing shared local AI budget ledger. This is not an independent dollar budget per project or a provider-wide billing cap. The worker runs locally while Archer runs; multiple projects may queue, but a single worker executes serially. Pausing cancels queued authorization, not necessarily an already running request. Restart disarms by default; optional explicit restart permission preserves only safe unclaimed work and CI polling within the original deadline. Ambiguous interrupted paid planning or publication is blocked, not retried.

Archer's project registry is still backed by the local state store. It is not the production multi-device database, hardened secrets vault or a full autonomous coding sandbox. The phone app has not been modified in this batch.

## Example profile (only if the owner chooses to register it)

GeoShift: `GravyBets/GeoShift`, .NET, roots `GeoShift.Core, GeoShift.Web`, workflow `archer-ci.yml`, job `GeoShift build and tests`, required steps `Build Web` and `Run core regression tests`. This profile is illustrative; no default project is seeded. Do not run the first paid autonomous mission until the new Archer build is verified and configured.

## Sequential workspaces and additive regressions

New registration options are disabled by default. Enable cumulative workspaces only for a project whose draft PR CI runs on both opening and synchronize events. All tasks in a batch share one Archer-created draft branch. The next task reads the exact previously published and verified commit, produces an atomic commit, rechecks the open draft PR identity and performs a non-force fast-forward. Existing branch history is not rewritten. CI must pass again before the next task. A failed attempt can repair the same head once; interfering changes or a PR marked ready for review block further writes. Review the final head and all accumulated changes; intermediate green checks are historical evidence, not a deployment. Do not edit or merge a branch while Archer is working on it.

Optional additive regression roots require cumulative mode and a dedicated existing test directory (such as tests/unit or MyApp.Tests). One new regression per task may accompany application changes. Allowed names: ArcherNameTests.cs, archer-name.test.ts/js/tsx/jsx, test_archer_name.py. Existing tests and CI remain protected. A test created during a failed attempt is frozen as read-only context while application code is repaired. If the test itself is erroneous, owner review is required. The configured CI must actually discover the new tests; green CI alone does not prove a newly added test ran or reproduced an issue.

Before planning, a bounded deterministic source retrieval pass ranks filenames using the objective and extracts relevant real declarations/imports and source excerpts from the same tree. It does not clone the full repository, run source, or grant permission to edit context. This is an initial code retrieval capability, not a complete architecture graph or persistent semantic index.

## Optional isolated verification

Projects may now require an additional exact-head isolated Linux container verdict before accepting GitHub CI. It is off by default, requires owner-provisioned local rootless Podman, a pinned trusted offline image and a dedicated disposable Linux environment. See [ARCHER_SANDBOX.md](ARCHER_SANDBOX.md). No direct host execution or model-supplied shell commands; remote sandbox hosting and real application image provisioning remain separate work.

## Continuous objectives (optional, cumulative projects only)

In Mission Control, choose **Delegate continuous objective** instead of **Delegate to Archer**. The confirmation states the original 24-hour deadline, the maximum of four total paid planning calls (including the initial plan) and up to twelve total coding attempts, limited further by the registered project's rolling allowance. The API accepts explicit lower limits. This is one nonrenewable delegation, not a recurring budget, subscription or indefinite autonomous loop. All paid model requests still pass the shared AI spend ledger.

After each set of up to three tasks passes required verification, Archer checks the exact prior head on the same open draft PR again (including current required sandbox evidence) and reads the immutable cumulative source snapshot. The next planning call receives the original objective, earlier CI-verified task history and relevant actual source. The planner can recommend continuation with new tasks, owner review with no new tasks, or stop as blocked. Subsequent work appends to the same batch and draft branch; verified history and previous attempts are retained. The engine rejects exact duplicate task instructions/path sets and refuses to exceed the original cycle, coding-attempt, daily project or time allowance. It does not guarantee the model discovers every remaining improvement or correctly judges completion.

Cancel delegation or pause the project/batch to stop further cycles. An already-started external request can finish. Restart only resumes explicitly authorized, safe unclaimed work; interrupted planning or uncertain publication is blocked. An expired, canceled or exhausted continuous objective cannot be silently rearmed or renewed through the standalone batch controls. To continue beyond the limit, the owner must review the draft and create a fresh explicitly authorized objective. The draft is never automatically merged or deployed. An Archer recommendation of completion is not independent proof of semantic correctness, owner acceptance, or a deployed feature; dependent objectives still require owner acceptance.

## Protected Archer self-development

Archer itself can now be registered using a separately gated, cumulative self-project preset. Its first pilot is restricted to existing approved inbox/developer presentation files and fixed CI steps. See [ARCHER_SELF_DEVELOPMENT.md](ARCHER_SELF_DEVELOPMENT.md) for authorization, candidate review and manual release/rollback boundaries.
