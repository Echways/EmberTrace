#!/usr/bin/env python3
import argparse
import os
import sys
import zipfile
from pathlib import Path
from xml.etree import ElementTree as ET

REQUIRED = ("description", "authors", "repository", "tags")
PAYLOAD_ROOTS = ("lib/", "analyzers/", "build/", "tools/", "runtimes/")


def read_nuspec(package: Path) -> tuple[dict[str, str], set[str]]:
    with zipfile.ZipFile(package) as archive:
        names = set(archive.namelist())
        nuspec = next((n for n in names if n.endswith(".nuspec") and "/" not in n), None)
        if nuspec is None:
            raise ValueError("package contains no .nuspec")
        root = ET.fromstring(archive.read(nuspec))

    metadata_node = next((c for c in root if c.tag.rsplit("}", 1)[-1] == "metadata"), None)
    if metadata_node is None:
        raise ValueError("nuspec has no <metadata>")

    metadata = {}
    for child in metadata_node:
        key = child.tag.rsplit("}", 1)[-1]
        metadata[key] = child.get("url", "") if key == "repository" else (child.text or "").strip()
    return metadata, names


def check(package: Path, version: str | None, want_symbols: bool) -> tuple[str, list[str]]:
    try:
        metadata, names = read_nuspec(package)
    except (zipfile.BadZipFile, ValueError, ET.ParseError) as exc:
        return "?", [f"cannot read package: {exc}"]

    problems = []
    if version and metadata.get("version") != version:
        problems.append(f"version is {metadata.get('version')!r}, expected {version!r}")

    problems += [f"missing <{key}>" for key in REQUIRED if not metadata.get(key)]

    if not (metadata.get("license") or metadata.get("licenseUrl")):
        problems.append("missing license expression")

    readme = metadata.get("readme")
    if not readme:
        problems.append("missing <readme>")
    elif readme not in names:
        problems.append(f"<readme> points at {readme!r}, absent from the package")

    if not any(name.startswith(PAYLOAD_ROOTS) for name in names):
        problems.append("no lib/, analyzers/, build/, tools/ or runtimes/ content")

    has_library = any(n.startswith("lib/") and n.endswith(".dll") for n in names)
    if want_symbols and has_library and not package.with_suffix(".snupkg").exists():
        problems.append(f"no {package.with_suffix('.snupkg').name} beside it")

    return metadata.get("version", "?"), problems


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("directory", help="folder holding the .nupkg files")
    parser.add_argument("--version", help="version every package must carry")
    parser.add_argument("--no-symbols", action="store_true")
    args = parser.parse_args()

    packages = sorted(Path(args.directory).glob("*.nupkg"))
    if not packages:
        emit(f"## ❌ Package validation\n\n> No `.nupkg` files in `{args.directory}`.\n")
        return 1

    lines = ["| | Package | Version | Size | Notes |", "|---|---|---|---:|---|"]
    failed = False
    for package in packages:
        version, problems = check(package, args.version, not args.no_symbols)
        failed = failed or bool(problems)
        lines.append(
            f"| {'❌' if problems else '✅'} | `{package.name}` | {version} | "
            f"{package.stat().st_size / 1024:.0f} KB | {'<br>'.join(problems) or 'ok'} |"
        )

    emit("\n".join([f"## {'❌' if failed else '✅'} Package validation", "", *lines, ""]))
    return 1 if failed else 0


def emit(report: str) -> None:
    print(report)
    if path := os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(path, "a", encoding="utf-8") as handle:
            handle.write(report + "\n")


if __name__ == "__main__":
    sys.exit(main())
