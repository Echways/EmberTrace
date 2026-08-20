#!/usr/bin/env python3
import argparse
import json
import os
import sys
from pathlib import Path

SEVERITIES = ["low", "moderate", "high", "critical"]


def load(path: str | None) -> dict:
    if not path:
        return {}
    file = Path(path)
    if not file.is_file() or not file.stat().st_size:
        return {}
    try:
        return json.loads(file.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return {}


def walk(report: dict):
    for project in report.get("projects", []):
        name = Path(project.get("path", "?")).stem
        for framework in project.get("frameworks") or []:
            for key in ("topLevelPackages", "transitivePackages"):
                for package in framework.get(key) or []:
                    yield name, package, key == "transitivePackages"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--vulnerable", help="json from dotnet list package --vulnerable")
    parser.add_argument("--deprecated", help="json from dotnet list package --deprecated")
    parser.add_argument("--fail-on", choices=SEVERITIES + ["never"], default="moderate")
    args = parser.parse_args()

    floor = SEVERITIES.index(args.fail_on) if args.fail_on != "never" else len(SEVERITIES)

    findings = []
    for project, package, transitive in walk(load(args.vulnerable)):
        for advisory in package.get("vulnerabilities") or []:
            findings.append({
                "project": project,
                "id": package.get("id", "?"),
                "version": package.get("resolvedVersion") or package.get("requestedVersion", "?"),
                "severity": (advisory.get("severity") or "unknown").lower(),
                "url": advisory.get("advisoryurl") or advisory.get("advisoryUrl", ""),
                "transitive": transitive,
            })

    deprecations = [
        (project, package.get("id", "?"), package.get("resolvedVersion", "?"),
         ", ".join(package.get("deprecationReasons") or []))
        for project, package, _ in walk(load(args.deprecated))
        if package.get("deprecationReasons")
    ]

    blocking = [f for f in findings
                if f["severity"] in SEVERITIES and SEVERITIES.index(f["severity"]) >= floor]

    lines = [f"## Dependency audit: {'failed' if blocking else 'passed'}", ""]
    if findings:
        lines += [
            f"**{len(findings)} advisory finding(s)**, {len(blocking)} at or above `{args.fail_on}`.",
            "",
            "| Package | Scope | Version | Severity | Project | Advisory |",
            "|---|---|---|---|---|---|",
        ]
        rank = lambda f: SEVERITIES.index(f["severity"]) if f["severity"] in SEVERITIES else -1
        for finding in sorted(findings, key=rank, reverse=True):
            link = f"[details]({finding['url']})" if finding["url"] else "n/a"
            lines.append(
                f"| `{finding['id']}` | {'transitive' if finding['transitive'] else 'direct'} | "
                f"{finding['version']} | {finding['severity']} | "
                f"`{finding['project']}` | {link} |"
            )
        lines.append("")
    else:
        lines += ["No known advisories against any direct or transitive package.", ""]

    if deprecations:
        lines += ["<details><summary>Deprecated packages</summary>", "",
                  "| Package | Version | Project | Reason |", "|---|---|---|---|"]
        lines += [f"| `{pkg}` | {version} | `{project}` | {reason} |"
                  for project, pkg, version, reason in deprecations]
        lines += ["", "</details>", ""]

    report = "\n".join(lines)
    print(report)
    if path := os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(path, "a", encoding="utf-8") as handle:
            handle.write(report + "\n")

    for finding in blocking:
        print(f"::error title=Vulnerable package::{finding['id']} {finding['version']} "
              f"({finding['severity']}) in {finding['project']} {finding['url']}")

    return 1 if blocking else 0


if __name__ == "__main__":
    sys.exit(main())
