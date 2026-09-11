#!/usr/bin/env python3
"""Task 5b-2: migrate bucketed WAF integration test classes to IntegrationTestBase<TFactory>.

Driven by the map file (one line per class: `path|K=<n>|FAC=<FactoryType>`).
For each entry:
  1. Find the `[Collection("IntegrationParallel<K>")]` line in the file.
  2. Find the class declaration on the following non-blank line and add
     `IntegrationTestBase<FAC>` as the FIRST base (before any existing bases).
  3. Find that class's primary constructor — `public <ClassName>(<FAC> factory)`
     — and add a `BucketKDatabase bucketDb` parameter plus `: base(factory, bucketDb)`,
     preserving the existing body (expression `=>` or block `{ ... }`).

Deterministic, idempotent-checked (skips a class already migrated), and fails loudly
(does not silently skip) if any entry's expected shape isn't found.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
MAP_FILE = REPO_ROOT / ".superpowers/sdd/2026-09-10-stage-12-18-parallel-integration-tests/t5b2-map.txt"


class MigrationError(Exception):
    pass


def parse_map(path: Path) -> list[tuple[str, int, str]]:
    entries = []
    for lineno, raw in enumerate(path.read_text().splitlines(), start=1):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        parts = line.split("|")
        if len(parts) != 3:
            raise MigrationError(f"map line {lineno}: expected 3 fields, got {parts!r}")
        file_rel, k_field, fac_field = parts
        m = re.fullmatch(r"K=(\d+)", k_field)
        if not m:
            raise MigrationError(f"map line {lineno}: bad K field {k_field!r}")
        k = int(m.group(1))
        m2 = re.fullmatch(r"FAC=(\w+)", fac_field)
        if not m2:
            raise MigrationError(f"map line {lineno}: bad FAC field {fac_field!r}")
        fac = m2.group(1)
        entries.append((file_rel, k, fac))
    return entries


def migrate_file(file_rel: str, k: int, fac: str) -> str:
    """Returns 'migrated' or 'skipped-already-done'."""
    path = REPO_ROOT / file_rel
    if not path.is_file():
        raise MigrationError(f"{file_rel}: file not found")
    text = path.read_text()
    lines = text.split("\n")

    collection_tag = f'[Collection("IntegrationParallel{k}")]'
    bucket_type = f"Bucket{k}Database"
    base_type = f"IntegrationTestBase<{fac}>"

    # 1. Locate the [Collection("IntegrationParallelK")] line.
    coll_idx = None
    for i, line in enumerate(lines):
        if line.strip() == collection_tag:
            coll_idx = i
            break
    if coll_idx is None:
        raise MigrationError(
            f"{file_rel}: could not find line exactly matching {collection_tag!r}"
        )

    # 2. Locate the class declaration line following it (skip blank lines).
    class_idx = None
    for i in range(coll_idx + 1, min(coll_idx + 5, len(lines))):
        if lines[i].strip() == "":
            continue
        if re.match(r"^public (sealed |abstract )?class \w+", lines[i]):
            class_idx = i
            break
        else:
            raise MigrationError(
                f"{file_rel}: line after {collection_tag!r} is not a class decl: {lines[i]!r}"
            )
    if class_idx is None:
        raise MigrationError(f"{file_rel}: no class declaration found after {collection_tag!r}")

    class_line = lines[class_idx]
    m = re.match(r"^public (sealed |abstract )?class (\w+)(.*)$", class_line)
    if not m:
        raise MigrationError(f"{file_rel}: class line didn't parse: {class_line!r}")
    modifier, class_name, rest = m.groups()
    modifier = modifier or ""

    # Idempotency: already migrated?
    if base_type in class_line:
        return "skipped-already-done"

    rest = rest.strip()
    if rest.startswith(":"):
        existing_bases = rest[1:].strip()
        new_rest = f" : {base_type}, {existing_bases}"
    elif rest == "":
        new_rest = f" : {base_type}"
    else:
        raise MigrationError(f"{file_rel}: unexpected trailing text on class line: {rest!r}")

    lines[class_idx] = f"public {modifier}class {class_name}{new_rest}"

    # 3. Locate the primary constructor: public <ClassName>(<FAC> factory)
    ctor_prefix = f"public {class_name}({fac} factory)"
    ctor_idx = None
    for i in range(class_idx + 1, len(lines)):
        if lines[i].strip().startswith(ctor_prefix):
            ctor_idx = i
            break
    if ctor_idx is None:
        raise MigrationError(
            f"{file_rel}: could not find constructor line starting with {ctor_prefix!r}"
        )

    ctor_line = lines[ctor_idx]
    indent_match = re.match(r"^(\s*)", ctor_line)
    indent = indent_match.group(1) if indent_match else ""

    new_signature = f"{indent}public {class_name}({fac} factory, {bucket_type} bucketDb)"

    stripped = ctor_line.strip()
    remainder = stripped[len(ctor_prefix):]  # whatever follows "...factory)"

    if remainder.startswith(" =>"):
        # Expression-bodied ctor, single line: "... => _factory = factory;"
        body_part = remainder[len(" =>"):].strip()
        lines[ctor_idx] = f"{new_signature} : base(factory, bucketDb) => {body_part}"
    elif remainder == "":
        # Block-bodied ctor: signature on its own line, "{" expected on the next.
        if ctor_idx + 1 >= len(lines) or lines[ctor_idx + 1].strip() != "{":
            raise MigrationError(
                f"{file_rel}: block ctor at line {ctor_idx + 1} not followed by lone '{{'"
            )
        lines[ctor_idx] = f"{new_signature} : base(factory, bucketDb)"
    else:
        raise MigrationError(
            f"{file_rel}: unexpected constructor line tail: {remainder!r} (full line: {ctor_line!r})"
        )

    path.write_text("\n".join(lines))
    return "migrated"


def main() -> int:
    entries = parse_map(MAP_FILE)
    print(f"Loaded {len(entries)} map entries from {MAP_FILE}")

    migrated = 0
    skipped = 0
    errors: list[str] = []

    for file_rel, k, fac in entries:
        try:
            result = migrate_file(file_rel, k, fac)
            if result == "migrated":
                migrated += 1
                print(f"  migrated  K={k} FAC={fac}  {file_rel}")
            else:
                skipped += 1
                print(f"  skipped   K={k} FAC={fac}  {file_rel}  (already migrated)")
        except MigrationError as e:
            errors.append(str(e))
            print(f"  ERROR     {e}", file=sys.stderr)

    print(f"\n{migrated} migrated, {skipped} already-done, {len(errors)} errors "
          f"(of {len(entries)} total)")

    if errors:
        print("\nFailed entries:", file=sys.stderr)
        for e in errors:
            print(f"  - {e}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
