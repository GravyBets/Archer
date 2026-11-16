# Archer: CEO / AI Business Operator Roadmap

**Owner role:** CEO / final authority  
**Archer role:** Operations manager and delegated AI agent  
**Initial business:** GeoShift (KML/KMZ -> CSV conversion)
**Future business:** A separate website/service, scope to be defined after GeoShift shows sustainable economics.

## North-star outcome

Archer helps complete GeoShift, launch it, and operate repeatable business workflows, while the owner receives clear daily updates and immediate escalations for genuinely important events. GeoShift's **collected revenue**, less refunds, taxes, infrastructure, customer-acquisition costs, AI usage, and support, should eventually cover Archer's own operating costs and generate positive cash flow. Success is measured on *net contribution*, not gross revenue alone.

An AI operator is **not** an independent legal officer or an infallible decision-maker. Archer is a controlled software system. The owner retains responsibility for finances, customer commitments, privacy, compliance, and approval of consequential actions.

## Phase 1 — Reliable research and grounded development review

**Current foundation:** local Mission Control, bounded research tasks, persistent reports, optional project knowledge, opt-in auto-run and a sampled **read-only GitHub repository inspector**.

**Next acceptance tests:**
- GitHub connectivity endpoint lists actual source paths from GeoShift.
- Code-review report cites specific inspected files/lines when available; it does not claim to have run the code.
- Separate a recommendation from an implemented change and report what still needs verification.
- A failed GitHub read stops the affected task, rather than producing fictional code analysis.

## Phase 2 — Archer as GeoShift development partner

Build a constrained development workflow:
1. Read relevant repository files and project specifications.
2. Create an actionable issue or task with acceptance criteria.
3. Generate changes **on a dedicated Git branch**, never directly to production or the default branch.
4. Run build, automated tests and static checks in an isolated runner with network restrictions.
5. Open a pull request with code diff, test results, limitations and rollback instructions.
6. Request CEO approval to merge. Production deployments require separate authorization.

**Exit criterion:** Archer prepares several correct, test-passing GeoShift pull requests that the owner can review and merge, without requiring hand-edited code copying.

## Phase 3 — GeoShift product launch and paid conversion

Focus on:
- File conversion fidelity, size limits, reliability, observability, and user-friendly error feedback.
- Data retention/privacy policy and deletion controls for geographic uploads.
- Payment processing integration and access control; never let an AI agent directly handle card details.
- Free sample conversion, pricing experiments, cancellation/refund policies, and metering.
- Conversion funnel analytics, bug reports, customer feedback, support playbooks and incident response.

**Exit criterion:** Verified successful end-to-end paid customer journeys, realistic conversion rates, support response plan, and reliable deployments with rollback.

## Phase 4 — Archer as day-to-day business operator

Archer can run preapproved routines:
- Check uptime, error rates and failed conversions.
- Prepare customer support replies from a trusted knowledge base; auto-send only low-risk templates the CEO explicitly allows.
- Review sales, expenses, refunds, churn and support backlog.
- Recommend product improvements based on measured customer pain points.
- Prepare code change proposals and test results in reviewable pull requests.
- Track marketing experiments with approved budgets and attribution.

### Communications policy

**Daily text digest** (time and timezone chosen by owner):
- Collected revenue and refunds
- Successful conversions and errors
- New users, trials, paying customers, churn
- Running expenses, API usage and net contribution estimate
- Support queue, anomalies, shipped changes and next priorities

**Text for normal exceptions:** request approval, low inventory/quota, noncritical deployment issue, customer response needed.

**Call for important time-sensitive situations:** sustained outage, suspected breach, large unexpected spending, serious customer-impacting error or loss of payment processing. Caller should first state the facts, confidence, impact, and proposed safe action. If the owner doesn't answer, escalate by SMS according to a separately approved contact policy. Never claim a true emergency without verified conditions.

**Quiet hours:** default to text-only and defer nonurgent calls. Define configurable emergency override. Avoid uncontrolled repeated calls.

### Approval and authority boundaries

**May automate after testing and permission:** diagnostics, metrics, daily summaries, non-destructive code generation, draft replies, scoped research.

**Require explicit owner approval:** outbound sales messages beyond approved templates, refunds beyond set thresholds, payment settings, advertising budget changes, repo merges, deployments, access policy changes, legal commitments, customer data exports.

**Never autonomous without separate controls:** use of credit cards/banking credentials, secrets disclosure, arbitrary shell commands on production, removal of backups, changes to owner approval rules.

## Phase 5 — Sustainable funding of a second business

Only allocate GeoShift surplus once:
- Gross receipts and costs are reconciled with payment processor and bank records.
- Refunds/chargebacks, taxes, support and hosting obligations are estimated.
- Archer's recurring expenses are covered by an explicit budget.
- A minimum operating reserve is established.
- The CEO approves a fixed monthly investment budget for the new website.

Archer may research, prototype, plan and propose development for the second business in an **isolated** project/repository. Spending and public launches require approval.

## Architecture targets

- **Backend:** .NET services, persistent job queue (not only in-memory tasks).
- **Memory:** curated knowledge documents, source-linked notes, audit log and indexed project repository context; versioned with relevance and expiration.
- **Execution:** separate task permissions, per-mission limits, cost/token metering, idempotency, dead-letter queue, circuit breakers, stop switch.
- **Source control:** GitHub App scoped to selected repositories; isolated coding branches, CI checks and protected main branch.
- **Communications:** SMS and outbound voice integration; verified destination allowlist, consent, quiet hours, delivery logs, retries and cost limits.
- **Business integrations:** payment provider, CRM/helpdesk, uptime/analytics, billing reports, approved external messaging.
- **Hosting/security:** authenticated control panel, secret vault, backups, monitoring, separation between development and production.

## First immediate priorities

1. Verify Archer v0.2.1 can read the actual GeoShift code.
2. Upgrade code review from sampled text to targeted repository queries with source-specific citation and issue tracking.
3. Implement a constrained code-development agent and pull-request review workflow.
4. Add usage/cost accounting and approval checkpoints before long-running autonomy.
5. Plan SMS daily briefings and configurable incident calls after business telemetry is available.

**Note:** This document is an engineering and operations roadmap, not proof of business viability or a promise that AI can run a company without human oversight.
