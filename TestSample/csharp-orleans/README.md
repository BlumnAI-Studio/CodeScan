# csharp-orleans — Microsoft Orleans virtual-actor fixture

Same domain as `csharp-akka` (World coordinator + En/Ko/Ja Speaker children),
but expressed through **virtual actors** instead of hierarchical spawn.

## Build & Run

```bash
docker build -t hello-orleans .
docker run --rm hello-orleans
```

Expected output:
```
[en] Alice: Hello, World!
[ko] 진수: 안녕, 세상아!
[ja] ハナコ: こんにちは、世界!
```

## Pattern — Orleans vs Akka

| Aspect | Akka.NET | Orleans |
|--------|----------|---------|
| Child creation | `Context.ActorOf(Props.Create<T>(...))` (explicit spawn) | `GrainFactory.GetGrain<T>(id)` (lookup; runtime activates) |
| Lifecycle | Parent supervises; explicit `Stop()` | Runtime decides; deactivation by idle timeout |
| Identity | Path (`/user/world/en`) | Primary key (`"Alice"`) — globally unique per type |
| Messaging | `actor.Tell(msg)` (fire-and-forget) | `await grain.Method(args)` (request-reply RPC) |
| Marker pattern | `: ReceiveActor` | `: Grain, IPersonGrain` (interface + base) |

This is a distinct actor-model paradigm (virtual actor) — neither Akka.NET nor
Pekko Typed have a direct equivalent. Including this fixture extends the
cross-toolkit matrix from 3 → 4 toolkits.

## Why regex strategy struggles

| Edge | Detectable by regex? |
|------|----------------------|
| `inherits_or_implements` (Grain base, IPersonGrain implementation) | ✅ same as Akka — class declaration is static |
| `creates` (true grain construction is internal to the runtime; user code only references) | ❌ no `new SpeakerGrain()` in user code |
| `imports` (`using Orleans;`) | ✅ |
| **`activates`** (`GrainFactory.GetGrain<T>(id)` — Orleans's spawn-equivalent) | ❌ generic argument extraction needs semantic analyzer |

→ Semantic-csharp PoC matcher additions for Orleans:
- `MemberAccessExpressionSyntax` ending in `.GetGrain` with `GenericNameSyntax`
  argument list → emit `activates` edge (parent grain → target grain interface).
- Tracked in `harness/knowledge/actor-model-cross-toolkit.md` (4-toolkit matrix).
