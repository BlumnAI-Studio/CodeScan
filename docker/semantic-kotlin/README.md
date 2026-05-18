# codescan/semantic-kotlin (Phase 1-B stub)

Kotlin semantic analyzer — currently only `--self-check` works. The real
matcher (Phase 1-B) targets **Pekko Typed** patterns — the most analyzer-friendly
actor toolkit because the generic type parameter on `AbstractBehavior<T>`
directly encodes the message dispatch table.

## Build & self-check

```bash
docker build -t codescan/semantic-kotlin:latest docker/semantic-kotlin
docker run --rm codescan/semantic-kotlin:latest --self-check
```

## Planned matchers (Phase 1-B priority — see harness/knowledge/actor-model-cross-toolkit.md)

| Edge | Kotlin Analysis API extraction |
|------|--------------------------------|
| `inherits_or_implements` | `KtClass.getSuperTypeListEntries()` → resolve FQN (regex already does most of this after R1/R2 sealing) |
| `creates` | `KtCallExpression` resolving to a class (Kotlin has no `new`; any `Type(...)` is construction) |
| `imports` | `KtImportDirective` |
| **`spawns_child`** | `context.spawn(BehaviorFactory(), name)` — first arg's static type is the child Behavior class |
| **`receives_message`** | `class XBehavior : AbstractBehavior<T>(context)` — generic `T` is the dispatch message |

## Phase 1-B exit criteria

On `TestSample/kotlin-pekko/`:

- `spawns_child`: 3/3 (WorldBehavior → En/Ko/Ja SpeakerBehavior)
- `receives_message`: 4/4 (each Behavior's `<SpeakerCommand>` or `<WorldCommand>` parameter)
- `inherits_or_implements`: 5/5 (regex already at 5/5 post-R1/R2, semantic confirms)

## Why Pekko Typed first

`AbstractBehavior<T>` makes the message contract a compile-time constant —
no need to walk the `Receive` builder pattern (`.match(T.class)` etc) that
Akka.NET and Akka Classic require. Of the three actor toolkits in the
cross-toolkit matrix, Pekko Typed needs the simplest matcher.
