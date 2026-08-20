#!/usr/bin/env python3
import json
import sys
from pathlib import Path


def load_json(path: Path):
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def find_results(path: Path) -> Path:
    if path.is_file():
        return path

    search_roots = [path]
    for root in (Path.cwd(), Path.cwd() / "benchmarks"):
        if not root.exists():
            continue
        search_roots.append(root / path)
        search_roots.extend(sorted(root.glob(f"**/{path.name}")))

    seen = set()
    candidates = []

    for root in search_roots:
        if root in seen or not root.exists():
            continue
        seen.add(root)
        candidates.extend(root.glob("**/*report*.json"))

    if not candidates:
        candidates = list(Path.cwd().glob("**/*report*.json"))

    if not candidates:
        candidates = list(Path.cwd().glob("**/*.json"))

    def is_benchmark_report(p: Path) -> bool:
        try:
            data = load_json(p)
        except Exception:
            return False
        return isinstance(data, dict) and isinstance(data.get("Benchmarks"), list)

    valid = [p for p in candidates if is_benchmark_report(p)]
    if valid:
        return max(valid, key=lambda p: p.stat().st_mtime)

    raise FileNotFoundError(f"No BenchmarkDotNet report JSON found under {path}")


def load_benchmark_records(path: Path):
    """Return [(full_name, mean_or_None)] for every case in a BenchmarkDotNet report."""
    data = load_json(path)
    records = []

    for bench in data.get("Benchmarks", []):
        name = bench.get("FullName")
        if not name:
            ns = bench.get("Namespace")
            typ = bench.get("Type")
            method = bench.get("Method")
            if ns and typ and method:
                name = f"{ns}.{typ}.{method}"
        if not name:
            continue

        stats = bench.get("Statistics") or {}
        records.append((name, stats.get("Mean")))

    return records


def matches(baseline_name: str, case_name: str) -> bool:
    """Parameterised cases are reported as `Namespace.Type.Method(param: value)`.

    A baseline entry keyed by the bare method name gates every one of its cases,
    which keeps the baseline stable when the argument source depends on the
    machine (e.g. Environment.ProcessorCount).
    """
    return case_name == baseline_name or case_name.startswith(baseline_name + "(")


def main():
    if len(sys.argv) < 3:
        print("Usage: compare_benchmarks.py <baseline.json> <results.json|dir> [threshold]", file=sys.stderr)
        return 2

    baseline_path = Path(sys.argv[1])
    results_path = find_results(Path(sys.argv[2]))

    baseline = load_json(baseline_path)
    threshold = float(sys.argv[3]) if len(sys.argv) > 3 else float(baseline.get("threshold", 0.15))

    baseline_benchmarks = baseline.get("benchmarks", {})
    if not baseline_benchmarks:
        print("Baseline file has no benchmarks.", file=sys.stderr)
        return 2

    records = load_benchmark_records(results_path)
    if not records:
        print(f"No benchmarks found in {results_path}", file=sys.stderr)
        return 2

    failed = False
    for name, base in baseline_benchmarks.items():
        cases = [(case, mean) for case, mean in records if matches(name, case)]
        if not cases:
            print(f"Missing benchmark result: {name}", file=sys.stderr)
            failed = True
            continue

        measured = [(case, mean) for case, mean in cases if mean is not None]
        if not measured:
            print(f"Benchmark produced no statistics (did it fail to run?): {name}", file=sys.stderr)
            failed = True
            continue

        limit = base * (1.0 + threshold)
        for case, mean in sorted(measured):
            if mean > limit:
                print(f"Regression: {case} mean {mean:.2f}ns > baseline {base:.2f}ns (+{threshold*100:.0f}%)", file=sys.stderr)
                failed = True
            else:
                print(f"OK: {case} mean {mean:.2f}ns <= {limit:.2f}ns")

    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
