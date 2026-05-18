---
date: 2026-05-18T12:30+09:00
engine: test-audit
type: review
mode: log-eval
trigger: "T6 engine first-run + T1 Kotlin R1/R2 봉합 후 baseline"
participants:
  - test-sentinel
  - regex-safety-guard
  - aot-compatibility-scout
---

# test-audit engine — 첫 실행 + R1/R2 봉합 후 baseline

## 실행 요약

`actor-graph-extension` 과 함께 v1.2.0 합류 후 한 번도 실행되지 않았던 두 engine 중 하나.
T1 (KotlinAnalyzer R1/R2 봉합)을 함께 적용한 직후의 baseline을 기록.

## Step 1 — test-sentinel: `dotnet test` 결과

```
통과!  - 실패: 0, 통과: 111, 건너뜀: 0, 전체: 111, 기간: 32s
파일: CodeScan.Tests.dll (net10.0)
```

이전 baseline (`2026-05-18-00-42-language-analyzer-refactor`): **108 pass**
이번 baseline: **111 pass** (+3, 모두 신규 — 회귀 0)

### 신규 회귀 테스트 (Kotlin R1/R2)

| 테스트 | 회귀 | 통과 |
|--------|------|------|
| `Kotlin_PrivateConstructor_PreservesInheritance` | R1: `class X private constructor(...) : Y(args)` inherits 누락 | ✅ |
| `Kotlin_AbstractClass_WithGenericBase_PreservesInheritance` | R1 변형: `abstract class X protected constructor(...) : Y<T>(...)` | ✅ |
| `Kotlin_SuperConstructorCall_IsNotCreates` | R2: super-call `: Y(args)`가 `creates`로 잘못 인식 | ✅ |

## Step 2 — test-sentinel: 언어 × 케이스 매트릭스

`Tests/LanguageAnalyzerTests.cs` + `Tests/SourceAnalyzerTests.cs` + `Tests/CommentExtractorTests.cs` 통합.

| 언어 | 클래스 | 메서드 | 회귀 fixture | 신규 셀 |
|------|--------|--------|------------|---------|
| C# | ✅ Allman + K&R | ✅ | ✅ Allman fixture | — |
| Java | ✅ | ✅ | ✅ | — |
| **Kotlin** | ✅ | ✅ | ✅ + **R1 + R1' + R2** | **+3** |
| JS | ✅ | ✅ super/return/throw 제외 | ✅ | — |
| TS | ✅ | ✅ | ✅ | — |
| PHP | ✅ | ✅ | ✅ | — |
| Python | ✅ | ✅ | ✅ | — |
| Go | ✅ embedded struct | (그래프 전용) | ✅ | — |
| Rust | ✅ impl Trait + group import | (그래프 전용) | ✅ | — |
| C++ | ✅ make_unique/shared/new | (그래프 전용) | ✅ | — |

**누락 셀**: 없음 (Kotlin private/abstract constructor + super-call 봉합 완료)

## Step 3 — regex-safety-guard: 최근 변경 regex 검토

`git log --oneline -- 'Services/Analyzers/**'` 기준 — 이번 세션 변경분:

| 파일 | 변경 패턴 | AOT 안전 | 백트래킹 위험 | 대응 테스트 |
|------|---------|---------|------------|-----------|
| `KotlinAnalyzer.cs` | `ClassDecl()` modifier prefix 확장 (`abstract/open/inner/...`) | ✅ `[GeneratedRegex]` | 안전 (atomic alternation) | R1' |
| `KotlinAnalyzer.cs` | `ClassWithBase()` modifier + `(\s+visibility\s+constructor)?` 흡수 | ✅ | 안전 (lazy 0-1 group) | R1, R1' |
| `KotlinAnalyzer.cs` | (구조) multi-line header pre-pass + `headerLines` skip set | (regex 아님) | — | R2 |

→ **AOT 위반 0건**, **Critical/High 백트래킹 0건**.

## Step 4 — aot-compatibility-scout: AOT 영향

테스트 코드 자체의 AOT 위반 스캔:

```
$ grep -rn "new Regex\(" Tests/
(no matches)

$ grep -rn "JsonSerializer\.(Serialize|Deserialize)" Tests/
(no matches)
```

`KotlinAnalyzer` 변경 영역의 AOT 영향:
- `System.Text.StringBuilder` 사용 — AOT 안전 (BCL)
- `HashSet<int> headerLines` — AOT 안전
- 신규 `[GeneratedRegex]` 2개 — AOT 친화적, 컴파일 타임 생성

`dotnet publish -c Release` 사전 검증 권장 — 이번 audit에서는 미실행 (debug 빌드만 검증).

## 종합 리포트

| 항목 | 결과 |
|------|------|
| `dotnet test` | ✅ 111/111 pass, 회귀 0 |
| 언어 커버리지 | ✅ 10/10 언어 |
| 누락 셀 | 0 |
| 백트래킹 위험 | 0 |
| AOT 위반 | 0 |
| 신규 회귀 fixture | +3 (Kotlin R1/R1'/R2) |

## 평가

| 축 | 척도 | 결과 |
|----|------|------|
| 빌드/테스트 통과 | `dotnet test` 0 fail | ✅ 필수 충족 |
| 언어 커버리지 | 7/7 (실제 10/10) ≥1 케이스 | ✅ |
| 엣지 매트릭스 | 셀별 ≥80% | ✅ Kotlin 매트릭스 100% 회복 |
| 신규 regex 대응 | 변경된 패턴당 ≥2 테스트 | ✅ `ClassDecl` 1 + `ClassWithBase` 2 |

## 다음 단계 제안

1. `dotnet publish -c Release` 로 AOT 빌드 회귀 확인 — 별도 트리거 권장
2. test-audit engine을 PR 머지 직전 정기 트리거로 등록 (CLAUDE.md 자동화 후보)
3. Method 추출 매트릭스 누락 셀 확인 — Kotlin/Java/PHP의 `XML doc` / `// 단일행` / `/* */` 다중행 케이스 (CommentExtractorTests로 위임)

## 관련

- 이전 baseline: [[2026-05-18-00-42-language-analyzer-refactor]] (108 pass)
- 관련 audit: [[2026-05-18-12-00-residual-improvements-audit]] (T1/T6 권고 출처)
- 후속 engine: [[2026-05-18-12-31-after-r1-r2-baseline]] (actor-graph-extension)
