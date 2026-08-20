#!/usr/bin/env python3
import argparse
import os
import sys
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field
from pathlib import Path

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
SKIPPED = {"notexecuted", "skipped", "pending", "inconclusive"}


@dataclass
class Run:
    name: str
    passed: int = 0
    failed: int = 0
    skipped: int = 0
    duration: float = 0.0
    failures: list[tuple[str, str, str]] = field(default_factory=list)

    @property
    def total(self) -> int:
        return self.passed + self.failed + self.skipped

    @property
    def status(self) -> str:
        return "FAIL" if self.failed else "empty" if not self.total else "ok"


def parse_duration(value: str | None) -> float:
    if not value:
        return 0.0
    try:
        hours, minutes, seconds = value.split(":")
        return int(hours) * 3600 + int(minutes) * 60 + float(seconds)
    except ValueError:
        return 0.0


def parse_trx(path: Path) -> Run:
    run = Run(name=path.stem)
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError as exc:
        run.failed = 1
        run.failures.append((path.name, f"unreadable trx file: {exc}", ""))
        return run

    for result in root.findall(".//t:Results/t:UnitTestResult", NS):
        outcome = (result.get("outcome") or "").lower()
        run.duration += parse_duration(result.get("duration"))
        if outcome == "passed":
            run.passed += 1
        elif outcome in SKIPPED:
            run.skipped += 1
        else:
            run.failed += 1
            run.failures.append((
                result.get("testName") or "<unknown test>",
                (result.findtext(".//t:Output/t:ErrorInfo/t:Message", "", NS) or "").strip(),
                (result.findtext(".//t:Output/t:ErrorInfo/t:StackTrace", "", NS) or "").strip(),
            ))
    return run


def collect(paths: list[str]) -> list[Run]:
    files: list[Path] = []
    for raw in paths:
        path = Path(raw)
        if path.is_dir():
            files.extend(sorted(path.rglob("*.trx")))
        elif path.is_file():
            files.append(path)

    seen: set[Path] = set()
    runs = []
    for file in files:
        if file.resolve() not in seen:
            seen.add(file.resolve())
            runs.append(parse_trx(file))
    return runs


def fence(text: str, limit: int) -> str:
    text = text or "(no details)"
    if len(text) > limit:
        text = text[:limit] + "\n... truncated ..."
    return f"```\n{text}\n```"


def render(runs: list[Run], title: str, max_failures: int) -> str:
    if not runs:
        return f"## {title}: no data\n\n> No `.trx` files were produced.\n"

    passed = sum(r.passed for r in runs)
    failed = sum(r.failed for r in runs)
    skipped = sum(r.skipped for r in runs)
    duration = sum(r.duration for r in runs)

    lines = [
        f"## {title}: {'failed' if failed else 'passed'}",
        "",
        f"**{passed} passed, {failed} failed, {skipped} skipped** in {duration:.2f}s",
        "",
        "| Result | Suite | Passed | Failed | Skipped | Time |",
        "|---|---|---:|---:|---:|---:|",
    ]
    for run in sorted(runs, key=lambda r: (r.failed == 0, r.name)):
        lines.append(
            f"| {run.status} | `{run.name}` | {run.passed} | {run.failed} | "
            f"{run.skipped} | {run.duration:.2f}s |"
        )
    lines.append("")

    failures = [(run.name, *failure) for run in runs for failure in run.failures]
    if failures:
        lines += ["### Failures", ""]
        for suite, name, message, stack in failures[:max_failures]:
            lines += [
                f"<details open><summary><code>{name}</code> in <em>{suite}</em></summary>",
                "",
                fence(message, 1600),
            ]
            if stack:
                lines += ["", fence(stack, 2400)]
            lines += ["", "</details>", ""]
        if len(failures) > max_failures:
            lines += [f"> {len(failures) - max_failures} more failure(s) in the artifacts.", ""]

    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("paths", nargs="+", help=".trx files or directories to scan")
    parser.add_argument("--title", default="Test results")
    parser.add_argument("--max-failures", type=int, default=20)
    parser.add_argument("--fail-on-error", action="store_true")
    args = parser.parse_args()

    runs = collect(args.paths)
    report = render(runs, args.title, args.max_failures)

    print(report)
    if path := os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(path, "a", encoding="utf-8") as handle:
            handle.write(report + "\n")

    return 1 if args.fail_on_error and (not runs or any(r.failed for r in runs)) else 0


if __name__ == "__main__":
    sys.exit(main())
