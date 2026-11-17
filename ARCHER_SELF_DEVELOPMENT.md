# Letting Archer work on Archer — safely

## What works now

Archer's **Developer Workspace** already reads selected, explicitly allowlisted
source files from GitHub, shows their exact content, and can prepare focused
AI-assisted code reviews. Its **Coding agent · review first** section can
generate proposed source changes from **one or two files**. It writes a
local draft without changing the GitHub repo. Using OpenAI for a proposal
requires the operator's explicit confirmation and is charged against the
local AI spending safeguards.

The existing GitHub writer can open an **isolated branch + draft PR** on
an explicitly approved repository, but only when the user has separately
configured a narrow GitHub write token, enabled coding, and clicked through
the explicit GitHub write confirmation. It cannot merge, deploy, run arbitrary
shell commands, or automatically authorize a release.

This milestone adds the **Inspect Archer** button: it fills in the Archer
repo and invokes the *same* allowlisted, read-only repository loader.
It does not bypass any security policy or start an AI request.

## Try it from your PERSONAL Windows PC

1. Stop Archer on your home PC.
2. In the PowerShell session used to launch it, add the exact self-review
   allowlist, keeping the GeoShift option if you also want it:

   ```powershell
   $env:ARCHER_GITHUB_ALLOWLIST="GravyBets/GeoShift,GravyBets/Archer"
   ```

   This environment variable is scoped to that PowerShell process. Do not
   configure it on your employer's computers.
3. Restart Archer from that same terminal. Open **Developer Workspace** and
   select **Inspect Archer**. Click **Preview selected** after checking one
   or two relevant files.
4. Under **Coding agent · review first**, describe a *small* change such as
   "Improve the voice pace control and propose a regression test." Select
   **Generate local code proposal**. Archer will ask for permission before
   submitting source excerpts to the AI. The resulting draft needs review;
   it is not an executed build or a verified fix.
5. Review the generated proposal locally. When you are comfortable with
   review-only work, we can configure *optional* GitHub draft PR publishing
   using a dedicated fine-grained token that can write to **only** your
   Archer repository. **Do not** paste GitHub credentials into this chat,
   store tokens in source, or enable broad organization access.

## What is *not yet autonomous*

- Archer does **not** safely select backlog issues by itself and apply
  patches continuously.
- It does **not** run its generated code in an isolated build sandbox or
  interpret GitHub Actions failures for self-repair loops yet.
- It does **not** automatically approve GitHub writes, merge PRs, deploy its
  own server, or spend outside configured budgets.
- It has no public iPhone gateway access yet; its website and login previews
  do not authorize code modifications.

## Recommended staged upgrade path

1. **Current:** owner-approved focused code review and proposal.
2. **Next:** sandboxed code-branch runner, dedicated repository allowlist,
   snapshots/rollback, controlled tool permissions, explicit human review.
3. **Then:** read GitHub Actions outcomes and propose one bounded repair for
   failed CI; no automatic merge.
4. **Later:** optional scheduled self-maintenance from an owner-approved
   issue backlog, per-mission costs/time limits, hard kill switch, audit trail.
5. **Only after proven:** gradually delegate nonproduction low-risk PR prep
   while still retaining explicit owner approval for merging, deployment,
   external spending and sensitive operations.

Do not grant unattended code execution against your Windows workstation,
personal passwords or entire GitHub account merely to accelerate Archer's
learning. Autonomous code generation without tests, rollback and narrow
permissions can quickly damage a working application.
