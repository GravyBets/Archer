# Controlled Archer self-development

This replaces the retired scheduled self-work workflow with the same managed-project engine used for other projects. It is disabled by default. Archer can prepare and verify changes to its own repository; it cannot authorize its own scope, approve its own release, or install itself.

## Configure the pilot

1. In Mission Control, open Managed projects → Archer self-development → Register protected Archer project. The preset uses GravyBets/Archer, cumulative workspaces and the fixed Archer Build workflow and required steps.
2. Configure the normal per-repository scoped read/write tokens and project allowlists. Ensure repository branch protection and independent PR checks are active. Never grant workflow, admin or deployment permission.
3. Set ARCHER_SELF_PROJECT_ENABLED=true and ARCHER_SELF_PROJECT_FILES to an exact comma-separated subset of:
   - Archer.MissionControl/wwwroot/js/inbox.js
   - Archer.MissionControl/wwwroot/js/developer.js

The compiled policy refuses other paths even if they are named in the environment, and refuses new files and regression test edits for Archer itself. The older manual publishing path refuses Archer entirely. This is a narrow pilot, not permission to modify the whole operating system or a sandbox security boundary. Keep generated code in drafts; never execute an unreviewed change with live Archer credentials.

4. Create an objective concerning an approved module, for example improving the inbox empty-state text. Delegate one cycle or bounded continuous work. No paid self-mission is initiated by registration.

## Candidate review and rollback preparation

A completed cumulative self-mission retains the original default-branch source commit, draft PR identity, current exact head and task evidence. Recheck self-development candidate from Mission Control. It refuses active, incomplete, unapproved, changed or non-draft candidates and queries independent CI again. It displays a source baseline, exact draft head and comparison. CI verification is not proof of correctness or release authorization.

Review the whole diff and all workflow checks before manually approving/merging. Retain the exact installed known-good package and back up local state before any installation. The baseline commit is a source recovery reference, not an assertion about the actual installed version or an automatic rollback package. Existing Windows installer rollback tests are separate; no new release installer, actual deployment or live rollback has been exercised by this feature.

The old GitHub scheduled self-work workflow is intentionally retired. Its historical tools remain for reference and existing tests; the claim job cannot execute. Use the protected project path only.

## Remaining work

A live owner-configured pilot with a real model key and scoped tokens is required. Expand the self-development scope only by human-reviewed policy changes, with independent verification. Actual signed release packaging, version-aware rollback orchestration, broader repository intelligence and remote operations are separate milestones. Do not claim that Archer has autonomously modified or upgraded its running instance merely because its self-development implementation passes synthetic checks.
