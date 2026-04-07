#!/usr/bin/env python3
"""
Update the <!-- BENCHMARK_START --> ... <!-- BENCHMARK_END --> block in README.md
with the latest BenchmarkDotNet results from the artifacts directory.

Called by .github/workflows/benchmarks.yml after a full benchmark run on main.
"""

import json
import os
import re
import sys
from pathlib import Path

RESULTS_DIR = Path("benchmarks/EggPdf.Benchmarks/BenchmarkDotNet.Artifacts/results")
README = Path("README.md")
MARKER_START = "<!-- BENCHMARK_START -->"
MARKER_END = "<!-- BENCHMARK_END -->"


def find_json_reports():
    if not RESULTS_DIR.exists():
        return []
    return sorted(RESULTS_DIR.glob("*-report-full.json"))


def format_time(ns: float) -> str:
    if ns < 1_000:
        return f"{ns:.0f} ns"
    if ns < 1_000_000:
        return f"{ns/1_000:.1f} µs"
    if ns < 1_000_000_000:
        return f"{ns/1_000_000:.1f} ms"
    return f"{ns/1_000_000_000:.2f} s"


def format_bytes(b: float) -> str:
    if b < 1024:
        return f"{b:.0f} B"
    if b < 1024 * 1024:
        return f"{b/1024:.0f} KB"
    return f"{b/(1024*1024):.1f} MB"


def parse_benchmarks(json_files):
    rows = []
    for f in json_files:
        try:
            data = json.loads(f.read_text())
            for bench in data.get("Benchmarks", []):
                name = bench.get("FullName", bench.get("Method", "Unknown"))
                # Shorten name: take just the method name part
                short = name.split(".")[-1]
                stats = bench.get("Statistics", {})
                mean_ns = stats.get("Mean", 0)
                memory = bench.get("Memory", {})
                alloc = memory.get("BytesAllocatedPerOperation", 0)
                rows.append((short, mean_ns, alloc))
        except Exception as e:
            print(f"Warning: could not parse {f}: {e}", file=sys.stderr)
    return rows


def build_table(rows):
    if not rows:
        return None
    lines = [
        "| Scenario | Mean | Memory |",
        "|----------|------|--------|",
    ]
    for name, mean_ns, alloc in rows:
        lines.append(f"| {name} | **{format_time(mean_ns)}** | {format_bytes(alloc)} |")
    lines.append("")
    lines.append("*Auto-updated by CI. Targets: simple < 50ms, invoice < 100ms, large table < 5s.*")
    return "\n".join(lines)


def update_readme(table: str):
    content = README.read_text(encoding="utf-8")
    pattern = re.compile(
        re.escape(MARKER_START) + r".*?" + re.escape(MARKER_END),
        re.DOTALL,
    )
    replacement = f"{MARKER_START}\n{table}\n{MARKER_END}"
    new_content, count = pattern.subn(replacement, content)
    if count == 0:
        print("Warning: benchmark markers not found in README.md", file=sys.stderr)
        return False
    README.write_text(new_content, encoding="utf-8")
    print(f"Updated README.md with {len(table.splitlines())} lines of benchmark data.")
    return True


def main():
    json_files = find_json_reports()
    if not json_files:
        print("No benchmark JSON reports found — skipping README update.", file=sys.stderr)
        sys.exit(0)

    rows = parse_benchmarks(json_files)
    if not rows:
        print("No benchmark data parsed — skipping README update.", file=sys.stderr)
        sys.exit(0)

    table = build_table(rows)
    if not update_readme(table):
        sys.exit(1)


if __name__ == "__main__":
    main()
