#!/usr/bin/env python3
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TESTS = ROOT / "tests" / "EmberTrace.Tests"
WORKFLOW = ROOT / ".github" / "workflows" / "ci.yml"


def main() -> int:
    covered = set(re.findall(r"FullyQualifiedName~EmberTrace\.Tests\.(\w+)", WORKFLOW.read_text()))
    namespaces = {d.name for d in TESTS.iterdir() if d.is_dir() and any(d.glob("*.cs"))}
    missing = sorted(namespaces - covered)

    for name in missing:
        print(f"::error title=CI suites::tests/EmberTrace.Tests/{name} has no suite in ci.yml")

    return 1 if missing else 0


if __name__ == "__main__":
    sys.exit(main())
