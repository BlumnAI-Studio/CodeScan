#!/bin/sh
# Semantic analyzer for Kotlin — runs inside codescan/semantic-kotlin:latest.
#
# Contract (see harness/knowledge/semantic-analyzer-docker.md):
#   INPUT:  /work mounted read-only (project root containing build.gradle.kts)
#   OUTPUT: stdout NDJSON, one edge per line
#   EXIT:   0 ok, 1 invalid input, 2 not implemented yet
#
# Special mode: --self-check emits a static NDJSON sample + exits 0.
#
# Phase 1-B plan: use the Kotlin Analysis API (kotlinc-embeddable) to extract
# Pekko Typed matchers — `Behavior<T>` generic argument → receives_message,
# `context.spawn(BehaviorFactory(), name)` → spawns_child.

TOOL_VERSION="kotlin-stub-0.1"

if [ "$1" = "--self-check" ]; then
    printf '{"kind":"selfcheck","tool":"%s","ok":true}\n' "$TOOL_VERSION"
    printf '{"kind":"edge","from":{"type":"class","name":"DemoChild"},"to":{"type":"type","name":"DemoBase"},"rel":"inherits_or_implements","detail":"semantic","line":1}\n'
    exit 0
fi

WORK_DIR="${1:-/work}"
if [ ! -d "$WORK_DIR" ]; then
    printf 'Work directory not found: %s\n' "$WORK_DIR" >&2
    exit 1
fi

printf '[%s] Kotlin semantic matcher is not yet implemented. ' "$TOOL_VERSION" >&2
printf 'Phase 1-B will add Pekko Typed matchers (Behavior<T> + context.spawn).\n' >&2
exit 2
