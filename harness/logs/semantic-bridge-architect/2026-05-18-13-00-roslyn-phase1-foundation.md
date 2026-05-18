---
date: 2026-05-18T13:00+09:00
agent: semantic-bridge-architect
type: creation
mode: log-eval
trigger: "T3 — Roslyn 도커 Phase 1 PoC foundation"
---

# Roslyn 도커 Phase 1 — PoC foundation 합류

## 실행 요약

`semantic-analyzer-docker.md` Phase 1 (C# Roslyn) PoC 골조를 한 번에 안전하게 합류.
호스트 측 골조 + 컨테이너 측 분석기 + 호스트 명령(`codescan semantic`)을 함께 추가하고,
`SemanticProbeStrategy` 자동 활성화는 다음 단계로 분리.

## 결과 — 8 체크리스트 진척

`semantic-analyzer-docker.md` 의 "다음 액션" 8 항목 대비:

| # | 항목 | 상태 |
|---|------|------|
| 1 | `docker/semantic-csharp/Dockerfile` (멀티스테이지) | ✅ build/runtime 2-stage, mcr.microsoft.com/dotnet/sdk:9.0 |
| 2 | `--self-check` 진입점 (NDJSON 샘플) | ✅ `Program.cs` SelfCheck |
| 3 | `Services/Semantic/SemanticCache.cs` | ✅ sha256(content)+toolVersion 키, `~/.codescan/semantic/<lang>/` 격리 |
| 4 | `Services/Semantic/SemanticDockerRunner.cs` | ✅ `Process.Start` 기반 AOT-safe, Run/SelfCheck/Pull/DockerAvailable |
| 5 | `Services/Semantic/SemanticResultMerger.cs` | ✅ `JsonDocument.Parse` (AOT-safe), edge 라인만 매핑, malformed skip |
| 6 | `Commands/SemanticCommand.cs` | ✅ status/install/self-check/analyze/clear 5 subcommand |
| 7 | `SemanticProbeStrategy.CanAnalyze` 활성화 | ⏳ **분리** — standalone `codescan semantic` 명령으로 dogfooding 가능. 자동 hybrid 합류는 별도 PR |
| 8 | dogfooding 첫 target = `CodeScan.csproj` | ⏳ 이미지 빌드 후 사용자 환경에서 실행 (도커 builds는 호스트 빌드와 분리) |

## 신규 코드 (호스트)

```
Models/
  EdgeKinds.cs                              (T2 — 11 const string + 신규 5 actor 엣지)
Services/Semantic/
  SemanticCache.cs                          (캐시 디렉토리/키/enable/disable/clear)
  SemanticDockerRunner.cs                   (docker run/pull/self-check/available)
  SemanticResultMerger.cs                   (NDJSON → List<SourceDependency>, Merge 정책)
Commands/
  SemanticCommand.cs                        (status/install/self-check/analyze/clear)
Tests/
  SemanticResultMergerTests.cs              (7 케이스 — edge/node/actor-edge/malformed/Merge/Cache key)
```

Program.cs 라우팅: `semantic` 명령 추가 (line ~64).
CodeScan.csproj: `docker/**` 컴파일 제외 (TestSample/와 동일 패턴).
AppPaths.cs: `SemanticDir` / `GetSemanticDir()` 추가.

## 신규 코드 (컨테이너)

```
docker/semantic-csharp/
  SemanticAnalyzer.csproj                   (net9.0 + Microsoft.CodeAnalysis.CSharp.Workspaces 4.13 + MSBuildLocator)
  Program.cs                                (--self-check 모드 + MSBuildWorkspace 분석 + NDJSON 출력)
  Dockerfile                                (멀티스테이지, sdk:9.0 runtime 유지 — MSBuildLocator가 MSBuild 필요)
  README.md                                 (빌드/실행/contract/edge 매핑)
```

호스트 빌드는 컨테이너 코드 제외 (`<Compile Remove="docker\**" />`).

## NDJSON 스키마 검증

`SemanticResultMergerTests` 가 다음 라운드트립 보장:

| 케이스 | 동작 |
|--------|------|
| `kind:"edge"` + `from`/`to`/`rel`/`line` | `SourceDependency` 생성, strategy="semantic" |
| `kind:"node"` 라인 | skip (host는 regex로 이미 노드 발견) |
| `rel:"spawns_child"` 같은 actor-model 엣지 | edge-kind-agnostic, EdgeKinds 상수와 round-trip |
| malformed JSON | skip + continue (전체 실패 안 됨) |
| 동일 (from, edge, to) regex + semantic | semantic 우선 (Merge 정책) |
| regex-only 엣지 | Merge 후 보존 |
| `ComputeKey` 결정성 | 같은 내용 + 같은 tool version → 같은 키 |

## AOT 영향

| 신규 API | AOT 안전 | 이유 |
|---------|---------|------|
| `Process.Start(ProcessStartInfo)` | ✅ | BCL P/Invoke 직접 사용, reflection 없음 |
| `JsonDocument.Parse(string)` | ✅ | reflection 미사용 — 명세 기반 파싱 |
| `SHA256.HashData(span)` | ✅ | static, AOT 친화 |
| `File.WriteAllBytes/Text` | ✅ | BCL |
| `Microsoft.CodeAnalysis.*` | (컨테이너 전용) | 호스트 미포함 — `docker/semantic-csharp/` 격리 |

→ `dotnet test` 통과 (123/123). `dotnet publish -c Release` AOT 빌드 회귀는 별도 단계에서 검증 권장.

## 사용 흐름 (사용자 dogfooding)

```bash
# 1) 이미지 빌드 (한 번)
docker build -t codescan/semantic-csharp:latest docker/semantic-csharp

# 2) 호스트 활성화
codescan semantic install csharp        # 이미지 pull 시도 + enabled 마커
codescan semantic status

# 3) self-check
codescan semantic self-check csharp

# 4) 분석 — CodeScan 자체에 dogfooding
codescan semantic analyze csharp .

# 5) 정리
codescan semantic clear csharp
```

## 평가 (semantic-bridge-architect)

| 축 | 척도 | 결과 |
|----|------|------|
| 플랜 완성도 | Phase 단계가 측정 가능 | ✅ 1/8 → 6/8 (코드 골조 합류) |
| 매처 명확도 | input → 출력 엣지 매핑 명세 | ✅ Phase 1: inherits/creates/imports 매처 ObjectCreation/UsingDirective/BaseType 매핑 |
| 캐시 적중률 | (PoC 후) 동일 소스 재스캔 100% hit | ⏳ 사용자 환경 측정 대기 |
| 도커 이미지 크기 | < 1.5GB (멀티스테이지) | ⏳ build 후 측정 (sdk:9.0 ~700MB 추정) |
| Fixture 회귀 | regex 매트릭스 동일 이상 | ⏳ 빌드 후 TestSample/csharp/ 매트릭스 비교 |

## 다음 단계 제안 (Phase 1-B → 2)

1. **이미지 빌드 + 첫 dogfooding** — 사용자가 `docker build` 후 `codescan semantic analyze csharp .` 실행하여 NDJSON 산출
2. **Phase 1-B 액터 매처** — `ObjectCreationExpressionSyntax` 외에 `InvocationExpressionSyntax` 분석 추가:
   - `Context.ActorOf(Props.Create<T>(...))` — `MemberAccessExpression.Name.TypeArgumentList`
   - `Props.Create(typeof(T))` — `TypeOfExpressionSyntax.Type`
   - `Props.Create(() => new T(...))` — `LambdaExpression.Body` 내 `ObjectCreationExpressionSyntax`
   - 매처 통과 시 `spawns_child` 엣지 emit (T2 EdgeKinds.SpawnsChild)
3. **`SemanticProbeStrategy` 자동 합류** — `IsConfiguredFor("csharp")` + `ProjectModelFound` 시 활성화, 첫 파일에서 docker run + NDJSON 캐시, 후속은 캐시 hit
4. **Phase 2 트랙** — TypeScript (`tsconfig` + `ts.createProgram`), Go (`go/packages`), Python (`jedi`)

## 관련

- 계획 출처: [[semantic-analyzer-docker]] (Phase 1~4 전체)
- 동기 audit: [[2026-05-18-12-00-residual-improvements-audit]] (T3 출처)
- 동기 엔진: [[2026-05-18-12-31-after-r1-r2-baseline]] (`spawns_child` 합격선 명시)
- 후속: Phase 1-B 액터 매처 (T4) → Pekko Typed 우선
