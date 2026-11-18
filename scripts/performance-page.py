#!/usr/bin/env python3
"""DeckHost's performance numbers for the docs site and the PR comment (docs/performance.md).

  performance-page.py index <perf-data-dir> <tag> <result.json>...   add a release's results (one per system)
  performance-page.py page <perf-data-dir> <budgets.json> <out.md>    the docs page's numbers
  performance-page.py compare <budgets.json> <result.json>... [--previous-dir DIR]
                                                                     the PR comment: every system next to the last release

<perf-data-dir> is the perf-data branch: releases/<tag>/<system>.json and index.json (newest first).
The result files are what benchmarks/DeckHost.Benchmark writes.
"""
import json
import sys
from pathlib import Path

SYSTEMS = [("windows", "Windows"), ("linux", "Linux"), ("macos", "macOS")]

# key, label, unit, decimals: the same numbers as benchmarks/DeckHost.Benchmark/Metrics.cs.
METRICS = [
    ("startup.firstFrameSeconds", "Start to first key image", "s", 1),
    ("idleWindow.cpuPercent", "Idle, window open: CPU", "% of a core", 1),
    ("idleWindow.memoryMb", "Idle, window open: memory", "MB", 0),
    ("idleTray.cpuPercent", "Idle, in the tray: CPU", "% of a core", 1),
    ("idleTray.memoryMb", "Idle, in the tray: memory", "MB", 0),
    ("busy.cpuPercent", "Busy (10 inputs/s): CPU", "% of a core", 1),
    ("busy.memoryMaxMb", "Busy: highest memory", "MB", 0),
    ("busy.memoryGrowthMb", "Busy: memory growth over the minute", "MB", 1),
    ("latency.p50Ms", "Press → new key image (median)", "ms", 0),
    ("latency.p95Ms", "Press → new key image (95th percentile)", "ms", 0),
]

# The history table: the few numbers that say most.
HISTORY = ["idleTray.cpuPercent", "idleWindow.cpuPercent", "busy.cpuPercent", "idleWindow.memoryMb", "latency.p95Ms"]

COMMENT_MARKER = "<!-- deckhost:performance -->"


def fmt(value, metric_key, with_unit=True):
    info = next((m for m in METRICS if m[0] == metric_key), None)
    if value is None:
        return "–"
    if info is None:
        return f"{value:g}"
    text = f"{value:.{info[3]}f}"
    return f"{text} {info[2]}" if with_unit else text


def limits_for(budgets, system):
    merged = dict(budgets.get("default", {}))
    merged.update(budgets.get(system, {}))
    return {key: value for key, value in merged.items() if isinstance(value, (int, float))}


def read_json(path):
    """JSON, allowing whole-line // comments (benchmarks/budgets.json explains itself with them)."""
    text = Path(path).read_text(encoding="utf-8")
    return json.loads("\n".join(line for line in text.splitlines() if not line.lstrip().startswith("//")))


def add_to_index(data_dir, tag, results):
    """Copies a release's results into releases/<tag>/ and puts the release first in index.json."""
    data_dir = Path(data_dir)
    target = data_dir / "releases" / tag
    target.mkdir(parents=True, exist_ok=True)
    systems = {}
    for path in results:
        result = read_json(path)
        system = result["machine"]["system"]
        (target / f"{system}.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
        systems[system] = f"releases/{tag}/{system}.json"
    index_path = data_dir / "index.json"
    index = read_json(index_path) if index_path.exists() else {"releases": []}
    index["releases"] = [entry for entry in index["releases"] if entry["tag"] != tag]
    index["releases"].insert(0, {"tag": tag, "systems": systems})
    index_path.write_text(json.dumps(index, indent=2) + "\n", encoding="utf-8")
    return index


def load_releases(data_dir):
    """[(tag, {system: result})], newest first; empty without data."""
    data_dir = Path(data_dir) if data_dir else None
    if data_dir is None or not (data_dir / "index.json").exists():
        return []
    releases = []
    for entry in read_json(data_dir / "index.json")["releases"]:
        results = {}
        for system, relative in entry["systems"].items():
            path = data_dir / relative
            if path.exists():
                results[system] = read_json(path)
        releases.append((entry["tag"], results))
    return releases


def page(data_dir, budgets):
    """The docs page's numbers: the latest release per system against the budgets, then a history."""
    releases = load_releases(data_dir)
    if not releases:
        return ("*No release has been measured yet. The numbers appear here after the next release; "
                "every pull request already shows them in its checks.*\n")
    tag, latest = releases[0]
    systems = [(key, name) for key, name in SYSTEMS if key in latest]
    lines = [f"### {tag}", ""]
    lines.append("| | " + " | ".join(name for _, name in systems) + " |")
    lines.append("|---|" + "---:|" * len(systems))
    for key, label, _, _ in METRICS:
        cells = []
        for system, _ in systems:
            value = latest[system]["metrics"].get(key)
            limit = limits_for(budgets, system).get(key)
            mark = "" if value is None or limit is None else (" ✅" if value <= limit else " ❌")
            cells.append(fmt(value, key) + mark)
        lines.append(f"| {label} | " + " | ".join(cells) + " |")
    lines.append("")
    machines = "; ".join(f"{name}: {latest[system]['machine']['description']}, "
                         f"{latest[system]['machine']['processors']} processors, {latest[system]['machine']['memoryGb']:g} GB"
                         for system, name in systems)
    lines.append(f"Measured on GitHub's hosted runners ({machines}). ✅ within the budget, ❌ over it.")
    lines.append("")
    lines.append("### Earlier releases")
    lines.append("")
    for system, name in SYSTEMS:
        rows = [(t, r[system]) for t, r in releases[:10] if system in r]
        if not rows:
            continue
        lines.append(f"**{name}**")
        lines.append("")
        lines.append("| Release | " + " | ".join(next(m[1] for m in METRICS if m[0] == k) for k in HISTORY) + " |")
        lines.append("|---|" + "---:|" * len(HISTORY))
        for release_tag, result in rows:
            lines.append(f"| {release_tag} | " + " | ".join(fmt(result["metrics"].get(k), k) for k in HISTORY) + " |")
        lines.append("")
    return "\n".join(lines)


def compare(budgets, results, previous_dir=None):
    """The PR comment: per system every number, the last release's and the budget."""
    releases = load_releases(previous_dir)
    previous_tag, previous = releases[0] if releases else (None, {})
    by_system = {r["machine"]["system"]: r for r in (read_json(p) for p in results)}
    over = []
    lines = [COMMENT_MARKER, "## Performance", ""]
    for system, name in SYSTEMS:
        result = by_system.get(system)
        if result is None:
            lines += [f"**{name}:** not measured (the job failed before writing a result).", ""]
            continue
        limits = limits_for(budgets, system)
        before = previous.get(system, {}).get("metrics", {})
        header = f"| {name} ({result['version']}) | This PR | {previous_tag} | Budget | |" if previous_tag else f"| {name} ({result['version']}) | This PR | Budget | |"
        lines += [header, "|---|---:|" + ("---:|" if previous_tag else "") + "---:|---|"]
        for key, label, _, _ in METRICS:
            value = result["metrics"].get(key)
            limit = limits.get(key)
            verdict = "" if value is None or limit is None else ("✅" if value <= limit else "❌")
            if verdict == "❌":
                over.append(f"{name}: {label}")
            cells = [label, fmt(value, key)]
            if previous_tag:
                cells.append(fmt(before.get(key), key))
            cells += [fmt(limit, key) if limit is not None else "", verdict]
            lines.append("| " + " | ".join(cells) + " |")
        lines.append("")
    lines.append(("**Over budget:** " + "; ".join(over) + ".") if over else "Every number is within its budget.")
    lines.append("")
    lines.append("Measured by `benchmarks/DeckHost.Benchmark` on GitHub's hosted runners, which vary a little from run to run. "
                 "Budgets: `benchmarks/budgets.json`. What the numbers mean: [Performance](https://vman26.github.io/DeckHost/performance/).")
    return "\n".join(lines) + "\n"


def main(argv):
    if len(argv) >= 4 and argv[1] == "index":
        add_to_index(argv[2], argv[3], argv[4:])
    elif len(argv) == 5 and argv[1] == "page":
        Path(argv[4]).write_text(page(argv[2], read_json(argv[3])), encoding="utf-8")
    elif len(argv) >= 3 and argv[1] == "compare":
        args = argv[3:]
        previous_dir = None
        if "--previous-dir" in args:
            at = args.index("--previous-dir")
            previous_dir = args[at + 1]
            args = args[:at] + args[at + 2:]
        sys.stdout.write(compare(read_json(argv[2]), args, previous_dir))
    else:
        sys.stderr.write(__doc__)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
