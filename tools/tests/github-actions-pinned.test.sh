#!/usr/bin/env bash
set -euo pipefail

python3 - <<'PY'
from pathlib import Path
import re
import sys

workflow_dir = Path(".github/workflows")
failures = []
uses_pattern = re.compile(r"^\\s*-\\s*uses:\\s*([^\\s#]+)", re.IGNORECASE)
sha_pattern = re.compile(r"^[0-9a-f]{40}$", re.IGNORECASE)

for workflow in sorted(workflow_dir.glob("*.yml")):
    for line_number, line in enumerate(workflow.read_text(encoding="utf-8").splitlines(), start=1):
        match = uses_pattern.match(line)
        if not match:
            continue
        reference = match.group(1)
        action, separator, revision = reference.rpartition("@")
        if not separator or not sha_pattern.fullmatch(revision):
            failures.append(f"{workflow}:{line_number}: action must use a full 40-character commit SHA: {reference}")

if failures:
    print("\\n".join(failures), file=sys.stderr)
    raise SystemExit(1)

print(f"All GitHub Actions references are pinned by full commit SHA across {len(list(workflow_dir.glob('*.yml')))} workflow files.")
PY
