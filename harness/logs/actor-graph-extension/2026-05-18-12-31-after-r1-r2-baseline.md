---
date: 2026-05-18T12:31+09:00
engine: actor-graph-extension
type: review
mode: log-eval
trigger: "T6 engine first-run — R1/R2 봉합 후 3-툴킷 매트릭스 baseline"
participants:
  - actor-toolkit-scout
  - language-analyzer-keeper
  - semantic-bridge-architect
---

# actor-graph-extension engine — 첫 실행 (R1/R2 후)

## 실행 요약

T6 묶음. v1.2.0 합류 후 한 번도 실행되지 않았던 engine. T1 봉합 직후의 3-툴킷 매트릭스를
baseline으로 기록하고, `spawns_child` / `receives_message` 합류의 합격선을 명시.

> 정량 측정 방식: 회귀 테스트(111 pass) + fixture 정적 분석. CLI dogfooding 측정은
> T3 (Roslyn Phase 1 PoC) 단계에서 동일 fixture로 동시 측정 예정.

## Step 1 — actor-toolkit-scout: 3-fixture 빌드/실행 검증

이전 로그 [[2026-05-18-01-04-actor-hierarchy-cross-toolkit]] 에서 도커 빌드/실행 검증 완료. 변경 없음.

| 디렉토리 | 툴킷 | 도커 이미지 | 빌드+실행 |
|---------|------|----------|----------|
| `TestSample/csharp-akka/` | Akka.NET 1.5.51 | `hello-akka` | ✅ |
| `TestSample/java-akka/` | Akka Classic 2.7.0 | `hello-akka-java` | ✅ |
| `TestSample/kotlin-pekko/` | Pekko Typed 1.1.5 | `hello-pekko-kotlin` | ✅ |

## Step 2 — 정규식 매트릭스 (R1/R2 봉합 후)

### `inherits_or_implements`

| 툴킷 | Before (v1.2.0) | After (R1/R2 봉합) | 변화 |
|------|---------|---------|------|
| C# Akka.NET | ✅ 5/5 (SpeakerActor→PersonActor + ReceiveActor) | 동일 | — |
| Java Akka Classic | ✅ 5/5 (AbstractActor 베이스) | 동일 | — |
| **Kotlin Pekko Typed** | ⚠️ **2/2 (잘못)** — sealed message만 잡힘, Behavior 부모 미인식 | ✅ **5/5** — En/Ko/Ja/World Behavior → PersonBehavior/AbstractBehavior 모두 인식 | **회복** |

### `creates` (액터 hierarchy 부모-자식 — 핵심)

| 툴킷 | Before | After | 변화 |
|------|--------|-------|------|
| C# Akka.NET (`new` 람다) | ✅ 우연히 잡힘 (1/4) | 동일 | — |
| C# Akka.NET (`typeof`) | ❌ 0/2 | 동일 | — |
| Java Akka Classic (`.class`) | ❌ 0/3 | 동일 | — |
| Kotlin Pekko Typed (`context.spawn`) | ❌ 0/3 | 동일 | — |
| **Kotlin Pekko Typed (super-call false positive)** | ⚠️ +3 잘못된 엣지 | ✅ **0 false positive** | **봉합** |

→ **부모-자식 spawn 의미는 여전히 정규식으로 25%만 우연히 잡힘** (Track 3/T4 의미 분석 도커로만 해결).
다만 **잘못된 엣지(false positive)는 완전히 사라짐** — 매트릭스 정확도가 깨끗해짐.

### `imports` (Akka/Pekko 패키지 시그널)

| 툴킷 | 결과 |
|------|------|
| C# Akka.NET | ✅ `using Akka.Actor;` |
| Java Akka Classic | ✅ `akka.actor.AbstractActor`, `akka.actor.Props` 등 |
| Kotlin Pekko Typed | ✅ `org.apache.pekko.actor.typed.*` (패키지로 typed/untyped 식별) |

## Step 3 — language-analyzer-keeper: 정규식 잔여 한계

R1/R2 봉합 후 정규식이 풀 수 있는 영역은 **inherits 100%** 까지 도달. 더 이상 회귀 매트릭스에서
정규식 보강으로 잡을 수 있는 액터 hierarchy 엣지는 없음. 다음 영역은 모두 의미 분석 도커 영역:

| 패턴 | 정규식 가능? | Owner |
|------|------------|-------|
| `Context.ActorOf<T>(...)` generic 인자 | ❌ | semantic-bridge-architect |
| `Props.Create(typeof(T))` typeof 첫 인자 | ❌ | semantic-bridge-architect |
| `Props.create(T.class)` `.class` 리터럴 | ❌ | semantic-bridge-architect |
| `context.spawn(T.create(), ...)` 팩토리 반환 타입 | ❌ | semantic-bridge-architect |
| `Receive<T>(h)` / `.match(T.class, h)` / `.onMessage(T.class, h)` 핸들러 dispatch | ❌ | semantic-bridge-architect |
| `actor.tell(msg)` 데이터 흐름 | ❌ | semantic-bridge-architect |

→ `[[language-analyzer-patterns]]` 의 "알려졌으나 미해결" 표에서 **Kotlin R1/R2 두 행 제거 가능** — fixture로 봉합됨.

## Step 4 — semantic-bridge-architect: 합격선 명시

`spawns_child` / `receives_message` 엣지가 GraphModels에 합류한 뒤 의미 분석 도커 통과 기준:

| 엣지 | 합격선 | 측정 방법 |
|------|--------|---------|
| `spawns_child` | **12/12** — 3 툴킷 × 4 자식 액터 (En/Ko/Ja + GenericWorld nested) | 도커 매처가 `ActorOf/actorOf/spawn` 첫 인자에서 target 타입 추출 |
| `receives_message` | **각 액터의 Receive/onMessage 핸들러 수와 일치** | Pekko Typed `AbstractBehavior<T>` generic + `.onMessage(M.class, ...)` |
| `sends_message_to` | 각 `tell` 호출 위치 | 데이터 흐름 (정적 ActorRef 타입 추적) |
| `inherits` | **5/5 모든 툴킷** | ✅ 정규식만으로 달성 (R1/R2 봉합 후) |

### PoC 우선순위 (확정)

1. **Pekko Typed (Kotlin)** — `Behavior<T>` generic이 dispatch table을 명시 → 매처 가장 단순
2. **Akka.NET (C#)** — Roslyn workspace, dogfooding 가능 (CodeScan 자체 .NET)
3. **Akka Classic (Java)** — JDT의 `.class` 리터럴 추적, 매처 복잡도 중간

## 종합 매트릭스 (3-툴킷, R1/R2 후)

```
                    │ inherits │ creates (parent-child spawn) │ imports │
────────────────────┼──────────┼──────────────────────────────┼─────────┤
C# Akka.NET (5)     │   5/5    │   1/4 (람다 우연)             │   ✅    │
Java Akka Classic   │   5/5    │   0/3                        │   ✅    │
Kotlin Pekko Typed  │   5/5    │   0/3                        │   ✅    │
────────────────────┴──────────┴──────────────────────────────┴─────────┘
요약: inherits 15/15 ✅ | spawn 1/10 (정규식 한계) | imports 3/3 ✅
```

→ **정규식 영역에서 잡을 수 있는 모든 것은 잡혔다**. 남은 9/10 spawn 엣지는 의미 분석 도커 영역.

## 평가

| 축 | 척도 | 결과 |
|----|------|------|
| Fixture 빌드 | 3개 도커 이미지 모두 실행 가능 | ✅ |
| 매트릭스 일관성 | 같은 의미가 툴킷별로 어떻게 표현되는지 표로 정리 | ✅ |
| 매처 사양 명확도 | Roslyn/JDT/Kotlin 매처 의사코드 | ✅ ([[actor-model-cross-toolkit]]) |
| 신규 엣지 후보 | spawns_child 외 4종 검토 | ✅ |
| `spawns_child` 12/12 | 의미 분석 도커 도입 후 합격 | ⏳ T2/T4 진행 시 |

## 다음 단계 제안

1. **T2 (P1)** — `spawns_child` 엣지 GraphModels 합류 (스키마 변경). 즉시 가능.
2. **T3 (P1)** — Roslyn Phase 1 PoC 시작. `docker/semantic-csharp/Dockerfile` 부터.
3. **T4 (P2)** — Pekko Typed 매처 PoC. T3 완료 후 Phase 1-B로 진행.
4. `[[language-analyzer-patterns]]` "알려졌으나 미해결" 표에서 Kotlin R1/R2 두 행 제거 (별도 패치).

## 관련

- 이전 베이스 로그: [[2026-05-18-01-04-actor-hierarchy-cross-toolkit]] (3-fixture 빌드/실행)
- 관련 audit: [[2026-05-18-12-00-residual-improvements-audit]] (T2 합류 합격선 출처)
- 동기 engine: [[2026-05-18-12-30-baseline-after-r1-r2]] (test-audit 첫 실행)
- 후속 작업: T2 (`spawns_child` 엣지) → T3 (Roslyn 도커 PoC)
