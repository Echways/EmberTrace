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
        memory = bench.get("Memory") or {}
        records.append((name, stats.get("Mean"), memory.get("BytesAllocatedPerOperation")))

    return records


def matches(baseline_name: str, case_name: str) -> bool:
    return case_name == baseline_name or case_name.startswith(baseline_name + "(")


def main():
    if len(sys.argv) < 3:
        print("Usage: compare_benchmarks.py <baseline.json> <results.json|dir> [threshold]", file=sys.stderr)
        return 2

    baseline = load_json(Path(sys.argv[1]))
    results_path = find_results(Path(sys.argv[2]))
    threshold = float(sys.argv[3]) if len(sys.argv) > 3 else float(baseline.get("threshold", 0.25))

    expected = baseline.get("benchmarks", {})
    reference_name = baseline.get("reference")
    if not expected or not reference_name:
        print("Baseline file needs 'reference' and 'benchmarks'.", file=sys.stderr)
        return 2

    records = load_benchmark_records(results_path)
    reference = [mean for name, mean, _ in records if matches(reference_name, name) and mean]
    if not reference:
        print(f"Reference benchmark produced no statistics (did it fail to run?): {reference_name}", file=sys.stderr)
        return 1

    clock = min(reference)
    failed = False

    for name, limits in expected.items():
        cases = [(case, mean, allocated) for case, mean, allocated in records if matches(name, case)]
        if not cases:
            print(f"Missing benchmark result: {name}", file=sys.stderr)
            failed = True
            continue

        for case, mean, allocated in sorted(cases, key=lambda c: c[0]):
            if mean is None:
                print(f"Benchmark produced no statistics (did it fail to run?): {case}", file=sys.stderr)
                failed = True
                continue

            ratio = mean / clock
            ratio_limit = limits["ratio"] * (1.0 + threshold)
            allocated = allocated or 0
            line = f"{case}: {mean:.1f}ns ratio={ratio:.2f} (limit {ratio_limit:.2f}) allocated={allocated}B (limit {limits['allocated']}B)"

            if ratio > ratio_limit or allocated > limits["allocated"]:
                print(f"Regression: {line}", file=sys.stderr)
                failed = True
            else:
                print(f"OK: {line}")

    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
