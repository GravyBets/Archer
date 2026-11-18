#!/usr/bin/env python3
"""Read-only GitHub Actions failure summary -> one unapproved GitHub triage issue.

The triggering workflow_run may contain untrusted metadata. Verify event,
repo, branch, event type and failure status BEFORE using a privileged
repository-scoped token. Never download/run build artifacts, checkout PR
code, read logs, touch source, trigger AI, or auto-add archer-approved.
"""
from __future__ import annotations

import json
import os
from pathlib import Path
import re
import sys
import urllib.error
import urllib.request

REPOSITORY = "GravyBets/Archer"
TRIAGE_LABEL = "archer-ci-triage"
ISSUE_TITLE = "Archer main build is failing — triage needed"
WORKFLOW_NAME = "Archer Build"


class Refused(RuntimeError):
    pass


def validate_event(event: dict, actual_repo: str) -> dict:
    run = event.get("workflow_run", {})
    if not isinstance(run, dict) or actual_repo != REPOSITORY:
        raise Refused("Unexpected GitHub event or repository; no issue created.")
    trusted_head = run.get("head_repository") or {}
    if (run.get("name") != WORKFLOW_NAME or
        run.get("head_branch") != "main" or
        run.get("event") != "push" or
        run.get("conclusion") != "failure" or
        trusted_head.get("full_name") != REPOSITORY):
        raise Refused("Only failed pushes to Archer main are eligible.")
    run_id = run.get("id")
    if not isinstance(run_id, int) or isinstance(run_id, bool) or run_id <= 0:
        raise Refused("Invalid workflow run ID.")
    exact = f"https://github.com/{REPOSITORY}/actions/runs/{run_id}"
    if run.get("html_url") != exact:
        raise Refused("Workflow URL does not match the verified repository and run.")
    return {"id": run_id, "url": exact}


def trim_title(value: str) -> str:
    # Job/step names are untrusted labels. Never use as commands or HTML.
    cleaned = re.sub(r"[^\w .:\-()/]", " ", value)
    return re.sub(r"\s+", " ", cleaned).strip()[:110] or "unnamed check"


def summarize_failures(jobs: list[dict]) -> list[str]:
    names: list[str] = []
    for job in jobs:
        if job.get("conclusion") != "failure":
            continue
        name = trim_title(str(job.get("name") or "job"))
        steps = job.get("steps") or []
        failed_steps = [
            trim_title(str(step.get("name") or "step"))
            for step in steps if step.get("conclusion") == "failure"
        ]
        if failed_steps:
            names.extend(f"{name}: {step}" for step in failed_steps[:8])
        else:
            names.append(name + ": failure (step not reported)")
    return names[:16]


def issue_body(run: dict, failures: list[str]) -> str:
    details = "\n".join("- " + text for text in failures) or "- No failed step metadata available"
    return (
        "## Archer CI regression detected\n\n"
        f"Failed **main** build: {run['url']}\n\n"
        "Failed checks (names only; no logs, source, secrets or artifacts copied):\n\n"
        f"{details}\n\n"
        "### What Archer may do next\n"
        "This issue is **triage only**. It is NOT approved for paid AI self-work. "
        "Review the actual GitHub Actions run and confirm the failure is reproducible. "
        "A separate owner decision is required before applying the "
        "`archer-approved` label for any bounded code repair.\n\n"
        "No automatic merge, deployment or model request was triggered."
    )


def fetch_json(path: str, token: str, method: str = "GET",
               payload: dict | None = None) -> dict | list:
    if not path.startswith("/repos/GravyBets/Archer/"):
        raise Refused("Unexpected GitHub API destination.")
    url = "https://api.github.com" + path
    headers = {
        "Accept": "application/vnd.github+json",
        "Authorization": "Bearer " + token,
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "Archer-CI-Triage/1.0",
    }
    data = None if payload is None else json.dumps(payload).encode("utf-8")
    if data is not None:
        headers["Content-Type"] = "application/json"
    try:
        with urllib.request.urlopen(urllib.request.Request(
            url, data=data, method=method, headers=headers), timeout=30
        ) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        # No raw GitHub response bodies to avoid accidentally echoing secrets.
        raise Refused(f"GitHub API unavailable (HTTP {error.code}).") from None
    except urllib.error.URLError:
        raise Refused("GitHub API is unavailable.") from None


def run() -> None:
    token = os.environ.get("GH_TOKEN", "")
    if not token:
        raise Refused("Missing narrowly scoped GitHub Actions token.")
    filename = os.environ.get("GITHUB_EVENT_PATH", "")
    if not filename:
        raise Refused("Missing GitHub event payload.")
    event = json.loads(Path(filename).read_text(encoding="utf-8"))
    workflow = validate_event(event, os.environ.get("GITHUB_REPOSITORY", ""))
    base = "/repos/GravyBets/Archer"
    jobs = fetch_json(base + f"/actions/runs/{workflow['id']}/jobs?per_page=100", token)
    failures = summarize_failures(jobs.get("jobs", []))
    body = issue_body(workflow, failures)

    # Only one open issue for main CI regressions. Repeated failed runs update
    # it with a new comment, not an endless stream of duplicate issues.
    labels = fetch_json(base + "/labels?per_page=100", token)
    if not any(x.get("name") == TRIAGE_LABEL for x in labels):
        fetch_json(base + "/labels", token, "POST", {
            "name": TRIAGE_LABEL, "color": "A2B3BE",
            "description": "CI failure requiring owner triage; never auto-approved for AI work",
        })
    open_issues = fetch_json(
        base + "/issues?state=open&labels=archer-ci-triage&per_page=100", token)
    existing = next((x for x in open_issues
                     if x.get("title") == ISSUE_TITLE and
                     not x.get("pull_request")), None)
    if existing is not None:
        number = existing.get("number")
        if not isinstance(number, int) or number < 1:
            raise Refused("Invalid existing CI issue.")
        # No issue label mutation, no model invocation or auto-approval.
        fetch_json(base + f"/issues/{number}/comments", token, "POST", {
            "body": "Another failed **main** build needs review:\n\n" + body,
        })
        print(f"Updated existing unapproved Archer CI triage issue #{number}.")
    else:
        result = fetch_json(base + "/issues", token, "POST", {
            "title": ISSUE_TITLE,
            "body": body,
            "labels": [TRIAGE_LABEL],
        })
        print(f"Opened unapproved Archer CI triage issue #{result['number']}.")


if __name__ == "__main__":
    try:
        run()
    except Refused as error:
        print("CI triage declined: " + str(error), file=sys.stderr)
        raise SystemExit(1) from None
