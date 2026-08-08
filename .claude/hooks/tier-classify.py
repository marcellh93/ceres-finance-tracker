#!/usr/bin/env python3
"""Classify a session write-list into a dotnet test tier.

Reads track-session-writes.js JSON on stdin, prints 0, 1, or 2.

  TIER 0 — frontend only (.ts/.tsx). No .NET impact, skip dotnet test.
  TIER 1 — production .cs/.csproj under ProjectCeres/ only. Unit tests only.
  TIER 2 — test files, .sln, or anything ambiguous. Full suite.

Ambiguity always tiers UP: a bad parse must never skip the suite.
Extracted from run-tests.sh so it can be tested (see __tests__/tier-classify.test.js).
"""
import json
import sys


def classify(files):
    has_dotnet = False
    has_test = False
    has_sln = False

    for f in files:
        ext = f.rsplit(".", 1)[-1].lower() if "." in f else ""
        if ext == "sln":
            has_sln = True
        if ext == "cs":
            has_dotnet = True
            if f.startswith("ProjectCeres.Tests/"):
                has_test = True
        if ext == "csproj":
            has_dotnet = True
            if f.startswith("ProjectCeres.Tests/"):
                has_test = True

    # TIER 0 — purely frontend, no dotnet impact at all.
    if not has_dotnet and not has_sln:
        return 0

    # TIER 2 — anything touching test code or the solution file.
    if has_test or has_sln:
        return 2

    # TIER 1 — only production .cs / .csproj.
    return 1


def main():
    try:
        d = json.load(sys.stdin)
        files = d.get("files", []) if isinstance(d, dict) else []
    except Exception:
        print(2)
        return

    if not files:
        print(2)
        return

    print(classify(files))


if __name__ == "__main__":
    main()
