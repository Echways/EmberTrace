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


def load_benchmark_means(path: Path):
    data = load_json(path)
    results = {}

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
        mean = stats.get("Mean")
        if mean is None:
            continue

        results[name] = mean

    return results


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

    current = load_benchmark_means(results_path)
    if not current:
        print(f"No benchmarks found in {results_path}", file=sys.stderr)
        return 2

    failed = False
    for name, base in baseline_benchmarks.items():
        current_mean = current.get(name)
        if current_mean is None:
            print(f"Missing benchmark result: {name}", file=sys.stderr)
            failed = True
            continue

        limit = base * (1.0 + threshold)
        if current_mean > limit:
            print(f"Regression: {name} mean {current_mean:.2f}ns > baseline {base:.2f}ns (+{threshold*100:.0f}%)", file=sys.stderr)
            failed = True
        else:
            print(f"OK: {name} mean {current_mean:.2f}ns <= {limit:.2f}ns")

    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
