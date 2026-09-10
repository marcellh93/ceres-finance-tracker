#!/usr/bin/env python3
"""Helper for tools/bucket-integration-tests.sh.

Does steps 2-3 of the bucketing: parse the .trx for per-test durations, aggregate to
per-file, and greedily LPT-assign the [Collection("IntegrationTests")] files across N
buckets balanced by summed runtime.

Prints "<bucket>\t<file>" lines to stdout (consumed by the shell wrapper's rewrite loop).
Prints a human-readable balance report to stderr.
"""
import re
import statistics
import subprocess
import sys
from pathlib import Path

TAG_RE = re.compile(r'\[Collection\("IntegrationTests"\)\]')
UNIT_TEST_RESULT_RE = re.compile(
    r'<UnitTestResult\b[^>]*\btestName="([^"]+)"[^>]*\bduration="([^"]+)"'
)
# duration attribute can appear before or after testName in some trx writers; also match
# that ordering so parsing doesn't depend on attribute order.
UNIT_TEST_RESULT_RE_ALT = re.compile(
    r'<UnitTestResult\b[^>]*\bduration="([^"]+)"[^>]*\btestName="([^"]+)"'
)


def duration_to_seconds(hhmmss: str) -> float:
    # Format: HH:MM:SS.fffffff
    h, m, s = hhmmss.split(":")
    return int(h) * 3600 + int(m) * 60 + float(s)


def find_integration_tests_files(tests_root: str) -> list[str]:
    out = subprocess.run(
        [
            "grep",
            "-rlF",
            '[Collection("IntegrationTests")]',
            tests_root,
            "--include=*.cs",
        ],
        capture_output=True,
        text=True,
        check=False,
    )
    files = sorted(line for line in out.stdout.splitlines() if line.strip())
    return files


def class_name_from_test_name(test_name: str) -> str:
    # testName is "Namespace.Class.Method" (occasionally with a Theory-data suffix in
    # parens, which doesn't affect this split since it's always after the last dot-split
    # for Method). Class is the second-to-last dot-separated segment.
    parts = test_name.split(".")
    if len(parts) < 2:
        return test_name
    return parts[-2]


def parse_trx_durations_by_class(trx_path: str) -> dict[str, float]:
    """Returns {ClassName: summed_duration_seconds} across all tests in the trx."""
    text = Path(trx_path).read_text(encoding="utf-8-sig", errors="replace")
    per_class: dict[str, float] = {}

    def record(test_name: str, duration: str) -> None:
        cls = class_name_from_test_name(test_name)
        try:
            secs = duration_to_seconds(duration)
        except ValueError:
            return
        per_class[cls] = per_class.get(cls, 0.0) + secs

    for m in UNIT_TEST_RESULT_RE.finditer(text):
        record(m.group(1), m.group(2))
    # Also try the alt attribute ordering, but only for lines the primary regex missed —
    # detect via a second scan keyed on distinct matches; UnitTestResult lines all carry
    # both attributes in this trx writer, so in practice one regex covers all of them.
    if not per_class:
        for m in UNIT_TEST_RESULT_RE_ALT.finditer(text):
            record(m.group(2), m.group(1))

    return per_class


def map_classes_to_files(files: list[str]) -> dict[str, str]:
    """Returns {ClassName: file_path} for every class declared in one of `files`.

    Only classes declared in the IntegrationTests-tagged file set are mapped — a class
    declared elsewhere (e.g. a helper base class) is irrelevant to this bucketing.
    """
    class_re = re.compile(r'\bclass\s+([A-Za-z_][A-Za-z0-9_]*)\b')
    mapping: dict[str, str] = {}
    for f in files:
        try:
            content = Path(f).read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        for m in class_re.finditer(content):
            mapping.setdefault(m.group(1), f)
    return mapping


def main() -> int:
    n = int(sys.argv[1])
    trx_path = sys.argv[2]
    tests_root = sys.argv[3]

    files = find_integration_tests_files(tests_root)
    if not files:
        print("no [Collection(\"IntegrationTests\")] files found", file=sys.stderr)
        return 1

    class_durations = parse_trx_durations_by_class(trx_path)
    class_to_file = map_classes_to_files(files)

    file_durations: dict[str, float] = {f: 0.0 for f in files}
    timed_files: set[str] = set()
    for cls, secs in class_durations.items():
        f = class_to_file.get(cls)
        if f is None:
            continue
        file_durations[f] += secs
        timed_files.add(f)

    untimed = [f for f in files if f not in timed_files]
    if timed_files:
        median = statistics.median(file_durations[f] for f in timed_files)
    else:
        median = 0.0
    for f in untimed:
        file_durations[f] = median

    # Greedy LPT: sort files descending by duration, assign each to the lightest bucket.
    # Ties in duration are broken by file path so the assignment is deterministic.
    ordered = sorted(files, key=lambda f: (-file_durations[f], f))
    bucket_totals = [0.0] * n
    bucket_files: list[list[str]] = [[] for _ in range(n)]
    for f in ordered:
        k = min(range(n), key=lambda i: (bucket_totals[i], i))
        bucket_totals[k] += file_durations[f]
        bucket_files[k].append(f)

    for k in range(n):
        for f in bucket_files[k]:
            print(f"{k + 1}\t{f}")

    print("=== bucket balance report ===", file=sys.stderr)
    for k in range(n):
        mins = bucket_totals[k] / 60.0
        print(
            f"bucket {k + 1}: {len(bucket_files[k])} files, "
            f"{bucket_totals[k]:.1f}s ({mins:.1f} min) summed duration",
            file=sys.stderr,
        )
    print(f"untimed files (assigned median {median:.2f}s): {len(untimed)}", file=sys.stderr)
    if untimed:
        for f in untimed:
            print(f"  untimed: {f}", file=sys.stderr)

    return 0


if __name__ == "__main__":
    sys.exit(main())
