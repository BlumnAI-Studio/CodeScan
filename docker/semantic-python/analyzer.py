"""Semantic analyzer for Python — runs inside codescan/semantic-python:latest.

Contract (see harness/knowledge/semantic-analyzer-docker.md):
  INPUT:  /work mounted read-only (project root)
  OUTPUT: stdout NDJSON, one edge per line
  EXIT:   0 ok, 1 invalid input, 2 not implemented yet

Special mode: --self-check emits a static NDJSON sample + exits 0.

Phase 2-C plan: walk libcst tree + jedi.Project for cross-file symbol resolution.
This stub validates the contract (entrypoint + self-check) while the matcher is
fleshed out in Phase 2-C proper.
"""

import json
import sys
from pathlib import Path

TOOL_VERSION = "python-stub-0.1"


def emit(obj: dict) -> None:
    sys.stdout.write(json.dumps(obj, ensure_ascii=False) + "\n")


def self_check() -> int:
    emit({"kind": "selfcheck", "tool": TOOL_VERSION, "ok": True})
    emit({
        "kind": "edge",
        "from": {"type": "class", "name": "DemoChild"},
        "to": {"type": "type", "name": "DemoBase"},
        "rel": "inherits_or_implements",
        "detail": "semantic",
        "line": 1,
    })
    return 0


def analyze(work_dir: str) -> int:
    if not Path(work_dir).is_dir():
        sys.stderr.write(f"Work directory not found: {work_dir}\n")
        return 1

    # Phase 2-C stub: real matcher uses libcst + jedi.Project.
    # Until then, emit nothing and exit non-zero so the host falls back to regex.
    sys.stderr.write(
        f"[{TOOL_VERSION}] Python semantic matcher is not yet implemented. "
        "Falling back is the host's responsibility.\n"
    )
    return 2


def main(argv: list[str]) -> int:
    if argv and argv[0] == "--self-check":
        return self_check()
    work_dir = argv[0] if argv else "/work"
    return analyze(work_dir)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
