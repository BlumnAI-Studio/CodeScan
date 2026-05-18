# codescan/semantic-typescript

TypeScript Compiler API semantic analyzer — uses `ts.createProgram` + the type
checker to emit edges that the regex strategy can't resolve (true heritage
chains across imports, structural-typed inheritance, etc.).

## Build

```bash
docker build -t codescan/semantic-typescript:latest docker/semantic-typescript
```

## Run

```bash
# Health check
docker run --rm codescan/semantic-typescript:latest --self-check

# Analyze a project (must contain tsconfig.json or jsconfig.json)
docker run --rm -v "$(pwd):/work:ro" codescan/semantic-typescript:latest
```

Via host CLI:

```bash
codescan semantic install typescript
codescan semantic self-check typescript
codescan semantic analyze typescript .
```

## Emitted edges (Phase 2-A)

| Edge | Source |
|------|--------|
| `inherits_or_implements` | `HeritageClause.types` resolved by `TypeChecker.getSymbolAtLocation` |
| `creates` | `NewExpression.expression` resolved by symbol |
| `imports` | `ImportDeclaration.moduleSpecifier` (literal preserved) |

## Image size

Base `node:22-alpine` (~200MB) + `typescript` (~70MB on disk). Total ~270MB —
the smallest of the Phase 1+2 images.

## Phase 2 roadmap

- Phase 2-A (this image): inherits/creates/imports — minimum viable matcher.
- Phase 2-B: generic instantiations — `Map<K,V>` resolves both `K` and `V`.
- Phase 2-C: structural-typed shapes — `interface Foo { x: T }` adds
  `interface -> type` edges.
