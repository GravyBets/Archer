#!/usr/bin/env python3
"""Pure CI regression triage checks: no network, GitHub token, issue or model."""
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
import archer_ci_triage as triage


def event(**overrides):
    run = {
        "id": 42,
        "name": "Archer Build",
        "head_branch": "main",
        "head_repository": {"full_name": "GravyBets/Archer"},
        "event": "push",
        "conclusion": "failure",
        "html_url": "https://github.com/GravyBets/Archer/actions/runs/42",
    }
    run.update(overrides)
    return {"workflow_run": run}


class TriageChecks(unittest.TestCase):
    def test_accept_only_actual_failed_main_push(self):
        doc = triage.validate_event(event(), "GravyBets/Archer")
        self.assertEqual(doc["id"], 42)
        self.assertEqual(doc["url"], "https://github.com/GravyBets/Archer/actions/runs/42")

    def test_reject_untrusted_events_and_repositories(self):
        for changed in [
            {"head_branch": "some-pr"},
            {"event": "pull_request"},
            {"conclusion": "success"},
            {"conclusion": "cancelled"},
            {"name": "Other Workflow"},
            {"head_repository": {"full_name": "attacker/Archer"}},
            {"html_url": "https://attacker.example/a"},
            {"id": -1},
            {"id": True},
            {"id": "42"},
        ]:
            with self.subTest(changed=changed):
                with self.assertRaises(triage.Refused):
                    triage.validate_event(event(**changed), "GravyBets/Archer")
        for wrong_repo in ["attacker/Archer", "", "../GravyBets/Archer"]:
            with self.assertRaises(triage.Refused):
                triage.validate_event(event(), wrong_repo)

    def test_failed_step_names_only_and_sanitized(self):
        jobs = [
            {"name": "Build <script>alert(1)</script>", "conclusion": "failure",
             "steps": [
                 {"name": "Restore", "conclusion": "success"},
                 {"name": "Run tests `$(sh)`", "conclusion": "failure"},
             ]},
            {"name": "Lint", "conclusion": "failure", "steps": []},
            {"name": "Success", "conclusion": "success",
             "steps": [{"name": "Bad", "conclusion": "failure"}]}
        ]
        problems = triage.summarize_failures(jobs)
        self.assertEqual(len(problems), 2)
        self.assertNotIn("<script>", " ".join(problems))
        self.assertNotIn("`", " ".join(problems))
        self.assertIn("Lint", problems[1])

    def test_issue_body_does_not_claim_approval(self):
        run = triage.validate_event(event(), "GravyBets/Archer")
        text = triage.issue_body(run, ["Build: Compile"])
        self.assertIn("https://github.com/GravyBets/Archer/actions/runs/42", text)
        self.assertIn("NOT approved", text)
        self.assertIn("No automatic merge", text)
        self.assertIn("Build: Compile", text)

    def test_api_destination_pinned(self):
        with self.assertRaises(triage.Refused):
            triage.fetch_json("/repos/attacker/Archer/issues", "fake")

    def test_workflow_does_not_run_model_or_download_pr_artifacts(self):
        content = (Path(__file__).resolve().parents[1] /
                   ".github/workflows/archer-ci-triage.yml").read_text()
        self.assertIn("workflow_run:", content)
        self.assertIn("head_branch == 'main'", content)
        self.assertIn("head_repository.full_name == github.repository", content)
        self.assertIn("persist-credentials: false", content)
        self.assertNotIn("download-artifact@", content)
        self.assertNotIn("OPENAI_API_KEY", content)
        self.assertNotIn("pull-requests: write", content)
        self.assertNotIn("contents: write", content)


if __name__ == "__main__":
    unittest.main()
