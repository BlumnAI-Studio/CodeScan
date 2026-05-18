# codescan/semantic-python (Phase 2-C stub)

Python semantic analyzer — currently only `--self-check` is implemented. The
real matcher (Phase 2-C) will use:

- **`libcst`** — concrete syntax tree with whitespace preserved (round-trippable)
- **`jedi.Project(workdir)`** — cross-file name resolution and goto-definition

## Build & self-check

```bash
docker build -t codescan/semantic-python:latest docker/semantic-python
docker run --rm codescan/semantic-python:latest --self-check
```

## Planned matchers

| Edge | libcst + jedi extraction |
|------|---------------------------|
| `inherits_or_implements` | `ClassDef.bases` → `jedi.Project.goto()` for FQN |
| `creates` | `Call(func=Name('X'))` where `X` resolves to a class |
| `imports` | `Import` / `ImportFrom` — module FQN preserved |
| `defines` | `FunctionDef` inside `ClassDef` |

## Phase 2-C exit criteria

- `inherits` matrix on `TestSample/python/` reaches 3/3 (matches regex)
- `creates` jumps from regex's 4/4 to 4/4 + factory-function resolution
  (`foo = make_speaker("en")` → `Main -[creates]-> EnSpeaker`)
