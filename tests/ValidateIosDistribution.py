#!/usr/bin/env python3
"""Offline checks for manual-only, owner-approved iOS distribution."""
from pathlib import Path
import plistlib
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
workflow = (root / ".github/workflows/ios-testflight.yml").read_text()
release = (root / "tools/ios_release_config.py").read_text()
info = plistlib.loads((root / "ios/ArcherMobile/App/Info.plist").read_bytes())
assert "workflow_dispatch:" in workflow
assert "\npull_request:" not in workflow and "\npush:" not in workflow
assert "refs/heads/main" in workflow and "UPLOAD_ARCHER_TO_TESTFLIGHT" in workflow
assert "environment: ios-testflight" in workflow and "permissions:\n  contents: read" in workflow
assert "app-store-connect" in workflow and "--upload-app" in workflow
assert "CODE_SIGN_STYLE=Manual" in workflow and "security set-key-partition-list" in workflow
assert "IOS_PRIVATE_HOST" in workflow and "IOS_NATIVE_CLIENT_ID" in workflow
assert "run: xcodegen generate" in workflow
assert "if: always()" in workflow and "security delete-keychain" in workflow
assert "ARCHER_COMPANION_BRIDGE_KEY" not in workflow and "OPENAI_API_KEY" not in workflow
assert "REPLACE_WITH_APPROVED_PRIVATE_TS_HOST" not in release
assert "validate_host" in release and "validate_client" in release
assert info.get("CFBundleVersion") == "$(CURRENT_PROJECT_VERSION)"
assert info.get("CFBundleShortVersionString") == "$(MARKETING_VERSION)"
result = subprocess.run([sys.executable, str(root / "tools/ios_release_config.py"), "--self-test"],
                        check=True, capture_output=True, text=True)
assert "PASS:" in result.stdout
print("PASS: manual owner TestFlight workflow, explicit signing, safe app config and no embedded secrets")
