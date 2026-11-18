#!/usr/bin/env python3
"""Deterministic self-work safety checks; no API key, GitHub writes or AI calls."""
from pathlib import Path
import json
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
import archer_self_work as worker


class SafetyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.allowed = worker.FILE_PAIRS["voice"]
        for name in self.allowed:
            target = self.root / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text("function start() {\n  return 'old';\n}\n", encoding="utf-8")

    def patch(self, edits=None):
        return {
            "summary": "Repair a small validated source behavior",
            "tests": "Run voice browser and regression checks",
            "edits": ([{
                "path": self.allowed[0],
                "old_text": "return 'old';",
                "new_text": "return 'new';",
            }] if edits is None else edits),
        }

    def test_public_allowlist_and_scope(self):
        self.assertEqual(len(worker.FILE_PAIRS), 5)
        for pair in worker.FILE_PAIRS.values():
            self.assertEqual(len(pair), 2)
            self.assertTrue(all(p.startswith(("Archer.MissionControl/wwwroot/", "tests/"))
                                for p in pair))
            self.assertFalse(any(".." in p for p in pair))

    def test_only_approved_unattempted_issue_selected(self):
        issues = [
            {"number": 2, "created_at": "2026-11-02", "state": "open",
             "labels": [{"name": "archer-approved"}],
             "pull_request": {"url": "some-pr"}},
            {"number": 1, "created_at": "2026-11-01", "state": "open",
             "labels": [{"name": "archer-approved"}, {"name": "archer-attempted"}]},
            {"number": 4, "created_at": "2026-11-05", "state": "open",
             "labels": [{"name": "archer-approved"}]},
            {"number": 3, "created_at": "2026-11-03", "state": "open",
             "labels": [{"name": "archer-approved"}]},
        ]
        self.assertEqual(worker.select_issue(issues)["number"], 3)
        self.assertEqual(worker.select_issue(issues, 4)["number"], 4)
        self.assertIsNone(worker.select_issue(issues, 1))
        self.assertIsNone(worker.select_issue(issues, 99))

    def test_keyword_route_does_not_require_file_selection(self):
        category, paths = worker.choose_files("Fix long speech pauses", "Mic quits listening")
        self.assertEqual(category, "voice")
        self.assertIn("archer-home.js", paths[0])
        category, paths = worker.choose_files("Code proposal should remember review", "")
        self.assertEqual(category, "coding")
        with self.assertRaises(worker.SafeFailure):
            worker.choose_files("Billing and bank transfer operations", "Do transfers")

    def test_exact_one_occurrence_required(self):
        changed = worker.validate_patch(self.patch(), self.allowed, self.root)
        self.assertIn("return 'new';", changed[self.allowed[0]])
        self.assertEqual(len(changed), 1)
        (self.root / self.allowed[0]).write_text("return 'old'; return 'old';",
                                                  encoding="utf-8")
        with self.assertRaises(worker.SafeFailure):
            worker.validate_patch(self.patch(), self.allowed, self.root)

    def test_refuse_unapproved_write_path(self):
        for bad in (".github/workflows/build.yml", "../secret.env",
                    "Archer.MissionControl/Program.cs", "tests/../secret.txt"):
            p = self.patch([{
                "path": bad, "old_text": "return 'old';",
                "new_text": "return 'oops';",
            }])
            with self.assertRaises(worker.SafeFailure):
                worker.validate_patch(p, self.allowed, self.root)

    def test_refuse_oversized_and_malformed_patch(self):
        for payload in [
            self.patch([]),
            self.patch([{"path": self.allowed[0], "old_text": "old", "new_text": "new"}]),
            self.patch([{"path": self.allowed[0], "old_text": "return 'old';",
                         "new_text": "B" * 5501}]),
            self.patch([{"path": self.allowed[0], "old_text": "return 'old';",
                         "new_text": "return 'old';"}]),
            {"summary": "Short", "tests": "yes", "edits": [], "surprise": "test"},
        ]:
            with self.assertRaises(worker.SafeFailure):
                worker.validate_patch(payload, self.allowed, self.root)

    def test_refuse_symlink(self):
        path = self.root / self.allowed[0]
        path.unlink()
        path.symlink_to(self.root / self.allowed[1])
        with self.assertRaises(worker.SafeFailure):
            worker.validate_patch(self.patch(), self.allowed, self.root)

    def test_patch_schema_requires_exact_document_fields(self):
        schema = worker.patch_schema()
        self.assertEqual(schema["type"], "object")
        self.assertFalse(schema["additionalProperties"])
        self.assertEqual(schema["required"], ["summary", "tests", "edits"])
        props = schema["properties"]["edits"]["items"]
        self.assertFalse(props["additionalProperties"])

    def test_validate_claim_rejects_unauthorized_scopes(self):
        claim = {"selected": True, "number": 7, "category": "voice",
                 "allowedPaths": list(self.allowed)}
        self.assertEqual(worker.validate_claim(claim)[1], self.allowed)
        claim["allowedPaths"] = [".env", self.allowed[1]]
        with self.assertRaises(worker.SafeFailure):
            worker.validate_claim(claim)

    def test_repair_must_match_original_approval_without_api_call(self):
        import argparse
        import os
        from unittest.mock import patch as mock_patch
        claim = {"selected": True, "number": 19, "category": "voice",
                 "allowedPaths": list(self.allowed), "title": "Fix speech",
                 "body": "Allow a long pause"}
        claim_file = self.root / "claim.json"
        claim_file.write_text(json.dumps(claim), encoding="utf-8")
        prior = {"issue": 20, "category": "voice",
                 "allowedPaths": list(self.allowed),
                 "patch": self.patch(), "attempt": 1}
        patch_file = self.root / "first.json"
        patch_file.write_text(json.dumps(prior), encoding="utf-8")
        feedback = self.root / "feedback.txt"
        feedback.write_text("Failing independent regression check.", encoding="utf-8")
        args = argparse.Namespace(claim=str(claim_file), output=str(self.root / "revised.json"),
                                  previous=str(patch_file), feedback=str(feedback))
        with mock_patch.dict(os.environ, {"ARCHER_SELF_WORK_OPENAI_API_KEY": ""}), \
             mock_patch.object(worker, "ROOT", self.root):
            with self.assertRaisesRegex(worker.SafeFailure, "not bound"):
                worker.generate(args)
            prior["issue"] = 19
            prior["attempt"] = 2
            patch_file.write_text(json.dumps(prior), encoding="utf-8")
            with self.assertRaisesRegex(worker.SafeFailure, "not bound"):
                worker.generate(args)
            prior["attempt"] = 1
            patch_file.write_text(json.dumps(prior), encoding="utf-8")
            # A valid retry passes owner/scope checks and refuses to call
            # OpenAI without explicit secret; no billable request occurs.
            with self.assertRaisesRegex(worker.SafeFailure, "API key missing"):
                worker.generate(args)

    def test_repair_requires_bounded_feedback(self):
        import argparse
        from unittest.mock import patch as mock_patch
        claim = {"selected": True, "number": 2, "category": "voice",
                 "allowedPaths": list(self.allowed)}
        claim_file = self.root / "claim.json"
        claim_file.write_text(json.dumps(claim), encoding="utf-8")
        prior = {"issue": 2, "category": "voice",
                 "allowedPaths": list(self.allowed),
                 "patch": self.patch(), "attempt": 1}
        previous = self.root / "prior.json"
        previous.write_text(json.dumps(prior), encoding="utf-8")
        feedback = self.root / "failure.txt"
        feedback.write_text("x" * 5001, encoding="utf-8")
        args = argparse.Namespace(claim=str(claim_file), previous=str(previous),
                                  feedback=str(feedback), output=str(self.root / "output.json"))
        with mock_patch.object(worker, "ROOT", self.root):
            with self.assertRaisesRegex(worker.SafeFailure, "bounded"):
                worker.generate(args)

    def test_repo_validation(self):
        self.assertEqual(worker.validate_repo("GravyBets/Archer"), "GravyBets/Archer")
        for invalid in ("", "../Archer", "GravyBets/Archer/other", "github.com/x/y"):
            with self.assertRaises(worker.SafeFailure):
                worker.validate_repo(invalid)


if __name__ == "__main__":
    unittest.main()
