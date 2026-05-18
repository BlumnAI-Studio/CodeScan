---
date: 2026-05-18T12:00+09:00
agent: tamer
type: evaluation
mode: log-eval
trigger: "코드스캔 분석을 위한 잔여 개선건 정리.."
---

# 잔여 개선건 정리 — v1.2.0 기준 audit

## 실행 요약

`v1.2.0` 합류 직후 시점에서 하네스 전 영역(에이전트 합격선·knowledge known-limits·engine 정의·세션 로그)을
교차 검증하여 **아직 닫히지 않은 개선건**을 카테고리별로 정리.

수집 출처:
- `harness/docs/v1.2.0.md` 의 "다음 버전 후보"
- `harness/knowledge/language-analyzer-patterns.md` 의 "알려졌으나 미해결" 표
- `harness/knowledge/actor-model-cross-toolkit.md` 의 "정규식 한계 매트릭스"
- `harness/knowledge/semantic-analyzer-docker.md` 의 "다음 액션" 체크리스트
- `harness/engine/{test-audit, actor-graph-extension}.md` 의 미실행 상태
- 최근 로그: [[2026-05-18-01-04-actor-hierarchy-cross-toolkit]], [[2026-05-18-00-42-language-analyzer-refactor]], [[2026-05-18-01-16-add-language-and-actor-experts]]

## 결과 — 잔여 개선건 8 트랙

### Track 1. KotlinAnalyzer 회귀 봉합 (R1, R2)

**상태**: fixture는 회귀로 등록되어 있으나 regex 미수정 → `creates` 9.5/10, `inherits` Kotlin 셀이 비정상.

| ID | 회귀 | 영향 | 해결 후보 |
|----|------|------|----------|
| R1 | `class X private constructor(...) : Y(args)` — inherits 누락 | Pekko Typed fixture에서 Behavior 클래스 베이스 미인식 | `(\w+)` 뒤 `(?:\s+\w+)*` 로 modifier 시퀀스 흡수 |
| R2 | `class X : Y(args)` super-call이 `creates`로 잘못 인식 | 잘못된 `XxxBehavior -[creates]-> PersonBehavior` 3건 | 첫 `:` 이후 첫 `Type(args)`를 creates에서 제외 |

- **Owner**: [[language-analyzer-keeper]]
- **검증 fixture**: `TestSample/kotlin-pekko/` (이미 존재)
- **합격선**: 23개 `LanguageAnalyzerTests` + 신규 2 케이스 통과, 매트릭스 inherits 10/10 회복

### Track 2. 그래프 모델 신규 엣지 추가 (5종)

**상태**: 지식 문서에 명세만 있고 `GraphModels`에 미반영.

| 엣지 | 의미 | 첫 도입 우선순위 |
|------|------|----------------|
| `spawns_child` | parent actor → child actor type | **1순위** (3 fixture 즉시 적용 가능) |
| `receives_message` | actor → message type (Receive/onMessage 인자) | 2순위 (Pekko Typed 우선) |
| `sends_message_to` | sender → ref+msg type | 3순위 (데이터 흐름 추적 필요) |
| `supervises_with` | parent → SupervisionStrategy | 4순위 |
| `actor_named` | actor → logical path 문자열 | 5순위 |

- **Owner**: [[actor-toolkit-scout]] (사양) + [[semantic-bridge-architect]] (검출)
- **검증 fixture**: `TestSample/{csharp-akka, java-akka, kotlin-pekko}/`
- **합격선**: `actor-graph-extension` engine의 합격선 — `spawns_child` 12/12 (3 툴킷 × 4 자식 액터 평균)

### Track 3. 의미 분석 도커 Phase 1 — Roslyn PoC

**상태**: 플랜(`semantic-analyzer-docker.md`) 작성 완료, 코드 0건.

체크리스트 (`semantic-analyzer-docker.md` 의 "다음 액션"):

- [ ] `docker/semantic-csharp/Dockerfile` (멀티스테이지, mcr.microsoft.com/dotnet/sdk:9.0)
- [ ] `--self-check` 진입점 (NDJSON 샘플 stdout)
- [ ] `Services/Semantic/SemanticCache.cs`
- [ ] `Services/Semantic/SemanticDockerRunner.cs`
- [ ] `Services/Semantic/SemanticResultMerger.cs`
- [ ] `Commands/SemanticCommand.cs` (status/install/clear/scan --semantic)
- [ ] `SemanticProbeStrategy.CanAnalyze` 활성화 (현재 항상 false)
- [ ] dogfooding: `CodeScan.csproj` 자체를 first target

- **Owner**: [[semantic-bridge-architect]]
- **합격선**: TestSample/csharp 기준 regex 매트릭스 동일 이상 + self-check 통과 + 첫 분석 <60초

### Track 4. 의미 분석 도커 Phase 1-B — 액터 매처 3트랙

**상태**: 매처 의사코드만 작성, PoC 미진행.

| 매처 | 도커 이미지 | 진척 |
|------|-----------|------|
| Pekko Typed (Kotlin) | `codescan/semantic-kotlin` | 의사코드만 — `Behavior<T>` generic 활용으로 매처 가장 단순, **우선순위 1** |
| Akka.NET (C#) | `codescan/semantic-csharp` | 의사코드만 — Roslyn workspace, dogfooding 가능 |
| Akka Classic (Java) | `codescan/semantic-java` | 의사코드만 — JDT의 `.class` 리터럴 추적 필요 |

- **Owner**: [[semantic-bridge-architect]] (구현) + [[actor-toolkit-scout]] (매처 검증)
- **합격선**: 3 fixture에서 `spawns_child` 12/12, `receives_message` 핸들러 수와 일치

### Track 5. 의미 분석 도커 Phase 2~4 (후순위)

**상태**: 우선순위 정의만 됨, 작업 미착수.

- Phase 2: TS (tsc API), Go (`go/packages`), Python (jedi)
- Phase 3: Rust (rust-analyzer), Java (JDT)
- Phase 4: C++ (Clang LibTooling), Kotlin (Kotlin Analysis API), PHP (nikic/PHP-Parser)

- **Owner**: [[semantic-bridge-architect]]
- **블로커**: Track 3 (Phase 1) 완료 후 진행

### Track 6. Engine 실행 로그 부재

**상태**: 두 엔진 모두 정의는 있으나 실제 실행 로그 없음.

| Engine | 정의 | 실행 로그 |
|--------|------|----------|
| `test-audit` | ✅ test-sentinel + regex-safety-guard + aot-compatibility-scout 3자 워크플로우 | ❌ `harness/logs/test-audit/` 자체 없음 |
| `actor-graph-extension` | ✅ 3-툴킷 매트릭스 + 합격선 명시 | ❌ `harness/logs/actor-graph-extension/` 자체 없음 |

→ 엔진은 "정의됨"이지 "검증됨"이 아님. 한 번도 트리거되지 않은 엔진은 회귀 가드 가치가 0.

- **Owner**: [[tamer]] (트리거 책임)
- **즉시 가능**: `테스트 감사`, `액터 그래프 확장` 트리거 호출

### Track 7. Fixture 매트릭스 확장 (액터 툴킷)

**상태**: 3 툴킷에 멈춤 — v1.2.0 docs에 후보로 명시.

| 후보 툴킷 | 언어 | 가치 |
|----------|------|------|
| Microsoft Orleans | C# (.NET) | 분산 액터 — virtual actor 패턴, `IGrain` |
| Erlang/OTP | Erlang | 원조 액터 모델 — gen_server/supervisor tree |
| Proto.Actor | Go/C#/Java | gRPC 통합 액터 — 비-JVM 비-.NET 비교 |
| Microsoft Dapr Actor | 언어 무관 | 사이드카 액터 — 클라우드 네이티브 |

- **Owner**: [[actor-toolkit-scout]]
- **합격선**: 신규 fixture는 `TestSample/<toolkit-id>/` (World 부모 + 3 자식) 도메인 동일, 도커 빌드+실행 통과
- **블로커**: 없음 (병렬 가능)

### Track 8. 정규식 측면 known-but-未봉인 (잔여)

**상태**: `language-analyzer-patterns.md` 의 "알려졌으나 미해결" 표 마지막 행.

| 한계 | 영향 | 해결 후보 |
|------|------|----------|
| 모든 언어 — 툴킷 패턴(`ActorOf<T>`, `Props.create(T.class)`, `context.spawn(...)`) 미인식 | 부모-자식 액터 관계 손실 | **의미 분석 도커**로만 해결 — Track 3/4와 동일 |

→ 별도 트랙이 아니라 Track 3/4로 흡수됨. 이 표 행은 Track 3/4 진행 후 한 줄로 통합/이동 권장.

## 우선순위 매트릭스

| 우선순위 | 트랙 | 노력 | 효과 | 블로커 |
|---------|------|------|------|--------|
| **P0** | T1 Kotlin R1/R2 봉합 | S | 매트릭스 10/10 회복 | 없음 |
| **P0** | T6 Engine 첫 실행 (test-audit + actor-graph-extension) | S | 회귀 가드 활성화, 베이스라인 로그 | 없음 |
| **P1** | T2 `spawns_child` 엣지 GraphModels 합류 | M | 액터 매트릭스 측정 가능 | 합의 필요 (스키마) |
| **P1** | T3 Roslyn 도커 Phase 1 PoC (dogfooding) | L | regex vs semantic 정량 비교 시작 | 없음 |
| **P2** | T4 Pekko Typed 매처 PoC | M | typed actor → `receives_message` 자동 도출 | T2/T3 |
| **P2** | T7 Orleans 또는 Proto.Actor fixture 추가 | M | 4-툴킷 매트릭스 | 없음 (병렬) |
| **P3** | T5 Phase 2~4 도커 이미지 | XL | 9 언어 의미 분석 완성 | T3 완료 |
| —    | T8 통합/이동 | XS | 문서 정합성 | T3/T4 진행 시 자동 |

## 권고 — 다음 1주 액션 (P0/P1 묶음)

세 번의 트리거로 P0를 모두 닫고 P1을 시작할 수 있다:

1. **`/harness-kakashi-creator 언어 분석기 점검해`**
   → [[language-analyzer-keeper]] 진입, KotlinAnalyzer R1/R2 수정안 도출 + fixture 회귀 통과 확인
2. **`/harness-kakashi-creator 테스트 감사`**
   → `test-audit` engine 첫 실행, baseline 매트릭스 + 로그 생성
3. **`/harness-kakashi-creator 액터 그래프 확장`**
   → `actor-graph-extension` engine 첫 실행, 3-툴킷 매트릭스 갱신 + `spawns_child` 엣지 합류 합의

이후 P1로 진행:
4. **`/harness-kakashi-creator 의미 분석 계획`**
   → Phase 1 Roslyn PoC 작업 분해 — `docker/semantic-csharp/Dockerfile` 부터

## 평가 (tamer 3축)

| 축 | 척도 | 결과 |
|----|------|------|
| 워크플로우 개선도 | v1.2.0 합류 후 정원 구조는 충분 — 잔여는 **트리거 부재** | **B** (도구 있음, 실행 안 됨) |
| Claude 스킬 활용도 | 7 에이전트 + 2 engine 정의됨, 그러나 engine 실행 0회 | **3/5** (정의 완성 vs 실행 부재) |
| 하네스 성숙도 | knowledge 5 + agents 7 + engine 2, 모두 [[wiki-link]] 상호 연결, logs 7개 누적 | **L4** (유지) |

→ 정원의 골격은 잘 깔렸으나 **engine이 단 한 번도 트리거되지 않은 점**이 가장 큰 잔여 부채.
가장 적은 노력으로 가장 큰 가치를 회복하는 길은 **engine 첫 실행 2건 + Kotlin R1/R2 봉합** 한 묶음.

## 다음 단계 제안

P0 묶음을 먼저 실행할 경우 자연스럽게 다음 버전 후보가 생긴다:

- **v1.2.1 (patch)** — Kotlin R1/R2 봉합 + 두 engine 첫 실행 로그 합류
- **v1.3.0 (minor)** — `spawns_child` 엣지 합류 (그래프 스키마 변경)
- **v1.4.0 (minor)** — Roslyn 도커 Phase 1 PoC (의미 분석 첫 진입)

## 관련

- 출처 문서:
  - [[language-analyzer-patterns]] — Kotlin R1/R2 명세, 측정된 정확도
  - [[actor-model-cross-toolkit]] — 정규식 한계 매트릭스, 신규 엣지 명세
  - [[semantic-analyzer-docker]] — Phase 1~4 플랜, 다음 액션 체크리스트
- 엔진:
  - [[test-audit]] — 첫 실행 대기
  - [[actor-graph-extension]] — 첫 실행 대기
- 이전 베이스 로그:
  - [[2026-05-18-00-42-language-analyzer-refactor]] — 9 분석기 분리 + R1/R2 노출
  - [[2026-05-18-01-04-actor-hierarchy-cross-toolkit]] — 3-툴킷 fixture + R1/R2 회귀 등록
  - [[2026-05-18-01-16-add-language-and-actor-experts]] — v1.2.0 합류
