#!/usr/bin/env bash
# tools/bucket-integration-tests.sh — greedy longest-processing-time (LPT) bin-pack of the
# [Collection("IntegrationTests")] files into N buckets balanced by summed runtime, then
# rewrite each file's attribute to its assigned IntegrationParallel<k> collection.
#
# Serial collections (RateLimitTests, MfaRateLimitTests, AppRoleTests, RlsTests) are never
# touched — this script only ever looks for the literal string [Collection("IntegrationTests")].
#
# Usage: tools/bucket-integration-tests.sh <N> <path/to/timings.trx>
#
# Algorithm:
#   1. List every *.cs file under ProjectCeres.Tests (repo-root-relative) tagged
#      [Collection("IntegrationTests")].
#   2. Parse the .trx for <UnitTestResult testName="Ns.Class.Method" duration="HH:MM:SS.fffffff">
#      entries, sum durations per fully-qualified class, then map each class to the ONE file
#      (among the files from step 1) that declares `class <ClassName>`. Sum per file.
#      A class the trx never timed contributes nothing; a file with zero timed classes is
#      flagged "untimed" and gets the run's median file-duration in step 3, so it is still
#      placed deterministically.
#   3. Greedy LPT: sort files by descending duration; repeatedly assign the next file to
#      whichever bucket currently has the smallest running total (linear scan over N — N is
#      always small, 4, so no heap is needed).
#   4. Rewrite: for each file assigned to bucket k, replace its
#      [Collection("IntegrationTests")] with [Collection("IntegrationParallel<k>")].
#
# Prints a per-bucket report (file count + summed duration) and the placements it made.

set -euo pipefail

N="${1:-4}"
TRX="${2:?usage: bucket-integration-tests.sh <N> <timings.trx>}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT"

TESTS_ROOT="ProjectCeres.Tests"

if [[ ! -f "$TRX" ]]; then
  echo "error: trx file not found: $TRX" >&2
  exit 1
fi

if ! command -v python3 >/dev/null 2>&1; then
  echo "error: python3 is required" >&2
  exit 1
fi

# Steps 1-3 (discovery, duration aggregation, LPT assignment) are done in one python3 pass
# because per-file duration aggregation and greedy bin-packing are both easier to get right
# there than in awk/sed. Its only output is a TSV of "<bucket><TAB><file>" lines — the
# rewrite (step 4) happens back in bash so the sed/attribute-editing stays inspectable.
ASSIGNMENTS="$(python3 "$SCRIPT_DIR/bucket-integration-tests.py" "$N" "$TRX" "$TESTS_ROOT")"

count=0
while IFS=$'\t' read -r bucket file; do
  [[ -z "$file" ]] && continue
  sed -i '' "s/\[Collection(\"IntegrationTests\")\]/[Collection(\"IntegrationParallel${bucket}\")]/" "$file"
  count=$((count + 1))
done <<< "$ASSIGNMENTS"

echo "rebucketed ${count} files into ${N} buckets"
