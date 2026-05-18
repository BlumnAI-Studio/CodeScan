# codescan/semantic-go (Phase 2-B stub)

Go semantic analyzer — currently only `--self-check` works. Real matcher will
use `golang.org/x/tools/go/packages` and `go/types` for symbol-level analysis.

## Build & self-check

```bash
docker build -t codescan/semantic-go:latest docker/semantic-go
docker run --rm codescan/semantic-go:latest --self-check
```

## Planned matchers

| Edge | go/packages + go/types extraction |
|------|------------------------------------|
| `inherits_or_implements` | embedded fields (already partially in regex) + interface satisfaction (`var _ Foo = (*Bar)(nil)`) |
| `creates` | constructor-function calls — `pkg.NewType()` returning `*Type` → `caller -[creates]-> Type` (regex misses these entirely) |
| `imports` | `ImportSpec.Path` (already in regex) |

## Phase 2-B exit criteria

`TestSample/go/` matrix shifts from `creates: 0/4` (Go has no `&Type{}` for
this fixture's NewXxx convention) to `creates: 4/4` after constructor-function
resolution lands.

## Image size

`alpine:3.20` + statically linked binary → ~15MB total. Smallest of all
Phase 2 images.
