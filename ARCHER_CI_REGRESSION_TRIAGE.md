# Archer CI failure detection — no AI calls or owner work required

**Review-only implementation:** this workflow becomes active only after the
owner reviews and merges it into the GitHub default branch. It is NOT active
in a pull request. It does not access the home PC, live Archer service, APIs
or phone, and creates no model usage charges.

## What it does

If the ordinary **Archer Build** GitHub Actions workflow fails on an actual
push to the **main** branch of `GravyBets/Archer`, the CI triage workflow
runs a small Python script from the trusted default branch. It verifies the
trigger repository, workflow, branch, event kind, conclusion and exact GitHub
run URL; it does **not** inspect or execute artifacts from the failed build.

It queries GitHub only for failed job and step **names**, never raw build logs
or source. It creates one open issue titled:

> Archer main build is failing — triage needed

The issue receives only the `archer-ci-triage` label, never
`archer-approved`. Further main failures add comments to that same issue
rather than opening duplicates. The owner can review the issue and source
run, diagnose the cause, and approve a **separate focused fix** if desired.

The separate Archer self-work worker proposed in PR #42 runs only on
`archer-approved` issues, does at most one bounded model call, and submits
a **draft** PR only after isolated tests pass. A CI regression report is
**not** automatic consent for Archer to change its code or spend money.
If a reported issue is too broad or outside the current allowlisted files,
create a smaller issue and explicitly approve that one.

## How to stop

Disable the **Archer CI Regression Triage — No AI Spending** workflow in
GitHub Actions, or revert this workflow file. No paid API key is stored or
required. The only permission to mutate GitHub is scoped `issues: write`
in the triage job.

## Safeguards

- Only failed **main** push runs from `GravyBets/Archer`; never forked PRs.
- `actions/checkout` explicitly checks out `main`, without persisted auth.
- Uses the normal GitHub Actions token with `contents: read`, `actions: read`
  and `issues: write` in the triage job; cannot push commits, merge or deploy.
- No logs or artifacts downloaded; no secrets, model keys or shell commands
  are sourced from failed CI metadata.
- No auto-approval or automatic repair; failure issues stay open for human review.
- No VPS/cloud service or always-on desktop is required.

The built-in workflow notification preferences in GitHub may be used for
separate email/mobile push notices of failed actions. This code does not
send emails, texts or phone calls.
