# codescan/semantic-csharp

Roslyn-backed semantic analyzer for C# — emits NDJSON edges that the regex
strategy can't reach (resolved symbol info, true base types, target-typed
`new`, etc.).

## Build

```bash
docker build -t codescan/semantic-csharp:latest docker/semantic-csharp
```

## Run

```bash
# Health check
docker run --rm codescan/semantic-csharp:latest --self-check

# Analyze a project (project root must contain *.sln or *.csproj)
docker run --rm -v "$(pwd):/work:ro" codescan/semantic-csharp:latest
```

Or via the host CLI once the image is built:

```bash
codescan semantic install csharp           # marks csharp enabled
codescan semantic self-check csharp
codescan semantic analyze csharp .
```

## Contract

- **Input**: `/work` mount, read-only. Must contain `*.sln` or `*.csproj`.
- **Output**: NDJSON on stdout, one edge per line. See `harness/knowledge/semantic-analyzer-docker.md`.
- **Exit codes**: `0` ok, `1` MSBuildLocator failure, `2` no project model, `3` workspace open failure.

## Emitted edges (Phase 1)

| Edge | Source |
|------|--------|
| `inherits_or_implements` | `INamedTypeSymbol.BaseType` + `.Interfaces` |
| `creates` | `ObjectCreationExpressionSyntax` resolved by `GetSymbolInfo` |
| `imports` | `UsingDirectiveSyntax` (FQN preserved) |

Phase 1-B will add `spawns_child` and `receives_message` for Akka.NET — those
need `Props.Create<T>` / `typeof(T)` argument tracking and `Receive<T>(...)`
handler-table extraction.
