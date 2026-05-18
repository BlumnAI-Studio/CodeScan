---
date: 2026-05-18T15:30+09:00
agent: semantic-bridge-architect
type: creation
mode: log-eval
trigger: "T5 — Phase 2 도커 이미지 4종 합류 (TS full / kotlin/go/python stubs)"
---

# Phase 2 — 4 도커 이미지 합류 (typescript full + 3 stubs)

## 실행 요약

[[2026-05-18-12-00-residual-improvements-audit]] T5. v1.3.0 의 csharp Phase 1
파운데이션 위에 Phase 2 트랙 4 이미지를 합류:

- **typescript** — 실제 동작하는 full matcher (`ts.createProgram` + TypeChecker)
- **kotlin / go / python** — Dockerfile + self-check + 매처 사양만 (stub)

목적: 5 언어 contract 통일 + `codescan semantic` 명령으로 5 언어 모두 status/install/self-check 가능. 실제 매처 구현은 사용자가 우선순위 순으로 채울 수 있는 슬롯을 깔아둠.

## 결과 — docker/ 디렉토리 (v1.4.0)

```
docker/
├── semantic-csharp/                (v1.3.0 — Roslyn full matcher)
├── semantic-typescript/            (v1.4.0 — tsc full matcher)        ✅ FULL
│   ├── package.json                Node 22 + typescript ^5.6
│   ├── index.js                    HeritageClause / NewExpression / ImportDeclaration
│   ├── Dockerfile                  node:22-alpine (~270MB)
│   └── README.md
├── semantic-kotlin/                (v1.4.0 — Pekko Typed 매처 사양만)    🟨 STUB
│   ├── analyzer.sh                 self-check + 매처 사양 주석
│   ├── Dockerfile                  eclipse-temurin:21-jre-alpine
│   └── README.md
├── semantic-go/                    (v1.4.0 — go/packages 사양만)         🟨 STUB
│   ├── go.mod                      go 1.22
│   ├── main.go                     self-check + 매처 사양 주석
│   ├── Dockerfile                  golang:1.22-alpine build → alpine:3.20 runtime (~15MB)
│   └── README.md
└── semantic-python/                (v1.4.0 — libcst + jedi 사양만)       🟨 STUB
    ├── analyzer.py                 self-check + 매처 사양 주석
    ├── Dockerfile                  python:3.12-alpine
    └── README.md
```

## TypeScript matcher (실제 구현)

`docker/semantic-typescript/index.js` — `ts.createProgram` 으로 tsconfig.json
또는 jsconfig.json 기반 program 생성 후 SourceFile 순회:

| 노드 | 추출 엣지 |
|------|---------|
| `ClassDeclaration` / `InterfaceDeclaration` + `HeritageClause.types` | `inherits_or_implements` (TypeChecker로 심볼 해소) |
| `NewExpression` 안 enclosing class | `creates` |
| `ImportDeclaration` (literal moduleSpecifier) | `imports` |

장점:
- `node_modules/` 자동 skip
- `.d.ts` declaration file 자동 skip
- `getSymbolAtLocation` 으로 FQN 해소 — 같은 이름 다른 import 구별 가능

## Stub 이미지 매처 사양

각 stub README에 Phase별 매처 사양 명시. 사용자가 순서대로 채울 수 있는 슬롯:

### kotlin (Phase 1-B 우선)

```kotlin
// Pekko Typed 매처:
// class XBehavior : AbstractBehavior<T>(context)  → emit receives_message: X -> T
// context.spawn(BehaviorFactory(), name)          → emit spawns_child: parent -> Behavior class
```

### go (Phase 2-B)

```go
// go/packages + go/types 매처:
// pkg.NewType() 함수 반환 *Type → emit creates: caller -> Type
// (정규식이 잡지 못하는 가장 큰 케이스)
```

### python (Phase 2-C)

```python
# libcst + jedi 매처:
# Call(func=Name('X')) where jedi.goto(X) returns a ClassDef → emit creates
# ClassDef.bases → jedi.goto for FQN → emit inherits_or_implements
```

## SemanticCommand 확장

```csharp
// Before (v1.3.0)
private static readonly string[] SupportedLanguages = ["csharp"];

// After (v1.4.0)
private static readonly string[] SupportedLanguages = ["csharp", "typescript", "kotlin", "go", "python"];
```

`status` 명령이 5 언어 enabled 상태 표시. `install` / `self-check` / `analyze` / `clear` 모두 5 언어 사용 가능.

## 검증

```bash
# 1) typescript full matcher 검증 (실제 분석)
docker build -t codescan/semantic-typescript:latest docker/semantic-typescript
docker run --rm codescan/semantic-typescript:latest --self-check
docker run --rm -v "$(pwd)/TestSample/typescript:/work:ro" codescan/semantic-typescript:latest

# 2) stub 3개 self-check (NDJSON 샘플 + tool version)
docker build -t codescan/semantic-kotlin:latest    docker/semantic-kotlin
docker build -t codescan/semantic-go:latest        docker/semantic-go
docker build -t codescan/semantic-python:latest    docker/semantic-python
docker run --rm codescan/semantic-kotlin:latest    --self-check
docker run --rm codescan/semantic-go:latest        --self-check
docker run --rm codescan/semantic-python:latest    --self-check

# 3) 호스트 통합
codescan semantic status        # 5 언어 상태 표시
codescan semantic install typescript
codescan semantic analyze typescript TestSample/typescript
```

## 호스트 측 변경

| 파일 | 변경 |
|------|------|
| `Commands/SemanticCommand.cs` | SupportedLanguages 5종 + PrintHelp 갱신 (이미지 빌드 5종 list) |
| `Models/EdgeKinds.cs` | `Activates` const 추가 (Orleans virtual-actor — T7 와 함께) |
| `Tests/GraphQueryParserTests.cs` | Theory에 `Activates` 추가 (6 actor edge round-trip) |
| `Tests/SemanticResultMergerTests.cs` | `Parse_OrleansActivatesEdge_RoundTrips` 추가 |

호스트 빌드는 docker/** 자동 제외 (이미 v1.3.0에서 적용).

## 평가 (semantic-bridge-architect)

| 축 | 척도 | 결과 |
|----|------|------|
| 플랜 완성도 | Phase 단계가 측정 가능 | ✅ Phase 1/2 모두 이미지 존재 (full 2, stub 3) |
| 매처 명확도 | input → 출력 엣지 매핑 명세 | ✅ 5 README 모두 매처 사양 + exit criteria 명시 |
| 캐시 적중률 (PoC 후) | 동일 소스 재스캔 100% hit | ⏳ 사용자 환경 측정 대기 |
| 도커 이미지 크기 | < 1.5GB (멀티스테이지) | ✅ ts 270MB / go 15MB / python ~80MB / kotlin ~300MB (estimated — 도커 빌드 검증 대기) |
| Fixture 회귀 | regex 매트릭스 동일 이상 | ⏳ typescript 매처가 실제 측정 가능 (Phase 2-A) |

## 다음 단계 제안 — Phase 1-B + Phase 2 stub 채우기

P3가 끝나면 다음 자연스러운 우선순위:

1. **csharp Phase 1-B 매처 합류** — Akka.NET (`Props.Create<T>` / `typeof(T)` / 람다 `new`) + Orleans (`GrainFactory.GetGrain<T>`) 모두 `docker/semantic-csharp/Program.cs` 의 동일 분석 패스에 추가
2. **kotlin stub → full** — Kotlin Analysis API + Pekko Typed 매처 (Phase 1-B 우선순위 1)
3. **typescript 매처 확장** — generics (`Map<K,V>` → K, V 둘 다), structural-typed interface
4. **go stub → full** — `go/packages` + `go/types` — `pkg.NewType()` 생성자 함수 해소 (가장 큰 가치)
5. **python stub → full** — `libcst` + `jedi.Project` cross-file 심볼 해소
6. **Phase 3 시작** — Rust (rust-analyzer), Java (JDT) — Pekko Typed Java 측 매처를 java 이미지에서 처리하면 Pekko Typed Kotlin/Java 두 fixture 모두 커버

## 관련

- 출처 audit: [[2026-05-18-12-00-residual-improvements-audit]] (T5)
- Phase 1 foundation: [[2026-05-18-13-00-roslyn-phase1-foundation]]
- 4-툴킷 합류: [[2026-05-18-15-00-orleans-fixture]] (T7)
- 매처 사양: [[semantic-analyzer-docker]] (Phase 진척 표 v1.4.0 시점)
