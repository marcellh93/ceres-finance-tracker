#!/usr/bin/env python3
"""Classify a session write-list into a dotnet test tier.

Reads track-session-writes.js JSON on stdin, prints 0, 1, or 2.

  TIER 0 — frontend only (.ts/.tsx). No .NET impact, skip dotnet test.
  TIER 1 — production .cs/.csproj under ProjectCeres/ only. Unit tests only.
  TIER 2 — test files, .sln, a composition root (see COMPOSITION_ROOTS), or
           anything ambiguous. Full suite.

Ambiguity always tiers UP: a bad parse must never skip the suite.
Extracted from run-tests.sh so it can be tested (see __tests__/tier-classify.test.js).
"""
import json
import sys


# Files whose behaviour only exists once the app is assembled: middleware order,
# DI registration, environment branching. Unit tests cannot reach any of it, so
# the TIER 1 unit filter is no gate at all for them — they must run the full suite.
#
# Incident that added this (2026-08-29, commit dafee78c, reverted in 19ff467a): a
# Program.cs-only change moved SPA shell serving behind the Vite dev server. That
# classified TIER 1, unit tests passed, and the commit landed with
# DashboardApiTests.GetDashboardRoot_ServesSpaShell red on main.
#
# Deliberately narrow — one path, not a prefix sweep. Escalating every file that
# smells like wiring would tax ordinary service edits with a 5-minute suite.
COMPOSITION_ROOTS = frozenset({"projectceres/program.cs"})


def classify(files):
    has_dotnet = False
    has_test = False
    has_sln = False
    has_composition_root = False

    for f in files:
        ext = f.rsplit(".", 1)[-1].lower() if "." in f else ""
        if f.lower() in COMPOSITION_ROOTS:
            has_composition_root = True
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

    # TIER 2 — test code, the solution file, or a composition root whose behaviour
    # only the integration suite can see.
    if has_test or has_sln or has_composition_root:
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
